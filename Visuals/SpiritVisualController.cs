using System.Collections.Generic;
using SpiritHelper.Core;
using UnityEngine;

namespace SpiritHelper.Visuals;

public enum SpiritEmotion { Neutral, Curious, Happy, Frustrated, Afraid, Alert, Resting, Mourning }

public sealed class SpiritVisualController
{
    private const float CollisionRadius = 0.28f;
    private const float CollisionPadding = 0.06f;
    private const float NavigationCellSize = 1.2f;
    private const float NavigationRange = 12f;
    private const float NavigationVerticalRange = 6f;
    private const int NavigationNodeLimit = 512;
    private const int NavigationExpansionsPerFrame = 24;
    private const float NavigationRepathInterval = 0.35f;
    private const float NavigationBlockedDelay = 8f;
    private const float MaximumApproachAdjustment = 2f;
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
    private readonly List<Material> _generatedMaterials = new List<Material>();
    private SpiritEmotion _emotion;
    private float _emotionUntil;
    private float _nextIdleGesture;
    private bool _disposed;
    private readonly RaycastHit[] _collisionHits = new RaycastHit[16];
    private readonly Collider[] _overlapHits = new Collider[16];
    private Vector3? _forbiddenAreaCentre;
    private float _forbiddenAreaRadius;
    private readonly List<Vector3> _navigationPath = new List<Vector3>();
    private int _navigationPathIndex;
    private Dictionary<Vector3Int, NavigationNode>? _navigationNodes;
    private List<Vector3Int>? _navigationOpenNodes;
    private Vector3 _navigationOrigin;
    private Vector3 _navigationGoal;
    private Vector3 _navigationDestination;
    private Vector3 _navigationApproachDestination;
    private float _nextNavigationRepath;
    private Vector3 _lastNavigationProgress;
    private Vector3 _progressDestination;
    private float _lastNavigationProgressTime;
    private SphereCollider? _overlapProbe;

    public bool NavigationBlocked { get; private set; }

    public Transform Transform => _root.transform;
    public bool IsAlive => !_disposed && _root;
    public SpiritLightMode LightMode => _config.LightMode.Value;

    public SpiritVisualController(SpiritConfig config)
    {
        _config = config;
        _root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _root.name = "SpiritHelper_LocalSpirit";
        Object.Destroy(_root.GetComponent<Collider>());
        _renderer = _root.GetComponent<Renderer>();
        _renderer.material = CreateMaterial();
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
        if (_disposed || !_root) return;
        _root.SetActive(true);
        _root.transform.position = position;
        _velocity = Vector3.zero;
        ResetNavigation();
        _lastNavigationProgress = position;
        _lastNavigationProgressTime = Time.time;
        _nextIdleGesture = Time.time + Random.Range(3f, 7f);
    }

    public void Move(Vector3 destination, float speed, float deltaTime)
    {
        if (_disposed || !_root || deltaTime <= 0f) return;
        var origin = ResolveInitialOverlap(_root.transform.position);
        if (origin != _root.transform.position) _root.transform.position = origin;
        destination = KeepOutsideForbiddenArea(origin, destination);
        UpdateNavigationDestination(destination);
        var steeredDestination = GetNavigationTarget(origin, destination);
        var desiredPosition = Vector3.SmoothDamp(origin, steeredDestination, ref _velocity,
            _config.MovementInertia.Value, speed, deltaTime);
        var nextPosition = ClampForbiddenMovement(origin, SweepMove(origin, desiredPosition));
        _root.transform.position = nextPosition;
        TrackNavigationProgress(nextPosition, destination);
    }

    public void ResetNavigation()
    {
        _navigationPath.Clear();
        _navigationPathIndex = 0;
        _navigationNodes = null;
        _navigationOpenNodes = null;
        _nextNavigationRepath = 0f;
        NavigationBlocked = false;
        _velocity = Vector3.zero;
        _lastNavigationProgress = _root ? _root.transform.position : Vector3.zero;
        _progressDestination = _lastNavigationProgress;
        _lastNavigationProgressTime = Time.time;
    }

