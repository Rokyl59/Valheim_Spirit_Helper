using BepInEx;
using BepInEx.Logging;
using SpiritHelper.Core;
using UnityEngine;

namespace SpiritHelper;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class SpiritHelperPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.danil.spirithelper";
    public const string PluginName = "Spirit Helper";
    public const string PluginVersion = "0.5.0";
    internal static ManualLogSource Log = null!;
    private SpiritController? _controller;
    private readonly SpiritInputBridge _inputBridge = new SpiritInputBridge();

    private void Awake()
    {
        Log = Logger;
        var config = new SpiritConfig(Config);
        _controller = gameObject.AddComponent<SpiritController>();
        _controller.Initialize(config);
        SpiritInputBridge.MenuIsOpen = () => _controller != null && _controller.MenuOpen;
        SpiritInputBridge.PingReceived = _controller.HandleSpiritPing;
        _inputBridge.Initialize();
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded; no custom network objects registered.");
    }

    private void OnDestroy()
    {
        _inputBridge.Dispose();
        _controller?.Shutdown();
    }
}
