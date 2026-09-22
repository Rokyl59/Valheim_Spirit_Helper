using System;
using System.Collections.Generic;
using SpiritHelper.Core;
using SpiritHelper.Resources;

namespace SpiritHelper.Progression;

[Serializable]
public sealed class SpiritSaveData
{
    public int SpiritLevel = 1;
    public float SpiritXp;
    public Dictionary<Profession, int> ProfessionLevels = new Dictionary<Profession, int>();
    public Dictionary<Profession, float> ProfessionXp = new Dictionary<Profession, float>();
    public HashSet<string> UnlockedTalents = new HashSet<string>();
    public BalanceMode BalanceMode = BalanceMode.Vanilla;
    public float Energy = 100f;
    public bool HasUnloadPoint;
    public float[] UnloadPoint = new float[3];
    public UnloadPointType UnloadPointType = UnloadPointType.Universal;
    public List<SavedUnloadPoint> UnloadPoints = new List<SavedUnloadPoint>();
    public bool HasWorkZone;
    public float[] WorkZone = new float[3];
    public WorkZoneMode WorkZoneMode = WorkZoneMode.Circle;
    public bool HasForbiddenZone;
    public float[] ForbiddenZone = new float[3];
    public ResourceFilterMode ResourceFilterMode = ResourceFilterMode.All;
    public ResourceCategory ResourceCategory = ResourceCategory.Wood;
    public string SelectedResource = string.Empty;
    public string SpiritName = string.Empty;
    public string Personality = string.Empty;
    public long TreesWorked;
    public long OreWorked;
    public long PlantsGathered;
    public long ItemsDelivered;
    public float DistanceFlown;
    public float Bond;
    public List<string> Journal = new List<string>();
    public SpiritAutomationData Automation = new SpiritAutomationData();
}

[Serializable]
public sealed class SavedUnloadPoint
{
    public float[] Position = new float[3];
    public UnloadPointType Type = UnloadPointType.Universal;
    public long ContainerUserId;
    public uint ContainerId;
    public string CustomResourceName = string.Empty;
}