    public void SetForbiddenArea(Vector3? centre, float radius)
    {
        _forbiddenAreaCentre = centre;
        _forbiddenAreaRadius = Mathf.Max(0f, radius);
    }

    private Vector3 KeepOutsideForbiddenArea(Vector3 origin, Vector3 destination)
    {
        if (!_forbiddenAreaCentre.HasValue || _forbiddenAreaRadius <= 0f) return destination;
        var centre = _forbiddenAreaCentre.Value;
        var safeRadius = _forbiddenAreaRadius + CollisionRadius + CollisionPadding;
        var offset = destination - centre;
        offset.y = 0f;
        if (offset.sqrMagnitude >= safeRadius * safeRadius) return destination;

        var direction = origin - centre;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = destination - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
        direction.Normalize();
        return centre + direction * safeRadius + Vector3.up * (destination.y - centre.y);
    }

    private Vector3 ClampForbiddenMovement(Vector3 origin, Vector3 destination)
    {
        if (!_forbiddenAreaCentre.HasValue || _forbiddenAreaRadius <= 0f) return destination;
        var centre = _forbiddenAreaCentre.Value;
        var safeRadius = _forbiddenAreaRadius + CollisionRadius + CollisionPadding;
        var start = new Vector2(origin.x - centre.x, origin.z - centre.z);
        var end = new Vector2(destination.x - centre.x, destination.z - centre.z);
        var delta = end - start;
        var startDistanceSquared = start.sqrMagnitude;
        if (startDistanceSquared < safeRadius * safeRadius)
        {
            var direction = start.sqrMagnitude > 0.001f ? start.normalized : (delta.sqrMagnitude > 0.001f ? -delta.normalized : Vector2.right);
            return new Vector3(centre.x + direction.x * safeRadius, destination.y, centre.z + direction.y * safeRadius);
        }
        var lengthSquared = delta.sqrMagnitude;
        if (lengthSquared < 0.0001f) return destination;
        var projection = Mathf.Clamp01(-Vector2.Dot(start, delta) / lengthSquared);
        if ((start + delta * projection).sqrMagnitude >= safeRadius * safeRadius) return destination;
        var b = Vector2.Dot(start, delta);
        var discriminant = b * b - lengthSquared * (startDistanceSquared - safeRadius * safeRadius);
        if (discriminant <= 0f) return destination;
        var entry = (-b - Mathf.Sqrt(discriminant)) / lengthSquared;
        if (entry <= 0f || entry >= 1f) return destination;
        return Vector3.Lerp(origin, destination, Mathf.Max(0f, entry - 0.001f));
    }

    private Vector3 GetNavigationTarget(Vector3 origin, Vector3 destination)
    {
        var approachDestination = FindSafeApproachDestination(destination);
        var directOffset = approachDestination - origin;
        if (directOffset.sqrMagnitude < 0.05f * 0.05f || IsPathClear(origin, approachDestination))
        {
            _navigationPath.Clear();
            _navigationPathIndex = 0;
            _navigationNodes = null;
            _navigationOpenNodes = null;
            return approachDestination;
        }

        var goalChanged = (destination - _navigationDestination).sqrMagnitude > NavigationCellSize * NavigationCellSize;
        if (goalChanged)
        {
            _navigationPath.Clear();
            _navigationPathIndex = 0;
            BeginNavigationSearch(origin, destination, approachDestination);
        }
        if (_navigationPathIndex < _navigationPath.Count)
        {
            while (_navigationPathIndex < _navigationPath.Count &&
                   Vector3.Distance(origin, _navigationPath[_navigationPathIndex]) < NavigationCellSize * 0.45f)
                _navigationPathIndex++;
            if (_navigationPathIndex < _navigationPath.Count)
            {
                var waypoint = _navigationPath[_navigationPathIndex];
                if (IsPathClear(origin, waypoint)) return waypoint;
                _navigationPath.Clear();
                _navigationPathIndex = 0;
            }
        }

        if (_navigationNodes == null || _navigationOpenNodes == null || Time.time >= _nextNavigationRepath && _navigationOpenNodes.Count == 0)
            BeginNavigationSearch(origin, destination, approachDestination);
        AdvanceNavigationSearch();
        return _navigationPathIndex < _navigationPath.Count ? _navigationPath[_navigationPathIndex] : origin;
    }

