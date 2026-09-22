using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    private const float ScanCacheLifetime = 1.5f;
    private const float ScanCacheMovementTolerance = 1f;
    private const float SectorSize = 32f;
    private const int SectorsPerLargeScan = 4;
    private const int MaximumCachedSectors = 1024;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? MineRockAreas = typeof(MineRock).GetField("m_hitAreas", InstanceFields);
    private static readonly FieldInfo? MineRock5Areas = typeof(MineRock5).GetField("m_hitAreas", InstanceFields);
    private readonly ResourceDatabase _database;
    private Collider[] _hits = new Collider[InitialScanColliders];
    private Collider[] _buildingHits = new Collider[InitialScanColliders];
    private readonly Dictionary<Vector3Int, SectorCache> _sectors = new Dictionary<Vector3Int, SectorCache>();
    private readonly HashSet<int> _cachedColliderIds = new HashSet<int>();
    private Vector3 _cachedCentre;
    private float _cachedRadius;
    private float _cacheUntil;
    private int _cachedCount;
    private Vector3 _cachedBuildingCentre;
    private float _cachedBuildingRadius;
    private float _buildingCacheUntil;
    private List<Vector3> _cachedBuildingPositions = new List<Vector3>();
    private bool _lastLargeAreaScanIncomplete;

    public string LastSearchFailure { get; private set; } = string.Empty;

    private sealed class SectorCache
    {
        public readonly List<Collider> Colliders = new List<Collider>();
        public float UpdatedAt;
    }

    public TargetScanner(ResourceDatabase database) => _database = database;

    public SpiritTarget? Find(Vector3 centre, float radius, SpiritJob job, ResourceSelection selection, float safeBaseRadius,
        bool protectPlantedTrees, bool protectCrops, SpiritProgression progression, ISet<int>? blacklist = null,
        Func<Vector3, bool>? forbidden = null, Vector3? unloadPoint = null, bool cleanupOnly = false)
    {
        LastSearchFailure = string.Empty;
        var count = Scan(centre, radius);
        var buildingPositions = CollectBuildingPositions(centre, radius, safeBaseRadius);
        SpiritTarget? nearest = null;
        var bestScore = float.MaxValue;
        var seenTargets = new HashSet<int>();
        var diagnostics = new SearchDiagnostics();

        for (var index = 0; index < count; index++)
        {
            var collider = _hits[index];
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            var component = Resolve(collider);
            if (!component || component is not MineRock && component is not MineRock5 && !seenTargets.Add(component.GetInstanceID())) continue;
            var definition = _database.ByTarget(component);
            if (definition == null || !definition.CanAutoHarvest || !selection.Allows(definition) || !MatchesJob(definition.Category, job)) continue;

            diagnostics.MatchingTargets++;
            if (progression.Level < definition.RequiredSpiritLevel)
            {
                diagnostics.RecordSpiritLevel(definition);
                continue;
            }
            if (progression.ProfessionLevel(definition.Profession) < definition.RequiredProfessionLevel)
            {
                diagnostics.RecordProfessionLevel(definition);
                continue;
            }
            if (blacklist != null && blacklist.Contains(component.GetInstanceID()))
            {
                diagnostics.BlacklistedTargets++;
                continue;
            }
            if (!IsUsableCollider(component, collider)) continue;
            if (component is Pickable pickable && !pickable.CanBePicked())
            {
                diagnostics.ExhaustedTargets++;
                continue;
            }
            if (IsProtected(component, job, protectPlantedTrees, protectCrops, cleanupOnly) ||
                cleanupOnly && !IsCleanupTarget(component, definition))
            {
                diagnostics.ProtectedTargets++;
                continue;
            }

            var point = collider.bounds.center;
            if ((point - centre).sqrMagnitude > EffectiveRadius(radius, definition, progression) * EffectiveRadius(radius, definition, progression))
            {
                diagnostics.OutOfRangeTargets++;
                continue;
            }
            if (forbidden != null && forbidden(point))
            {
                diagnostics.ForbiddenTargets++;
                continue;
            }
            if (safeBaseRadius > 0f && IsNearPlayerBuilding(point, safeBaseRadius, buildingPositions))
            {
                diagnostics.ProtectedTargets++;
                continue;
            }

            var candidate = new SpiritTarget(component, collider, point, definition);
            var score = TargetScorer.Score(candidate, centre, job, unloadPoint, progression);
            if (score >= bestScore) continue;
            bestScore = score;
            nearest = candidate;
        }
        if (nearest == null) LastSearchFailure = diagnostics.ToMessage(_lastLargeAreaScanIncomplete);
        return nearest;
    }

    public SpiritTarget? FindTreeLog(Vector3 centre, float radius, ResourceSelection? selection = null,
        float safeBaseRadius = 0f, bool protectPlantedTrees = true, SpiritProgression? progression = null,
        ISet<int>? blacklist = null, Func<Vector3, bool>? forbidden = null)
    {
        var count = Scan(centre, radius);
        var buildingPositions = CollectBuildingPositions(centre, radius, safeBaseRadius);
        SpiritTarget? nearest = null;
        var bestScore = float.MaxValue;
        var seenTargets = new HashSet<int>();
        var effectiveSelection = selection ?? new ResourceSelection();

        for (var index = 0; index < count; index++)
        {
            var collider = _hits[index];
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            var log = collider.GetComponentInParent<TreeLog>();
            if (!log || !seenTargets.Add(log.GetInstanceID())) continue;
            var definition = _database.ByTarget(log);
            if (definition == null || !definition.CanAutoHarvest || !effectiveSelection.Allows(definition) ||
                progression != null && (progression.Level < definition.RequiredSpiritLevel || progression.ProfessionLevel(definition.Profession) < definition.RequiredProfessionLevel) ||
                blacklist != null && blacklist.Contains(log.GetInstanceID()) || !IsUsableCollider(log, collider) ||
                IsProtected(log, SpiritJob.Woodcutting, protectPlantedTrees, false, false)) continue;
            var point = collider.bounds.center;
            if (forbidden != null && forbidden(point) || safeBaseRadius > 0f && IsNearPlayerBuilding(point, safeBaseRadius, buildingPositions)) continue;
            var distance = (point - centre).sqrMagnitude;
            if (distance > radius * radius || distance >= bestScore) continue;
            bestScore = distance;
            nearest = new SpiritTarget(log, collider, point, definition);
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
            if (definition == null || !definition.CanAutoHarvest || !selection.Allows(definition) ||
                progression.Level < definition.RequiredSpiritLevel ||
                progression.ProfessionLevel(definition.Profession) < definition.RequiredProfessionLevel ||
                component is Pickable pickable && !pickable.CanBePicked()) continue;
            var position = component.transform.position;
            if ((position - centre).sqrMagnitude > EffectiveRadius(radius, definition, progression) * EffectiveRadius(radius, definition, progression)) continue;
            if (!report.TryGetValue(definition.Name, out var entry))
            {
                entry = new SurveyEntry();
                report[definition.Name] = entry;
            }
            entry.Count++;
            entry.Positions.Add(position);
            var distance = Vector3.Distance(centre, position);
            if (distance >= entry.NearestDistance) continue;
            entry.NearestDistance = distance;
            entry.NearestPosition = position;
        }
        return report;
    }

    private static float EffectiveRadius(float radius, ResourceDefinition definition, SpiritProgression progression) =>
        Mathf.Min(radius, definition.SearchRadius * (1f + (progression.ProfessionLevel(Profession.Exploration) +
            progression.TalentTier("exploration") * 4) * 0.025f));

    private List<Vector3> CollectBuildingPositions(Vector3 centre, float radius, float safeBaseRadius)
    {
        var scanRadius = radius + Mathf.Max(0f, safeBaseRadius);
        if (Time.time < _buildingCacheUntil &&
            Vector3.SqrMagnitude(centre - _cachedBuildingCentre) <= ScanCacheMovementTolerance * ScanCacheMovementTolerance &&
            Mathf.Abs(scanRadius - _cachedBuildingRadius) < 0.01f)
            return _cachedBuildingPositions;

        var positions = new List<Vector3>();
        var seen = new HashSet<int>();
        while (true)
        {
            var count = Physics.OverlapSphereNonAlloc(centre, scanRadius, _buildingHits, Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (var index = 0; index < count; index++)
            {
                var piece = _buildingHits[index] ? _buildingHits[index].GetComponentInParent<Piece>() : null;
                if (piece && piece.IsPlacedByPlayer() && seen.Add(piece.GetInstanceID())) positions.Add(piece.transform.position);
            }
            if (count < _buildingHits.Length || _buildingHits.Length >= MaxScanColliders)
            {
                _cachedBuildingCentre = centre;
                _cachedBuildingRadius = scanRadius;
                _buildingCacheUntil = Time.time + ScanCacheLifetime;
                _cachedBuildingPositions = positions;
                return positions;
            }
            _buildingHits = new Collider[Mathf.Min(_buildingHits.Length * 2, MaxScanColliders)];
        }
    }
    private int Scan(Vector3 centre, float radius)
    {
        if (radius > SectorSize) return ScanLargeArea(centre, radius);
        _lastLargeAreaScanIncomplete = false;
        if (Time.time < _cacheUntil && Vector3.SqrMagnitude(centre - _cachedCentre) <= ScanCacheMovementTolerance * ScanCacheMovementTolerance &&
            Mathf.Abs(radius - _cachedRadius) < 0.01f) return _cachedCount;
        while (true)
        {
            var count = Physics.OverlapSphereNonAlloc(centre, radius, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
            if (count < _hits.Length || _hits.Length >= MaxScanColliders)
            {
                _cachedCentre = centre;
                _cachedRadius = radius;
                _cachedCount = count;
                _cacheUntil = Time.time + ScanCacheLifetime;
                return count;
            }
            _hits = new Collider[Mathf.Min(_hits.Length * 2, MaxScanColliders)];
        }
    }

    private int ScanLargeArea(Vector3 centre, float radius)
    {
        PruneSectors(centre, radius);
        _cachedColliderIds.Clear();
        var immediateCount = ScanSphere(centre, SectorSize);
        var immediateHits = new Collider[immediateCount];
        Array.Copy(_hits, immediateHits, immediateCount);

        var sectors = RelevantSectors(centre, radius);
        sectors.Sort((left, right) => CompareRefreshPriority(left, right, centre));
        var staleSectors = 0;
        foreach (var sector in sectors)
            if (!_sectors.TryGetValue(sector, out var cached) || Time.time >= cached.UpdatedAt + ScanCacheLifetime)
                staleSectors++;
        var refreshed = 0;
        foreach (var sector in sectors)
        {
            if (refreshed >= SectorsPerLargeScan) break;
            if (!_sectors.TryGetValue(sector, out var cached) || Time.time >= cached.UpdatedAt + ScanCacheLifetime)
            {
                RefreshSector(sector);
                refreshed++;
            }
        }
        _lastLargeAreaScanIncomplete = staleSectors > refreshed;

        var count = 0;
        foreach (var collider in immediateHits)
        {
            if (!collider || (collider.bounds.center - centre).sqrMagnitude > radius * radius ||
                !_cachedColliderIds.Add(collider.GetInstanceID())) continue;
            _hits[count++] = collider;
        }
        foreach (var sector in sectors)
        {
            if (!_sectors.TryGetValue(sector, out var cached)) continue;
            foreach (var collider in cached.Colliders)
            {
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                if ((collider.bounds.center - centre).sqrMagnitude > radius * radius) continue;
                if (!_cachedColliderIds.Add(collider.GetInstanceID())) continue;
                EnsureHitCapacity(count + 1);
                _hits[count++] = collider;
            }
        }
        return count;
    }

    private sealed class SearchDiagnostics
    {
        public int MatchingTargets;
        public int ProtectedTargets;
        public int ForbiddenTargets;
        public int ExhaustedTargets;
        public int BlacklistedTargets;
        public int OutOfRangeTargets;
        private ResourceDefinition? _spiritLevelResource;
        private ResourceDefinition? _professionLevelResource;

        public void RecordSpiritLevel(ResourceDefinition definition)
        {
            if (_spiritLevelResource == null || definition.RequiredSpiritLevel > _spiritLevelResource.RequiredSpiritLevel)
                _spiritLevelResource = definition;
        }

        public void RecordProfessionLevel(ResourceDefinition definition)
        {
            if (_professionLevelResource == null || definition.RequiredProfessionLevel > _professionLevelResource.RequiredProfessionLevel)
                _professionLevelResource = definition;
        }

        public string ToMessage(bool scanIncomplete)
        {
            if (_spiritLevelResource != null)
                return $"Для «{_spiritLevelResource.DisplayName}» нужен уровень духа {_spiritLevelResource.RequiredSpiritLevel}.";
            if (_professionLevelResource != null)
                return $"Для «{_professionLevelResource.DisplayName}» нужен уровень профессии {ProfessionName(_professionLevelResource.Profession)} {_professionLevelResource.RequiredProfessionLevel}.";
            if (ProtectedTargets > 0)
                return "Подходящие цели защищены или находятся рядом с постройками.";
            if (ForbiddenTargets > 0)
                return "Подходящие цели находятся в запрещённой зоне.";
            if (ExhaustedTargets > 0)
                return "Подходящие цели уже исчерпаны.";
            if (BlacklistedTargets > 0)
                return "Недавние цели временно исключены после неудачной попытки.";
            if (OutOfRangeTargets > 0)
                return "Подходящие цели находятся вне доступного радиуса поиска.";
            if (scanIncomplete)
                return "Поиск ещё проверяет рабочую зону.";
            return "Подходящих целей пока не найдено. Проверь ресурс и рабочую зону.";
        }

        private static string ProfessionName(Profession profession) => profession switch
        {
            Profession.Woodcutting => "лесоруба",
            Profession.Mining => "шахтёра",
            Profession.Gathering => "собирателя",
            Profession.Logistics => "носильщика",
            Profession.Exploration => "следопыта",
            _ => "духа"
        };
    }

    private int CompareRefreshPriority(Vector3Int left, Vector3Int right, Vector3 centre)
    {
        var leftKnown = _sectors.TryGetValue(left, out var leftCache);
        var rightKnown = _sectors.TryGetValue(right, out var rightCache);
        if (leftKnown != rightKnown) return leftKnown ? 1 : -1;
        if (leftKnown)
        {
            var byAge = leftCache!.UpdatedAt.CompareTo(rightCache!.UpdatedAt);
            if (byAge != 0) return byAge;
        }
        return SectorDistanceSquared(left, centre).CompareTo(SectorDistanceSquared(right, centre));
    }

    private void PruneSectors(Vector3 centre, float radius)
    {
        if (_sectors.Count == 0) return;
        var maximumDistance = radius + SectorSize * 2f;
        var remove = new List<Vector3Int>();
        foreach (var pair in _sectors)
        {
            if (SectorDistanceSquared(pair.Key, centre) > maximumDistance * maximumDistance ||
                pair.Value.Colliders.RemoveAll(collider => !collider) > 0 && pair.Value.Colliders.Count == 0)
                remove.Add(pair.Key);
        }
        if (_sectors.Count - remove.Count > MaximumCachedSectors)
        {
            var candidates = new List<Vector3Int>();
            foreach (var sector in _sectors.Keys) if (!remove.Contains(sector)) candidates.Add(sector);
            candidates.Sort((left, right) => SectorDistanceSquared(right, centre).CompareTo(SectorDistanceSquared(left, centre)));
            var excess = _sectors.Count - remove.Count - MaximumCachedSectors;
            for (var index = 0; index < excess; index++) remove.Add(candidates[index]);
        }
        foreach (var sector in remove) _sectors.Remove(sector);
    }

    private int ScanSphere(Vector3 centre, float radius)
    {
        while (true)
        {
            var count = Physics.OverlapSphereNonAlloc(centre, radius, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
            if (count < _hits.Length || _hits.Length >= MaxScanColliders) return count;
            _hits = new Collider[Mathf.Min(_hits.Length * 2, MaxScanColliders)];
        }
    }

    private void EnsureHitCapacity(int required)
    {
        if (required <= _hits.Length) return;
        var capacity = _hits.Length;
        while (capacity < required && capacity < MaxScanColliders) capacity = Mathf.Min(capacity * 2, MaxScanColliders);
        if (capacity > _hits.Length) Array.Resize(ref _hits, capacity);
    }

    private void RefreshSector(Vector3Int sector)
    {
        var centre = new Vector3((sector.x + 0.5f) * SectorSize, (sector.y + 0.5f) * SectorSize,
            (sector.z + 0.5f) * SectorSize);
        var count = ScanSphere(centre, SectorSize * 0.9f);
        if (!_sectors.TryGetValue(sector, out var cached))
        {
            cached = new SectorCache();
            _sectors[sector] = cached;
        }
        cached.Colliders.Clear();
        for (var index = 0; index < count; index++) if (_hits[index]) cached.Colliders.Add(_hits[index]);
        cached.UpdatedAt = Time.time;
    }

    private static List<Vector3Int> RelevantSectors(Vector3 centre, float radius)
    {
        var min = ToSector(centre - Vector3.one * radius);
        var max = ToSector(centre + Vector3.one * radius);
        var sectors = new List<Vector3Int>();
        for (var x = min.x; x <= max.x; x++)
        for (var y = min.y; y <= max.y; y++)
        for (var z = min.z; z <= max.z; z++)
        {
            var sector = new Vector3Int(x, y, z);
            if (SectorDistanceSquared(sector, centre) <= radius * radius) sectors.Add(sector);
        }
        return sectors;
    }

    private static Vector3Int ToSector(Vector3 position) => new Vector3Int(
        Mathf.FloorToInt(position.x / SectorSize), Mathf.FloorToInt(position.y / SectorSize), Mathf.FloorToInt(position.z / SectorSize));

    private static float SectorDistanceSquared(Vector3Int sector, Vector3 point)
    {
        var min = new Vector3(sector.x * SectorSize, sector.y * SectorSize, sector.z * SectorSize);
        var max = min + Vector3.one * SectorSize;
        var nearest = new Vector3(Mathf.Clamp(point.x, min.x, max.x), Mathf.Clamp(point.y, min.y, max.y),
            Mathf.Clamp(point.z, min.z, max.z));
        return (nearest - point).sqrMagnitude;
    }

    private static Component? Resolve(Collider collider) =>
        (Component?)collider.GetComponentInParent<Pickable>() ??
        (Component?)collider.GetComponentInParent<TreeLog>() ??
        (Component?)collider.GetComponentInParent<TreeBase>() ??
        (Component?)collider.GetComponentInParent<MineRock5>() ??
        (Component?)collider.GetComponentInParent<MineRock>() ??
        collider.GetComponentInParent<Destructible>();

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

    private static bool IsCleanupTarget(Component component, ResourceDefinition definition)
    {
        if (component is TreeLog) return true;
        if (component is TreeBase)
        {
            var name = component.gameObject.name;
            return name.IndexOf("stub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("stump", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        return definition.Category == ResourceCategory.Stone;
    }

    private static bool IsProtected(Component component, SpiritJob job, bool protectPlantedTrees, bool protectCrops, bool cleanupOnly)
    {
        var piece = component.GetComponentInParent<Piece>();
        if (!piece) return false;
        if (cleanupOnly) return piece.IsPlacedByPlayer();
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
