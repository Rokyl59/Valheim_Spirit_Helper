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
            var data = File.Exists(source) ? JsonConvert.DeserializeObject<SpiritSaveData>(File.ReadAllText(source)) ?? new SpiritSaveData() : new SpiritSaveData();
            SpiritProgression.Normalize(data);
            return data;
        }
        catch (Exception error)
        {
            SpiritHelperPlugin.Log.LogError($"Could not load Spirit Helper save: {error.Message}");
            var data = new SpiritSaveData();
            SpiritProgression.Normalize(data);
            return data;
        }
    }
    public void Save(string saveKey, SpiritSaveData data)
    {
        var path = GetPath(saveKey);
        var temporaryPath = path + ".tmp";
        var backupPath = path + ".bak";
        try
        {
            SpiritProgression.Normalize(data);
            if (File.Exists(path) && !CanDeserialize(path))
            {
                SpiritHelperPlugin.Log.LogError("Could not save Spirit Helper progress because the existing save is malformed. The original file was preserved.");
                return;
            }
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(data, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporaryPath, path, backupPath, true);
            else File.Move(temporaryPath, path);
        }
        catch (Exception error) { SpiritHelperPlugin.Log.LogError($"Could not save Spirit Helper progress: {error.Message}"); }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception error) { SpiritHelperPlugin.Log.LogWarning($"Could not remove temporary Spirit Helper save: {error.Message}"); }
        }
    }

    private static bool CanDeserialize(string path)
    {
        try { return JsonConvert.DeserializeObject<SpiritSaveData>(File.ReadAllText(path)) != null; }
        catch (Exception) { return false; }
    }

    private static string GetPath(string saveKey) => Path.Combine(Paths.ConfigPath, $"SpiritHelper.{saveKey}.save.json");
}