    private void BeginNavigationSearch(Vector3 origin, Vector3 destination, Vector3 approachDestination)
    {
        _navigationOrigin = origin;
        _navigationDestination = destination;
        _navigationApproachDestination = approachDestination;
        var offset = approachDestination - origin;
        _navigationGoal = offset.magnitude > NavigationRange * 0.8f
            ? origin + offset.normalized * (NavigationRange * 0.8f)
            : approachDestination;
        _navigationNodes = new Dictionary<Vector3Int, NavigationNode>();
        _navigationOpenNodes = new List<Vector3Int>();
        var start = Vector3Int.zero;
        _navigationNodes[start] = new NavigationNode(0f, NavigationHeuristic(origin), start);
        _navigationOpenNodes.Add(start);
        _nextNavigationRepath = Time.time + NavigationRepathInterval;
    }

    private void AdvanceNavigationSearch()
    {
        if (_navigationNodes == null || _navigationOpenNodes == null) return;
        for (var expansion = 0; expansion < NavigationExpansionsPerFrame && _navigationOpenNodes.Count > 0; expansion++)
        {
            var currentKey = TakeBestOpenNode();
            var current = _navigationNodes[currentKey];
            var currentPosition = NavigationPosition(currentKey);
            if (IsPathClear(currentPosition, _navigationApproachDestination))
            {
                BuildNavigationPath(currentKey);
                _navigationPath.Add(_navigationApproachDestination);
                _navigationOpenNodes.Clear();
                return;
            }
            foreach (var offset in NavigationOffsets)
            {
                var neighborKey = currentKey + offset;
                if (!IsNavigationCoordinateValid(neighborKey)) continue;
                var neighborPosition = NavigationPosition(neighborKey);
                if (!IsPathClear(currentPosition, neighborPosition)) continue;
                var tentativeCost = current.Cost + Vector3.Distance(currentPosition, neighborPosition);
                if (_navigationNodes.TryGetValue(neighborKey, out var existing) && tentativeCost >= existing.Cost) continue;
                _navigationNodes[neighborKey] = new NavigationNode(tentativeCost, NavigationHeuristic(neighborPosition), currentKey);
                if (!_navigationOpenNodes.Contains(neighborKey)) _navigationOpenNodes.Add(neighborKey);
                if (_navigationNodes.Count >= NavigationNodeLimit)
                {
                    _navigationOpenNodes.Clear();
                    return;
                }
            }
        }
    }

    private Vector3Int TakeBestOpenNode()
    {
        var bestIndex = 0;
        var bestScore = float.MaxValue;
        for (var index = 0; index < _navigationOpenNodes!.Count; index++)
        {
            var node = _navigationNodes![_navigationOpenNodes[index]];
            var score = node.Cost + node.Heuristic;
            if (score >= bestScore) continue;
            bestScore = score;
            bestIndex = index;
        }
        var result = _navigationOpenNodes[bestIndex];
        _navigationOpenNodes.RemoveAt(bestIndex);
        return result;
    }

    private void BuildNavigationPath(Vector3Int end)
    {
        _navigationPath.Clear();
        var current = end;
        while (current != Vector3Int.zero)
        {
            _navigationPath.Add(NavigationPosition(current));
            current = _navigationNodes![current].Parent;
        }
        _navigationPath.Reverse();
        _navigationPathIndex = 0;
    }

    private float NavigationHeuristic(Vector3 position) => Vector3.Distance(position, _navigationGoal);

