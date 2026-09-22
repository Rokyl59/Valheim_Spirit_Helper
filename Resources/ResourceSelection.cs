using System;

namespace SpiritHelper.Resources;

public enum ResourceFilterMode { All, Category, Exact }

public sealed class ResourceSelection
{
    public ResourceFilterMode Mode { get; private set; } = ResourceFilterMode.All;
    public ResourceCategory Category { get; private set; }
    public string ExactName { get; private set; } = string.Empty;

    public bool Allows(ResourceDefinition definition)
    {
        if (Mode == ResourceFilterMode.Category) return definition.Category == Category;
        if (Mode != ResourceFilterMode.Exact) return true;
        if (ExactName.StartsWith(ResourceDatabase.ItemSelectionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var itemPrefab = ExactName.Substring(ResourceDatabase.ItemSelectionPrefix.Length);
            return definition.DropNames.Contains(itemPrefab);
        }
        return definition.Name.Equals(ExactName, StringComparison.OrdinalIgnoreCase) ||
            definition.SourceResourceName.Equals(ExactName, StringComparison.OrdinalIgnoreCase);
    }

    public void AllowAll() { Mode = ResourceFilterMode.All; ExactName = string.Empty; }
    public void SelectCategory(ResourceCategory category) { Mode = ResourceFilterMode.Category; Category = category; ExactName = string.Empty; }
    public void SelectExact(string name) { Mode = ResourceFilterMode.Exact; ExactName = name; }
    public void Restore(ResourceFilterMode mode, ResourceCategory category, string exactName)
    {
        Mode = mode; Category = category; ExactName = exactName ?? string.Empty;
    }
}
