using System.Collections.Generic;
using SpiritHelper.Core;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.Zones;

public sealed class WorldMarkers
{
    private sealed class TimedMarker
    {
        public GameObject Visual = null!;
        public Renderer? Renderer;
        public LineRenderer? Line;
        public Color Color;
        public float CreatedAt;
        public float Until;
    }

    private sealed class UnloadMarker
    {
        public Vector3 Position;
        public UnloadPointType Type;
        public GameObject Visual = null!;
    }

    private GameObject? _unloadVisual;
    private readonly List<UnloadMarker> _unloadMarkers = new List<UnloadMarker>();
    private readonly List<TimedMarker> _temporaryMarkers = new List<TimedMarker>();
    private readonly List<TimedMarker> _navigationTrail = new List<TimedMarker>();
    private GameObject? _zoneVisual;
    private GameObject? _forbiddenVisual;
    private Vector3 _shownZoneCentre;
    private float _shownZoneRadius;
    private Vector3 _lastBreadcrumbPosition;
    private GameObject? _shrineVisual;
    private Light? _shrineLight;
    private LineRenderer? _shrineEnergyStream;
    public bool HasUnloadPoint { get; private set; }
    public Vector3 UnloadPoint { get; private set; }
    public bool HasWorkZone { get; private set; }
    public Vector3 WorkZoneCentre { get; private set; }
    public UnloadPointType UnloadType { get; private set; }
    public WorkZoneMode ZoneMode { get; set; } = WorkZoneMode.Circle;
    public bool HasForbiddenZone { get; private set; }
    public Vector3 ForbiddenZoneCentre { get; private set; }

    public void Restore(bool hasUnload, float[] unload, UnloadPointType unloadType, bool hasZone, float[] zone,
        WorkZoneMode zoneMode, float radius, bool showWorkZone, IReadOnlyList<SavedUnloadPoint>? unloadPoints = null)
    {
        UnloadType = unloadType;
        ZoneMode = zoneMode;
        if (unloadPoints != null && unloadPoints.Count > 0)
        {
            foreach (var saved in unloadPoints)
                if (saved.Position.Length == 3) AddUnload(new Vector3(saved.Position[0], saved.Position[1], saved.Position[2]), saved.Type);
        }
        else if (hasUnload && unload.Length == 3) AddUnload(new Vector3(unload[0], unload[1], unload[2]), unloadType);
        if (hasZone && zone.Length == 3) SetWorkZone(new Vector3(zone[0], zone[1], zone[2]), radius, showWorkZone);
    }

    public void RestoreForbidden(bool hasZone, float[] position)
    {
        if (hasZone && position.Length == 3) SetForbiddenZone(new Vector3(position[0], position[1], position[2]));
    }

    public void SetUnload(Vector3 point, int maximumPoints = 1)
    {
        if (_unloadMarkers.Count >= maximumPoints)
        {
            if (maximumPoints == 1) RemoveUnload();
            else
            {
                Object.Destroy(_unloadMarkers[0].Visual);
                _unloadMarkers.RemoveAt(0);
            }
        }
        AddUnload(point, UnloadType);
    }

    public void RemoveUnload()
    {
        HasUnloadPoint = false;
        foreach (var marker in _unloadMarkers) if (marker.Visual) Object.Destroy(marker.Visual);
        _unloadMarkers.Clear();
        if (_unloadVisual) Object.Destroy(_unloadVisual);
    }

    public void CycleUnloadType()
    {
        UnloadType = (UnloadPointType)(((int)UnloadType + 1) % System.Enum.GetValues(typeof(UnloadPointType)).Length);
        if (!HasUnloadPoint) return;
        var last = _unloadMarkers[_unloadMarkers.Count - 1];
        last.Type = UnloadType;
        Object.Destroy(last.Visual);
        last.Visual = CreateUnloadVisual(last.Position, last.Type);
    }

    public void CycleZoneMode()
    {
        ZoneMode = ZoneMode switch
        {
            WorkZoneMode.Circle => WorkZoneMode.FollowPlayer,
            WorkZoneMode.FollowPlayer => WorkZoneMode.UnloadPointCentered,
            _ => WorkZoneMode.Circle
        };
    }

    public bool Accepts(ResourceDefinition definition, ResourceSelection customSelection) => UnloadType switch
    {
        UnloadPointType.Wood => definition.Category == ResourceCategory.Wood,
        UnloadPointType.Ore => definition.Category == ResourceCategory.Ore,
        UnloadPointType.Stone => definition.Category == ResourceCategory.Stone,
        UnloadPointType.Plants => definition.Category == ResourceCategory.Plant,
        UnloadPointType.Food => definition.Category == ResourceCategory.Food,
        UnloadPointType.Custom => customSelection.Allows(definition),
        _ => true
    };

