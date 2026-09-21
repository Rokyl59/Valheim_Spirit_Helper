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
    public bool HasWorkZone;
    public float[] WorkZone = new float[3];
    public WorkZoneMode WorkZoneMode = WorkZoneMode.Circle;
    public ResourceFilterMode ResourceFilterMode = ResourceFilterMode.All;
    public ResourceCategory ResourceCategory = ResourceCategory.Wood;
    public string SelectedResource = string.Empty;
}

public sealed class SpiritProgression
{
    private readonly SpiritSaveData _data;
    public SpiritProgression(SpiritSaveData data) { _data = data; EnsureProfessions(); }
    public SpiritSaveData Data => _data;
    public int Level => _data.SpiritLevel;
    public float Xp => _data.SpiritXp;
    public float NextXp => 150f + (_data.SpiritLevel - 1) * 100f;
    public int ProfessionLevel(Profession profession) => _data.ProfessionLevels[profession];

    public bool Award(float xp, Profession profession, float spiritMultiplier, float professionMultiplier)
    {
        _data.SpiritXp += xp * spiritMultiplier;
        _data.ProfessionXp[profession] += xp * professionMultiplier;
        while (_data.SpiritLevel < 30 && _data.SpiritXp >= NextXp) { _data.SpiritXp -= NextXp; _data.SpiritLevel++; }
        var needed = 100f + (_data.ProfessionLevels[profession] - 1) * 75f;
        if (_data.ProfessionLevels[profession] < 20 && _data.ProfessionXp[profession] >= needed)
        {
            _data.ProfessionXp[profession] -= needed;
            _data.ProfessionLevels[profession]++;
            return true;
        }
        return false;
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
