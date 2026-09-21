using System.Collections.Generic;
using SpiritHelper.Core;
using UnityEngine;

namespace SpiritHelper.Visuals;

public enum SpiritEmotion { Neutral, Curious, Happy, Frustrated, Afraid, Alert, Resting, Mourning }

public sealed class SpiritVisualController
{
    private const float CollisionRadius = 0.28f;
    private readonly SpiritConfig _config;
    private readonly GameObject _root;
    private readonly Renderer _renderer;
    private readonly TrailRenderer _trail;
    private readonly Light _spiritLight;
    private readonly GameObject _cameraLightRoot;
    private readonly Light _cameraLight;
    private readonly ParticleSystem? _particles;
    private Vector3 _velocity;
    private Vector3 _cameraLightVelocity;
    private GameObject? _levelRing;
    private readonly List<GameObject> _satellites = new List<GameObject>();
    private SpiritEmotion _emotion;
    private float _emotionUntil;
    private float _nextIdleGesture;

    public Transform Transform => _root.transform;
    public SpiritLightMode LightMode => _config.LightMode.Value;

    public SpiritVisualController(SpiritConfig config)
    {
        _config = config;
        _root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _root.name = "SpiritHelper_LocalSpirit";
        Object.Destroy(_root.GetComponent<Collider>());
        _renderer = _root.GetComponent<Renderer>();
        _renderer.material = new Material(Shader.Find("Sprites/Default"));
        _spiritLight = _root.AddComponent<Light>();
        _trail = _root.AddComponent<TrailRenderer>();
        _trail.time = 0.65f; _trail.startWidth = 0.22f; _trail.endWidth = 0f; _trail.material = _renderer.material;

        if (config.EnableParticles.Value)
        {
            _particles = _root.AddComponent<ParticleSystem>();
            var main = _particles.main; main.startLifetime = 1f; main.startSpeed = 0.25f; main.startSize = 0.08f;
            var emission = _particles.emission; emission.rateOverTime = 12f;
            var shape = _particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.25f;
        }

        _cameraLightRoot = new GameObject("SpiritHelper_CameraLight");
        _cameraLight = _cameraLightRoot.AddComponent<Light>();
        _cameraLight.type = LightType.Point;
        ApplyLighting(false);
    }

    public void Spawn(Vector3 position)
    {
        _root.SetActive(true);
        _root.transform.position = position;
        _velocity = Vector3.zero;
        _nextIdleGesture = Time.time + Random.Range(3f, 7f);
    }

    public void Move(Vector3 destination, float speed, float deltaTime)
    {
        destination = AvoidObstacles(_root.transform.position, destination);
        _root.transform.position = Vector3.SmoothDamp(_root.transform.position, destination, ref _velocity,
            _config.MovementInertia.Value, speed, deltaTime);
    }

    private static Vector3 AvoidObstacles(Vector3 origin, Vector3 destination)
    {
        var offset = destination - origin;
        var distance = offset.magnitude;
        if (distance < 0.05f) return destination;
        var direction = offset / distance;
        if (!Physics.SphereCast(origin, CollisionRadius, direction, out var hit, Mathf.Min(distance, 3f),
                Physics.AllLayers, QueryTriggerInteraction.Ignore)) return destination;

        var tangent = Vector3.Cross(hit.normal, Vector3.up);
        if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.Cross(hit.normal, Vector3.right);
        tangent.Normalize();
        if (Vector3.Dot(tangent, direction) < 0f) tangent = -tangent;
        var clearance = hit.point + hit.normal * (CollisionRadius + 0.35f);
        return clearance + tangent * 1.8f + Vector3.up * Mathf.Clamp(hit.normal.y < 0.2f ? 0.8f : 0.25f, 0.25f, 0.8f);
    }

