using System;
using System.Collections.Generic;
using SpiritHelper.Core;

namespace SpiritHelper.Resources;

public enum ResourceCategory { Wood, Stone, Ore, Plant, Food, Rare, Special }
public enum RequiredToolType { None, Axe, Pickaxe }
public enum ResourceDamageType { None, Chop, Pickaxe, Interact }

public sealed class ResourceDefinition
{
    public string Name { get; }
    public ResourceCategory Category { get; }
    public HashSet<string> PrefabNames { get; }
    public HashSet<string> DropNames { get; }
    public int RequiredSpiritLevel { get; }
    public int RequiredProfessionLevel { get; }
    public RequiredToolType RequiredToolType { get; }
    public int MinToolTier { get; }
    public ResourceDamageType DamageType { get; }
    public float SearchRadius { get; }
    public float WorkInterval { get; }
    public float EnergyCost { get; }
    public float XpReward { get; }
    public float ProfessionXpReward { get; }
    public Profession Profession { get; }
    public bool CanAutoHarvest { get; }
    public bool CanAutoTransport { get; }

    public ResourceDefinition(string name, ResourceCategory category, IEnumerable<string> prefabs,
        IEnumerable<string> drops, int professionLevel, RequiredToolType tool, int tier,
        ResourceDamageType damageType, float energyCost, float xp, Profession profession,
        int spiritLevel = 1, float searchRadius = 50f, float workInterval = 1.5f,
        float? professionXp = null, bool canAutoHarvest = true, bool canAutoTransport = true)
    {
        Name = name;
        Category = category;
        PrefabNames = new HashSet<string>(prefabs, StringComparer.OrdinalIgnoreCase);
        DropNames = new HashSet<string>(drops, StringComparer.OrdinalIgnoreCase);
        RequiredSpiritLevel = spiritLevel;
        RequiredProfessionLevel = professionLevel;
        RequiredToolType = tool;
        MinToolTier = tier;
        DamageType = damageType;
        SearchRadius = searchRadius;
        WorkInterval = workInterval;
        EnergyCost = energyCost;
        XpReward = xp;
        ProfessionXpReward = professionXp ?? xp;
        Profession = profession;
        CanAutoHarvest = canAutoHarvest;
        CanAutoTransport = canAutoTransport;
    }
}
