using SpiritHelper.Core;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.Zones;

public sealed class WorldMarkers
{
    private GameObject? _unloadVisual;
    private GameObject? _zoneVisual;
    private Vector3 _shownZoneCentre;
    private float _shownZoneRadius;
    public bool HasUnloadPoint { get; private set; }
    public Vector3 UnloadPoint { get; private set; }
    public bool HasWorkZone { get; private set; }
    public Vector3 WorkZoneCentre { get; private set; }
    public UnloadPointType UnloadType { get; private set; }
    public WorkZoneMode ZoneMode { get; set; } = WorkZoneMode.Circle;

    public void Restore(bool hasUnload, float[] unload, UnloadPointType unloadType, bool hasZone, float[] zone,
        WorkZoneMode zoneMode, float radius)
    {
        UnloadType = unloadType;
        ZoneMode = zoneMode;
        if (hasUnload && unload.Length == 3) SetUnload(new Vector3(unload[0], unload[1], unload[2]));
        if (hasZone && zone.Length == 3) SetWorkZone(new Vector3(zone[0], zone[1], zone[2]), radius);
    }

    public void SetUnload(Vector3 point)
    {
        HasUnloadPoint = true;
        UnloadPoint = point;
        if (_unloadVisual) Object.Destroy(_unloadVisual);
        _unloadVisual = CreateRing("SpiritHelper_UnloadRune", point + Vector3.up * 0.06f, 1.6f, TypeColor(UnloadType), 72);
        var light = _unloadVisual.AddComponent<Light>();
        light.color = TypeColor(UnloadType); light.range = 4f; light.intensity = 1.2f;
    }

    public void RemoveUnload()
    {
        HasUnloadPoint = false;
        if (_unloadVisual) Object.Destroy(_unloadVisual);
    }

    public void CycleUnloadType()
    {
        UnloadType = (UnloadPointType)(((int)UnloadType + 1) % System.Enum.GetValues(typeof(UnloadPointType)).Length);
        if (HasUnloadPoint) SetUnload(UnloadPoint);
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

    public void SetWorkZone(Vector3 point, float radius)
    {
        HasWorkZone = true;
        WorkZoneCentre = point;
        ShowWorkZone(point, radius);
    }

    public void RemoveWorkZone()
    {
        HasWorkZone = false;
        if (_zoneVisual) Object.Destroy(_zoneVisual);
    }

    public void Tick(Player player, float radius)
    {
        var centre = GetWorkCentre(player);
        if (_zoneVisual && Vector3.SqrMagnitude(centre - _shownZoneCentre) < 4f && Mathf.Approximately(radius, _shownZoneRadius)) return;
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
        if (_zoneVisual) Object.Destroy(_zoneVisual);
    }

    private static GameObject CreateRing(string name, Vector3 position, float radius, Color color, int segments)
    {
        var root = new GameObject(name);
        var line = root.AddComponent<LineRenderer>();
        line.loop = true; line.useWorldSpace = true; line.positionCount = segments; line.widthMultiplier = Mathf.Clamp(radius * 0.015f, 0.04f, 0.15f);
        line.material = new Material(Shader.Find("Sprites/Default")); line.startColor = color; line.endColor = color;
        for (var i = 0; i < segments; i++)
        {
            var angle = i * Mathf.PI * 2f / segments;
            var sample = position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (ZoneSystem.instance) sample.y = ZoneSystem.instance.GetGroundHeight(sample) + 0.08f;
            line.SetPosition(i, sample);
        }
        return root;
    }

    private void ShowWorkZone(Vector3 centre, float radius)
    {
        if (_zoneVisual) Object.Destroy(_zoneVisual);
        _shownZoneCentre = centre;
        _shownZoneRadius = radius;
        _zoneVisual = CreateRing("SpiritHelper_WorkZone", centre + Vector3.up * 0.04f, radius, new Color(0.2f, 0.75f, 1f, 0.7f), 128);
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
