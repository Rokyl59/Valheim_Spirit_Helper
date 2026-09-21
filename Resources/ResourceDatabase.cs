using System;
using System.Collections.Generic;
using System.Linq;
using SpiritHelper.Core;
using UnityEngine;

namespace SpiritHelper.Resources;

public sealed class ResourceDatabase
{
    private readonly List<ResourceDefinition> _definitions = new List<ResourceDefinition>
    {
        new ResourceDefinition("Wood", ResourceCategory.Wood, new[]{"Beech1","Beech_small1","FirTree","FirTree_oldLog","PineTree","Birch1","Birch2","Oak1","SwampTree1","SwampTree2","TreeLog","FirTree_log","PineTree_log","Birch_log","Oak_log","SwampTree1_log"}, new[]{"Wood","RoundLog","FineWood","ElderBark","YggdrasilWood"}, 1, RequiredToolType.Axe, 0, ResourceDamageType.Chop, 3f, 1f, Profession.Woodcutting),
        new ResourceDefinition("Stone", ResourceCategory.Stone, new[]{"rock1_mountain","Rock_3","Rock_4","Rock_7","MineRock_Stone","rock2_heath","rock2_mountain"}, new[]{"Stone"}, 1, RequiredToolType.Pickaxe, 0, ResourceDamageType.Pickaxe, 4f, 1f, Profession.Mining),
        new ResourceDefinition("Stone", ResourceCategory.Stone, new[]{"Pickable_Stone","Pickable_Stone2"}, new[]{"Stone"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 0.5f, 0.5f, Profession.Gathering),
        new ResourceDefinition("Flint", ResourceCategory.Stone, new[]{"Pickable_Flint"}, new[]{"Flint"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 0.5f, 0.75f, Profession.Gathering),
        new ResourceDefinition("Copper", ResourceCategory.Ore, new[]{"MineRock_Copper","rock4_copper"}, new[]{"CopperOre"}, 5, RequiredToolType.Pickaxe, 1, ResourceDamageType.Pickaxe, 7f, 4f, Profession.Mining),
        new ResourceDefinition("Tin", ResourceCategory.Ore, new[]{"Pickable_Tin","MineRock_Tin"}, new[]{"TinOre"}, 4, RequiredToolType.None, 0, ResourceDamageType.Interact, 1f, 4f, Profession.Mining),
        new ResourceDefinition("Silver", ResourceCategory.Ore, new[]{"silvervein","MineRock_Silver"}, new[]{"SilverOre"}, 15, RequiredToolType.Pickaxe, 2, ResourceDamageType.Pickaxe, 10f, 10f, Profession.Mining),
        new ResourceDefinition("Obsidian", ResourceCategory.Ore, new[]{"MineRock_Obsidian"}, new[]{"Obsidian"}, 17, RequiredToolType.Pickaxe, 2, ResourceDamageType.Pickaxe, 9f, 8f, Profession.Mining),
        new ResourceDefinition("Black Marble", ResourceCategory.Stone, new[]{"blackmarble_1","blackmarble_2","giant_brain_frac"}, new[]{"BlackMarble"}, 20, RequiredToolType.Pickaxe, 3, ResourceDamageType.Pickaxe, 12f, 12f, Profession.Mining),
        new ResourceDefinition("Berries", ResourceCategory.Food, new[]{"RaspberryBush","BlueberryBush","CloudberryBush"}, new[]{"Raspberry","Blueberries","Cloudberry"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 1f, 2f, Profession.Gathering),
        new ResourceDefinition("Plants", ResourceCategory.Plant, new[]{"Mushroom","MushroomYellow","Pickable_Mushroom","Pickable_Mushroom_yellow","Thistle","Pickable_Thistle","Dandelion","Pickable_Dandelion","Pickable_SeedCarrot","Pickable_SeedTurnip","Pickable_SeedOnion"}, new[]{"Mushroom","MushroomYellow","Thistle","Dandelion","CarrotSeeds","TurnipSeeds","OnionSeeds"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 1f, 2f, Profession.Gathering)
    };

    public IReadOnlyList<ResourceDefinition> All => _definitions;
    public ResourceDefinition Wood => _definitions.First(d => d.Category == ResourceCategory.Wood);
    public ResourceDefinition? ByPrefab(string prefab) => _definitions.FirstOrDefault(d => d.PrefabNames.Contains(Clean(prefab)));
    public ResourceDefinition? ByDrop(string prefab) => _definitions.FirstOrDefault(d => d.DropNames.Contains(Clean(prefab)));
    public ResourceDefinition? ByTarget(Component component)
    {
        var exact = ByPrefab(component.gameObject.name);
        if (exact != null) return exact;
        if (component is TreeBase or TreeLog) return Wood;
        var name = Clean(component.gameObject.name).ToLowerInvariant();
        if (name.Contains("copper")) return ByName("Copper");
        if (name.Contains("tin")) return ByName("Tin");
        if (name.Contains("silver")) return ByName("Silver");
        if (name.Contains("obsidian")) return ByName("Obsidian");
        if (name.Contains("blackmarble") || name.Contains("black_marble")) return ByName("Black Marble");
        if (component is MineRock or MineRock5 && (name.Contains("rock") || name.Contains("stone"))) return ByName("Stone", ResourceDamageType.Pickaxe);
        if (component is Pickable && name.Contains("flint")) return ByName("Flint");
        if (component is Pickable && (name.Contains("stone") || name.Contains("rock"))) return ByName("Stone", ResourceDamageType.Interact);
        if (component is Pickable && (name.Contains("raspberry") || name.Contains("blueberry") || name.Contains("cloudberry"))) return ByName("Berries");
        if (component is Pickable && (name.Contains("mushroom") || name.Contains("thistle") || name.Contains("dandelion") || name.Contains("seed"))) return ByName("Plants");
        return null;
    }

    private ResourceDefinition? ByName(string name, ResourceDamageType? damageType = null) =>
        _definitions.FirstOrDefault(d => d.Name == name && (!damageType.HasValue || d.DamageType == damageType.Value));

    private static string Clean(string value)
    {
        var separator = value.IndexOfAny(new[] { '(', ' ' });
        return separator >= 0 ? value.Substring(0, separator) : value;
    }
}
