using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;
using SpiritHelper.Core;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.AI;

public sealed class TargetScanner
{
    public sealed class SurveyEntry
    {
        public int Count;
        public Vector3 NearestPosition;
        public float NearestDistance = float.MaxValue;
        public readonly List<Vector3> Positions = new List<Vector3>();
    }
    private const int InitialScanColliders = 1024;
    private const int MaxScanColliders = 16384;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? MineRockAreas = typeof(MineRock).GetField("m_hitAreas", InstanceFields);
    private static readonly FieldInfo? MineRock5Areas = typeof(MineRock5).GetField("m_hitAreas", InstanceFields);
    private readonly ResourceDatabase _database;
    private Collider[] _hits = new Collider[InitialScanColliders];
    private Vector3Int _cachedSector;
    private float _cachedRadius;
    private float _cacheUntil;
    private int _cachedCount;

    public TargetScanner(ResourceDatabase database) => _database = database;

    public SpiritTarget? Find(Vector3 centre, float radius, SpiritJob job, ResourceSelection selection, float safeBaseRadius,
        bool protectPlantedTrees, bool protectCrops, SpiritProgression progression, ISet<int>? blacklist = null,
        System.Func<Vector3, bool>? forbidden = null, Vector3? unloadPoint = null)
    {
        var count = Scan(centre, radius);
        var buildingPositions = CollectBuildingPositions(count);
        SpiritTarget? nearest = null;
        var bestScore = float.MaxValue;
        var foundTreeLog = false;
        var seenNonMineTargets = new HashSet<int>();

        for (var index = 0; index < count; index++)
        {
            var collider = _hits[index];
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            var component = Resolve(collider);
            if (!component) continue;
            var definition = _database.ByTarget(component);
            if (definition == null || !definition.CanAutoHarvest || !MatchesJob(definition.Category, job) || !selection.Allows(definition)) continue;
            if (progression.Level < definition.RequiredSpiritLevel ||
                progression.ProfessionLevel(definition.Profession) < definition.RequiredProfessionLevel) continue;
            if (!IsUsableCollider(component, collider)) continue;
            if (blacklist != null && blacklist.Contains(component.GetInstanceID())) continue;
            if (component is Pickable pickable && !pickable.CanBePicked()) continue;
            if (component is not MineRock && component is not MineRock5 && !seenNonMineTargets.Add(component.GetInstanceID())) continue;
            if (IsProtected(component, job, protectPlantedTrees, protectCrops)) continue;
            var hitPoint = collider.bounds.center;
            if (forbidden != null && forbidden(hitPoint)) continue;
            if (safeBaseRadius > 0f && IsNearPlayerBuilding(hitPoint, safeBaseRadius, buildingPositions)) continue;

            var isTreeLog = component is TreeLog;
            if (job == SpiritJob.Woodcutting)
            {
                if (foundTreeLog && !isTreeLog) continue;
                if (isTreeLog && !foundTreeLog) { nearest = null; bestScore = float.MaxValue; foundTreeLog = true; }
            }

            var effectiveRadius = Mathf.Min(radius, definition.SearchRadius * (1f + progression.ProfessionLevel(Profession.Exploration) * 0.025f));
            var distance = (hitPoint - centre).sqrMagnitude;
            if (distance > effectiveRadius * effectiveRadius) continue;
            var candidate = new SpiritTarget(component, collider, hitPoint, definition);
            var score = TargetScorer.Score(candidate, centre, job, unloadPoint);
            if (score >= bestScore) continue;
            bestScore = score;
            nearest = candidate;
        }

        return nearest;
    }