    public void Tick(SpiritState state, float energyRatio, float deltaTime, int level = 1,
        Profession dominantProfession = Profession.Exploration, float bond = 0f)
    {
        if (_emotion != SpiritEmotion.Neutral && Time.time >= _emotionUntil) _emotion = SpiritEmotion.Neutral;
        var baseColor = ParseColor(_config.OrbColor.Value, new Color(0.2f, 0.9f, 1f));
        var accent = state switch
        {
            SpiritState.Work => new Color(1f, 0.55f, 0.15f),
            SpiritState.CarryDrops => new Color(0.75f, 0.35f, 1f),
            SpiritState.Rest or SpiritState.Recharge => new Color(0.25f, 0.45f, 0.65f),
            SpiritState.Guard => new Color(1f, 0.25f, 0.2f),
            SpiritState.Celebrate => Color.white,
            _ => baseColor
        };
        accent = EmotionColor(_emotion, accent);
        var color = state is SpiritState.Idle or SpiritState.FollowPlayer or SpiritState.SearchTarget ? baseColor : Color.Lerp(baseColor, accent, 0.6f);
        if (_emotion != SpiritEmotion.Neutral) color = Color.Lerp(color, accent, 0.72f);
        if (energyRatio < 0.2f) color *= 0.55f + Mathf.PingPong(Time.time * 2f, 0.3f);
        _renderer.material.color = color;
        _trail.startColor = color; _trail.endColor = new Color(color.r, color.g, color.b, 0f);
        var pulseSpeed = _emotion is SpiritEmotion.Afraid or SpiritEmotion.Alert or SpiritEmotion.Frustrated ? 9f :
            _emotion is SpiritEmotion.Resting or SpiritEmotion.Mourning ? 1.2f : 2.5f;
        var pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * (_emotion == SpiritEmotion.Happy ? 0.12f : 0.06f);
        _root.transform.localScale = Vector3.one * _config.OrbSize.Value * pulse;
        UpdatePersonalityMotion(state, deltaTime, bond);
        UpdateEvolution(level, dominantProfession, bond);
        UpdateParticles(level, dominantProfession, color);
        ApplyLighting(state == SpiritState.Scout);
        UpdateCameraLight(deltaTime);
    }

    public void ShowEmotion(SpiritEmotion emotion, float duration = 2.5f)
    {
        _emotion = emotion;
        _emotionUntil = Time.time + Mathf.Max(0.25f, duration);
        if (_particles != null)
        {
            var burst = new ParticleSystem.EmitParams { startColor = EmotionColor(emotion, Color.white) };
            _particles.Emit(burst, emotion == SpiritEmotion.Happy ? 18 : 8);
        }
    }

