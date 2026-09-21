namespace SpiritHelper.Resources;

public enum ResourceFilterMode { All, Category, Exact }

public sealed class ResourceSelection
{
    public ResourceFilterMode Mode { get; private set; } = ResourceFilterMode.All;
    public ResourceCategory Category { get; private set; }
    public string ExactName { get; private set; } = string.Empty;

    public bool Allows(ResourceDefinition definition) => Mode switch
    {
        ResourceFilterMode.Category => definition.Category == Category,
        ResourceFilterMode.Exact => definition.Name == ExactName,
        _ => true
    };

    public void AllowAll() { Mode = ResourceFilterMode.All; ExactName = string.Empty; }
    public void SelectCategory(ResourceCategory category) { Mode = ResourceFilterMode.Category; Category = category; ExactName = string.Empty; }
    public void SelectExact(string name) { Mode = ResourceFilterMode.Exact; ExactName = name; }
    public void Restore(ResourceFilterMode mode, ResourceCategory category, string exactName)
    {
        Mode = mode; Category = category; ExactName = exactName ?? string.Empty;
    }
}