    public SpiritTarget? FindTreeLog(Vector3 centre, float radius)
    {
        var count = Scan(centre, radius);
        SpiritTarget? nearest = null;
        var nearestDistance = float.MaxValue;
        var seen = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var collider = _hits[index];
            var log = collider ? collider.GetComponentInParent<TreeLog>() : null;
            if (!log || !seen.Add(log.GetInstanceID())) continue;
            var point = collider!.bounds.center;
            var distance = (point - centre).sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = new SpiritTarget(log, collider, point, _database.Wood);
        }
        return nearest;
    }

    public Dictionary<string, SurveyEntry> Survey(Vector3 centre, float radius, ResourceSelection selection,
        SpiritProgression progression)
    {
        var count = Scan(centre, radius);
        var report = new Dictionary<string, SurveyEntry>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var collider = _hits[index];
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            var component = Resolve(collider);
            if (!component || !seen.Add(component.GetInstanceID())) continue;
            var definition = _database.ByTarget(component);
            if (definition == null || !definition.CanAutoHarvest || !selection.Allows(definition) || progression.Level < definition.RequiredSpiritLevel ||
                progression.ProfessionLevel(definition.Profession) < definition.RequiredProfessionLevel) continue;
            if (component is Pickable pickable && !pickable.CanBePicked()) continue;
            if (!report.TryGetValue(definition.Name, out var entry))
            {
                entry = new SurveyEntry();
                report[definition.Name] = entry;
            }
            entry.Count++;
            entry.Positions.Add(component.transform.position);
            var distance = Vector3.Distance(centre, component.transform.position);
            if (distance >= entry.NearestDistance) continue;
            entry.NearestDistance = distance;
            entry.NearestPosition = component.transform.position;
        }
        return report;
    }

    private List<Vector3> CollectBuildingPositions(int count)
    {
        var positions = new List<Vector3>();
        var seen = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var piece = _hits[index] ? _hits[index].GetComponentInParent<Piece>() : null;
            if (piece && piece.IsPlacedByPlayer() && seen.Add(piece.GetInstanceID())) positions.Add(piece.transform.position);
        }
        return positions;
    }

    private int Scan(Vector3 centre, float radius)
    {
        var sector = new Vector3Int(Mathf.FloorToInt(centre.x / 32f), Mathf.FloorToInt(centre.y / 32f), Mathf.FloorToInt(centre.z / 32f));
        if (Time.time < _cacheUntil && sector == _cachedSector && Mathf.Abs(radius - _cachedRadius) < 1f)
            return _cachedCount;
        while (true)
        {
            var count = Physics.OverlapSphereNonAlloc(centre, radius, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
            if (count < _hits.Length || _hits.Length >= MaxScanColliders)
            {
                _cachedSector = sector;
                _cachedRadius = radius;
                _cachedCount = count;
                _cacheUntil = Time.time + 2f;
                return count;
            }
            _hits = new Collider[Mathf.Min(_hits.Length * 2, MaxScanColliders)];
        }
    }

    private static Component? Resolve(Collider collider) =>
        (Component?)collider.GetComponentInParent<Pickable>() ??
        (Component?)collider.GetComponentInParent<TreeLog>() ??
        (Component?)collider.GetComponentInParent<TreeBase>() ??
        (Component?)collider.GetComponentInParent<MineRock5>() ??
        (Component?)collider.GetComponentInParent<MineRock>() ??
        (Component?)collider.GetComponentInParent<Destructible>();

    private static bool IsUsableCollider(Component component, Collider collider)
    {
        if (component is MineRock rock && MineRockAreas?.GetValue(rock) is Collider[] rockAreas)
        {
            foreach (var area in rockAreas) if (area == collider) return true;
            return false;
        }
        if (component is MineRock5 rock5 && MineRock5Areas?.GetValue(rock5) is IEnumerable areas)
        {
            foreach (var area in areas)
            {
                if (area == null) continue;
                var field = area.GetType().GetField("m_collider", InstanceFields);
                if (field?.GetValue(area) as Collider == collider) return true;
            }
            return false;
        }
        return true;
    }

    private static bool IsProtected(Component component, SpiritJob job, bool protectPlantedTrees, bool protectCrops)
    {
        var piece = component.GetComponentInParent<Piece>();
        if (!piece) return false;
        if (job == SpiritJob.Woodcutting) return protectPlantedTrees;
        if (job == SpiritJob.Gathering) return protectCrops;
        return piece.IsPlacedByPlayer();
    }

    private static bool MatchesJob(ResourceCategory category, SpiritJob job) =>
        job == SpiritJob.Woodcutting ? category == ResourceCategory.Wood :
        job == SpiritJob.Mining ? category is ResourceCategory.Ore or ResourceCategory.Stone :
        job == SpiritJob.Gathering && category is ResourceCategory.Plant or ResourceCategory.Food;

    private static bool IsNearPlayerBuilding(Vector3 position, float radius, List<Vector3> buildingPositions)
    {
        var squaredRadius = radius * radius;
        foreach (var buildingPosition in buildingPositions)
            if ((buildingPosition - position).sqrMagnitude <= squaredRadius) return true;
        return false;
    }
}
