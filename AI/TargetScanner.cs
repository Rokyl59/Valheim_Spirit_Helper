using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SpiritHelper.Core;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.AI;

public sealed class TargetScanner
{
    private const int InitialScanColliders = 1024;
    private const int MaxScanColliders = 16384;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? MineRockAreas = typeof(MineRock).GetField("m_hitAreas", InstanceFields);
    private static readonly FieldInfo? MineRock5Areas = typeof(MineRock5).GetField("m_hitAreas", InstanceFields);
    private readonly ResourceDatabase _database;
    private Collider[] _hits = new Collider[InitialScanColliders];

    public TargetScanner(ResourceDatabase database) => _database = database;

    public SpiritTarget? Find(Vector3 centre, float radius, SpiritJob job, ResourceSelection selection, float safeBaseRadius,
        bool protectPlantedTrees, bool protectCrops)
    {
        var count = Scan(centre, radius);
        var buildingPositions = CollectBuildingPositions(count);
        SpiritTarget? nearest = null;
        var nearestDistance = float.MaxValue;
        var foundTreeLog = false;
        var seenNonMineTargets = new HashSet<int>();

        for (var index = 0; index < count; index++)
        {
            var collider = _hits[index];
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            var component = Resolve(collider);
            if (!component) continue;
            var definition = _database.ByTarget(component);
            if (definition == null || !MatchesJob(definition.Category, job) || !selection.Allows(definition)) continue;
            if (!IsUsableCollider(component, collider)) continue;
            if (component is not MineRock && component is not MineRock5 && !seenNonMineTargets.Add(component.GetInstanceID())) continue;
            if (IsProtected(component, job, protectPlantedTrees, protectCrops)) continue;
            var hitPoint = collider.bounds.center;
            if (safeBaseRadius > 0f && IsNearPlayerBuilding(hitPoint, safeBaseRadius, buildingPositions)) continue;

            var isTreeLog = component is TreeLog;
            if (job == SpiritJob.Woodcutting)
            {
                if (foundTreeLog && !isTreeLog) continue;
                if (isTreeLog && !foundTreeLog) { nearest = null; nearestDistance = float.MaxValue; foundTreeLog = true; }
            }

            var distance = (hitPoint - centre).sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = new SpiritTarget(component, collider, hitPoint, definition);
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
        while (true)
        {
            var count = Physics.OverlapSphereNonAlloc(centre, radius, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
            if (count < _hits.Length || _hits.Length >= MaxScanColliders) return count;
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
