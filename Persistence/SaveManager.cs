using System;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using SpiritHelper.Progression;

namespace SpiritHelper.Persistence;

public sealed class SaveManager
{
    private readonly string _legacyPath = Path.Combine(Paths.ConfigPath, "SpiritHelper.save.json");
    public SpiritSaveData Load(string saveKey)
    {
        var path = GetPath(saveKey);
        try
        {
            var source = File.Exists(path) ? path : _legacyPath;
            return File.Exists(source) ? JsonConvert.DeserializeObject<SpiritSaveData>(File.ReadAllText(source)) ?? new SpiritSaveData() : new SpiritSaveData();
        }
        catch (Exception error) { SpiritHelperPlugin.Log.LogError($"Could not load Spirit Helper save: {error.Message}"); return new SpiritSaveData(); }
    }
    public void Save(string saveKey, SpiritSaveData data)
    {
        try { File.WriteAllText(GetPath(saveKey), JsonConvert.SerializeObject(data, Formatting.Indented)); }
        catch (Exception error) { SpiritHelperPlugin.Log.LogError($"Could not save Spirit Helper progress: {error.Message}"); }
    }

    private static string GetPath(string saveKey) => Path.Combine(Paths.ConfigPath, $"SpiritHelper.{saveKey}.save.json");
}