    public bool TryFindUnload(ResourceDefinition definition, ResourceSelection customSelection, Vector3 origin, out Vector3 point)
    {
        var bestDistance = float.MaxValue;
        point = default;
        var found = false;
        foreach (var marker in _unloadMarkers)
        {
            if (!Accepts(marker.Type, definition, customSelection)) continue;
            var distance = Vector3.SqrMagnitude(marker.Position - origin);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            point = marker.Position;
            found = true;
        }
        return found;
    }

    public List<SavedUnloadPoint> ExportUnloadPoints()
    {
        var saved = new List<SavedUnloadPoint>(_unloadMarkers.Count);
        foreach (var marker in _unloadMarkers)
            saved.Add(new SavedUnloadPoint { Position = new[] { marker.Position.x, marker.Position.y, marker.Position.z }, Type = marker.Type });
        return saved;
    }

    public void SetWorkZone(Vector3 point, float radius, bool show = true)
    {
        HasWorkZone = true;
        WorkZoneCentre = point;
        SetWorkZoneVisible(show, point, radius);
    }

    public void RemoveWorkZone()
    {
        HasWorkZone = false;
        if (_zoneVisual) Object.Destroy(_zoneVisual);
    }

    public void SetForbiddenZone(Vector3 point)
    {
        HasForbiddenZone = true;
        ForbiddenZoneCentre = point;
        if (_forbiddenVisual) Object.Destroy(_forbiddenVisual);
        _forbiddenVisual = CreateRing("SpiritHelper_ForbiddenZone", point + Vector3.up * 0.05f, 10f,
            new Color(1f, 0.2f, 0.2f, 0.75f), 72, true);
    }

    public void RemoveForbiddenZone()
    {
        HasForbiddenZone = false;
        if (_forbiddenVisual) Object.Destroy(_forbiddenVisual);
    }

    public bool IsForbidden(Vector3 position) => HasForbiddenZone &&
        Vector3.SqrMagnitude(position - ForbiddenZoneCentre) <= 100f;

    public void Tick(Player player, float radius, bool showWorkZone)
    {
        for (var index = _temporaryMarkers.Count - 1; index >= 0; index--)
        {
            var marker = _temporaryMarkers[index];
            if (Time.time < marker.Until)
            {
                FadeMarker(marker);
                continue;
            }
            if (marker.Visual) Object.Destroy(marker.Visual);
            _temporaryMarkers.RemoveAt(index);
        }
        for (var index = _navigationTrail.Count - 1; index >= 0; index--)
        {
            var marker = _navigationTrail[index];
            if (Time.time < marker.Until)
            {
                FadeMarker(marker);
                continue;
            }
            if (marker.Visual) Object.Destroy(marker.Visual);
            _navigationTrail.RemoveAt(index);
        }
        if (!showWorkZone)
        {
            if (_zoneVisual) Object.Destroy(_zoneVisual);
            return;
        }
        var centre = GetWorkCentre(player);
        if (_zoneVisual && Vector3.SqrMagnitude(centre - _shownZoneCentre) < 4f && Mathf.Approximately(radius, _shownZoneRadius)) return;
        ShowWorkZone(centre, radius);
    }

    public void ShowTemporaryMarker(Vector3 position, Color color, float duration = 20f)
    {
        var root = new GameObject("SpiritHelper_ScoutMarker");
        var line = root.AddComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = 2; line.widthMultiplier = 0.08f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = color; line.endColor = new Color(color.r, color.g, color.b, 0f);
        line.SetPosition(0, position + Vector3.up * 0.2f);
        line.SetPosition(1, position + Vector3.up * 6f);
        _temporaryMarkers.Add(new TimedMarker
        {
            Visual = root, Line = line, Color = color, CreatedAt = Time.time, Until = Time.time + duration
        });
    }

