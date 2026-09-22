using System;
using HarmonyLib;
using UnityEngine;

namespace SpiritHelper.Core;

public readonly struct SpiritRemotePing
{
    public SpiritRemotePing(Vector3 position, long sender)
    {
        Position = position;
        Sender = sender;
        ReceivedAt = Time.unscaledTime;
    }

    public Vector3 Position { get; }
    public long Sender { get; }
    public float ReceivedAt { get; }
}

public sealed class SpiritInputBridge : IDisposable
{
    private const string HarmonyId = "com.danil.spirithelper.inputbridge";
    private const float LocalPingEchoWindow = 1f;
    private const float LocalPingPositionTolerance = 0.01f;

    private readonly Harmony _harmony = new Harmony(HarmonyId);
    private bool _initialized;
    private bool _disposed;
    private static Vector3 _lastLocalPingPosition;
    private static float _lastLocalPingAt = float.NegativeInfinity;

    public static Func<bool>? MenuIsOpen { get; set; }
    public static Action<Vector3, bool>? PingReceived { get; set; }
    public static SpiritRemotePing? LastRemotePing { get; private set; }

    public void Initialize()
    {
        if (_initialized || _disposed) return;
        try
        {
            Patch(nameof(Chat), AccessTools.Method(typeof(Chat), nameof(Chat.SendPing), new[] { typeof(Vector3) }),
                nameof(SendPingPrefix));
            Patch(nameof(Chat), AccessTools.Method(typeof(Chat), "RPC_ChatMessage",
                    new[] { typeof(long), typeof(Vector3), typeof(int), typeof(UserInfo), typeof(string) }),
                nameof(ChatMessagePrefix));
            Patch(nameof(PlayerController), AccessTools.Method(typeof(PlayerController), "TakeInput", new[] { typeof(bool) }),
                nameof(TakeInputPrefix));
            Patch(nameof(GameCamera), AccessTools.Method(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture), Type.EmptyTypes),
                nameof(UpdateMouseCapturePrefix));
            _initialized = true;
        }
        catch
        {
            _harmony.UnpatchSelf();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized) _harmony.UnpatchSelf();
        _initialized = false;
        MenuIsOpen = null;
        PingReceived = null;
        LastRemotePing = null;
    }

    public static void ClearRemotePing() => LastRemotePing = null;

    private void Patch(string typeName, System.Reflection.MethodInfo? original, string prefixName)
    {
        if (original == null) throw new MissingMethodException(typeName, prefixName);
        _harmony.Patch(original, prefix: new HarmonyMethod(typeof(SpiritInputBridge), prefixName));
    }

    private static void SendPingPrefix(Vector3 position)
    {
        _lastLocalPingPosition = position;
        _lastLocalPingAt = Time.unscaledTime;
        PingReceived?.Invoke(position, true);
    }

    private static void ChatMessagePrefix(long sender, Vector3 position, int type, UserInfo userInfo, string text)
    {
        if (type != (int)Talker.Type.Ping) return;
        if (Time.unscaledTime - _lastLocalPingAt <= LocalPingEchoWindow &&
            Vector3.SqrMagnitude(position - _lastLocalPingPosition) <= LocalPingPositionTolerance * LocalPingPositionTolerance)
            return;
        LastRemotePing = new SpiritRemotePing(position, sender);
        PingReceived?.Invoke(position, false);
    }

    private static bool TakeInputPrefix(ref bool __result)
    {
        if (!IsMenuOpen()) return true;
        __result = false;
        return false;
    }

    private static bool UpdateMouseCapturePrefix()
    {
        if (!IsMenuOpen()) return true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        return false;
    }

    private static bool IsMenuOpen() => MenuIsOpen?.Invoke() == true;
}
