using System.Collections.Generic;
using SpiritHelper.Core;
using UnityEngine;

namespace SpiritHelper.Audio;

public enum SpiritAudioEvent { Spawn, FollowStart, IdleAmbient, Recall, WorkStart, WoodHit, MiningHitStone, MiningHitOre, GatherPlant, PickupDrop, CarryStart, Unload, JobComplete, TargetFound, DangerDetected, LowEnergy, EnergyEmpty, EnergyRestored, NoTool, InvalidTarget, InvalidZone, SpiritStuck, MenuOpen, MenuClose, MenuSelect, UnloadPointPlaced, UnloadPointRemoved, WorkZonePlaced, SpiritLevelUp, ProfessionLevelUp, TalentUnlocked, ScoutStart, ScanPulse, GuardEnable, RestStart, RestEnd }

public sealed class SpiritAudioController
{
    private readonly Transform _origin;
    private readonly bool _enabled;
    private readonly float _volume;
    private readonly bool _ambientEnabled;
    private readonly float _ambientVolume;
    private readonly float _ambientIntervalMin;
    private readonly float _ambientIntervalMax;
    private readonly AudioSource[] _sources;
    private readonly Dictionary<SpiritAudioEvent, AudioClip[]> _clips = new Dictionary<SpiritAudioEvent, AudioClip[]>();
    private readonly Dictionary<SpiritAudioEvent, float> _lastPlayed = new Dictionary<SpiritAudioEvent, float>();
    private readonly List<AudioClip> _proceduralClips = new List<AudioClip>();
    private int _next;
    private float _nextAmbientAt;
    private bool _disposed;
    public SpiritAudioController(Transform origin, bool enabled, float volume, bool ambientEnabled,
        float ambientVolume, float ambientIntervalMin, float ambientIntervalMax, int poolSize = 8)
    {
        _origin = origin; _enabled = enabled; _volume = volume; _ambientEnabled = ambientEnabled;
        _ambientVolume = ambientVolume; _ambientIntervalMin = Mathf.Max(2f, ambientIntervalMin);
        _ambientIntervalMax = Mathf.Max(_ambientIntervalMin, ambientIntervalMax); _sources = new AudioSource[poolSize];
        for (var i = 0; i < poolSize; i++) { _sources[i] = origin.gameObject.AddComponent<AudioSource>(); _sources[i].spatialBlend = 1f; _sources[i].minDistance = 2f; _sources[i].maxDistance = 25f; }
        ScheduleAmbient();
    }
    public void Register(SpiritAudioEvent audioEvent, params AudioClip[] variants) => _clips[audioEvent] = variants;
    public void RegisterProceduralDefaults()
    {
        var soft = new[] { CreateVoice("SpiritSoft1", 660f, 820f, 0.14f, 0.12f, 0.02f), CreateVoice("SpiritSoft2", 720f, 610f, 0.16f, 0.1f, 0.03f) };
        var work = new[] { CreateVoice("SpiritWork1", 420f, 330f, 0.1f, 0.18f, 0.08f), CreateVoice("SpiritWork2", 350f, 470f, 0.09f, 0.15f, 0.1f) };
        var warning = new[] { CreateVoice("SpiritWarning1", 260f, 160f, 0.22f, 0.2f, 0.14f), CreateVoice("SpiritWarning2", 330f, 190f, 0.18f, 0.17f, 0.12f) };
        var joy = new[] { CreateVoice("SpiritJoy1", 520f, 1040f, 0.3f, 0.14f, 0.025f), CreateVoice("SpiritJoy2", 680f, 1280f, 0.22f, 0.12f, 0.02f) };
        var discovery = new[] { CreateVoice("SpiritDiscovery1", 440f, 880f, 0.24f, 0.12f, 0.04f), CreateVoice("SpiritDiscovery2", 560f, 960f, 0.2f, 0.11f, 0.035f) };
        var fatigue = new[] { CreateVoice("SpiritFatigue1", 440f, 260f, 0.32f, 0.1f, 0.025f), CreateVoice("SpiritFatigue2", 360f, 210f, 0.36f, 0.09f, 0.03f) };
        foreach (SpiritAudioEvent audioEvent in System.Enum.GetValues(typeof(SpiritAudioEvent)))
            Register(audioEvent, audioEvent is SpiritAudioEvent.NoTool or SpiritAudioEvent.InvalidTarget or SpiritAudioEvent.InvalidZone or SpiritAudioEvent.SpiritStuck ? warning : audioEvent is SpiritAudioEvent.WoodHit or SpiritAudioEvent.MiningHitOre or SpiritAudioEvent.MiningHitStone ? work : soft);
        Register(SpiritAudioEvent.SpiritLevelUp, joy);
        Register(SpiritAudioEvent.ProfessionLevelUp, joy);
        Register(SpiritAudioEvent.JobComplete, joy);
        Register(SpiritAudioEvent.TargetFound, discovery);
        Register(SpiritAudioEvent.ScanPulse, discovery);
        Register(SpiritAudioEvent.DangerDetected, warning);
        Register(SpiritAudioEvent.LowEnergy, fatigue);
        Register(SpiritAudioEvent.EnergyEmpty, fatigue);
        Register(SpiritAudioEvent.RestStart, fatigue);
        Register(SpiritAudioEvent.IdleAmbient,
            CreateVoice("SpiritAmbient1", 740f, 890f, 0.32f, 0.08f, 0.015f),
            CreateVoice("SpiritAmbient2", 880f, 760f, 0.24f, 0.065f, 0.02f),
            CreateVoice("SpiritAmbient3", 560f, 670f, 0.38f, 0.075f, 0.025f));
    }
    public void Tick(SpiritState state)
    {
        if (_disposed || !_origin || !_enabled || !_ambientEnabled || Time.unscaledTime < _nextAmbientAt) return;
        if (state is SpiritState.Idle or SpiritState.FollowPlayer or SpiritState.Rest or SpiritState.SearchTarget)
            Play(SpiritAudioEvent.IdleAmbient, 2f, _ambientVolume);
        ScheduleAmbient();
    }
    public void Play(SpiritAudioEvent audioEvent, float cooldown = 0.5f, float volumeMultiplier = 1f)
    {
        if (_disposed || !_origin || !_enabled || !_clips.TryGetValue(audioEvent, out var variants) || variants.Length == 0) return;
        if (_lastPlayed.TryGetValue(audioEvent, out var last) && Time.unscaledTime - last < cooldown) return;
        var source = _sources[_next++ % _sources.Length];
        source.transform.position = _origin.position; source.pitch = Random.Range(0.95f, 1.05f); source.volume = _volume * volumeMultiplier;
        source.PlayOneShot(variants[Random.Range(0, variants.Length)]); _lastPlayed[audioEvent] = Time.unscaledTime;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var source in _sources)
        {
            if (!source) continue;
            source.Stop();
            Object.Destroy(source);
        }
        foreach (var clip in _proceduralClips)
            if (clip) Object.Destroy(clip);
        _proceduralClips.Clear();
        _clips.Clear();
        _lastPlayed.Clear();
    }

    private void ScheduleAmbient() => _nextAmbientAt = Time.unscaledTime + Random.Range(_ambientIntervalMin, _ambientIntervalMax);

    private AudioClip CreateVoice(string name, float startFrequency, float endFrequency, float duration,
        float amplitude, float noiseAmount)
    {
        const int sampleRate = 44100;
        var samples = Mathf.CeilToInt(sampleRate * duration);
        var data = new float[samples];
        var phase = 0f;
        for (var index = 0; index < samples; index++)
        {
            var progress = index / (float)samples;
            var attack = Mathf.Clamp01(progress / 0.08f);
            var release = Mathf.Pow(1f - progress, 1.8f);
            var frequency = Mathf.Lerp(startFrequency, endFrequency, progress * progress);
            phase += 2f * Mathf.PI * frequency / sampleRate;
            var sine = Mathf.Sin(phase);
            var triangle = 2f * Mathf.Asin(sine) / Mathf.PI;
            var noise = Random.Range(-1f, 1f) * noiseAmount;
            data[index] = (sine * 0.65f + triangle * 0.3f + noise) * amplitude * attack * release;
        }
        var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        _proceduralClips.Add(clip);
        return clip;
    }
}
