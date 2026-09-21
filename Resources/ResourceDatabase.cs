using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using SpiritHelper.Core;
using UnityEngine;

namespace SpiritHelper.Resources;

public sealed class ResourceDatabase
{
    private readonly List<ResourceDefinition> _learnedDefinitions = new List<ResourceDefinition>();
    private sealed class CustomResourceDefinition
    {
        public string Name { get; set; } = string.Empty;
        public ResourceCategory Category { get; set; }
        public string[] Prefabs { get; set; } = Array.Empty<string>();
        public string[] Drops { get; set; } = Array.Empty<string>();
        public int RequiredSpiritLevel { get; set; } = 1;
        public int RequiredProfessionLevel { get; set; } = 1;
        public RequiredToolType Tool { get; set; }
        public int Tier { get; set; }
        public ResourceDamageType DamageType { get; set; }
        public float SearchRadius { get; set; } = 50f;
        public float WorkInterval { get; set; } = 1.5f;
        public float EnergyCost { get; set; } = 1f;
        public float XpReward { get; set; } = 1f;
        public float ProfessionXpReward { get; set; } = 1f;
        public Profession Profession { get; set; }
        public bool CanAutoHarvest { get; set; } = true;
        public bool CanAutoTransport { get; set; } = true;
    }

    private readonly List<ResourceDefinition> _definitions = new List<ResourceDefinition>
    {
        new ResourceDefinition("Wood", ResourceCategory.Wood, new[]{"Beech1","Beech_small1","FirTree","FirTree_oldLog","PineTree","Birch1","Birch2","Oak1","SwampTree1","SwampTree2","TreeLog","FirTree_log","PineTree_log","Birch_log","Oak_log","SwampTree1_log","Beech_Stub","FirTree_Stub","PineTree_Stub","Birch_Stub","Oak_Stub","stubbe","stubbe_spawner"}, new[]{"Wood","RoundLog","FineWood","ElderBark","YggdrasilWood"}, 1, RequiredToolType.Axe, 0, ResourceDamageType.Chop, 3f, 1f, Profession.Woodcutting),
        new ResourceDefinition("Stone", ResourceCategory.Stone, new[]{"rock1_mountain","Rock_3","Rock_4","Rock_7","MineRock_Stone","rock2_heath","rock2_mountain"}, new[]{"Stone"}, 1, RequiredToolType.Pickaxe, 0, ResourceDamageType.Pickaxe, 4f, 1f, Profession.Mining),
        new ResourceDefinition("Stone", ResourceCategory.Stone, new[]{"Pickable_Stone","Pickable_Stone2"}, new[]{"Stone"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 0.5f, 0.5f, Profession.Gathering),
        new ResourceDefinition("Flint", ResourceCategory.Stone, new[]{"Pickable_Flint"}, new[]{"Flint"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 0.5f, 0.75f, Profession.Gathering),
        new ResourceDefinition("Copper", ResourceCategory.Ore, new[]{"MineRock_Copper","rock4_copper"}, new[]{"CopperOre"}, 5, RequiredToolType.Pickaxe, 1, ResourceDamageType.Pickaxe, 7f, 4f, Profession.Mining, spiritLevel: 5, searchRadius: 40f, workInterval: 1.8f),
        new ResourceDefinition("Tin", ResourceCategory.Ore, new[]{"Pickable_Tin","MineRock_Tin"}, new[]{"TinOre"}, 4, RequiredToolType.None, 0, ResourceDamageType.Interact, 1f, 4f, Profession.Mining),
        new ResourceDefinition("Silver", ResourceCategory.Ore, new[]{"silvervein","MineRock_Silver"}, new[]{"SilverOre"}, 15, RequiredToolType.Pickaxe, 2, ResourceDamageType.Pickaxe, 10f, 10f, Profession.Mining, spiritLevel: 10, searchRadius: 15f, workInterval: 2.4f, professionXp: 14f),
        new ResourceDefinition("Obsidian", ResourceCategory.Ore, new[]{"MineRock_Obsidian"}, new[]{"Obsidian"}, 17, RequiredToolType.Pickaxe, 2, ResourceDamageType.Pickaxe, 9f, 8f, Profession.Mining),
        new ResourceDefinition("Black Marble", ResourceCategory.Stone, new[]{"blackmarble_1","blackmarble_2","giant_brain_frac"}, new[]{"BlackMarble"}, 20, RequiredToolType.Pickaxe, 3, ResourceDamageType.Pickaxe, 12f, 12f, Profession.Mining, spiritLevel: 20, searchRadius: 20f, workInterval: 2.8f, professionXp: 16f),
        new ResourceDefinition("Berries", ResourceCategory.Food, new[]{"RaspberryBush","BlueberryBush","CloudberryBush"}, new[]{"Raspberry","Blueberries","Cloudberry"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 1f, 2f, Profession.Gathering, searchRadius: 35f, workInterval: 0.55f),
        new ResourceDefinition("Plants", ResourceCategory.Plant, new[]{"Mushroom","MushroomYellow","Pickable_Mushroom","Pickable_Mushroom_yellow","Thistle","Pickable_Thistle","Dandelion","Pickable_Dandelion","Pickable_SeedCarrot","Pickable_SeedTurnip","Pickable_SeedOnion"}, new[]{"Mushroom","MushroomYellow","Thistle","Dandelion","CarrotSeeds","TurnipSeeds","OnionSeeds"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 1f, 2f, Profession.Gathering)
    };

    public ResourceDatabase()
    {
        LoadCustomDefinitions(Path.Combine(Paths.ConfigPath, "SpiritHelper.resources.json"));
        LoadCustomDefinitions(Path.Combine(Paths.ConfigPath, "SpiritHelper.learned-resources.json"), true);
    }

    public ResourceDefinition Learn(Component component, RequiredToolType tool)
    {
        var prefab = Clean(component.gameObject.name);
        var existing = ByPrefab(prefab);
        if (existing != null) return existing;
        var damageType = component is Pickable ? ResourceDamageType.Interact :
            tool == RequiredToolType.Axe ? ResourceDamageType.Chop : ResourceDamageType.Pickaxe;
        var category = tool == RequiredToolType.Axe ? ResourceCategory.Wood :
            component is Pickable ? ResourceCategory.Special : ResourceCategory.Ore;
        var definition = new ResourceDefinition(prefab, category, new[] { prefab }, Array.Empty<string>(), 1,
            tool, 0, damageType, 3f, 2f, component is Pickable ? Profession.Gathering :
                tool == RequiredToolType.Axe ? Profession.Woodcutting : Profession.Mining);
        _definitions.Add(definition);
        _learnedDefinitions.Add(definition);
        SaveLearnedDefinitions();
        return definition;
    }

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

    private void LoadCustomDefinitions(string path, bool learned = false)
    {
        if (!File.Exists(path)) return;
        try
        {
            var custom = JsonConvert.DeserializeObject<List<CustomResourceDefinition>>(File.ReadAllText(path));
            if (custom == null) return;
            foreach (var resource in custom)
            {
                if (string.IsNullOrWhiteSpace(resource.Name) || resource.Prefabs.Length == 0 && resource.Drops.Length == 0)
                {
                    SpiritHelperPlugin.Log.LogWarning("Skipped invalid custom resource definition.");
                    continue;
                }
                var definition = new ResourceDefinition(resource.Name.Trim(), resource.Category, resource.Prefabs,
                    resource.Drops, resource.RequiredProfessionLevel, resource.Tool, resource.Tier, resource.DamageType,
                    resource.EnergyCost, resource.XpReward, resource.Profession, resource.RequiredSpiritLevel,
                    resource.SearchRadius, resource.WorkInterval, resource.ProfessionXpReward,
                    resource.CanAutoHarvest, resource.CanAutoTransport);
                _definitions.Add(definition);
                if (learned) _learnedDefinitions.Add(definition);
            }
        }
        catch (Exception error)
        {
            SpiritHelperPlugin.Log.LogError($"Could not load custom resource definitions: {error.Message}");
        }
    }

    private void SaveLearnedDefinitions()
    {
        try
        {
            var learnedPath = Path.Combine(Paths.ConfigPath, "SpiritHelper.learned-resources.json");
            var definitions = _learnedDefinitions.Select(definition => new CustomResourceDefinition
            {
                Name = definition.Name,
                Category = definition.Category,
                Prefabs = definition.PrefabNames.ToArray(),
                Drops = definition.DropNames.ToArray(),
                RequiredSpiritLevel = definition.RequiredSpiritLevel,
                RequiredProfessionLevel = definition.RequiredProfessionLevel,
                Tool = definition.RequiredToolType,
                Tier = definition.MinToolTier,
                DamageType = definition.DamageType,
                SearchRadius = definition.SearchRadius,
                WorkInterval = definition.WorkInterval,
                EnergyCost = definition.EnergyCost,
                XpReward = definition.XpReward,
                ProfessionXpReward = definition.ProfessionXpReward,
                Profession = definition.Profession,
                CanAutoHarvest = definition.CanAutoHarvest,
                CanAutoTransport = definition.CanAutoTransport
            }).ToList();
            File.WriteAllText(learnedPath, JsonConvert.SerializeObject(definitions, Formatting.Indented));
        }
        catch (Exception error)
        {
            SpiritHelperPlugin.Log.LogError($"Could not persist learned resource: {error.Message}");
        }
    }
}