    public void UpdateNavigationTrail(Vector3 spiritPosition, bool active, float lifetime = 12f)
    {
        if (!active)
        {
            _lastBreadcrumbPosition = default;
            return;
        }
        if (_lastBreadcrumbPosition != default && Vector3.SqrMagnitude(spiritPosition - _lastBreadcrumbPosition) < 2.25f)
            return;
        _lastBreadcrumbPosition = spiritPosition;
        var position = spiritPosition + Vector3.down * 0.35f;
        var breadcrumb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        breadcrumb.name = "SpiritHelper_NavigationBreadcrumb";
        Object.Destroy(breadcrumb.GetComponent<Collider>());
        breadcrumb.transform.position = position;
        breadcrumb.transform.localScale = Vector3.one * 0.11f;
        var renderer = breadcrumb.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        var color = new Color(0.35f, 0.9f, 1f, 0.8f);
        renderer.material.color = color;
        var light = breadcrumb.AddComponent<Light>();
        light.color = color; light.range = 1.8f; light.intensity = 0.35f;
        _navigationTrail.Add(new TimedMarker
        {
            Visual = breadcrumb, Renderer = renderer, Color = color, CreatedAt = Time.time,
            Until = Time.time + Mathf.Max(3f, lifetime)
        });
        while (_navigationTrail.Count > 48)
        {
            if (_navigationTrail[0].Visual) Object.Destroy(_navigationTrail[0].Visual);
            _navigationTrail.RemoveAt(0);
        }
    }

    public void ClearNavigationTrail()
    {
        foreach (var marker in _navigationTrail) if (marker.Visual) Object.Destroy(marker.Visual);
        _navigationTrail.Clear();
        _lastBreadcrumbPosition = default;
    }

    public void SetShrine(Vector3 position, int level = 1)
    {
        RemoveShrine();
        _shrineVisual = new GameObject("SpiritHelper_Shrine");
        var baseColor = new Color(0.55f, 0.35f, 1f, 0.85f);
        for (var ringIndex = 0; ringIndex < Mathf.Clamp(level + 1, 2, 4); ringIndex++)
        {
            var ring = CreateRing("SpiritHelper_ShrineRune", position + Vector3.up * (0.12f + ringIndex * 0.13f),
                0.7f + ringIndex * 0.22f, baseColor, 48, false);
            ring.transform.SetParent(_shrineVisual.transform, true);
        }
        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "SpiritHelper_ShrineCore";
        Object.Destroy(core.GetComponent<Collider>());
        core.transform.SetParent(_shrineVisual.transform, true);
        core.transform.position = position + Vector3.up * 0.8f;
        core.transform.localScale = Vector3.one * (0.12f + Mathf.Clamp(level, 1, 3) * 0.04f);
        core.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default"));
        core.GetComponent<Renderer>().material.color = baseColor;
        _shrineLight = core.AddComponent<Light>();
        _shrineLight.color = baseColor; _shrineLight.range = 5f + level * 2f; _shrineLight.intensity = 1.1f;
        _shrineEnergyStream = _shrineVisual.AddComponent<LineRenderer>();
        _shrineEnergyStream.positionCount = 2; _shrineEnergyStream.useWorldSpace = true;
        _shrineEnergyStream.widthMultiplier = 0.035f;
        _shrineEnergyStream.material = new Material(Shader.Find("Sprites/Default"));
        _shrineEnergyStream.startColor = baseColor;
        _shrineEnergyStream.endColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
        _shrineEnergyStream.enabled = false;
    }

    public void UpdateShrine(bool recharging, Vector3 spiritPosition)
    {
        if (!_shrineVisual || _shrineLight == null || _shrineEnergyStream == null) return;
        _shrineLight.intensity = recharging ? 1.8f + Mathf.PingPong(Time.time * 2f, 1.2f) : 1.1f;
        _shrineEnergyStream.enabled = recharging;
        if (!recharging) return;
        var origin = _shrineLight.transform.position;
        var sway = new Vector3(Mathf.Sin(Time.time * 5f), Mathf.Cos(Time.time * 4f), 0f) * 0.08f;
        _shrineEnergyStream.SetPosition(0, origin + sway);
        _shrineEnergyStream.SetPosition(1, spiritPosition);
    }

    public void RemoveShrine()
    {
        if (_shrineVisual) Object.Destroy(_shrineVisual);
        _shrineVisual = null; _shrineLight = null; _shrineEnergyStream = null;
    }

    public void SetWorkZoneVisible(bool visible, Vector3 centre, float radius)
    {
        if (!visible)
        {
            if (_zoneVisual) Object.Destroy(_zoneVisual);
            return;
        }
        ShowWorkZone(centre, radius);
    }

    public Vector3 GetWorkCentre(Player player) => ZoneMode switch
    {
        WorkZoneMode.FollowPlayer => player.transform.position,
        WorkZoneMode.UnloadPointCentered when HasUnloadPoint => UnloadPoint,
        _ when HasWorkZone => WorkZoneCentre,
        _ => player.transform.position
    };