    private Vector3 NavigationPosition(Vector3Int coordinate) => _navigationOrigin + new Vector3(
        coordinate.x * NavigationCellSize, coordinate.y * NavigationCellSize, coordinate.z * NavigationCellSize);

    private static readonly Vector3Int[] NavigationOffsets =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        new Vector3Int(1, 0, 1), new Vector3Int(1, 0, -1), new Vector3Int(-1, 0, 1), new Vector3Int(-1, 0, -1),
        new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0)
    };

    private static bool IsNavigationCoordinateValid(Vector3Int coordinate) =>
        Mathf.Abs(coordinate.x) <= Mathf.CeilToInt(NavigationRange / NavigationCellSize) &&
        Mathf.Abs(coordinate.z) <= Mathf.CeilToInt(NavigationRange / NavigationCellSize) &&
        Mathf.Abs(coordinate.y) <= Mathf.CeilToInt(NavigationVerticalRange / NavigationCellSize);

    private void TrackNavigationProgress(Vector3 position, Vector3 destination)
    {
        if (Vector3.Distance(position, destination) <= 0.9f)
        {
            _lastNavigationProgress = position;
            _lastNavigationProgressTime = Time.time;
            NavigationBlocked = false;
            return;
        }
        if (Vector3.Distance(position, _lastNavigationProgress) >= 0.12f)
        {
            _lastNavigationProgress = position;
            _lastNavigationProgressTime = Time.time;
            NavigationBlocked = false;
            return;
        }
        if (Vector3.Distance(position, destination) > 0.9f && Time.time - _lastNavigationProgressTime >= NavigationBlockedDelay)
            NavigationBlocked = true;
    }

    private void UpdateNavigationDestination(Vector3 destination)
    {
        if ((destination - _progressDestination).sqrMagnitude <= NavigationCellSize * NavigationCellSize) return;
        _progressDestination = destination;
        _lastNavigationProgress = _root.transform.position;
        _lastNavigationProgressTime = Time.time;
        NavigationBlocked = false;
    }

    private Vector3 SweepMove(Vector3 origin, Vector3 desiredPosition)
    {
        var offset = desiredPosition - origin;
        var distance = offset.magnitude;
        if (distance < 0.001f || !TryGetBlockingHit(origin, offset / distance, distance, out var hit)) return desiredPosition;

        var travel = Mathf.Max(0f, hit.distance - CollisionPadding);
        _velocity = Vector3.ProjectOnPlane(_velocity, hit.normal);
        return origin + offset / distance * travel;
    }

    private bool IsPathClear(Vector3 origin, Vector3 destination)
    {
        var offset = destination - origin;
        var distance = offset.magnitude;
        if (distance < 0.001f) return true;
        if (!TryGetBlockingHit(origin, offset / distance, distance, out _))
            return ClampForbiddenMovement(origin, destination) == destination;
        return false;
    }

    private Vector3 ResolveInitialOverlap(Vector3 position)
    {
        return ClampForbiddenMovement(position, ResolveOverlaps(position, 4));
    }

    private Vector3 FindSafeApproachDestination(Vector3 destination)
    {
        var resolved = ResolveOverlaps(destination, 3);
        var adjustment = resolved - destination;
        return adjustment.magnitude <= MaximumApproachAdjustment
            ? resolved
            : destination + adjustment.normalized * MaximumApproachAdjustment;
    }

    private Vector3 ResolveOverlaps(Vector3 position, int iterations)
    {
        var probe = GetOverlapProbe();
        if (probe == null) return position;
        var resolved = position;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var hitCount = Physics.OverlapSphereNonAlloc(resolved, CollisionRadius, _overlapHits, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var moved = false;
            if (hitCount == _overlapHits.Length)
            {
                foreach (var collider in Physics.OverlapSphere(resolved, CollisionRadius, Physics.DefaultRaycastLayers,
                             QueryTriggerInteraction.Ignore))
                    moved |= ResolvePenetration(probe, ref resolved, collider);
            }
            else
            {
                for (var index = 0; index < hitCount; index++)
                    moved |= ResolvePenetration(probe, ref resolved, _overlapHits[index]);
            }
            if (!moved) break;
        }
        return resolved;
    }

    private SphereCollider? GetOverlapProbe()
    {
        if (_overlapProbe) return _overlapProbe;
        var probeObject = new GameObject("SpiritHelper_NavigationProbe")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        var ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        probeObject.layer = ignoreRaycastLayer >= 0 ? ignoreRaycastLayer : 2;
        _overlapProbe = probeObject.AddComponent<SphereCollider>();
        _overlapProbe.radius = CollisionRadius;
        _overlapProbe.isTrigger = true;
        _overlapProbe.enabled = false;
        return _overlapProbe;
    }

    private bool ResolvePenetration(SphereCollider probe, ref Vector3 position, Collider collider)
    {
        if (!IsBlockingCollider(collider)) return false;
        probe.transform.position = position;
        if (!Physics.ComputePenetration(probe, position, Quaternion.identity, collider, collider.transform.position,
                collider.transform.rotation, out var direction, out var distance) || distance <= 0.001f)
            return false;
        position += direction * (distance + CollisionPadding);
        return true;
    }

    private bool TryGetBlockingHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit blockingHit)
    {
        blockingHit = default;
        if (distance <= 0.001f) return false;
        var hitCount = Physics.SphereCastNonAlloc(origin, CollisionRadius, direction, _collisionHits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        var closestDistance = float.MaxValue;
        if (hitCount == _collisionHits.Length)
        {
            foreach (var hit in Physics.SphereCastAll(origin, CollisionRadius, direction, distance,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                SetClosestBlockingHit(hit, ref closestDistance, ref blockingHit);
        }
        else
        {
            for (var index = 0; index < hitCount; index++)
                SetClosestBlockingHit(_collisionHits[index], ref closestDistance, ref blockingHit);
        }
        return closestDistance < float.MaxValue;
    }

    private void SetClosestBlockingHit(RaycastHit hit, ref float closestDistance, ref RaycastHit blockingHit)
    {
        if (!IsBlockingCollider(hit.collider) || hit.distance >= closestDistance) return;
        closestDistance = hit.distance;
        blockingHit = hit;
    }

    private bool IsBlockingCollider(Collider? collider)
    {
        if (collider == null || collider.transform.IsChildOf(_root.transform)) return false;
        return collider.GetComponentInParent<Character>() == null && collider.GetComponentInParent<ItemDrop>() == null;
    }

    public void Tick(SpiritState state, float energyRatio, float deltaTime, int level = 1,
        Profession dominantProfession = Profession.Exploration, float bond = 0f)
    {
        if (_disposed || !_root) return;
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
        if (_disposed || !_root) return;
        var changed = _emotion != emotion;
        _emotion = emotion;
        _emotionUntil = Time.time + Mathf.Max(0.25f, duration);
        if (changed && _particles != null)
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
            _levelRing.GetComponent<Renderer>().material = CreateMaterial();
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
            satellite.GetComponent<Renderer>().material = CreateMaterial();
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
        if (_disposed) return;
        _disposed = true;
        foreach (var material in _generatedMaterials)
            if (material) Object.Destroy(material);
        _generatedMaterials.Clear();
        if (_overlapProbe) Object.Destroy(_overlapProbe.gameObject);
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

    private Material CreateMaterial()
    {
        var material = new Material(Shader.Find("Sprites/Default"));
        _generatedMaterials.Add(material);
        return material;
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

    private sealed class NavigationNode
    {
        public NavigationNode(float cost, float heuristic, Vector3Int parent)
        {
            Cost = cost;
            Heuristic = heuristic;
            Parent = parent;
        }

        public float Cost { get; }
        public float Heuristic { get; }
        public Vector3Int Parent { get; }
    }
}
