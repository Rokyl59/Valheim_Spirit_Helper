using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json;
using SpiritHelper.Core;
using UnityEngine;

namespace SpiritHelper.Resources;

public sealed class ResourceDatabase
{
    private const string EditableDefinitionsFileName = "SpiritHelper.resources.json";
    private const string LearnedDefinitionsFileName = "SpiritHelper.learned-resources.json";
    public const string ItemSelectionPrefix = "item:";
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<ResourceDefinition> _learnedDefinitions = new List<ResourceDefinition>();
    private readonly List<ResourceDefinition> _itemChoices = new List<ResourceDefinition>();
    private readonly Dictionary<string, ResourceDefinition> _itemsByDrop = new Dictionary<string, ResourceDefinition>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResourceDefinition> _harvestTargetsByItem = new Dictionary<string, ResourceDefinition>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResourceDefinition> _targetDefinitionsByPrefab = new Dictionary<string, ResourceDefinition>(StringComparer.OrdinalIgnoreCase);

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
        new ResourceDefinition("Fine Wood", ResourceCategory.Wood, new[]{"Birch1","Birch2","Oak1","Oak_log"}, new[]{"FineWood"}, 1, RequiredToolType.Axe, 0, ResourceDamageType.Chop, 3f, 1f, Profession.Woodcutting),
        new ResourceDefinition("Core Wood", ResourceCategory.Wood, new[]{"PineTree","PineTree_log"}, new[]{"CoreWood","RoundLog"}, 1, RequiredToolType.Axe, 0, ResourceDamageType.Chop, 3f, 1f, Profession.Woodcutting),
        new ResourceDefinition("Wood", ResourceCategory.Wood, new[]{"Beech1","Beech_small1","FirTree","FirTree_oldLog","Birch_log","SwampTree1","SwampTree2","TreeLog","FirTree_log","SwampTree1_log","Beech_Stub","FirTree_Stub","Birch_Stub","Oak_Stub","stubbe","stubbe_spawner"}, new[]{"Wood","ElderBark","YggdrasilWood"}, 1, RequiredToolType.Axe, 0, ResourceDamageType.Chop, 3f, 1f, Profession.Woodcutting),
        new ResourceDefinition("Stone", ResourceCategory.Stone, new[]{"rock1_mountain","Rock_3","Rock_4","Rock_7","MineRock_Stone","rock2_heath","rock2_mountain"}, new[]{"Stone"}, 1, RequiredToolType.Pickaxe, 0, ResourceDamageType.Pickaxe, 4f, 1f, Profession.Mining),
        new ResourceDefinition("Stone", ResourceCategory.Stone, new[]{"Pickable_Stone","Pickable_Stone2"}, new[]{"Stone"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 0.5f, 0.5f, Profession.Gathering),
        new ResourceDefinition("Flint", ResourceCategory.Stone, new[]{"Pickable_Flint"}, new[]{"Flint"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 0.5f, 0.75f, Profession.Gathering),
        new ResourceDefinition("Copper", ResourceCategory.Ore, new[]{"MineRock_Copper","rock4_copper"}, new[]{"CopperOre"}, 5, RequiredToolType.Pickaxe, 1, ResourceDamageType.Pickaxe, 7f, 4f, Profession.Mining, spiritLevel: 5, searchRadius: 40f, workInterval: 1.8f),
        new ResourceDefinition("Tin", ResourceCategory.Ore, new[]{"Pickable_Tin"}, new[]{"TinOre"}, 4, RequiredToolType.None, 0, ResourceDamageType.Interact, 1f, 4f, Profession.Mining),
        new ResourceDefinition("Tin", ResourceCategory.Ore, new[]{"MineRock_Tin"}, new[]{"TinOre"}, 4, RequiredToolType.Pickaxe, 0, ResourceDamageType.Pickaxe, 4f, 4f, Profession.Mining),
        new ResourceDefinition("Silver", ResourceCategory.Ore, new[]{"silvervein","MineRock_Silver"}, new[]{"SilverOre"}, 15, RequiredToolType.Pickaxe, 2, ResourceDamageType.Pickaxe, 10f, 10f, Profession.Mining, spiritLevel: 10, searchRadius: 15f, workInterval: 2.4f, professionXp: 14f),
        new ResourceDefinition("Obsidian", ResourceCategory.Ore, new[]{"MineRock_Obsidian"}, new[]{"Obsidian"}, 17, RequiredToolType.Pickaxe, 2, ResourceDamageType.Pickaxe, 9f, 8f, Profession.Mining),
        new ResourceDefinition("Black Marble", ResourceCategory.Stone, new[]{"blackmarble_1","blackmarble_2","giant_brain_frac"}, new[]{"BlackMarble"}, 20, RequiredToolType.Pickaxe, 3, ResourceDamageType.Pickaxe, 12f, 12f, Profession.Mining, spiritLevel: 20, searchRadius: 20f, workInterval: 2.8f, professionXp: 16f),
        new ResourceDefinition("Berries", ResourceCategory.Food, new[]{"RaspberryBush","BlueberryBush","CloudberryBush"}, new[]{"Raspberry","Blueberries","Cloudberry"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 1f, 2f, Profession.Gathering, searchRadius: 35f, workInterval: 0.55f),
        new ResourceDefinition("Plants", ResourceCategory.Plant, new[]{"Mushroom","MushroomYellow","Pickable_Mushroom","Pickable_Mushroom_yellow","Thistle","Pickable_Thistle","Dandelion","Pickable_Dandelion","Pickable_SeedCarrot","Pickable_SeedTurnip","Pickable_SeedOnion"}, new[]{"Mushroom","MushroomYellow","Thistle","Dandelion","CarrotSeeds","TurnipSeeds","OnionSeeds"}, 1, RequiredToolType.None, 0, ResourceDamageType.Interact, 1f, 2f, Profession.Gathering),
        new ResourceDefinition("Coal", ResourceCategory.Special, Array.Empty<string>(), new[]{"Coal"}, 1, RequiredToolType.None, 0, ResourceDamageType.None, 0f, 0f, Profession.Logistics, canAutoHarvest: false),
        new ResourceDefinition("Copper ingot", ResourceCategory.Special, Array.Empty<string>(), new[]{"Copper"}, 1, RequiredToolType.None, 0, ResourceDamageType.None, 0f, 0f, Profession.Logistics, canAutoHarvest: false),
        new ResourceDefinition("Tin ingot", ResourceCategory.Special, Array.Empty<string>(), new[]{"Tin"}, 1, RequiredToolType.None, 0, ResourceDamageType.None, 0f, 0f, Profession.Logistics, canAutoHarvest: false),
        new ResourceDefinition("Iron ingot", ResourceCategory.Special, Array.Empty<string>(), new[]{"Iron"}, 1, RequiredToolType.None, 0, ResourceDamageType.None, 0f, 0f, Profession.Logistics, canAutoHarvest: false),
        new ResourceDefinition("Silver ingot", ResourceCategory.Special, Array.Empty<string>(), new[]{"Silver"}, 1, RequiredToolType.None, 0, ResourceDamageType.None, 0f, 0f, Profession.Logistics, canAutoHarvest: false),
        new ResourceDefinition("BlackMetal", ResourceCategory.Special, Array.Empty<string>(), new[]{"BlackMetal"}, 1, RequiredToolType.None, 0, ResourceDamageType.None, 0f, 0f, Profession.Logistics, canAutoHarvest: false),
        new ResourceDefinition("FlametalNew", ResourceCategory.Special, Array.Empty<string>(), new[]{"FlametalNew"}, 1, RequiredToolType.None, 0, ResourceDamageType.None, 0f, 0f, Profession.Logistics, canAutoHarvest: false)
    };

    public ResourceDatabase()
    {
        var editablePath = Path.Combine(Paths.ConfigPath, EditableDefinitionsFileName);
        EnsureEditableDefinitions(editablePath);
        LoadCustomDefinitions(editablePath);
        LoadCustomDefinitions(Path.Combine(Paths.ConfigPath, LearnedDefinitionsFileName), true);
    }

    public ResourceDefinition Learn(Component component, RequiredToolType tool, IEnumerable<string>? observedDrops = null, int toolTier = 0) =>
        Learn(component.gameObject.name, tool, component is Pickable, observedDrops, toolTier);

    public ResourceDefinition Learn(string prefabName, RequiredToolType tool, bool isPickable,
        IEnumerable<string>? observedDrops = null, int toolTier = 0)
    {
        var prefab = Clean(prefabName);
        var existing = ByPrefab(prefab);
        if (existing != null) return existing;
        var drops = new HashSet<string>(observedDrops ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var damageType = isPickable ? ResourceDamageType.Interact : tool == RequiredToolType.Axe ? ResourceDamageType.Chop : ResourceDamageType.Pickaxe;
        var category = tool == RequiredToolType.Axe ? ResourceCategory.Wood : isPickable ? ResourceCategory.Special : ResourceCategory.Ore;
        var definition = new ResourceDefinition(prefab, category, new[] { prefab }, drops, 1, tool, Math.Max(0, toolTier), damageType,
            3f, 2f, isPickable ? Profession.Gathering : tool == RequiredToolType.Axe ? Profession.Woodcutting : Profession.Mining,
            canAutoHarvest: drops.Count > 0, canAutoTransport: drops.Count > 0);
        _definitions.Add(definition);
        _learnedDefinitions.Add(definition);
        SaveLearnedDefinitions();
        return definition;
    }
    public IReadOnlyList<ResourceDefinition> All => _definitions;
    public IReadOnlyList<ResourceDefinition> ItemChoices => _itemChoices;
    public ResourceDefinition Wood => _definitions.First(definition => definition.Name == "Wood" && definition.Category == ResourceCategory.Wood);
    public ResourceDefinition? ByPrefab(string prefab) => _definitions.FirstOrDefault(definition => definition.PrefabNames.Contains(Clean(prefab)));
    public ResourceDefinition? ByDrop(string prefab)
    {
        var itemPrefab = Clean(prefab);
        return _itemsByDrop.TryGetValue(itemPrefab, out var item)
            ? item
            : _definitions.FirstOrDefault(definition => definition.DropNames.Contains(itemPrefab));
    }

    public ResourceDefinition? BySelection(string selection) =>
        _itemChoices.FirstOrDefault(item => item.Name.Equals(selection, StringComparison.OrdinalIgnoreCase)) ??
        _definitions.FirstOrDefault(definition => definition.Name.Equals(selection, StringComparison.OrdinalIgnoreCase));

    public void RefreshItemCatalog()
    {
        if (!ObjectDB.instance) return;
        _itemChoices.Clear();
        _itemsByDrop.Clear();
        _harvestTargetsByItem.Clear();
        _targetDefinitionsByPrefab.Clear();
        foreach (var itemPrefab in ObjectDB.instance.m_items)
        {
            if (!itemPrefab || !itemPrefab.TryGetComponent<ItemDrop>(out var itemDrop)) continue;
            var prefabName = Clean(itemPrefab.name);
            if (string.IsNullOrWhiteSpace(prefabName) || _itemsByDrop.ContainsKey(prefabName)) continue;
            var matchingResource = _definitions.FirstOrDefault(definition => definition.DropNames.Contains(prefabName));
            var label = LocalizeItemName(itemDrop.m_itemData.m_shared.m_name, prefabName);
            var item = new ResourceDefinition(ItemSelectionPrefix + prefabName,
                matchingResource?.Category ?? ResourceCategory.Special, Array.Empty<string>(), new[] { prefabName },
                1, RequiredToolType.None, 0, ResourceDamageType.None, 0f, 0f, Profession.Logistics,
                canAutoHarvest: matchingResource?.CanAutoHarvest ?? false, canAutoTransport: true, displayName: label,
                sourceResourceName: matchingResource?.Name);
            _itemChoices.Add(item);
            _itemsByDrop.Add(prefabName, item);
        }
        _itemChoices.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayName, right.DisplayName));
    }
    public ResourceDefinition? ByTarget(Component component)
    {
        if (component is Pickable pickable && pickable.m_itemPrefab &&
            TryGetHarvestTarget(pickable, out var harvested)) return harvested;
        var exact = ByPrefab(component.gameObject.name);
        if (exact != null) return WithActualDrops(component, exact);
        if (component is TreeBase or TreeLog) return WithActualDrops(component, Wood);
        var name = Clean(component.gameObject.name).ToLowerInvariant();
        if (name.Contains("copper")) return WithActualDrops(component, ByName("Copper"));
        if (name.Contains("tin")) return WithActualDrops(component, ByName("Tin"));
        if (name.Contains("silver")) return WithActualDrops(component, ByName("Silver"));
        if (name.Contains("obsidian")) return WithActualDrops(component, ByName("Obsidian"));
        if (name.Contains("blackmarble") || name.Contains("black_marble")) return WithActualDrops(component, ByName("Black Marble"));
        if (component is MineRock or MineRock5 && (name.Contains("rock") || name.Contains("stone"))) return WithActualDrops(component, ByName("Stone", ResourceDamageType.Pickaxe));
        if (component is Pickable && name.Contains("flint")) return WithActualDrops(component, ByName("Flint"));
        if (component is Pickable && (name.Contains("stone") || name.Contains("rock"))) return WithActualDrops(component, ByName("Stone", ResourceDamageType.Interact));
        if (component is Pickable && (name.Contains("raspberry") || name.Contains("blueberry") || name.Contains("cloudberry"))) return WithActualDrops(component, ByName("Berries"));
        if (component is Pickable && (name.Contains("mushroom") || name.Contains("thistle") || name.Contains("dandelion") || name.Contains("seed"))) return WithActualDrops(component, ByName("Plants"));
        return null;
    }

    private ResourceDefinition? WithActualDrops(Component component, ResourceDefinition? source)
    {
        if (source == null) return null;
        var key = Clean(component.gameObject.name) + ":" + source.Name;
        if (_targetDefinitionsByPrefab.TryGetValue(key, out var cached)) return cached;
        var drops = DiscoverDrops(component).ToArray();
        if (drops.Length == 0) return source;
        var target = new ResourceDefinition(source.Name, source.Category, source.PrefabNames, drops,
            source.RequiredProfessionLevel, source.RequiredToolType, source.MinToolTier, source.DamageType,
            source.EnergyCost, source.XpReward, source.Profession, source.RequiredSpiritLevel, source.SearchRadius,
            source.WorkInterval, source.ProfessionXpReward, source.CanAutoHarvest, source.CanAutoTransport,
            source.DisplayName, source.SourceResourceName);
        _targetDefinitionsByPrefab.Add(key, target);
        return target;
    }

    private bool TryGetHarvestTarget(Pickable pickable, out ResourceDefinition definition)
    {
        var itemPrefab = Clean(pickable.m_itemPrefab.name);
        if (_harvestTargetsByItem.TryGetValue(itemPrefab, out definition!)) return true;
        var pickablePrefab = Clean(pickable.gameObject.name);
        var source = _definitions.FirstOrDefault(candidate => candidate.CanAutoHarvest &&
            candidate.PrefabNames.Contains(pickablePrefab) && candidate.DropNames.Contains(itemPrefab)) ??
            _definitions.FirstOrDefault(candidate => candidate.CanAutoHarvest && candidate.DamageType == ResourceDamageType.Interact &&
                candidate.DropNames.Contains(itemPrefab));
        if (source == null)
        {
            definition = null!;
            return false;
        }
        definition = new ResourceDefinition(ItemSelectionPrefix + itemPrefab, source.Category, source.PrefabNames,
            new[] { itemPrefab }, source.RequiredProfessionLevel, source.RequiredToolType, source.MinToolTier,
            source.DamageType, source.EnergyCost, source.XpReward, source.Profession, source.RequiredSpiritLevel,
            source.SearchRadius, source.WorkInterval, source.ProfessionXpReward, canAutoHarvest: true,
            canAutoTransport: source.CanAutoTransport, displayName: ItemDisplayName(itemPrefab), sourceResourceName: source.Name);
        _harvestTargetsByItem.Add(itemPrefab, definition);
        return true;
    }

    private ResourceDefinition? ByName(string name, ResourceDamageType? damageType = null) =>
        _definitions.FirstOrDefault(definition => definition.Name == name && (!damageType.HasValue || definition.DamageType == damageType.Value));

    private static string Clean(string value)
    {
        var separator = value.IndexOfAny(new[] { '(', ' ' });
        return separator >= 0 ? value.Substring(0, separator) : value;
    }

    private static string LocalizeItemName(string localizationKey, string fallback)
    {
        if (string.IsNullOrWhiteSpace(localizationKey)) return fallback;
        return Localization.instance != null ? Localization.instance.Localize(localizationKey) :
            localizationKey.TrimStart('$').Replace('_', ' ');
    }

    private string ItemDisplayName(string itemPrefab) =>
        _itemsByDrop.TryGetValue(itemPrefab, out var item) ? item.DisplayName : itemPrefab;

    private void EnsureEditableDefinitions(string path)
    {
        if (File.Exists(path)) return;
        try { File.WriteAllText(path, JsonConvert.SerializeObject(_definitions.Select(ToCustomDefinition).ToList(), Formatting.Indented)); }
        catch (Exception error) { SpiritHelperPlugin.Log.LogError($"Could not create editable resource definitions: {error.Message}"); }
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
                if (!TryCreateDefinition(resource, out var definition))
                {
                    SpiritHelperPlugin.Log.LogWarning($"Skipped invalid custom resource definition in {Path.GetFileName(path)}.");
                    continue;
                }
                ReplaceDefinition(definition);
                if (learned) _learnedDefinitions.Add(definition);
            }
        }
        catch (Exception error) { SpiritHelperPlugin.Log.LogError($"Could not load custom resource definitions: {error.Message}"); }
    }

    private static bool TryCreateDefinition(CustomResourceDefinition resource, out ResourceDefinition definition)
    {
        definition = null!;
        if (string.IsNullOrWhiteSpace(resource.Name) || resource.Prefabs == null || resource.Drops == null ||
            resource.Prefabs.Length == 0 && resource.Drops.Length == 0 || !Enum.IsDefined(typeof(ResourceCategory), resource.Category) ||
            !Enum.IsDefined(typeof(RequiredToolType), resource.Tool) || !Enum.IsDefined(typeof(ResourceDamageType), resource.DamageType) ||
            !Enum.IsDefined(typeof(Profession), resource.Profession) || resource.RequiredSpiritLevel < 1 || resource.RequiredProfessionLevel < 1 ||
            resource.Tier < 0 || !IsPositiveFinite(resource.SearchRadius) || !IsPositiveFinite(resource.WorkInterval) ||
            !IsNonNegativeFinite(resource.EnergyCost) || !IsNonNegativeFinite(resource.XpReward) || !IsNonNegativeFinite(resource.ProfessionXpReward)) return false;
        var prefabs = resource.Prefabs.Where(value => !string.IsNullOrWhiteSpace(value)).Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var drops = resource.Drops.Where(value => !string.IsNullOrWhiteSpace(value)).Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (prefabs.Length == 0 && drops.Length == 0) return false;
        definition = new ResourceDefinition(resource.Name.Trim(), resource.Category, prefabs, drops, resource.RequiredProfessionLevel,
            resource.Tool, resource.Tier, resource.DamageType, resource.EnergyCost, resource.XpReward, resource.Profession,
            resource.RequiredSpiritLevel, resource.SearchRadius, resource.WorkInterval, resource.ProfessionXpReward,
            resource.CanAutoHarvest, resource.CanAutoTransport);
        return true;
    }

    private void ReplaceDefinition(ResourceDefinition definition)
    {
        var index = _definitions.FindIndex(existing => existing.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) &&
            existing.DamageType == definition.DamageType);
        if (index >= 0) _definitions[index] = definition;
        else _definitions.Add(definition);
    }

    private void SaveLearnedDefinitions()
    {
        try
        {
            var path = Path.Combine(Paths.ConfigPath, LearnedDefinitionsFileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(_learnedDefinitions.Select(ToCustomDefinition).ToList(), Formatting.Indented));
        }
        catch (Exception error) { SpiritHelperPlugin.Log.LogError($"Could not persist learned resource: {error.Message}"); }
    }

    private static CustomResourceDefinition ToCustomDefinition(ResourceDefinition definition) => new CustomResourceDefinition
    {
        Name = definition.Name, Category = definition.Category, Prefabs = definition.PrefabNames.ToArray(), Drops = definition.DropNames.ToArray(),
        RequiredSpiritLevel = definition.RequiredSpiritLevel, RequiredProfessionLevel = definition.RequiredProfessionLevel,
        Tool = definition.RequiredToolType, Tier = definition.MinToolTier, DamageType = definition.DamageType,
        SearchRadius = definition.SearchRadius, WorkInterval = definition.WorkInterval, EnergyCost = definition.EnergyCost,
        XpReward = definition.XpReward, ProfessionXpReward = definition.ProfessionXpReward, Profession = definition.Profession,
        CanAutoHarvest = definition.CanAutoHarvest, CanAutoTransport = definition.CanAutoTransport
    };

    private static IEnumerable<string> DiscoverDrops(Component component)
    {
        var drops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedPrefabs = new HashSet<int>();
        foreach (var source in component.GetComponentsInParent<Component>(true))
        {
            if (!source) continue;
            foreach (var field in source.GetType().GetFields(InstanceFields))
            {
                if (!field.Name.StartsWith("m_drop", StringComparison.Ordinal)) continue;
                CollectDropPrefabs(field.GetValue(source), drops, visitedPrefabs);
            }
            if (source is TreeBase tree && tree.m_logPrefab) CollectPrefabDrops(tree.m_logPrefab, drops, visitedPrefabs);
        }
        return drops;
    }

    private static void CollectDropPrefabs(object? value, ISet<string> drops, ISet<int> visitedPrefabs)
    {
        if (value == null) return;
        if (value is string) return;
        if (value is GameObject prefab)
        {
            var itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop)
            {
                drops.Add(Clean(prefab.name));
                return;
            }
            CollectPrefabDrops(prefab, drops, visitedPrefabs);
            return;
        }
        if (value is IEnumerable entries)
        {
            foreach (var entry in entries) CollectDropPrefabs(entry, drops, visitedPrefabs);
            return;
        }
        var itemField = value.GetType().GetField("m_item", InstanceFields) ?? value.GetType().GetField("m_prefab", InstanceFields);
        var item = itemField?.GetValue(value);
        var prefabName = item switch { GameObject gameObject => gameObject.name, Component drop => drop.gameObject.name, _ => null };
        if (!string.IsNullOrWhiteSpace(prefabName))
        {
            drops.Add(Clean(prefabName));
            return;
        }
        var entriesField = value.GetType().GetField("m_drops", InstanceFields);
        if (entriesField?.GetValue(value) is IEnumerable nestedEntries)
            foreach (var entry in nestedEntries) CollectDropPrefabs(entry, drops, visitedPrefabs);
    }

    private static void CollectPrefabDrops(GameObject prefab, ISet<string> drops, ISet<int> visitedPrefabs)
    {
        if (!visitedPrefabs.Add(prefab.GetInstanceID())) return;
        foreach (var component in prefab.GetComponents<Component>())
        {
            if (!component) continue;
            foreach (var field in component.GetType().GetFields(InstanceFields))
            {
                if (!field.Name.StartsWith("m_drop", StringComparison.Ordinal)) continue;
                CollectDropPrefabs(field.GetValue(component), drops, visitedPrefabs);
            }
        }
    }

    private static bool IsPositiveFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    private static bool IsNonNegativeFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
}