    public void Dispose()
    {
        if (_unloadVisual) Object.Destroy(_unloadVisual);
        foreach (var marker in _unloadMarkers) if (marker.Visual) Object.Destroy(marker.Visual);
        foreach (var marker in _temporaryMarkers) if (marker.Visual) Object.Destroy(marker.Visual);
        ClearNavigationTrail();
        RemoveShrine();
        if (_zoneVisual) Object.Destroy(_zoneVisual);
        if (_forbiddenVisual) Object.Destroy(_forbiddenVisual);
    }

    private void AddUnload(Vector3 point, UnloadPointType type)
    {
        HasUnloadPoint = true;
        UnloadPoint = point;
        UnloadType = type;
        var visual = CreateUnloadVisual(point, type);
        _unloadVisual = visual;
        _unloadMarkers.Add(new UnloadMarker { Position = point, Type = type, Visual = visual });
    }

    private static GameObject CreateUnloadVisual(Vector3 point, UnloadPointType type)
    {
        var visual = CreateRing("SpiritHelper_UnloadRune", point + Vector3.up * 0.06f, 1.6f, TypeColor(type), 72, false);
        var light = visual.AddComponent<Light>();
        light.color = TypeColor(type); light.range = 4f; light.intensity = 1.2f;
        return visual;
    }

    private static bool Accepts(UnloadPointType type, ResourceDefinition definition, ResourceSelection customSelection) => type switch
    {
        UnloadPointType.Wood => definition.Category == ResourceCategory.Wood,
        UnloadPointType.Ore => definition.Category == ResourceCategory.Ore,
        UnloadPointType.Stone => definition.Category == ResourceCategory.Stone,
        UnloadPointType.Plants => definition.Category == ResourceCategory.Plant,
        UnloadPointType.Food => definition.Category == ResourceCategory.Food,
        UnloadPointType.Custom => customSelection.Allows(definition),
        _ => true
    };

    private static GameObject CreateRing(string name, Vector3 position, float radius, Color color, int segments,
        bool conformToTerrain)
    {
        var root = new GameObject(name);
        var line = root.AddComponent<LineRenderer>();
        line.loop = true; line.useWorldSpace = true; line.positionCount = segments; line.widthMultiplier = Mathf.Clamp(radius * 0.015f, 0.04f, 0.15f);
        line.material = new Material(Shader.Find("Sprites/Default")); line.startColor = color; line.endColor = color;
        for (var i = 0; i < segments; i++)
        {
            var angle = i * Mathf.PI * 2f / segments;
            var sample = position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (conformToTerrain && ZoneSystem.instance) sample.y = ZoneSystem.instance.GetGroundHeight(sample) + 0.08f;
            line.SetPosition(i, sample);
        }
        return root;
    }

    private void ShowWorkZone(Vector3 centre, float radius)
    {
        if (_zoneVisual) Object.Destroy(_zoneVisual);
        _shownZoneCentre = centre;
        _shownZoneRadius = radius;
        _zoneVisual = CreateRing("SpiritHelper_WorkZone", centre + Vector3.up * 0.04f, radius, new Color(0.2f, 0.75f, 1f, 0.7f), 128, true);
    }

    private static void FadeMarker(TimedMarker marker)
    {
        var duration = Mathf.Max(0.01f, marker.Until - marker.CreatedAt);
        var alpha = Mathf.Clamp01((marker.Until - Time.time) / duration);
        var faded = new Color(marker.Color.r, marker.Color.g, marker.Color.b, marker.Color.a * alpha);
        if (marker.Renderer) marker.Renderer.material.color = faded;
        if (marker.Line)
        {
            marker.Line.startColor = faded;
            marker.Line.endColor = new Color(faded.r, faded.g, faded.b, 0f);
        }
        var light = marker.Visual.GetComponent<Light>();
        if (light) light.intensity = 0.35f * alpha;
    }

    private static Color TypeColor(UnloadPointType type) => type switch
    {
        UnloadPointType.Wood => new Color(0.35f, 0.9f, 0.3f),
        UnloadPointType.Ore => new Color(0.65f, 0.65f, 1f),
        UnloadPointType.Stone => new Color(0.7f, 0.75f, 0.8f),
        UnloadPointType.Plants => new Color(0.3f, 1f, 0.55f),
        UnloadPointType.Food => new Color(1f, 0.45f, 0.6f),
        UnloadPointType.Custom => new Color(1f, 0.65f, 0.2f),
        _ => new Color(0.25f, 0.85f, 1f)
    };
}
