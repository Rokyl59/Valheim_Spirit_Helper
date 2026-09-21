using SpiritHelper.Core;
using UnityEngine;

namespace SpiritHelper.Visuals;

public sealed class SpiritVisualController
{
    private readonly SpiritConfig _config;
    private readonly GameObject _root;
    private readonly Renderer _renderer;
    private readonly TrailRenderer _trail;
    private readonly Light _spiritLight;
    private readonly GameObject _cameraLightRoot;
    private readonly Light _cameraLight;
    private Vector3 _velocity;
    private Vector3 _cameraLightVelocity;

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
            var particles = _root.AddComponent<ParticleSystem>();
            var main = particles.main; main.startLifetime = 1f; main.startSpeed = 0.25f; main.startSize = 0.08f;
            var emission = particles.emission; emission.rateOverTime = 12f;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.25f;
        }

        _cameraLightRoot = new GameObject("SpiritHelper_CameraLight");
        _cameraLight = _cameraLightRoot.AddComponent<Light>();
        _cameraLight.type = LightType.Point;
        ApplyLighting();
    }

    public void Spawn(Vector3 position)
    {
        _root.SetActive(true);
        _root.transform.position = position;
        _velocity = Vector3.zero;
    }

    public void Move(Vector3 destination, float speed, float deltaTime)
    {
        _root.transform.position = Vector3.SmoothDamp(_root.transform.position, destination, ref _velocity,
            _config.MovementInertia.Value, speed, deltaTime);
    }

    public void Tick(SpiritState state, float energyRatio, float deltaTime)
    {
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
        var color = state is SpiritState.Idle or SpiritState.FollowPlayer or SpiritState.SearchTarget ? baseColor : Color.Lerp(baseColor, accent, 0.6f);
        if (energyRatio < 0.2f) color *= 0.55f + Mathf.PingPong(Time.time * 2f, 0.3f);
        _renderer.material.color = color;
        _trail.startColor = color; _trail.endColor = new Color(color.r, color.g, color.b, 0f);
        var pulse = 1f + Mathf.Sin(Time.time * 2.5f) * 0.06f;
        _root.transform.localScale = Vector3.one * _config.OrbSize.Value * pulse;
        ApplyLighting();
        UpdateCameraLight(deltaTime);
    }

    public void CycleLightMode() => _config.LightMode.Value = (SpiritLightMode)(((int)_config.LightMode.Value + 1) % 3);

    public void Dispose()
    {
        if (_root) Object.Destroy(_root);
        if (_cameraLightRoot) Object.Destroy(_cameraLightRoot);
    }

    private void ApplyLighting()
    {
        var color = ParseColor(_config.LightColor.Value, new Color(0.72f, 0.96f, 1f));
        _spiritLight.enabled = _config.LightMode.Value == SpiritLightMode.Spirit;
        _cameraLight.enabled = _config.LightMode.Value == SpiritLightMode.CameraForward;
        _spiritLight.color = color; _cameraLight.color = color;
        _spiritLight.intensity = _config.LightIntensity.Value; _cameraLight.intensity = _config.LightIntensity.Value;
        _spiritLight.range = _config.LightRange.Value; _cameraLight.range = _config.LightRange.Value;
    }

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
