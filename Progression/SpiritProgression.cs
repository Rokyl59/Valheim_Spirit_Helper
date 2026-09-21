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
}

public sealed class SpiritProgression
{
    private readonly SpiritSaveData _data;
    public SpiritProgression(SpiritSaveData data)
    {
        _data = data;
        _data.Automation ??= new SpiritAutomationData();
        _data.UnlockedTalents ??= new HashSet<string>();
        _data.Journal ??= new List<string>();
        _data.UnloadPoints ??= new List<SavedUnloadPoint>();
        EnsureProfessions();
    }
    public SpiritSaveData Data => _data;
    public int Level => _data.SpiritLevel;
    public float Xp => _data.SpiritXp;
    public float NextXp => 150f + (_data.SpiritLevel - 1) * 100f;
    public int ProfessionLevel(Profession profession) => _data.ProfessionLevels[profession];
    public int AvailableTalentPoints => Math.Max(0, Level / 5 - _data.UnlockedTalents.Count);
    public bool HasTalent(string talent) => _data.UnlockedTalents.Contains(talent);

    public string? UnlockNextTalent(string branch)
    {
        if (AvailableTalentPoints <= 0) return null;
        for (var tier = 1; tier <= 4; tier++)
        {
            var talent = $"{branch}.{tier}";
            if (_data.UnlockedTalents.Add(talent)) return talent;
        }
        return null;
    }

    public bool Award(float spiritXp, float professionXp, Profession profession, float spiritMultiplier, float professionMultiplier)
    {
        var previousLevel = _data.SpiritLevel;
        _data.SpiritXp += spiritXp * spiritMultiplier;
        _data.ProfessionXp[profession] += professionXp * professionMultiplier;
        while (_data.SpiritLevel < 30 && _data.SpiritXp >= NextXp) { _data.SpiritXp -= NextXp; _data.SpiritLevel++; }
        var needed = 100f + (_data.ProfessionLevels[profession] - 1) * 75f;
        if (_data.ProfessionLevels[profession] < 20 && _data.ProfessionXp[profession] >= needed)
        {
            _data.ProfessionXp[profession] -= needed;
            _data.ProfessionLevels[profession]++;
            return true;
        }
        return _data.SpiritLevel > previousLevel;
    }

    private void EnsureProfessions()
    {
        foreach (Profession profession in Enum.GetValues(typeof(Profession)))
        {
            if (!_data.ProfessionLevels.ContainsKey(profession)) _data.ProfessionLevels[profession] = 1;
            if (!_data.ProfessionXp.ContainsKey(profession)) _data.ProfessionXp[profession] = 0f;
        }
    }
}