    private void UpdateEvolution(int level, Profession profession, float bond)
    {
        _trail.enabled = level >= 5;
        _trail.time = level >= 20 ? 1.1f : 0.65f;
        EnsureSatellites(level >= 10 ? (level >= 30 ? 4 : 2) : 0);
        for (var index = 0; index < _satellites.Count; index++)
        {
            var angle = Time.time * (1.6f + bond * 0.001f) + index * Mathf.PI * 2f / _satellites.Count;
            _satellites[index].transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle * 0.6f) * 0.35f,
                Mathf.Sin(angle)) * (1.4f + index * 0.08f);
        }
        if (level < 20)
        {
            if (_levelRing) Object.Destroy(_levelRing);
            return;
        }
        if (!_levelRing)
        {
            _levelRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _levelRing.name = "SpiritHelper_LevelCore";
            Object.Destroy(_levelRing.GetComponent<Collider>());
            _levelRing.transform.SetParent(_root.transform, false);
            _levelRing.transform.localScale = new Vector3(1.5f, 0.04f, 1.5f);
            _levelRing.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default"));
        }
        _levelRing.transform.localRotation = Quaternion.Euler(65f, Time.time * 80f, 0f);
        _levelRing.GetComponent<Renderer>().material.color = profession switch
        {
            Profession.Mining => new Color(1f, 0.5f, 0.15f, 0.8f),
            Profession.Gathering => new Color(0.25f, 1f, 0.45f, 0.8f),
            Profession.Logistics => new Color(0.75f, 0.4f, 1f, 0.8f),
            _ => new Color(0.3f, 0.9f, 1f, 0.8f)
        };
        if (level >= 30) _spiritLight.intensity = Mathf.Max(_spiritLight.intensity, 2.5f);
    }

    private void UpdatePersonalityMotion(SpiritState state, float deltaTime, float bond)
    {
        var targetRotation = Quaternion.identity;
        if (_emotion == SpiritEmotion.Afraid)
            targetRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 22f) * 18f);
        else if (_emotion == SpiritEmotion.Frustrated)
            targetRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 16f) * 25f, 0f);
        else if (state == SpiritState.Celebrate || _emotion == SpiritEmotion.Happy)
            targetRotation = Quaternion.Euler(0f, Time.time * 240f, Mathf.Sin(Time.time * 8f) * 15f);
        else if (state == SpiritState.Rest || _emotion == SpiritEmotion.Resting)
            targetRotation = Quaternion.Euler(0f, Time.time * 12f, 8f);
        else if (state == SpiritState.Idle && Time.time >= _nextIdleGesture)
        {
            targetRotation = Quaternion.Euler(0f, 0f, 18f);
            _nextIdleGesture = Time.time + Mathf.Lerp(8f, 4f, Mathf.Clamp01(bond / 100f));
        }
        _root.transform.localRotation = Quaternion.Slerp(_root.transform.localRotation, targetRotation,
            deltaTime * (state == SpiritState.Celebrate ? 10f : 4f));
    }

    private void UpdateParticles(int level, Profession profession, Color color)
    {
        if (_particles == null) return;
        var main = _particles.main;
        main.startColor = new ParticleSystem.MinMaxGradient(color);
        main.startLifetime = profession == Profession.Exploration ? Mathf.Lerp(0.7f, 1.8f, level / 30f) : 0.9f;
        main.startSize = profession == Profession.Gathering ? new ParticleSystem.MinMaxCurve(0.05f, 0.13f) :
            new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
        var emission = _particles.emission;
        emission.rateOverTime = level >= 30 ? 24f : level >= 10 ? 16f : 9f;
        var velocity = _particles.velocityOverLifetime;
        velocity.enabled = profession == Profession.Mining;
        if (velocity.enabled)
        {
            velocity.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.5f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        }
    }

    private void EnsureSatellites(int count)
    {
        while (_satellites.Count < count)
        {
            var satellite = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            satellite.name = "SpiritHelper_Satellite";
            Object.Destroy(satellite.GetComponent<Collider>());
            satellite.transform.SetParent(_root.transform, false);
            satellite.transform.localScale = Vector3.one * 0.18f;
            satellite.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default"));
            _satellites.Add(satellite);
        }
        while (_satellites.Count > count)
        {
            var last = _satellites[_satellites.Count - 1];
            _satellites.RemoveAt(_satellites.Count - 1);
            if (last) Object.Destroy(last);
        }
    }

    public void CycleLightMode() => _config.LightMode.Value = (SpiritLightMode)(((int)_config.LightMode.Value + 1) % 3);

    public void Dispose()
    {
        if (_root) Object.Destroy(_root);
        if (_cameraLightRoot) Object.Destroy(_cameraLightRoot);
    }

    private void ApplyLighting(bool forceSpiritLight)
    {
        var color = ParseColor(_config.LightColor.Value, new Color(0.72f, 0.96f, 1f));
        _spiritLight.enabled = forceSpiritLight || _config.LightMode.Value == SpiritLightMode.Spirit;
        _cameraLight.enabled = !forceSpiritLight && _config.LightMode.Value == SpiritLightMode.CameraForward;
        _spiritLight.color = color; _cameraLight.color = color;
        _spiritLight.intensity = forceSpiritLight ? Mathf.Max(2f, _config.LightIntensity.Value) : _config.LightIntensity.Value;
        _cameraLight.intensity = _config.LightIntensity.Value;
        _spiritLight.range = forceSpiritLight ? Mathf.Max(18f, _config.LightRange.Value) : _config.LightRange.Value;
        _cameraLight.range = _config.LightRange.Value;
    }

    private static Color EmotionColor(SpiritEmotion emotion, Color fallback) => emotion switch
    {
        SpiritEmotion.Curious => new Color(0.35f, 0.9f, 1f),
        SpiritEmotion.Happy => new Color(1f, 0.9f, 0.35f),
        SpiritEmotion.Frustrated => new Color(1f, 0.25f, 0.12f),
        SpiritEmotion.Afraid => new Color(0.65f, 0.35f, 1f),
        SpiritEmotion.Alert => new Color(1f, 0.12f, 0.08f),
        SpiritEmotion.Resting => new Color(0.25f, 0.45f, 0.75f),
        SpiritEmotion.Mourning => new Color(0.35f, 0.3f, 0.55f),
        _ => fallback
    };

    private void UpdateCameraLight(float deltaTime)
    {
        if (!_cameraLight.enabled || !Camera.main) return;
        var cameraTransform = Camera.main.transform;
        var target = cameraTransform.position + cameraTransform.forward * _config.CameraLightDistance.Value;
        _cameraLightRoot.transform.position = Vector3.SmoothDamp(_cameraLightRoot.transform.position, target,
            ref _cameraLightVelocity, Mathf.Max(0.05f, _config.MovementInertia.Value * 0.5f), 40f, deltaTime);
    }

    private static Color ParseColor(string value, Color fallback) => ColorUtility.TryParseHtmlString(value, out var color) ? color : fallback;
}