public sealed class SpiritProgression
{
    private const int MaxSpiritLevel = 30;
    private const int MaxProfessionLevel = 20;
    private const int MaxTalentTier = 4;
    private static readonly HashSet<string> TalentBranches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "woodcutting", "mining", "logistics", "exploration"
    };
    private readonly SpiritSaveData _data;

    public SpiritProgression(SpiritSaveData data)
    {
        _data = data;
        Normalize(_data);
        EnsureProfessions();
    }

    public static void Normalize(SpiritSaveData data)
    {
        data.ProfessionLevels ??= new Dictionary<Profession, int>();
        data.ProfessionXp ??= new Dictionary<Profession, float>();
        data.UnlockedTalents ??= new HashSet<string>();
        data.Journal ??= new List<string>();
        data.UnloadPoint = Position(data.UnloadPoint);
        data.WorkZone = Position(data.WorkZone);
        data.ForbiddenZone = Position(data.ForbiddenZone);
        data.UnloadPoints ??= new List<SavedUnloadPoint>();
        foreach (var point in data.UnloadPoints) if (point != null)
        {
            point.Position = Position(point.Position);
            point.CustomResourceName ??= string.Empty;
        }

        data.Automation ??= new SpiritAutomationData();
        var automation = data.Automation;
        automation.PendingOrders ??= new List<SpiritOrderData>();
        automation.IgnoredTargets ??= new List<IgnoredTargetData>();
        automation.WorldMemory ??= new List<KnownResourceData>();
        automation.NamedZones ??= new List<NamedZoneData>();
        automation.CourierRoute ??= new CourierRouteData();
        automation.Rules ??= new List<SpiritRuleData>();
        automation.Schedule ??= new List<ScheduleEntryData>();
        automation.BiomeKnowledge ??= new List<BiomeKnowledgeData>();
        automation.ResourceStatistics ??= new List<ResourceStatisticData>();
        automation.DecisionHistory ??= new List<DecisionRecordData>();
        automation.ShrinePosition = Position(automation.ShrinePosition);
        automation.LastDeathPosition = Position(automation.LastDeathPosition);
        automation.LastBoatPosition = Position(automation.LastBoatPosition);
        automation.LastBedPosition = Position(automation.LastBedPosition);
        automation.CourierRoute.Source = Position(automation.CourierRoute.Source);
        automation.CourierRoute.Destination = Position(automation.CourierRoute.Destination);
        NormalizeOrder(automation.CurrentOrder);
        NormalizeOrder(automation.ObservedOrder);
        foreach (var order in automation.PendingOrders) NormalizeOrder(order);
        foreach (var ignored in automation.IgnoredTargets) if (ignored != null)
        {
            ignored.Prefab ??= string.Empty;
            ignored.Position = Position(ignored.Position);
        }
        foreach (var resource in automation.WorldMemory) if (resource != null)
        {
            resource.Resource ??= string.Empty;
            resource.Position = Position(resource.Position);
        }
        foreach (var zone in automation.NamedZones) if (zone != null)
        {
            zone.Name ??= string.Empty;
            zone.Position = Position(zone.Position);
        }
    }

    public SpiritSaveData Data => _data;
    public int Level => _data.SpiritLevel;
    public float Xp => _data.SpiritXp;
    public float NextXp => 150f + (_data.SpiritLevel - 1) * 100f;
    public int ProfessionLevel(Profession profession) => _data.ProfessionLevels[profession];
    public int AvailableTalentPoints => Math.Max(0, Level / 5 - _data.UnlockedTalents.Count);
    public bool HasTalent(string talent) => _data.UnlockedTalents.Contains(talent);

    public int TalentTier(string branch)
    {
        if (!TalentBranches.Contains(branch)) return 0;
        for (var tier = MaxTalentTier; tier >= 1; tier--)
            if (_data.UnlockedTalents.Contains($"{branch}.{tier}")) return tier;
        return 0;
    }

    public string? UnlockNextTalent(string branch)
    {
        if (AvailableTalentPoints <= 0 || !TalentBranches.Contains(branch)) return null;
        branch = branch.ToLowerInvariant();
        var nextTier = TalentTier(branch) + 1;
        if (nextTier > MaxTalentTier) return null;
        var talent = $"{branch}.{nextTier}";
        return _data.UnlockedTalents.Add(talent) ? talent : null;
    }

    public bool Award(float spiritXp, float professionXp, Profession profession, float spiritMultiplier, float professionMultiplier)
    {
        var previousSpiritLevel = _data.SpiritLevel;
        var previousProfessionLevel = _data.ProfessionLevels[profession];
        _data.SpiritXp += Math.Max(0f, spiritXp * spiritMultiplier);
        _data.ProfessionXp[profession] += Math.Max(0f, professionXp * professionMultiplier);
        while (_data.SpiritLevel < MaxSpiritLevel && _data.SpiritXp >= NextXp)
        {
            _data.SpiritXp -= NextXp;
            _data.SpiritLevel++;
        }
        while (_data.ProfessionLevels[profession] < MaxProfessionLevel)
        {
            var needed = 100f + (_data.ProfessionLevels[profession] - 1) * 75f;
            if (_data.ProfessionXp[profession] < needed) break;
            _data.ProfessionXp[profession] -= needed;
            _data.ProfessionLevels[profession]++;
        }
        return _data.SpiritLevel > previousSpiritLevel || _data.ProfessionLevels[profession] > previousProfessionLevel;
    }

    private void EnsureProfessions()
    {
        foreach (Profession profession in Enum.GetValues(typeof(Profession)))
        {
            if (!_data.ProfessionLevels.ContainsKey(profession)) _data.ProfessionLevels[profession] = 1;
            if (!_data.ProfessionXp.ContainsKey(profession)) _data.ProfessionXp[profession] = 0f;
        }
    }

    private static void NormalizeOrder(SpiritOrderData? order)
    {
        if (order == null) return;
        order.Resource ??= string.Empty;
        order.WorkZone = Position(order.WorkZone);
        order.DeliveryPoint = Position(order.DeliveryPoint);
        order.Quantity = Math.Max(0, order.Quantity);
        order.Collected = Math.Max(0, order.Collected);
        order.Delivered = Math.Max(0, order.Delivered);
    }

    private static float[] Position(float[]? position)
    {
        if (position != null && position.Length >= 3) return position;
        var normalized = new float[3];
        if (position != null) Array.Copy(position, normalized, position.Length);
        return normalized;
    }
}
