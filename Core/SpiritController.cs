using System;
using System.Collections.Generic;
using System.Linq;
using SpiritHelper.AI;
using SpiritHelper.Audio;
using SpiritHelper.Logistics;
using SpiritHelper.Networking;
using SpiritHelper.Persistence;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using SpiritHelper.UI;
using SpiritHelper.Visuals;
using SpiritHelper.Zones;
using UnityEngine;

namespace SpiritHelper.Core;

public sealed class SpiritController : MonoBehaviour
{
    private readonly struct SpiritAssignment
    {
        public SpiritJob Job { get; }
        public int Quantity { get; }
        public SpiritAssignment(SpiritJob job, int quantity) { Job = job; Quantity = quantity; }
    }

    private SpiritConfig _config = null!;
    private readonly SaveManager _saveManager = new SaveManager();
    private SpiritProgression _progression = null!;
    private SpiritKnowledge _knowledge = null!;
    private readonly WorldMarkers _markers = new WorldMarkers();
    private readonly SpiritUI _ui = new SpiritUI();
    private ResourceDatabase _resources = null!;
    private readonly ResourceSelection _resourceSelection = new ResourceSelection();
    private TargetScanner _scanner = null!;
    private VanillaInteractionHelper _interaction = null!;
    private CarrySystem _carry = null!;
    private SpiritVisualController? _visual;
    private SpiritAudioController? _audio;
    private SpiritTarget? _target;
    private SpiritState _state = SpiritState.Idle;
    private SpiritJob _job = SpiritJob.Follow;
    private SpiritJob _jobBeforeRest = SpiritJob.Follow;
    private float _nextScan;
    private float _nextWork;
    private float _targetStarted;
    private float _saveAt;
    private int _workAttempts;
    private string _lastError = string.Empty;
    private string _lastTargetPosition = string.Empty;
    private Vector3 _completedTargetPosition;
    private float _collectDropsUntil;
    private bool _targetWasStandingTree;
    private bool _awaitingTreeLog;
    private float _treeLogSearchUntil;
    private float _dropTrackingUntil;
    private Vector3 _lastWorkPosition;
    private bool _forceDelivery;
    private float _nextScoutReport;
    private float _nextGuardScan;
    private float _nextGuardNotice;
    private Character? _guardThreat;
    private bool _initialized;
    private string _saveKey = string.Empty;
    private readonly Dictionary<int, float> _targetBlacklist = new Dictionary<int, float>();
    private readonly Dictionary<int, int> _targetFailures = new Dictionary<int, int>();
    private float _celebrateUntil;
    private readonly Queue<SpiritAssignment> _assignments = new Queue<SpiritAssignment>();
    private bool _queueMode;
    private int _selectedQuantity;
    private int _assignmentQuantity;
    private int _assignmentProgress;
    private Vector3 _lastVisualPosition;
    private float _containerUnloadStarted;
    private SpiritJob _assistWorkJob = SpiritJob.Gathering;
    private ResourceDefinition? _observedResource;
    private float _nextObservation;
    private Vector3? _guideDestination;
    private string _guideLabel = string.Empty;
    private float _expeditionEndsAt;
    private float _nextAdvancedTick;
    private NamedZoneType _selectedNamedZoneType = NamedZoneType.Home;
    private int _activeNamedZoneIndex = -1;
    private bool _scheduleEnabled;
    private bool _rulesEnabled;
    private bool _baseAlarmEnabled;
    private bool _secretDetectorEnabled;
    private bool _courierAwaitingDestination;
    private bool _wasDead;
    private float _lastSuggestion;
    private float _nextRulesTick;
    private string _lastWorkOrder = string.Empty;
    private float _shrineBuffUntil;
    private float _shrineSpeedMultiplier = 1f;
    private float _shrineScoutMultiplier = 1f;
    private float _nextEmotion;

    public void Initialize(SpiritConfig config)
    {
        _config = config;
        _resources = new ResourceDatabase();
        _ui.SetResources(_resources.All);
        _scanner = new TargetScanner(_resources);
        _interaction = new VanillaInteractionHelper();
        _carry = new CarrySystem(_resources);
        _ui.ActionSelected += HandleMenuAction;
        _ui.ExactResourceSelected += name => SetExactFilter(name, ResourceName(name));
    }

    public void Shutdown()
    {
        _ui.Close();
        if (_initialized) Persist();
        _carry?.Release();
        _markers.Dispose();
        _visual?.Dispose();
    }

    private float WorkRadius => Mathf.Min(_config.WorkRadius.Value, _config.MaxWorkRadius.Value);
    private float EffectiveSearchRadius
    {
        get
        {
            var player = Player.m_localPlayer;
            var knowledge = player ? _knowledge.BiomeKnowledge(player.GetCurrentBiome()) : 0f;
            return Mathf.Min(_config.MaxWorkRadius.Value, WorkRadius * (1f + knowledge * 0.002f));
        }
    }
    private int EffectiveCarryStacks => _config.MaxCarryStacks.Value + TalentTier("logistics");
    private float EffectiveCarryWeight => _config.MaxCarryWeight.Value * (1f + TalentTier("logistics") * 0.2f);

    private void Update()
    {
        var player = Player.m_localPlayer;
        if (!player) return;
        EnsureSpirit(player);
        HandleInput();
        _ui.HandleInput();
        TickEnergy(Time.deltaTime);
        TickAi(player, Time.deltaTime);
        _audio?.Tick(_state);
        if (Time.unscaledTime >= _saveAt) { _saveAt = Time.unscaledTime + 30f; Persist(); }
    }

    private void EnsureSpirit(Player player)
    {
        if (!_initialized)
        {
            var worldId = ZNet.instance ? ZNet.instance.GetWorldUID() : 0L;
            _saveKey = $"{worldId}-{player.GetPlayerID()}";
            var data = _saveManager.Load(_saveKey);
            _progression = new SpiritProgression(data);
            _knowledge = new SpiritKnowledge(data);
            _scheduleEnabled = data.Automation.ScheduleEnabled;
            _rulesEnabled = data.Automation.RulesEnabled;
            _baseAlarmEnabled = data.Automation.BaseAlarmEnabled;
            _secretDetectorEnabled = data.Automation.SecretDetectorEnabled;
            EnsureIdentity(player);
            _config.BalanceMode.Value = data.BalanceMode;
            _resourceSelection.Restore(data.ResourceFilterMode, data.ResourceCategory, data.SelectedResource);
            _markers.Restore(data.HasUnloadPoint, data.UnloadPoint, data.UnloadPointType,
                data.HasWorkZone, data.WorkZone, data.WorkZoneMode, WorkRadius, _config.ShowWorkZone.Value,
                data.UnloadPoints);
            _markers.RestoreForbidden(data.HasForbiddenZone, data.ForbiddenZone);
            _initialized = true;
        }
        if (_visual != null) return;
        _visual = new SpiritVisualController(_config);
        _visual.Spawn(player.transform.position + player.transform.right * 1.5f + Vector3.up * 2f);
        _lastVisualPosition = _visual.Transform.position;
        _audio = new SpiritAudioController(_visual.Transform, _config.EnableSounds.Value, _config.MasterVolume.Value,
            _config.EnableAmbientSounds.Value, _config.AmbientVolume.Value,
            _config.AmbientIntervalMin.Value, _config.AmbientIntervalMax.Value);
        _audio.RegisterProceduralDefaults();
        _audio.Play(SpiritAudioEvent.Spawn);
        _ui.Notify($"{_progression.Data.SpiritName}, {_progression.Data.Personality.ToLowerInvariant()} дух, пробудился.");
    }

    private void HandleInput()
    {
        if (_config.MenuKey.Value.IsDown()) { _ui.ToggleMenu(); _audio?.Play(SpiritAudioEvent.MenuOpen); }
        if (!_ui.MenuOpen && Input.GetMouseButtonDown(2)) InspectLookTarget(true);
    }

    private void TickAi(Player player, float deltaTime)
    {
        if (_visual == null) return;
        _carry.TickClaims(EffectiveCarryStacks, EffectiveCarryWeight);
        TrackRecentDrops();
        _markers.Tick(player, WorkRadius, _config.ShowWorkZone.Value);
        TrackFlight();
        TickLongTermProgress(player, deltaTime);
        TickAutomationRules(player);
        if (player.IsSleeping())
        {
            SetState(SpiritState.Rest);
            _visual.Move(player.transform.position + Vector3.up * 1.6f, 4f, deltaTime);
            _visual.Tick(_state, EnergyRatio, deltaTime, _progression.Level, DominantProfession(), _progression.Data.Bond);
            return;
        }
        RemoveExpiredBlacklistEntries();
        if (Time.time < _celebrateUntil) { Celebrate(player, deltaTime); _visual.Tick(_state, EnergyRatio, deltaTime, _progression.Level, DominantProfession(), _progression.Data.Bond); return; }
        if (_job == SpiritJob.Stopped) { SetState(SpiritState.Idle); _visual.Tick(_state, EnergyRatio, deltaTime, _progression.Level, DominantProfession(), _progression.Data.Bond); return; }
        if (_progression.Data.Energy <= 0f && _config.EnergyEnabled.Value && _job != SpiritJob.Rest)
        {
            _jobBeforeRest = _job;
            _job = SpiritJob.Rest;
            SetState(SpiritState.Rest);
            _audio?.Play(SpiritAudioEvent.EnergyEmpty);
        }
        switch (_job)
        {
            case SpiritJob.Follow: Follow(player, deltaTime); break;
            case SpiritJob.Rest: Rest(player, deltaTime); break;
            case SpiritJob.Transport: TransportOnly(player, deltaTime); break;
            case SpiritJob.Scout: Scout(player, deltaTime); break;
            case SpiritJob.Guard: Guard(player, deltaTime); break;
            case SpiritJob.Observe: Observe(player, deltaTime); break;
            case SpiritJob.Assist: Assist(player, deltaTime); break;
            case SpiritJob.Cleanup: Cleanup(player, deltaTime); break;
            case SpiritJob.Courier: Courier(player, deltaTime); break;
            case SpiritJob.Caravan: Caravan(player, deltaTime); break;
            case SpiritJob.Bring: Bring(player, deltaTime); break;
            case SpiritJob.Sort: SortCargo(player, deltaTime); break;
            case SpiritJob.Patrol: Patrol(player, deltaTime); break;
            case SpiritJob.Guide: Guide(player, deltaTime); break;
            case SpiritJob.ExploreDungeon: ExploreDungeon(player, deltaTime); break;
            case SpiritJob.Expedition: Expedition(player, deltaTime); break;
            case SpiritJob.Production: Production(player, deltaTime); break;
            default: WorkJob(player, deltaTime); break;
        }
        _carry.Follow(_visual.Transform.position);
        _visual.Tick(_state, EnergyRatio, deltaTime, _progression.Level, DominantProfession(), _progression.Data.Bond);
    }

    private void Follow(Player player, float deltaTime)
    {
        SetState(SpiritState.FollowPlayer);
        var movementLead = player.GetVelocity();
        movementLead.y = 0f;
        movementLead = Vector3.ClampMagnitude(movementLead * 0.55f, 5f);
        var side = player.transform.right * _config.FollowDistance.Value + player.transform.forward * -0.7f + movementLead;
        var bob = Vector3.up * (_config.FollowHeight.Value + Mathf.Sin(Time.time * 1.8f) * _config.BobbingAmount.Value);
        if (_progression.Data.Personality == "Любопытный")
            side += new Vector3(Mathf.Cos(Time.time * 0.7f), 0f, Mathf.Sin(Time.time * 0.7f)) * 0.45f;
        _visual!.Move(player.transform.position + side + bob, _config.FollowSpeed.Value * ActiveSpeedMultiplier, deltaTime);
    }

    private void Rest(Player player, float deltaTime)
    {
        SetState(SpiritState.Rest);
        var automation = _progression.Data.Automation;
        var restPoint = automation.ShrineSet ? SpiritKnowledge.ToVector(automation.ShrinePosition) : player.transform.position;
        _visual!.Move(restPoint + Vector3.up * 1.1f, 5f * ActiveSpeedMultiplier, deltaTime);
        if (_progression.Data.Energy >= 99.9f)
        {
            _job = _jobBeforeRest == SpiritJob.Rest || _jobBeforeRest == SpiritJob.Stopped ? SpiritJob.Follow : _jobBeforeRest;
            _ui.Notify($"Энергия восстановлена. Возобновлена задача: {SpiritUI.JobName(_job)}.");
            _audio?.Play(SpiritAudioEvent.EnergyRestored);
        }
    }

    private void WorkJob(Player player, float deltaTime)
    {
        var activeWorkJob = _job is SpiritJob.Assist or SpiritJob.Cleanup ? _assistWorkJob : _job;
        if (_carry.Count > 0 && (_forceDelivery || _carry.IsFull(EffectiveCarryStacks, EffectiveCarryWeight)))
        {
            DeliverCarried(player, deltaTime);
            return;
        }
        if (_target == null && _awaitingTreeLog)
        {
            if (Time.time >= _treeLogSearchUntil) _awaitingTreeLog = false;
            else
            {
                SetState(SpiritState.WaitForDrop);
                if (Time.time >= _nextScan)
                {
                    _nextScan = Time.time + 0.35f;
                    _target = _scanner.FindTreeLog(_completedTargetPosition, 25f);
                    if (_target != null)
                    {
                        _awaitingTreeLog = false;
                        _targetWasStandingTree = false;
                        _targetStarted = Time.time;
                        _workAttempts = 0;
                        _lastTargetPosition = _target.Position.ToString("F0");
                        _ui.Notify("Дух нашёл упавшее бревно и продолжает рубку.", 2f);
                    }
                }
                if (_target == null) { FollowNearWorkCentre(player, deltaTime); return; }
            }
        }
        if (_target == null && Time.time < _collectDropsUntil)
        {
            SetState(SpiritState.WaitForDrop);
            if (_config.AutoCarry.Value) _carry.TryCollect(_completedTargetPosition, EffectiveCarryStacks,
                EffectiveCarryWeight, _resourceSelection, IsResourceForCurrentJob);
            if (_carry.Count == 0) FollowNearWorkCentre(player, deltaTime);
            return;
        }
        if (_target != null && !_target.IsValid) { CompleteTarget(); return; }
        if (_target != null && (_workAttempts >= _config.MaxWorkAttempts.Value || Time.time - _targetStarted > _config.TargetTimeout.Value))
        {
            CancelTarget("Дух отменил недоступную цель.");
            return;
        }
        if (_target == null)
        {
            if (Time.time < _nextScan) { FollowNearWorkCentre(player, deltaTime); return; }
            var biomeKnowledge = _knowledge.BiomeKnowledge(player.GetCurrentBiome());
            var personalitySearch = _progression.Data.Personality == "Трудолюбивый" ? 0.7f : 1f;
            SetState(SpiritState.SearchTarget); _nextScan = Time.time + _config.SearchInterval.Value * personalitySearch * (1f - biomeKnowledge * 0.003f);
            var centre = _markers.GetWorkCentre(player);
            _target = _scanner.Find(centre, EffectiveSearchRadius, activeWorkJob, _resourceSelection, _config.SafeBaseRadius.Value,
                _config.ProtectPlantedTrees.Value, _config.ProtectCrops.Value, _progression, ActiveBlacklist(), _markers.IsForbidden,
                _markers.HasUnloadPoint ? _markers.UnloadPoint : (Vector3?)null);
            if (_target == null)
            {
                if (_carry.Count > 0) DeliverCarried(player, deltaTime);
                else { FollowNearWorkCentre(player, deltaTime); SuggestNextWork(player); }
                return;
            }
            _targetStarted = Time.time; _workAttempts = 0; _lastTargetPosition = _target.Position.ToString("F0");
            _targetWasStandingTree = _target.Component is TreeBase;
            _ui.Notify($"Дух нашёл: {_target.Definition.Name}.", 2f);
        }
        if (Vector3.Distance(_target.Position, _markers.GetWorkCentre(player)) > WorkRadius)
        {
            CancelTarget("Цель находится вне рабочей зоны."); return;
        }
        if (Vector3.Distance(player.transform.position, _target.Position) > _config.MaxDistanceFromPlayer.Value)
        {
            CancelTarget("Дух слишком далеко от игрока."); return;
        }
        SetState(SpiritState.MoveToTarget);
        var destination = _target.Position + Vector3.up * 1.8f;
        _visual!.Move(destination, _config.BalanceMode.Value == BalanceMode.Free ? 18f : 10f, deltaTime);
        if (Vector3.Distance(_visual.Transform.position, destination) > 2.2f || Time.time < _nextWork) return;
        SetState(SpiritState.Work);
        var workInterval = Mathf.Max(0.2f, _target.Definition.WorkInterval * Mathf.Clamp(_config.WorkInterval.Value / 1.5f, 0.25f, 4f));
        var talentBranch = activeWorkJob == SpiritJob.Woodcutting ? "woodcutting" : activeWorkJob == SpiritJob.Mining ? "mining" : string.Empty;
        if (talentBranch.Length > 0) workInterval *= 1f - TalentTier(talentBranch) * 0.08f;
        if (EnergyRatio < 0.2f) workInterval *= 1.35f;
        else if (EnergyRatio < 0.5f) workInterval *= 1.15f;
        _nextWork = Time.time + (_config.BalanceMode.Value == BalanceMode.Free ? 0.35f : workInterval);
        if (!_interaction.TryWork(_target, player, _config.BalanceMode.Value, _progression,
                _config.UseToolDurability.Value, _visual.Transform.position, out var error))
        {
            CancelTarget(error); return;
        }
        _workAttempts++;
        _completedTargetPosition = _target.Position;
        _lastWorkPosition = _target.HitPoint;
        _dropTrackingUntil = Time.time + 4f;
        SpendEnergy(_target.Definition.EnergyCost);
        _audio?.Play(activeWorkJob == SpiritJob.Woodcutting ? SpiritAudioEvent.WoodHit : activeWorkJob == SpiritJob.Mining ? SpiritAudioEvent.MiningHitOre : SpiritAudioEvent.GatherPlant, 0.35f);
        if (_target.Definition.DamageType == ResourceDamageType.Interact) CompleteTarget();
    }

    private void CompleteTarget()
    {
        if (_target == null) return;
        _targetFailures.Remove(_target.Component.GetInstanceID());
        var definition = _target.Definition;
        if (_target.IsValid) _completedTargetPosition = _target.Position;
        if (_targetWasStandingTree)
        {
            _awaitingTreeLog = true;
            _treeLogSearchUntil = Time.time + 15f;
            _collectDropsUntil = 0f;
        }
        else
        {
            Award(definition.XpReward, definition.ProfessionXpReward, definition.Profession);
            RecordWork(definition);
            CountAssignmentProgress();
            _collectDropsUntil = Time.time + 4f;
        }
        _target = null; _workAttempts = 0;
    }

    private void TransportOnly(Player player, float deltaTime)
    {
        if (_carry.Count == 0 && _carry.PendingCount == 0)
        {
            var dropPosition = _carry.FindNearest(_markers.GetWorkCentre(player), WorkRadius, _resourceSelection);
            if (dropPosition.HasValue)
            {
                SetState(SpiritState.CollectDrops);
                _visual!.Move(dropPosition.Value + Vector3.up * 1.4f, _config.FollowSpeed.Value, deltaTime);
                if (Vector3.Distance(_visual.Transform.position, dropPosition.Value) <= 3f)
                    _carry.TryCollect(dropPosition.Value, EffectiveCarryStacks, EffectiveCarryWeight, _resourceSelection);
                return;
            }
        }
        if (_carry.Count == 0) { Follow(player, deltaTime); return; }
        DeliverCarried(player, deltaTime);
    }

    private void DeliverCarried(Player player, float deltaTime)
    {
        var cargoDefinition = _carry.FirstDefinition;
        var typedUnloadPoint = default(Vector3);
        var useUnloadPoint = cargoDefinition != null &&
            _markers.TryFindUnload(cargoDefinition, _resourceSelection, _visual!.Transform.position, out typedUnloadPoint);
        var deliveryPoint = useUnloadPoint ? typedUnloadPoint : player.transform.position + player.transform.forward * 1.5f;
        Func<ResourceDefinition, bool> accepts = useUnloadPoint
            ? definition => _markers.TryFindUnload(definition, _resourceSelection, _visual!.Transform.position, out var candidate) &&
                            Vector3.SqrMagnitude(candidate - deliveryPoint) < 0.25f
            : _ => true;
        SetState(SpiritState.CarryDrops);
        _visual!.Move(deliveryPoint + Vector3.up * 1.7f, 8f, deltaTime);
        if (Vector3.Distance(_visual.Transform.position, deliveryPoint) >= 2f) return;
        var container = useUnloadPoint ? FindContainer(deliveryPoint) : null;
        var delivered = 0;
        var usedContainer = false;
        if (container)
        {
            if (_containerUnloadStarted <= 0f) _containerUnloadStarted = Time.time;
            delivered = _carry.UnloadToContainer(container, accepts);
            usedContainer = delivered > 0;
            if (delivered == 0 && Time.time - _containerUnloadStarted >= 5f)
            {
                delivered = _carry.Unload(deliveryPoint, 1.4f, accepts);
                _lastError = "Container rejected or could not fit cargo; items were left safely nearby.";
            }
        }
        else delivered = _carry.Unload(deliveryPoint, 1.4f, accepts);
        if (delivered <= 0) return;
        _progression.Data.Automation.LongestTrip = Mathf.Max(_progression.Data.Automation.LongestTrip,
            Vector3.Distance(_lastWorkPosition, deliveryPoint));
        _containerUnloadStarted = 0f;
        _progression.Data.ItemsDelivered += delivered;
        _progression.Data.Bond += delivered * 0.05f;
        Award(_config.LogisticsXp.Value, _config.LogisticsXp.Value, Profession.Logistics);
        _forceDelivery = _carry.IsFull(EffectiveCarryStacks, EffectiveCarryWeight);
        _audio?.Play(SpiritAudioEvent.Unload);
        _ui.Notify(usedContainer
            ? $"Дух сложил груз в сундук: {delivered}."
            : useUnloadPoint ? $"Дух доставил груз к точке разгрузки: {delivered}." : $"Дух принёс груз к игроку: {delivered}.", 2f);
        SetState(SpiritState.Unload);
    }

    private void Scout(Player player, float deltaTime)
    {
        SetState(SpiritState.Scout);
        var point = player.transform.position + player.transform.forward * 22f + Vector3.up * 8f;
        _visual!.Move(point, 13f, deltaTime); SpendEnergy(0.3f * deltaTime);
        if (Time.time < _nextScoutReport) return;
        _nextScoutReport = Time.time + 6f;
        var radius = Mathf.Min((45f + TalentTier("exploration") * 10f + _knowledge.BiomeKnowledge(player.GetCurrentBiome()) * 0.2f) * _shrineScoutMultiplier, EffectiveSearchRadius);
        var report = _scanner.Survey(point, radius, _resourceSelection, _progression);
        var knownBefore = _progression.Data.Automation.WorldMemory.Count;
        _knowledge.RememberSurvey(report, CurrentDay());
        if (report.Count == 0) { _ui.Notify("Разведка: подходящих ресурсов не найдено.", 3f); return; }
        var lines = new List<string>();
        TargetScanner.SurveyEntry? nearest = null;
        string? nearestName = null;
        foreach (var entry in report)
        {
            lines.Add($"{ResourceName(entry.Key)}: {entry.Value.Count}");
            if (nearest == null || entry.Value.NearestDistance < nearest.NearestDistance)
            {
                nearest = entry.Value;
                nearestName = entry.Key;
            }
        }
        if (nearest != null)
        {
            _markers.ShowTemporaryMarker(nearest.NearestPosition, new Color(0.35f, 0.9f, 1f));
            lines.Add($"Ближайшее: {ResourceName(nearestName ?? string.Empty)}, {Vector3.Distance(player.transform.position, nearest.NearestPosition):0} м");
        }
        _ui.Notify("Разведка завершена\n" + string.Join(" • ", lines), 6f);
        if (_progression.Data.Automation.WorldMemory.Count > knownBefore)
            AddJournal($"Разведка открыла новые места: {_progression.Data.Automation.WorldMemory.Count - knownBefore}.");
    }

    private void Guard(Player player, float deltaTime)
    {
        SetState(SpiritState.Guard);
        var nearestDistance = _guardThreat && !_guardThreat.IsDead()
            ? Vector3.SqrMagnitude(_guardThreat.transform.position - player.transform.position)
            : 25f * 25f;
        if (Time.time >= _nextGuardScan)
        {
            _nextGuardScan = Time.time + 0.75f;
            _guardThreat = null;
            nearestDistance = 25f * 25f;
            foreach (var character in Character.GetAllCharacters())
            {
                if (!character || character == player || character.IsDead() || !BaseAI.IsEnemy(player, character)) continue;
                var distance = Vector3.SqrMagnitude(character.transform.position - player.transform.position);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                _guardThreat = character;
            }
        }
        var point = _guardThreat
            ? Vector3.Lerp(player.transform.position, _guardThreat.transform.position, 0.55f) + Vector3.up * 2.5f
            : player.transform.position + new Vector3(Mathf.Cos(Time.time), 0.85f, Mathf.Sin(Time.time)) * 3f + Vector3.up * 1.7f;
        _visual!.Move(point, 8f, deltaTime);
        if (_guardThreat && Time.time >= _nextGuardNotice)
        {
            _nextGuardNotice = Time.time + 5f;
            var critical = player.GetHealthPercentage() < 0.3f;
            _ui.Notify(critical
                ? $"Опасность: мало здоровья, враг в {Mathf.Sqrt(nearestDistance):0} м!"
                : $"Дозор: враг в {Mathf.Sqrt(nearestDistance):0} м!", 3f);
            _audio?.Play(SpiritAudioEvent.DangerDetected);
        }
    }

    private void Observe(Player player, float deltaTime)
    {
        SetState(SpiritState.Observe);
        Follow(player, deltaTime);
        if (Time.time < _nextObservation || !Input.GetMouseButton(0) && !Input.GetKey(KeyCode.E)) return;
        _nextObservation = Time.time + 0.6f;
        var definition = LookResource();
        if (definition == null || definition == _observedResource) return;
        _observedResource = definition;
        _knowledge.Remember(definition.Name, player.transform.position, CurrentDay());
        _knowledge.RecordDecision($"Learned player action: {definition.Name}");
        _ui.Notify($"✦ Дух понял действие с {ResourceName(definition.Name)}. Контекстная команда повторит его рядом.", 5f);
        _audio?.Play(SpiritAudioEvent.TargetFound);
    }

    private void Assist(Player player, float deltaTime)
    {
        SetState(SpiritState.Assist);
        if (Time.time >= _nextObservation)
        {
            _nextObservation = Time.time + 0.35f;
            var definition = LookResource();
            if (definition != null)
            {
                _observedResource = definition;
                _assistWorkJob = JobFor(definition);
                _resourceSelection.SelectExact(definition.Name);
            }
        }
        if (_observedResource == null) { Cleanup(player, deltaTime); return; }
        WorkJob(player, deltaTime);
    }

    private void Cleanup(Player player, float deltaTime)
    {
        SetState(SpiritState.CollectDrops);
        var centre = player.transform.position - player.transform.forward * 2f;
        var nearest = _carry.FindNearest(centre, Mathf.Min(30f, WorkRadius), _resourceSelection);
        if (nearest.HasValue)
        {
            _visual!.Move(nearest.Value + Vector3.up * 1.2f, _config.FollowSpeed.Value, deltaTime);
            if (Vector3.Distance(_visual.Transform.position, nearest.Value) <= 3f)
            {
                _carry.TryCollect(nearest.Value, EffectiveCarryStacks, EffectiveCarryWeight, _resourceSelection);
                if (ZoneSystem.instance && nearest.Value.y < ZoneSystem.instance.GetGroundHeight(nearest.Value) + 0.15f)
                    _progression.Data.Automation.ItemsRescued++;
            }
        }
        else
        {
            _assistWorkJob = _resourceSelection.Mode == ResourceFilterMode.Category
                ? _resourceSelection.Category == ResourceCategory.Wood ? SpiritJob.Woodcutting
                    : _resourceSelection.Category is ResourceCategory.Ore or ResourceCategory.Stone ? SpiritJob.Mining : SpiritJob.Gathering
                : SpiritJob.Woodcutting;
            WorkJob(player, deltaTime);
        }
        if (_carry.IsFull(EffectiveCarryStacks, EffectiveCarryWeight)) DeliverCarried(player, deltaTime);
    }

    private void Courier(Player player, float deltaTime)
    {
        var route = _progression.Data.Automation.CourierRoute;
        if (!route.Enabled) { Follow(player, deltaTime); return; }
        var source = SpiritKnowledge.ToVector(route.Source);
        var destination = SpiritKnowledge.ToVector(route.Destination);
        if (_carry.Count == 0)
        {
            SetState(SpiritState.CollectDrops);
            _visual!.Move(source + Vector3.up * 1.5f, _config.FollowSpeed.Value, deltaTime);
            if (Vector3.Distance(_visual.Transform.position, source) <= 3f)
                _carry.TryCollect(source, EffectiveCarryStacks, EffectiveCarryWeight, _resourceSelection);
            return;
        }
        SetState(SpiritState.CarryDrops);
        _visual!.Move(destination + Vector3.up * 1.5f, _config.FollowSpeed.Value, deltaTime);
        if (Vector3.Distance(_visual.Transform.position, destination) <= 2.5f)
            _carry.Unload(destination, 1.3f, _ => true);
    }

    private void Caravan(Player player, float deltaTime)
    {
        SetState(SpiritState.CarryDrops);
        Follow(player, deltaTime);
    }

    private void Bring(Player player, float deltaTime)
    {
        if (_carry.Count > 0)
        {
            SetState(SpiritState.CarryDrops);
            var destination = player.transform.position + player.transform.forward * 1.3f;
            _visual!.Move(destination + Vector3.up * 1.5f, _config.FollowSpeed.Value, deltaTime);
            if (Vector3.Distance(_visual.Transform.position, destination) <= 2f)
                _carry.Unload(destination, 1f, _ => true);
            return;
        }
        var nearest = _carry.FindNearest(player.transform.position, WorkRadius, _resourceSelection);
        if (!nearest.HasValue)
        {
            var container = FindNearestContainer(player.transform.position, WorkRadius);
            if (!container) { Follow(player, deltaTime); return; }
            _visual!.Move(container.transform.position + Vector3.up * 1.5f, _config.FollowSpeed.Value, deltaTime);
            if (Vector3.Distance(_visual.Transform.position, container.transform.position) <= 3f)
                _carry.TryWithdraw(container, container.transform.position + Vector3.up * 1.2f,
                    EffectiveCarryStacks, EffectiveCarryWeight, _resourceSelection);
            return;
        }
        _visual!.Move(nearest.Value + Vector3.up, _config.FollowSpeed.Value, deltaTime);
        if (Vector3.Distance(_visual.Transform.position, nearest.Value) <= 3f)
            _carry.TryCollect(nearest.Value, EffectiveCarryStacks, EffectiveCarryWeight, _resourceSelection);
    }

    private void SortCargo(Player player, float deltaTime)
    {
        if (_carry.Count == 0)
        {
            var nearest = _carry.FindNearest(player.transform.position, 14f, _resourceSelection);
            if (!nearest.HasValue) { Follow(player, deltaTime); return; }
            _visual!.Move(nearest.Value + Vector3.up, _config.FollowSpeed.Value, deltaTime);
            if (Vector3.Distance(_visual.Transform.position, nearest.Value) <= 3f)
                _carry.TryCollect(nearest.Value, EffectiveCarryStacks, EffectiveCarryWeight, _resourceSelection);
            return;
        }
        DeliverCarried(player, deltaTime);
    }

    private void Patrol(Player player, float deltaTime)
    {
        SetState(SpiritState.Guard);
        var centre = _markers.GetWorkCentre(player);
        var patrolPoint = centre + new Vector3(Mathf.Cos(Time.time * 0.4f) * 8f, 3f, Mathf.Sin(Time.time * 0.4f) * 8f);
        _visual!.Move(patrolPoint, 8f, deltaTime);
        if (Vector3.Distance(player.transform.position, centre) <= WorkRadius * 2f) Cleanup(player, deltaTime);
        TickBaseAlarm(player, centre);
    }

    private void Guide(Player player, float deltaTime)
    {
        SetState(SpiritState.Guide);
        if (!_guideDestination.HasValue) { Follow(player, deltaTime); return; }
        var destination = _guideDestination.Value;
        var distance = Vector3.Distance(player.transform.position, destination);
        if (distance <= 4f)
        {
            _ui.Notify($"Мы пришли: {_guideLabel}.");
            _guideDestination = null;
            SetJob(SpiritJob.Follow);
            return;
        }
        var direction = (destination - player.transform.position).normalized;
        var lead = player.transform.position + direction * Mathf.Min(9f, distance) + Vector3.up * 2.5f;
        _visual!.Move(lead, 12f, deltaTime);
    }

    private void ExploreDungeon(Player player, float deltaTime)
    {
        SetState(SpiritState.Scout);
        var point = player.transform.position + player.transform.forward * 7f + Vector3.up * 2.2f;
        _visual!.Move(point, 10f, deltaTime);
        if (Time.time < _nextAdvancedTick) return;
        _nextAdvancedTick = Time.time + 2f;
        var drop = _carry.FindNearest(point, 14f, _resourceSelection);
        if (drop.HasValue) _markers.ShowTemporaryMarker(drop.Value, new Color(0.8f, 0.65f, 1f), 8f);
        DetectNearbySecret(point, 18f);
        TickThreatWarning(player, 18f);
    }

    private void Expedition(Player player, float deltaTime)
    {
        SetState(SpiritState.Expedition);
        if (_expeditionEndsAt <= 0f)
        {
            _expeditionEndsAt = Time.time + 45f;
            _ui.Notify("Дух отправился в разведочную экспедицию.");
        }
        var progress = 1f - Mathf.Clamp01((_expeditionEndsAt - Time.time) / 45f);
        var angle = progress * Mathf.PI * 8f;
        var point = player.transform.position + new Vector3(Mathf.Cos(angle), 0.4f, Mathf.Sin(angle)) * (20f + progress * WorkRadius) + Vector3.up * 12f;
        _visual!.Move(point, 18f, deltaTime);
        if (Time.time < _expeditionEndsAt) return;
        _expeditionEndsAt = 0f;
        var report = _scanner.Survey(player.transform.position, WorkRadius, _resourceSelection, _progression);
        _knowledge.RememberSurvey(report, CurrentDay());
        _ui.Notify($"Экспедиция завершена: разведано {WorkRadius:0} м, типов ресурсов найдено: {report.Count}.", 7f);
        AddJournal($"Экспедиция обнаружила {report.Count} типов ресурсов.");
        SetJob(SpiritJob.Follow);
    }

    private void Production(Player player, float deltaTime)
    {
        SetState(SpiritState.Production);
        SortCargo(player, deltaTime);
        if (Time.time >= _nextAdvancedTick)
        {
            _nextAdvancedTick = Time.time + 6f;
            _ui.Notify("Производственная логистика: готовые drops переносятся к специализированным складам.", 3f);
        }
    }

    private void StartBuildAssist()
    {
        var player = Player.m_localPlayer;
        var piece = player ? player.GetSelectedPiece() : null;
        if (!piece || piece.m_resources == null || piece.m_resources.Length == 0)
        {
            _ui.Notify("Возьмите молот и выберите строительную деталь.");
            return;
        }
        foreach (var requirement in piece.m_resources)
        {
            if (!requirement.m_resItem) continue;
            var definition = _resources.ByDrop(requirement.m_resItem.gameObject.name);
            if (definition == null) continue;
            _resourceSelection.SelectExact(definition.Name);
            SetJob(SpiritJob.Bring);
            _ui.Notify($"Строительный помощник несёт {ResourceName(definition.Name)} для {piece.m_name}.");
            return;
        }
        _ui.Notify("Для выбранной детали нет известного духу материала.");
    }

    private static Container? FindNearestContainer(Vector3 centre, float radius)
    {
        var hits = Physics.OverlapSphere(centre, radius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        Container? nearest = null;
        var nearestDistance = float.MaxValue;
        var seen = new HashSet<int>();
        foreach (var hit in hits)
        {
            var container = hit ? hit.GetComponentInParent<Container>() : null;
            if (!container || !seen.Add(container.GetInstanceID())) continue;
            var distance = Vector3.SqrMagnitude(container.transform.position - centre);
            if (distance >= nearestDistance) continue;
            nearest = container;
            nearestDistance = distance;
        }
        return nearest;
    }

    private void FollowNearWorkCentre(Player player, float deltaTime)
    {
        SetState(SpiritState.Idle);
        var centre = _markers.GetWorkCentre(player);
        var point = centre + new Vector3(Mathf.Cos(Time.time * 0.5f) * 2f, 2f, Mathf.Sin(Time.time * 0.5f) * 2f);
        _visual!.Move(point, 6f, deltaTime);
    }

    private void SetJob(SpiritJob job)
    {
        if (!IsJobUnlocked(job, out var requiredLevel))
        {
            _ui.Notify($"Эта способность откроется на уровне {requiredLevel}.");
            _audio?.Play(SpiritAudioEvent.InvalidTarget);
            return;
        }
        if (_queueMode && job is SpiritJob.Woodcutting or SpiritJob.Mining or SpiritJob.Gathering)
        {
            _assignments.Enqueue(new SpiritAssignment(job, _selectedQuantity));
            _ui.Notify($"Добавлено в очередь: {SpiritUI.JobName(job)}, {QuantityName(_selectedQuantity)}.");
            return;
        }
        _job = job; _assignmentQuantity = _selectedQuantity; _assignmentProgress = 0;
        _containerUnloadStarted = 0f;
        _target = null; _workAttempts = 0; _awaitingTreeLog = false;
        _ui.Notify($"Задача духа: {SpiritUI.JobName(job)}.");
    }

    private void HandleMenuAction(SpiritMenuAction action)
    {
        switch (action)
        {
            case SpiritMenuAction.Follow: SetJob(SpiritJob.Follow); break;
            case SpiritMenuAction.Woodcutting: SetJob(SpiritJob.Woodcutting); break;
            case SpiritMenuAction.Mining: SetJob(SpiritJob.Mining); break;
            case SpiritMenuAction.Gathering: SetJob(SpiritJob.Gathering); break;
            case SpiritMenuAction.Transport: SetJob(SpiritJob.Transport); break;
            case SpiritMenuAction.Scout: SetJob(SpiritJob.Scout); break;
            case SpiritMenuAction.Guard: SetJob(SpiritJob.Guard); break;
            case SpiritMenuAction.Rest: SetJob(SpiritJob.Rest); break;
            case SpiritMenuAction.Stop: SetJob(SpiritJob.Stopped); break;
            case SpiritMenuAction.PlaceUnload: PlaceUnloadPoint(); break;
            case SpiritMenuAction.RemoveUnload:
                _markers.RemoveUnload(); _ui.Notify("Точка разгрузки удалена."); _audio?.Play(SpiritAudioEvent.UnloadPointRemoved); break;
            case SpiritMenuAction.CycleUnloadType:
                _markers.CycleUnloadType(); _ui.Notify($"Тип разгрузки: {UnloadTypeName(_markers.UnloadType)}."); _audio?.Play(SpiritAudioEvent.MenuSelect); break;
            case SpiritMenuAction.CycleZoneMode:
                _markers.CycleZoneMode(); _ui.Notify($"Режим рабочей зоны: {ZoneModeName(_markers.ZoneMode)}."); break;
            case SpiritMenuAction.PlaceWorkZone: PlaceWorkZone(); break;
            case SpiritMenuAction.RemoveWorkZone:
                _markers.RemoveWorkZone(); _ui.Notify("Рабочая зона удалена. Используется зона вокруг игрока."); break;
            case SpiritMenuAction.FilterAll: SetFilterAll(); break;
            case SpiritMenuAction.FilterWood: SetCategoryFilter(ResourceCategory.Wood, "Древесина"); break;
            case SpiritMenuAction.FilterStone: SetCategoryFilter(ResourceCategory.Stone, "Камень"); break;
            case SpiritMenuAction.FilterOre: SetCategoryFilter(ResourceCategory.Ore, "Руда"); break;
            case SpiritMenuAction.FilterPlants: SetCategoryFilter(ResourceCategory.Plant, "Растения"); break;
            case SpiritMenuAction.FilterFood: SetCategoryFilter(ResourceCategory.Food, "Еда"); break;
            case SpiritMenuAction.FilterExactWood: SetExactFilter("Wood", "Древесина"); break;
            case SpiritMenuAction.FilterExactStone: SetExactFilter("Stone", "Камень"); break;
            case SpiritMenuAction.FilterExactCopper: SetExactFilter("Copper", "Медь"); break;
            case SpiritMenuAction.FilterExactTin: SetExactFilter("Tin", "Олово"); break;
            case SpiritMenuAction.FilterExactSilver: SetExactFilter("Silver", "Серебро"); break;
            case SpiritMenuAction.FilterExactObsidian: SetExactFilter("Obsidian", "Обсидиан"); break;
            case SpiritMenuAction.FilterExactBlackMarble: SetExactFilter("Black Marble", "Чёрный мрамор"); break;
            case SpiritMenuAction.FilterExactBerries: SetExactFilter("Berries", "Ягоды"); break;
            case SpiritMenuAction.FilterExactPlants: SetExactFilter("Plants", "Прочие растения"); break;
            case SpiritMenuAction.Recall: RecallSpirit(); break;
            case SpiritMenuAction.ScanResources: ScanResources(); break;
            case SpiritMenuAction.ToggleTorch:
            case SpiritMenuAction.CycleLightMode:
                _visual?.CycleLightMode(); _ui.Notify($"Освещение: {LightModeName(_config.LightMode.Value)}."); break;
            case SpiritMenuAction.CycleBalance: CycleBalanceMode(); break;
            case SpiritMenuAction.ToggleAutoCarry:
                _config.AutoCarry.Value = !_config.AutoCarry.Value; _ui.Notify($"Автоперенос: {OnOff(_config.AutoCarry.Value)}."); break;
            case SpiritMenuAction.ToggleHud:
                _config.EnableHud.Value = !_config.EnableHud.Value; _ui.Notify($"Панель духа: {OnOff(_config.EnableHud.Value)}."); break;
            case SpiritMenuAction.ToggleWorkZoneVisibility:
                _config.ShowWorkZone.Value = !_config.ShowWorkZone.Value;
                var player = Player.m_localPlayer;
                if (player) _markers.SetWorkZoneVisible(_config.ShowWorkZone.Value, _markers.GetWorkCentre(player), WorkRadius);
                _ui.Notify($"Граница рабочей зоны: {OnOff(_config.ShowWorkZone.Value)}."); break;
            case SpiritMenuAction.CycleWorkRadius: CycleWorkRadius(); break;
            case SpiritMenuAction.CycleOrbSize: CycleFloat(_config.OrbSize, new[] { 0.25f, 0.38f, 0.55f, 0.75f }, "Размер шара", "0.00"); break;
            case SpiritMenuAction.CycleOrbColor: CycleColor(_config.OrbColor, new[] { "#33E6FF", "#8C6CFF", "#62FF8B", "#FFD65A", "#FF668F", "#FFFFFF" }, "Цвет шара"); break;
            case SpiritMenuAction.CycleFollowDistance: CycleFloat(_config.FollowDistance, new[] { 1f, 1.7f, 2.5f, 4f, 6f }, "Расстояние от игрока", "0.0 м"); break;
            case SpiritMenuAction.CycleFollowHeight: CycleFloat(_config.FollowHeight, new[] { 1f, 2f, 3f, 4.5f, 6f }, "Высота полёта", "0.0 м"); break;
            case SpiritMenuAction.CycleFollowSpeed: CycleFloat(_config.FollowSpeed, new[] { 5f, 9f, 13f, 18f, 25f }, "Скорость полёта", "0"); break;
            case SpiritMenuAction.CycleInertia: CycleFloat(_config.MovementInertia, new[] { 0.08f, 0.18f, 0.3f, 0.55f, 0.9f }, "Инертность", "0.00"); break;
            case SpiritMenuAction.CycleBobbing: CycleFloat(_config.BobbingAmount, new[] { 0f, 0.12f, 0.25f, 0.45f, 0.7f }, "Покачивание", "0.00"); break;
            case SpiritMenuAction.CycleLightIntensity: CycleFloat(_config.LightIntensity, new[] { 0.5f, 1f, 1.5f, 2.5f, 4f }, "Яркость света", "0.0"); break;
            case SpiritMenuAction.CycleLightRange: CycleFloat(_config.LightRange, new[] { 6f, 10f, 14f, 20f, 30f }, "Дальность света", "0 м"); break;
            case SpiritMenuAction.CycleLightDistance: CycleFloat(_config.CameraLightDistance, new[] { 3f, 6f, 9f, 12f, 16f }, "Свет перед камерой", "0 м"); break;
            case SpiritMenuAction.CycleLightColor: CycleColor(_config.LightColor, new[] { "#B8F4FF", "#FFF0C2", "#FFFFFF", "#A8C7FF", "#C6FFB0" }, "Цвет света"); break;
            case SpiritMenuAction.CycleSafeRadius: CycleFloat(_config.SafeBaseRadius, new[] { 0f, 5f, 10f, 15f, 25f }, "Защита построек", "0 м"); break;
            case SpiritMenuAction.ToggleProtectTrees:
                _config.ProtectPlantedTrees.Value = !_config.ProtectPlantedTrees.Value; _ui.Notify($"Защита посаженных деревьев: {OnOff(_config.ProtectPlantedTrees.Value)}."); break;
            case SpiritMenuAction.ToggleProtectCrops:
                _config.ProtectCrops.Value = !_config.ProtectCrops.Value; _ui.Notify($"Защита урожая: {OnOff(_config.ProtectCrops.Value)}."); break;
            case SpiritMenuAction.TalentWoodcutting: UnlockTalent("woodcutting", "Лесоруб"); break;
            case SpiritMenuAction.TalentMining: UnlockTalent("mining", "Шахтёр"); break;
            case SpiritMenuAction.TalentLogistics: UnlockTalent("logistics", "Носильщик"); break;
            case SpiritMenuAction.TalentExploration: UnlockTalent("exploration", "Следопыт"); break;
            case SpiritMenuAction.CycleQuantity: CycleQuantity(); break;
            case SpiritMenuAction.ToggleQueueMode:
                if (_progression.Level < 20) _ui.Notify("Очередь и сложные маршруты открываются на уровне 20.");
                else { _queueMode = !_queueMode; _ui.Notify($"Добавление в очередь: {OnOff(_queueMode)}."); }
                break;
            case SpiritMenuAction.ClearQueue:
                _assignments.Clear(); _ui.Notify("Очередь поручений очищена."); break;
            case SpiritMenuAction.IgnoreLookTarget: IgnoreLookTarget(); break;
            case SpiritMenuAction.PlaceForbiddenZone: PlaceForbiddenZone(); break;
            case SpiritMenuAction.RemoveForbiddenZone:
                _markers.RemoveForbiddenZone(); _ui.Notify("Запретная зона удалена."); break;
            case SpiritMenuAction.ShowStatistics: ShowStatistics(); break;
            case SpiritMenuAction.ShowJournal: ShowJournal(); break;
            case SpiritMenuAction.ShowDecisions: ShowDecisions(); break;
            case SpiritMenuAction.ExplainIdle: ExplainIdle(); break;
            case SpiritMenuAction.InspectTarget: InspectLookTarget(false); break;
            case SpiritMenuAction.ContextCommand: InspectLookTarget(true); break;
            case SpiritMenuAction.Observe: SetJob(SpiritJob.Observe); break;
            case SpiritMenuAction.Assist: SetJob(SpiritJob.Assist); break;
            case SpiritMenuAction.Cleanup: SetJob(SpiritJob.Cleanup); break;
            case SpiritMenuAction.FindLost: FindLostItems(); break;
            case SpiritMenuAction.Courier: ConfigureCourier(); break;
            case SpiritMenuAction.Caravan: SetJob(SpiritJob.Caravan); break;
            case SpiritMenuAction.Bring: SetJob(SpiritJob.Bring); break;
            case SpiritMenuAction.BuildAssist: StartBuildAssist(); break;
            case SpiritMenuAction.Sort: SetJob(SpiritJob.Sort); break;
            case SpiritMenuAction.Patrol: SetJob(SpiritJob.Patrol); break;
            case SpiritMenuAction.GuideHome: GuideToNamedZone(NamedZoneType.Home, "дом"); break;
            case SpiritMenuAction.GuideUnload: GuideToUnload(); break;
            case SpiritMenuAction.GuideWorkZone: StartGuide(_markers.GetWorkCentre(Player.m_localPlayer), "рабочая зона"); break;
            case SpiritMenuAction.GuideGrave: GuideToGrave(); break;
            case SpiritMenuAction.GuideBoat: GuideToRememberedPoint(_progression.Data.Automation.HasLastBoatPosition,
                _progression.Data.Automation.LastBoatPosition, "последний корабль"); break;
            case SpiritMenuAction.GuideBed: GuideToRememberedPoint(_progression.Data.Automation.HasLastBedPosition,
                _progression.Data.Automation.LastBedPosition, "последняя кровать"); break;
            case SpiritMenuAction.GuideResource: GuideToRememberedResource(); break;
            case SpiritMenuAction.ExploreDungeon: SetJob(SpiritJob.ExploreDungeon); break;
            case SpiritMenuAction.Expedition: SetJob(SpiritJob.Expedition); break;
            case SpiritMenuAction.PlaceShrine: PlaceShrine(); break;
            case SpiritMenuAction.UpgradeShrine: UpgradeShrine(); break;
            case SpiritMenuAction.ChargeShrine: GuideToShrine(); break;
            case SpiritMenuAction.FeedShrine: FeedShrine(); break;
            case SpiritMenuAction.SaveNamedZone: SaveNamedZone(); break;
            case SpiritMenuAction.CycleNamedZone: CycleNamedZone(); break;
            case SpiritMenuAction.CycleNamedZoneType:
                _selectedNamedZoneType = (NamedZoneType)(((int)_selectedNamedZoneType + 1) % Enum.GetValues(typeof(NamedZoneType)).Length);
                _ui.Notify($"Тип новой зоны: {_selectedNamedZoneType}."); break;
            case SpiritMenuAction.ToggleSuggestions:
                _progression.Data.Automation.SuggestionsEnabled = !_progression.Data.Automation.SuggestionsEnabled;
                _ui.Notify($"Предложения духа: {OnOff(_progression.Data.Automation.SuggestionsEnabled)}."); break;
            case SpiritMenuAction.ToggleRepeatOrders:
                _progression.Data.Automation.RepeatOrders = !_progression.Data.Automation.RepeatOrders;
                _ui.Notify($"Повтор поручений: {OnOff(_progression.Data.Automation.RepeatOrders)}."); break;
            case SpiritMenuAction.CycleMaintainStock: CycleMaintainStock(); break;
            case SpiritMenuAction.ToggleSchedule: ToggleSchedule(); break;
            case SpiritMenuAction.ToggleRules: ToggleRules(); break;
            case SpiritMenuAction.BaseAlarm:
                _baseAlarmEnabled = !_baseAlarmEnabled;
                _progression.Data.Automation.BaseAlarmEnabled = _baseAlarmEnabled;
                _ui.Notify($"Сигнализация базы: {OnOff(_baseAlarmEnabled)}."); break;
            case SpiritMenuAction.DetectSecret:
                _secretDetectorEnabled = !_secretDetectorEnabled;
                _progression.Data.Automation.SecretDetectorEnabled = _secretDetectorEnabled;
                _ui.Notify($"Детектор секретов: {OnOff(_secretDetectorEnabled)}."); break;
            case SpiritMenuAction.LearnTarget: LearnLookTarget(); break;
            case SpiritMenuAction.Production: SetJob(SpiritJob.Production); break;
            case SpiritMenuAction.ReleaseSpirit: Prestige(); break;
            case SpiritMenuAction.ShowMemory: ShowMemory(); break;
            case SpiritMenuAction.ShowBiomeKnowledge: ShowBiomeKnowledge(); break;
        }
    }

    private void UnlockTalent(string branch, string displayName)
    {
        var unlocked = _progression.UnlockNextTalent(branch);
        if (unlocked == null)
        {
            _ui.Notify(_progression.AvailableTalentPoints == 0
                ? "Нет свободных очков талантов. Новое очко даётся каждые 5 уровней."
                : $"Ветка «{displayName}» уже изучена полностью.");
            return;
        }
        _ui.Notify($"Открыт талант: {displayName} {unlocked[unlocked.Length - 1]}.");
        _audio?.Play(SpiritAudioEvent.TalentUnlocked);
    }

    private void CycleQuantity()
    {
        _selectedQuantity = _selectedQuantity switch { 0 => 10, 10 => 25, 25 => 50, _ => 0 };
        _ui.Notify($"Количество для новых поручений: {QuantityName(_selectedQuantity)}.");
    }

    private static string QuantityName(int quantity) => quantity == 0 ? "без ограничения" : quantity.ToString();

    private void CountAssignmentProgress()
    {
        if (_assignmentQuantity <= 0) return;
        _assignmentProgress++;
        if (_assignmentProgress < _assignmentQuantity) return;
        _ui.Notify($"Поручение выполнено: {SpiritUI.JobName(_job)} {_assignmentProgress}/{_assignmentQuantity}.");
        _audio?.Play(SpiritAudioEvent.JobComplete);
        _celebrateUntil = Time.time + 2.2f;
        if (_progression.Data.Automation.RepeatOrders)
        {
            _assignmentProgress = 0;
            _ui.Notify("Повторяющееся поручение запущено заново.");
            return;
        }
        if (_assignments.Count == 0)
        {
            _job = SpiritJob.Follow;
            _assignmentQuantity = 0;
            _assignmentProgress = 0;
            return;
        }
        var next = _assignments.Dequeue();
        _job = next.Job;
        _assignmentQuantity = next.Quantity;
        _assignmentProgress = 0;
    }

    private void SetFilterAll()
    {
        _resourceSelection.AllowAll(); ResetTargetAndClaims(); _ui.Notify("Фильтр: все подходящие ресурсы.");
    }

    private void SetCategoryFilter(ResourceCategory category, string displayName)
    {
        _resourceSelection.SelectCategory(category); ResetTargetAndClaims(); _ui.Notify($"Фильтр: {displayName}.");
    }

    private void SetExactFilter(string resourceName, string displayName)
    {
        _resourceSelection.SelectExact(resourceName); ResetTargetAndClaims(); _ui.Notify($"Искать и добывать: {displayName}.");
    }

    private void RecallSpirit()
    {
        var player = Player.m_localPlayer;
        if (!player || _visual == null) return;
        _target = null; _awaitingTreeLog = false;
        SetJob(SpiritJob.Follow); _audio?.Play(SpiritAudioEvent.Recall); _ui.Notify("Дух летит к игроку.");
    }

    private void ScanResources()
    {
        var player = Player.m_localPlayer;
        if (!player) return;
        SpiritTarget? nearest = null;
        foreach (var job in new[] { SpiritJob.Woodcutting, SpiritJob.Mining, SpiritJob.Gathering })
        {
            var candidate = _scanner.Find(player.transform.position, WorkRadius, job, _resourceSelection,
                _config.SafeBaseRadius.Value, _config.ProtectPlantedTrees.Value, _config.ProtectCrops.Value, _progression, ActiveBlacklist(), _markers.IsForbidden);
            if (candidate != null && (nearest == null || Vector3.SqrMagnitude(candidate.Position - player.transform.position) < Vector3.SqrMagnitude(nearest.Position - player.transform.position)))
                nearest = candidate;
        }
        if (nearest == null) _ui.Notify("Подходящие ресурсы не найдены.");
        else _ui.Notify($"Найдено: {ResourceName(nearest.Definition.Name)}, расстояние {Vector3.Distance(player.transform.position, nearest.Position):0} м.");
        _audio?.Play(SpiritAudioEvent.ScanPulse);
    }

    private void CycleBalanceMode()
    {
        _config.BalanceMode.Value = (BalanceMode)(((int)_config.BalanceMode.Value + 1) % 3);
        _ui.Notify($"Режим баланса: {BalanceName(_config.BalanceMode.Value)}.");
    }

    private void CycleWorkRadius()
    {
        var next = _config.WorkRadius.Value < 25f ? 25f : _config.WorkRadius.Value < 50f ? 50f : _config.WorkRadius.Value < 75f ? 75f : _config.WorkRadius.Value < 100f ? 100f : 25f;
        _config.WorkRadius.Value = Mathf.Min(next, _config.MaxWorkRadius.Value);
        if (_markers.HasWorkZone) _markers.SetWorkZone(_markers.WorkZoneCentre, WorkRadius);
        _ui.Notify($"Радиус работы: {WorkRadius:0} м.");
    }

    private static string OnOff(bool value) => value ? "включён" : "выключен";
    private static string BalanceName(BalanceMode mode) => mode switch { BalanceMode.Vanilla => "Ванильный", BalanceMode.Balanced => "Сбалансированный", _ => "Свободный" };
    private static string ResourceName(string name) => name switch
    {
        "Wood" => "Древесина", "Stone" => "Камень", "Copper" => "Медь", "Tin" => "Олово",
        "Silver" => "Серебро", "Obsidian" => "Обсидиан", "Black Marble" => "Чёрный мрамор",
        "Berries" => "Ягоды", "Plants" => "Растения", _ => name
    };

    private void PlaceUnloadPoint()
    {
        var player = Player.m_localPlayer;
        if (!player || !TryPlacementPoint(player, out var point)) { _ui.Notify("Не удалось определить поверхность для точки."); return; }
        if (_progression.Level < 7) { _ui.Notify("Точки разгрузки открываются на уровне 7."); return; }
        var maximumPoints = _progression.Level >= 25 ? 6 : _progression.Level >= 15 ? 3 : 1;
        _markers.SetUnload(point, maximumPoints);
        _ui.Notify($"Точка разгрузки установлена (доступно: {maximumPoints}).");
        _audio?.Play(SpiritAudioEvent.UnloadPointPlaced);
    }

    private void PlaceWorkZone()
    {
        var player = Player.m_localPlayer;
        if (_progression.Level < 5) { _ui.Notify("Рабочая зона открывается на уровне 5."); return; }
        if (!player || !TryPlacementPoint(player, out var point)) { _ui.Notify("Не удалось определить поверхность для зоны."); return; }
        _markers.SetWorkZone(point, WorkRadius, _config.ShowWorkZone.Value); _ui.Notify("Рабочая зона установлена."); _audio?.Play(SpiritAudioEvent.WorkZonePlaced);
    }

    private void PlaceForbiddenZone()
    {
        var player = Player.m_localPlayer;
        if (!player || !TryPlacementPoint(player, out var point)) { _ui.Notify("Не удалось определить поверхность."); return; }
        _markers.SetForbiddenZone(point);
        _ui.Notify("Запретная зона радиусом 10 м установлена.");
    }

    private void IgnoreLookTarget()
    {
        var camera = Camera.main;
        if (!camera || !Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit, 40f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            _ui.Notify("Объект для игнорирования не найден.");
            return;
        }
        Component? component = hit.collider.GetComponentInParent<Pickable>();
        component ??= hit.collider.GetComponentInParent<TreeLog>();
        component ??= hit.collider.GetComponentInParent<TreeBase>();
        component ??= hit.collider.GetComponentInParent<MineRock5>();
        component ??= hit.collider.GetComponentInParent<MineRock>();
        if (!component) { _ui.Notify("Этот объект нельзя добавить в список игнорирования."); return; }
        _targetBlacklist[component.GetInstanceID()] = float.PositiveInfinity;
        if (_target?.Component == component) _target = null;
        _ui.Notify("Объект добавлен в список игнорирования до выхода из мира.");
    }

    private void ShowStatistics()
    {
        var data = _progression.Data;
        _ui.Notify($"Статистика {data.SpiritName}\nДерево: {data.TreesWorked} • Руда/камень: {data.OreWorked} • Растения: {data.PlantsGathered}\nДоставлено: {data.ItemsDelivered} • Полёт: {data.DistanceFlown / 1000f:0.0} км • Связь: {data.Bond:0}", 9f);
        _ui.Notify($"Любимый ресурс: {_knowledge.FavoriteResource} • Спасено предметов: {data.Automation.ItemsRescued} • Дальний маршрут: {data.Automation.LongestTrip:0} м • Вместе: {data.Automation.TimeTogetherSeconds / 3600d:0.0} ч • Prestige: {data.Automation.PrestigeCount}", 9f);
    }

    private void ShowJournal()
    {
        var journal = _progression.Data.Journal;
        if (journal.Count == 0) { _ui.Notify("Журнал духа пока пуст."); return; }
        var start = Math.Max(0, journal.Count - 4);
        _ui.Notify("Журнал духа\n" + string.Join("\n", journal.GetRange(start, journal.Count - start)), 10f);
    }

    private void ShowDecisions()
    {
        var decisions = _progression.Data.Automation.DecisionHistory;
        if (decisions.Count == 0) { _ui.Notify("История решений пока пуста."); return; }
        var lines = new List<string>();
        for (var index = Math.Max(0, decisions.Count - 8); index < decisions.Count; index++)
            lines.Add($"{decisions[index].Time} {decisions[index].Message}");
        _ui.Notify("Последние решения\n" + string.Join("\n", lines), 12f);
    }

    private void ExplainIdle()
    {
        var reason = _lastError;
        if (string.IsNullOrWhiteSpace(reason))
            reason = _target != null ? $"Выполняю: {_target.Definition.Name}." :
                _carry.Count > 0 && !_markers.HasUnloadPoint ? "Груз есть, но точка разгрузки не назначена." :
                _state == SpiritState.SearchTarget ? $"Подходящих целей нет в радиусе {WorkRadius:0} м." :
                $"Текущее состояние: {SpiritUI.JobName(_job)} / {_state}.";
        _ui.Notify("Почему дух ждёт:\n" + reason, 7f);
    }

    private void InspectLookTarget(bool executeContextCommand)
    {
        var camera = Camera.main;
        var player = Player.m_localPlayer;
        if (!camera || !player || !Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit, 40f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            _ui.Notify("Дух не видит подходящий объект.");
            return;
        }
        var container = hit.collider.GetComponentInParent<Container>();
        if (container)
        {
            if (executeContextCommand)
            {
                var maximumPoints = _progression.Level >= 25 ? 6 : _progression.Level >= 15 ? 3 : 1;
                _markers.SetUnload(hit.point + hit.normal * 0.06f, maximumPoints);
                _ui.Notify("Сундук назначен точкой разгрузки.");
            }
            else _ui.Notify("Контейнер. Дух может разгружать сюда предметы при наличии доступа.", 6f);
            return;
        }
        Component? component = hit.collider.GetComponentInParent<Pickable>();
        component ??= hit.collider.GetComponentInParent<TreeLog>();
        component ??= hit.collider.GetComponentInParent<TreeBase>();
        component ??= hit.collider.GetComponentInParent<MineRock5>();
        component ??= hit.collider.GetComponentInParent<MineRock>();
        var definition = component ? _resources.ByTarget(component) : null;
        if (definition == null)
        {
            if (executeContextCommand)
            {
                _markers.SetWorkZone(hit.point, WorkRadius, _config.ShowWorkZone.Value);
                _ui.Notify("Здесь назначена рабочая зона.");
            }
            else _ui.Notify($"Неизвестный объект: {hit.collider.gameObject.name}.", 6f);
            return;
        }
        if (executeContextCommand)
        {
            SetExactFilter(definition.Name, ResourceName(definition.Name));
            SetJob(definition.Category == ResourceCategory.Wood ? SpiritJob.Woodcutting :
                definition.Category is ResourceCategory.Ore or ResourceCategory.Stone ? SpiritJob.Mining : SpiritJob.Gathering);
            return;
        }
        _ui.Notify($"{ResourceName(definition.Name)}\nИнструмент: {definition.RequiredToolType}, tier {definition.MinToolTier}\nУровень духа: {definition.RequiredSpiritLevel}; профессии: {definition.RequiredProfessionLevel}\nАвтодобыча: {(definition.CanAutoHarvest ? "да" : "нет")}", 8f);
    }

    private void ConfigureCourier()
    {
        var player = Player.m_localPlayer;
        if (!player || !TryPlacementPoint(player, out var point)) { _ui.Notify("Не удалось определить точку маршрута."); return; }
        var route = _progression.Data.Automation.CourierRoute;
        if (!_courierAwaitingDestination)
        {
            route.Source = ToArray(point);
            route.Resource = _resourceSelection.ExactName;
            route.Enabled = false;
            _courierAwaitingDestination = true;
            _ui.Notify("Источник маршрута A установлен. Наведитесь на точку B и выберите команду ещё раз.", 6f);
            return;
        }
        route.Destination = ToArray(point);
        route.Enabled = true;
        _courierAwaitingDestination = false;
        _ui.Notify("Маршрут почтальона A → B активирован.");
        SetJob(SpiritJob.Courier);
    }

    private void SaveNamedZone()
    {
        var player = Player.m_localPlayer;
        if (!player || !TryPlacementPoint(player, out var point)) { _ui.Notify("Не удалось определить центр зоны."); return; }
        var zones = _progression.Data.Automation.NamedZones;
        var sameTypeCount = 0;
        foreach (var zone in zones) if (zone.Type == _selectedNamedZoneType) sameTypeCount++;
        var name = $"{_selectedNamedZoneType} {sameTypeCount + 1}";
        zones.Add(new NamedZoneData { Name = name, Type = _selectedNamedZoneType, Position = ToArray(point), Radius = WorkRadius });
        _activeNamedZoneIndex = zones.Count - 1;
        _markers.SetWorkZone(point, WorkRadius, _config.ShowWorkZone.Value);
        _ui.Notify($"Сохранена зона «{name}».");
    }

    private void CycleNamedZone()
    {
        var zones = _progression.Data.Automation.NamedZones;
        if (zones.Count == 0) { _ui.Notify("Именованных зон пока нет."); return; }
        _activeNamedZoneIndex = (_activeNamedZoneIndex + 1) % zones.Count;
        var zone = zones[_activeNamedZoneIndex];
        _markers.SetWorkZone(SpiritKnowledge.ToVector(zone.Position), zone.Radius, _config.ShowWorkZone.Value);
        _ui.Notify($"Активна зона «{zone.Name}».");
    }

    private void PlaceShrine()
    {
        var player = Player.m_localPlayer;
        if (!player || !TryPlacementPoint(player, out var point)) { _ui.Notify("Не удалось поставить алтарь."); return; }
        var automation = _progression.Data.Automation;
        automation.ShrineSet = true;
        automation.ShrinePosition = ToArray(point);
        _markers.ShowTemporaryMarker(point, new Color(0.65f, 0.35f, 1f), 86400f);
        _ui.Notify("Локальный алтарь духа установлен. Он не создаёт сетевой prefab.");
    }

    private void UpgradeShrine()
    {
        var player = Player.m_localPlayer;
        var automation = _progression.Data.Automation;
        if (!player || !automation.ShrineSet ||
            Vector3.Distance(player.transform.position, SpiritKnowledge.ToVector(automation.ShrinePosition)) > 8f)
        {
            _ui.Notify("Подойдите к установленному алтарю.");
            return;
        }
        if (automation.ShrineLevel >= 3) { _ui.Notify("Алтарь уже максимального III уровня."); return; }
        var requiredCores = automation.ShrineLevel * 2;
        var inventory = player.GetInventory();
        var cores = inventory.GetAllItems().FirstOrDefault(item => item.m_dropPrefab &&
            item.m_dropPrefab.name.IndexOf("SurtlingCore", StringComparison.OrdinalIgnoreCase) >= 0 && item.m_stack >= requiredCores);
        if (cores == null) { _ui.Notify($"Для улучшения нужно Surtling Core ×{requiredCores}."); return; }
        inventory.RemoveItem(cores, requiredCores);
        automation.ShrineLevel++;
        AddJournal($"Алтарь улучшен до уровня {automation.ShrineLevel}.");
        _ui.Notify($"Алтарь улучшен до уровня {automation.ShrineLevel}: зарядка, радиус и дальние задания усилены.", 6f);
    }

    private void GuideToShrine()
    {
        var automation = _progression.Data.Automation;
        if (!automation.ShrineSet) { _ui.Notify("Алтарь духа не установлен."); return; }
        _guideDestination = SpiritKnowledge.ToVector(automation.ShrinePosition);
        _guideLabel = "алтарь духа";
        _jobBeforeRest = _job;
        _job = SpiritJob.Rest;
    }

    private void FeedShrine()
    {
        var player = Player.m_localPlayer;
        if (!player || !_progression.Data.Automation.ShrineSet ||
            Vector3.Distance(player.transform.position, SpiritKnowledge.ToVector(_progression.Data.Automation.ShrinePosition)) > 8f)
        {
            _ui.Notify("Подойдите к алтарю духа.");
            return;
        }
        ItemDrop.ItemData? offering = null;
        foreach (var item in player.GetInventory().GetAllItems())
        {
            var name = item.m_dropPrefab ? item.m_dropPrefab.name : item.m_shared.m_name;
            if (name.IndexOf("Resin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("GreydwarfEye", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("SurtlingCore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Eitr", StringComparison.OrdinalIgnoreCase) >= 0) { offering = item; break; }
        }
        if (offering == null) { _ui.Notify("Нужны Resin, Greydwarf Eye, Surtling Core или Eitr."); return; }
        var offeringName = offering.m_dropPrefab ? offering.m_dropPrefab.name : offering.m_shared.m_name;
        player.GetInventory().RemoveItem(offering, 1);
        _shrineSpeedMultiplier = 1f;
        _shrineScoutMultiplier = 1f;
        var duration = 300f;
        if (offeringName.IndexOf("Resin", StringComparison.OrdinalIgnoreCase) >= 0)
            _progression.Data.Energy = Mathf.Min(100f, _progression.Data.Energy + 35f);
        else if (offeringName.IndexOf("Greydwarf", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _shrineScoutMultiplier = 1.5f;
            _progression.Data.Automation.Stability = Mathf.Min(100f, _progression.Data.Automation.Stability + 30f);
        }
        else if (offeringName.IndexOf("Surtling", StringComparison.OrdinalIgnoreCase) >= 0)
            _shrineSpeedMultiplier = 1.3f;
        else if (offeringName.IndexOf("Eitr", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _progression.Data.Energy = 100f;
            _shrineSpeedMultiplier = 1.35f;
            _shrineScoutMultiplier = 1.75f;
            duration = 900f;
        }
        _shrineBuffUntil = Time.time + duration;
        _ui.Notify($"Алтарь принял {offeringName}. Магический эффект активен.");
    }

    private void GuideToNamedZone(NamedZoneType type, string label)
    {
        var zone = _progression.Data.Automation.NamedZones.Find(item => item.Type == type);
        if (zone == null) { _ui.Notify($"Зона типа {type} не сохранена."); return; }
        StartGuide(SpiritKnowledge.ToVector(zone.Position), label);
    }

    private void GuideToUnload()
    {
        if (!_markers.HasUnloadPoint) { _ui.Notify("Точка разгрузки не назначена."); return; }
        StartGuide(_markers.UnloadPoint, "точка разгрузки");
    }

    private void GuideToGrave()
    {
        var automation = _progression.Data.Automation;
        if (!automation.HasLastDeathPosition) { _ui.Notify("Дух пока не помнит место гибели."); return; }
        StartGuide(SpiritKnowledge.ToVector(automation.LastDeathPosition), "место последней гибели");
    }

    private void GuideToRememberedPoint(bool exists, float[] position, string label)
    {
        if (!exists || position.Length != 3) { _ui.Notify($"Дух пока не помнит {label}."); return; }
        StartGuide(SpiritKnowledge.ToVector(position), label);
    }

    private void GuideToRememberedResource()
    {
        var player = Player.m_localPlayer;
        if (!player) return;
        var resource = _resourceSelection.Mode == ResourceFilterMode.Exact ? _resourceSelection.ExactName : _knowledge.FavoriteResource;
        var remembered = _knowledge.FindNearest(resource, player.transform.position);
        if (remembered == null) { _ui.Notify("В памяти нет подходящего ресурса."); return; }
        StartGuide(SpiritKnowledge.ToVector(remembered.Position), ResourceName(remembered.Resource));
    }

    private void StartGuide(Vector3 destination, string label)
    {
        _guideDestination = destination;
        _guideLabel = label;
        SetJob(SpiritJob.Guide);
    }

    private void CycleMaintainStock()
    {
        var automation = _progression.Data.Automation;
        automation.MaintainStock = automation.MaintainStock switch { 0 => 50, 50 => 100, 100 => 200, _ => 0 };
        _ui.Notify(automation.MaintainStock == 0 ? "Поддержание запаса выключено." : $"Поддерживать на складе: {automation.MaintainStock} выбранного ресурса.");
    }

    private void ToggleSchedule()
    {
        _scheduleEnabled = !_scheduleEnabled;
        var schedule = _progression.Data.Automation.Schedule;
        if (_scheduleEnabled && schedule.Count == 0)
        {
            schedule.Add(new ScheduleEntryData { StartHour = 6f, EndHour = 17f, Job = SpiritJob.Woodcutting });
            schedule.Add(new ScheduleEntryData { StartHour = 17f, EndHour = 21f, Job = SpiritJob.Transport });
            schedule.Add(new ScheduleEntryData { StartHour = 21f, EndHour = 6f, Job = SpiritJob.Guard });
        }
        _progression.Data.Automation.ScheduleEnabled = _scheduleEnabled;
        _ui.Notify($"Расписание: {OnOff(_scheduleEnabled)}.");
    }

    private void ToggleRules()
    {
        _rulesEnabled = !_rulesEnabled;
        var rules = _progression.Data.Automation.Rules;
        if (_rulesEnabled && rules.Count == 0)
        {
            rules.Add(new SpiritRuleData { Trigger = SpiritRuleTrigger.CargoEightyPercent, Action = SpiritRuleAction.DeliverAndResume });
            rules.Add(new SpiritRuleData { Trigger = SpiritRuleTrigger.PlayerInventoryNinetyPercent, Action = SpiritRuleAction.CleanupNearby });
            rules.Add(new SpiritRuleData { Trigger = SpiritRuleTrigger.EnemyNearby, Action = SpiritRuleAction.ReturnToPlayer });
            rules.Add(new SpiritRuleData { Trigger = SpiritRuleTrigger.EnergyBelowTwenty, Action = SpiritRuleAction.RechargeAndResume });
        }
        _progression.Data.Automation.RulesEnabled = _rulesEnabled;
        _ui.Notify($"Правила духа: {OnOff(_rulesEnabled)}.");
    }

    private void ShowMemory()
    {
        var memory = _progression.Data.Automation.WorldMemory;
        if (memory.Count == 0) { _ui.Notify("Память мира пока пуста. Используйте разведку."); return; }
        var lines = new List<string>();
        var player = Player.m_localPlayer;
        for (var index = Math.Max(0, memory.Count - 6); index < memory.Count; index++)
        {
            var entry = memory[index];
            var distance = player ? Vector3.Distance(player.transform.position, SpiritKnowledge.ToVector(entry.Position)) : 0f;
            lines.Add($"{ResourceName(entry.Resource)} — {distance:0} м, день {entry.LastSeenDay}{(entry.Depleted ? " (истощено)" : string.Empty)}");
        }
        _ui.Notify("Память мира\n" + string.Join("\n", lines), 10f);
    }

    private void FindLostItems()
    {
        var player = Player.m_localPlayer;
        if (!player) return;
        var hits = Physics.OverlapSphere(player.transform.position, WorkRadius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        ResourceDefinition? nearestDefinition = null;
        var nearestDistance = float.MaxValue;
        foreach (var hit in hits)
        {
            var drop = hit ? hit.GetComponentInParent<ItemDrop>() : null;
            if (!drop) continue;
            var definition = _resources.ByDrop(drop.gameObject.name);
            if (definition == null) continue;
            var name = drop.m_itemData.m_shared.m_name;
            counts.TryGetValue(name, out var amount);
            counts[name] = amount + drop.m_itemData.m_stack;
            var distance = Vector3.SqrMagnitude(drop.transform.position - player.transform.position);
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearestDefinition = definition;
        }
        if (counts.Count == 0) { _ui.Notify("Потерянные предметы поблизости не найдены."); return; }
        var lines = counts.OrderByDescending(entry => entry.Value).Take(8).Select(entry => $"{entry.Key} ×{entry.Value}");
        _ui.Notify("Найденные предметы\n" + string.Join(" • ", lines), 8f);
        if (nearestDefinition != null) _resourceSelection.SelectExact(nearestDefinition.Name);
        SetJob(SpiritJob.Bring);
    }

    private void ShowBiomeKnowledge()
    {
        var knowledge = _progression.Data.Automation.BiomeKnowledge;
        if (knowledge.Count == 0) { _ui.Notify("Дух ещё не изучил биомы."); return; }
        var lines = new List<string>();
        foreach (var biome in knowledge) lines.Add($"{biome.Biome}: {biome.Experience:0}%");
        _ui.Notify("Знание биомов\n" + string.Join(" • ", lines), 9f);
    }

    private void Prestige()
    {
        if (_progression.Level < 30) { _ui.Notify("Перерождение доступно на 30 уровне."); return; }
        var data = _progression.Data;
        data.Automation.Legacy = _knowledge.FavoriteResource;
        data.Automation.PrestigeCount++;
        data.SpiritLevel = 1;
        data.SpiritXp = 0f;
        data.UnlockedTalents.Clear();
        foreach (Profession profession in Enum.GetValues(typeof(Profession)))
        {
            data.ProfessionLevels[profession] = 1;
            data.ProfessionXp[profession] = 0f;
        }
        _celebrateUntil = Time.time + 5f;
        AddJournal($"Дух переродился. Наследие: {data.Automation.Legacy}.");
        _ui.Notify($"Дух переродился. Косметическое наследие: {data.Automation.Legacy}.", 8f);
    }

    private void LearnLookTarget()
    {
        var player = Player.m_localPlayer;
        var component = LookComponent();
        if (!player || !component) { _ui.Notify("Неизвестный объект не найден."); return; }
        var weapon = player.GetCurrentWeapon();
        var damage = weapon?.GetDamage() ?? default;
        var tool = damage.m_chop > damage.m_pickaxe ? RequiredToolType.Axe : RequiredToolType.Pickaxe;
        if (component is Pickable) tool = RequiredToolType.None;
        var definition = _resources.Learn(component, tool);
        _ui.SetResources(_resources.All);
        _resourceSelection.SelectExact(definition.Name);
        _ui.Notify($"Дух научился работать с prefab «{definition.Name}». Определение сохранено в конфиге.", 7f);
        AddJournal($"Изучен новый ресурс: {definition.Name}.");
    }

    private ResourceDefinition? LookResource()
    {
        var component = LookComponent();
        return component ? _resources.ByTarget(component) : null;
    }

    private static Component? LookComponent()
    {
        var camera = Camera.main;
        if (!camera || !Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit, 45f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore)) return null;
        Component? component = hit.collider.GetComponentInParent<Pickable>();
        component ??= hit.collider.GetComponentInParent<TreeLog>();
        component ??= hit.collider.GetComponentInParent<TreeBase>();
        component ??= hit.collider.GetComponentInParent<MineRock5>();
        component ??= hit.collider.GetComponentInParent<MineRock>();
        component ??= hit.collider.GetComponentInParent<Destructible>();
        return component;
    }

    private static SpiritJob JobFor(ResourceDefinition definition) => definition.Category switch
    {
        ResourceCategory.Wood => SpiritJob.Woodcutting,
        ResourceCategory.Ore or ResourceCategory.Stone => SpiritJob.Mining,
        _ => SpiritJob.Gathering
    };

    private void TickLongTermProgress(Player player, float deltaTime)
    {
        var automation = _progression.Data.Automation;
        automation.TimeTogetherSeconds += deltaTime;
        var biome = player.GetCurrentBiome();
        if (_knowledge.VisitBiome(biome, deltaTime)) AddJournal($"Впервые посещён биом {biome}.");
        var isDead = player.IsDead();
        if (isDead && !_wasDead)
        {
            automation.LastDeathPosition = ToArray(player.transform.position);
            automation.HasLastDeathPosition = true;
            if (_visual != null) _carry.Release(_visual.Transform.position);
            AddJournal("Дух запомнил место гибели игрока.");
        }
        if (!isDead && _wasDead)
        {
            _celebrateUntil = Time.time + 2.5f;
            _ui.Notify("Дух рад вашему возвращению.");
        }
        _wasDead = isDead;

        var enemies = CountEnemies(player.transform.position, 25f);
        var biomeFear = biome switch
        {
            Heightmap.Biome.Meadows => 1,
            Heightmap.Biome.BlackForest => 5,
            Heightmap.Biome.Swamp => 10,
            Heightmap.Biome.Mountain => 15,
            Heightmap.Biome.Plains => 20,
            Heightmap.Biome.Mistlands => 25,
            _ => 30
        };
        var shouldFear = enemies >= 4 || _progression.Level < biomeFear;
        var stabilityChange = shouldFear ? -7f : _progression.Data.Personality == "Спокойный" ? 6f : 4f;
        automation.Stability = Mathf.Clamp(automation.Stability + stabilityChange * deltaTime, 0f, 100f);
        if (_progression.Data.Personality == "Осторожный" && enemies > 0 && _job is not SpiritJob.Follow and not SpiritJob.Guard)
        {
            _jobBeforeRest = _job;
            _job = SpiritJob.Follow;
            _target = null;
        }
        if (automation.Stability < 20f && Time.time >= _lastSuggestion + 10f)
        {
            _lastSuggestion = Time.time;
            _ui.Notify("✦ Дух напуган и держится ближе к игроку.", 4f);
            _audio?.Play(SpiritAudioEvent.DangerDetected);
        }
        if (Time.time >= _nextEmotion)
        {
            _nextEmotion = Time.time + 15f;
            var bossNearby = Character.GetAllCharacters().Any(character => character && character != player &&
                !character.IsDead() && character.IsBoss() && Vector3.Distance(character.transform.position, player.transform.position) < 60f);
            if (bossNearby) _ui.Notify("✦ Дух дрожит и прячется за вами: рядом могущественная угроза.", 5f);
            else if (EnvMan.instance && EnvMan.instance.GetCurrentEnvironment().m_rainCloudAlpha > 0.65f)
                _ui.Notify("✦ Вспышки грозы пробегают по духу.", 4f);
            else if (player.IsSitting() && IsNearFire(player.transform.position))
                _ui.Notify("✦ Дух спокойно отдыхает рядом с огнём.", 4f);
        }
        if (biome == Heightmap.Biome.Mistlands && Time.time >= _nextScoutReport)
        {
            var probe = player.transform.position + player.transform.forward * 6f + Vector3.up * 2f;
            if (!Physics.Raycast(probe, Vector3.down, 12f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                _nextScoutReport = Time.time + 5f;
                _ui.Notify("✦ Впереди резкий обрыв.", 4f);
                _audio?.Play(SpiritAudioEvent.DangerDetected);
            }
        }
        if (_secretDetectorEnabled && Time.time >= _nextAdvancedTick)
        {
            _nextAdvancedTick = Time.time + 4f;
            DetectNearbySecret(player.transform.position, 24f);
        }
        if (_baseAlarmEnabled)
        {
            var home = automation.NamedZones.Find(zone => zone.Type == NamedZoneType.Home);
            if (home != null) TickBaseAlarm(player, SpiritKnowledge.ToVector(home.Position));
        }
    }

    private void TickAutomationRules(Player player)
    {
        if (Time.time < _nextRulesTick) return;
        _nextRulesTick = Time.time + 2f;
        ReadWorkOrders(player);
        if (_scheduleEnabled) ApplySchedule();
        MaintainStock(player);
        if (!_rulesEnabled) return;
        var oldJob = _job;
        if (EnergyRatio < 0.2f && _job != SpiritJob.Rest)
        {
            _jobBeforeRest = _job;
            _job = SpiritJob.Rest;
            _knowledge.RecordDecision("Rule: energy below 20% → recharge, then resume");
        }
        else if (CountEnemies(player.transform.position, 18f) > 0 && _job is not SpiritJob.Guard and not SpiritJob.Follow)
        {
            _jobBeforeRest = _job;
            _job = SpiritJob.Follow;
            _knowledge.RecordDecision("Rule: enemy nearby → return to player");
        }
        else if (_carry.IsFull(EffectiveCarryStacks, EffectiveCarryWeight))
        {
            _forceDelivery = true;
            _knowledge.RecordDecision("Rule: cargo >= 80% → deliver and resume");
        }
        if (oldJob != _job) _target = null;
    }

    private void ReadWorkOrders(Player player)
    {
        var hits = Physics.OverlapSphere(player.transform.position, WorkRadius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            var sign = hit ? hit.GetComponentInParent<Sign>() : null;
            if (!sign) continue;
            var text = sign.GetText().Trim();
            if (!text.StartsWith("Spirit:", StringComparison.OrdinalIgnoreCase) || text == _lastWorkOrder) continue;
            var parts = text.Substring(7).Trim().Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var definition = _resources.All.FirstOrDefault(item =>
                string.Equals(item.Name, parts[0], StringComparison.OrdinalIgnoreCase));
            if (definition == null) continue;
            var quantity = 0;
            if (parts.Length > 1) int.TryParse(parts[1], out quantity);
            _lastWorkOrder = text;
            _resourceSelection.SelectExact(definition.Name);
            _selectedQuantity = Mathf.Max(0, quantity);
            _markers.SetWorkZone(sign.transform.position, WorkRadius, _config.ShowWorkZone.Value);
            SetJob(JobFor(definition));
            _knowledge.RecordDecision($"Work order sign: {definition.Name} x{quantity}");
            _ui.Notify($"Получен Work Order: {ResourceName(definition.Name)} × {(quantity == 0 ? "∞" : quantity.ToString())}.", 7f);
            return;
        }
    }

    private void MaintainStock(Player player)
    {
        var targetStock = _progression.Data.Automation.MaintainStock;
        if (targetStock <= 0 || _resourceSelection.Mode != ResourceFilterMode.Exact) return;
        var definition = _resources.All.FirstOrDefault(item => item.Name == _resourceSelection.ExactName);
        if (definition == null) return;
        var total = 0;
        var seen = new HashSet<int>();
        var hits = Physics.OverlapSphere(player.transform.position, WorkRadius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            var container = hit ? hit.GetComponentInParent<Container>() : null;
            if (!container || !seen.Add(container.GetInstanceID()) ||
                !PrivateArea.CheckAccess(container.transform.position, 0f, false, false)) continue;
            foreach (var dropName in definition.DropNames) total += container.GetInventory().CountItems(dropName, -1, true);
        }
        if (total >= targetStock)
        {
            if (_job == JobFor(definition)) _job = SpiritJob.Follow;
            return;
        }
        if (_job is SpiritJob.Follow or SpiritJob.Rest)
        {
            _job = JobFor(definition);
            _assignmentQuantity = targetStock - total;
            _assignmentProgress = 0;
            _ui.Notify($"Запас {ResourceName(definition.Name)} ниже цели: {total}/{targetStock}. Работа возобновлена.");
        }
    }

    private void ApplySchedule()
    {
        if (!EnvMan.instance) return;
        var hour = EnvMan.instance.GetDayFraction() * 24f;
        foreach (var entry in _progression.Data.Automation.Schedule)
        {
            var active = entry.StartHour <= entry.EndHour
                ? hour >= entry.StartHour && hour < entry.EndHour
                : hour >= entry.StartHour || hour < entry.EndHour;
            if (!active || _job == entry.Job) continue;
            _job = entry.Job;
            _target = null;
            _knowledge.RecordDecision($"Schedule → {entry.Job}");
            break;
        }
    }

    private void TickBaseAlarm(Player player, Vector3 centre)
    {
        var enemies = CountEnemies(centre, 25f);
        if (enemies == 0 || Time.time < _nextGuardNotice) return;
        _nextGuardNotice = Time.time + 8f;
        var direction = centre - player.transform.position;
        _ui.Notify($"⚠ Опасность у дома: {enemies} враг(а), направление {DirectionName(direction)}.", 6f);
        _audio?.Play(SpiritAudioEvent.DangerDetected);
    }

    private void TickThreatWarning(Player player, float radius)
    {
        var enemies = CountEnemies(player.transform.position, radius);
        if (enemies == 0 || Time.time < _nextGuardNotice) return;
        _nextGuardNotice = Time.time + 6f;
        _ui.Notify($"✦ Впереди опасность: {enemies} враг(а).", 4f);
        _audio?.Play(SpiritAudioEvent.DangerDetected);
    }

    private static int CountEnemies(Vector3 centre, float radius)
    {
        var squaredRadius = radius * radius;
        var player = Player.m_localPlayer;
        var count = 0;
        foreach (var character in Character.GetAllCharacters())
            if (character && player && character != player && !character.IsDead() && BaseAI.IsEnemy(player, character) &&
                Vector3.SqrMagnitude(character.transform.position - centre) <= squaredRadius) count++;
        return count;
    }

    private static bool IsNearFire(Vector3 centre)
    {
        var hits = Physics.OverlapSphere(centre, 8f, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            if (!hit) continue;
            var name = hit.gameObject.name.ToLowerInvariant();
            if (name.Contains("fire") || name.Contains("hearth") || name.Contains("bonfire")) return true;
        }
        return false;
    }

    private void DetectNearbySecret(Vector3 centre, float radius)
    {
        var hits = Physics.OverlapSphere(centre, radius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            if (!hit) continue;
            var name = hit.gameObject.name.ToLowerInvariant();
            if (!name.Contains("chest") && !name.Contains("vegvisir") && !name.Contains("boss") &&
                !name.Contains("location") && !name.Contains("rare")) continue;
            var approximate = Vector3.Lerp(centre, hit.transform.position, 0.65f);
            _markers.ShowTemporaryMarker(approximate, new Color(1f, 0.65f, 0.9f), 7f);
            _ui.Notify("✦ Что-то заинтересовало духа...", 4f);
            return;
        }
    }

    private static string DirectionName(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return "рядом";
        var angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        var directions = new[] { "север", "северо-восток", "восток", "юго-восток", "юг", "юго-запад", "запад", "северо-запад" };
        return directions[Mathf.RoundToInt(angle / 45f) % directions.Length];
    }

    private static int CurrentDay() => EnvMan.instance ? EnvMan.instance.GetDay() : 0;

    private static string UnloadTypeName(UnloadPointType type) => type switch
    {
        UnloadPointType.Universal => "Любые ресурсы", UnloadPointType.Wood => "Древесина",
        UnloadPointType.Ore => "Руда", UnloadPointType.Stone => "Камень",
        UnloadPointType.Plants => "Растения", UnloadPointType.Food => "Еда",
        UnloadPointType.Custom => "Пользовательский", _ => type.ToString()
    };

    private static string ZoneModeName(WorkZoneMode mode) => mode switch
    {
        WorkZoneMode.FollowPlayer => "вокруг игрока", WorkZoneMode.UnloadPointCentered => "вокруг разгрузки",
        _ => "установленный круг"
    };

    private static string LightModeName(SpiritLightMode mode) => mode switch
    {
        SpiritLightMode.Off => "выключено", SpiritLightMode.CameraForward => "перед камерой", _ => "вокруг духа"
    };

    private void CancelTarget(string reason)
    {
        if (_target != null)
        {
            var key = _target.Component.GetInstanceID();
            _targetFailures.TryGetValue(key, out var failures);
            failures++;
            _targetFailures[key] = failures;
            if (failures >= 3)
            {
                _targetBlacklist[key] = Time.time + 60f;
                _targetFailures.Remove(key);
                _knowledge.RecordDecision($"Target failed x3 → blacklisted 60 sec: {_target.Definition.Name}");
            }
        }
        _lastError = reason; _ui.Notify(reason); _target = null; _workAttempts = 0; SetState(SpiritState.ReturnToPlayer);
    }

    private void TickEnergy(float deltaTime)
    {
        if (!_config.EnergyEnabled.Value || _progression.Level >= 30) { _progression.Data.Energy = 100f; return; }
        if (_state is SpiritState.Rest or SpiritState.Recharge)
        {
            var shrineBonus = _progression.Data.Automation.ShrineSet && _visual != null &&
                Vector3.Distance(_visual.Transform.position, SpiritKnowledge.ToVector(_progression.Data.Automation.ShrinePosition)) < 4f
                ? 1f + _progression.Data.Automation.ShrineLevel * 0.5f : 1f;
            _progression.Data.Energy = Mathf.Min(100f, _progression.Data.Energy + _config.EnergyRegenRate.Value * shrineBonus * deltaTime);
        }
    }
    private void SpendEnergy(float value)
    {
        if (_config.EnergyEnabled.Value && _progression.Level < 30)
        {
            var player = Player.m_localPlayer;
            var knowledgeDiscount = player ? _knowledge.BiomeKnowledge(player.GetCurrentBiome()) * 0.002f : 0f;
            _progression.Data.Energy = Mathf.Max(0f, _progression.Data.Energy - value * (1f - knowledgeDiscount));
        }
    }
    private float EnergyRatio => _progression.Data.Energy / 100f;
    private void SetState(SpiritState state)
    {
        if (_state == state) return;
        _state = state;
        _knowledge?.RecordDecision($"State → {state}");
    }

    private static bool TryPlacementPoint(Player player, out Vector3 point)
    {
        var camera = Camera.main;
        if (camera && Physics.Raycast(camera.transform.position, camera.transform.forward, out var aimedHit, 40f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            point = aimedHit.point + aimedHit.normal * 0.06f;
            return true;
        }
        var direction = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        var sample = player.transform.position + direction * 8f;
        if (ZoneSystem.instance)
        {
            sample.y = ZoneSystem.instance.GetGroundHeight(sample);
            point = sample;
            return true;
        }
        var origin = sample + Vector3.up * 20f;
        if (Physics.Raycast(origin, Vector3.down, out var groundHit, 60f, Physics.AllLayers, QueryTriggerInteraction.Ignore)) { point = groundHit.point; return true; }
        point = default; return false;
    }

    private static Container? FindContainer(Vector3 point)
    {
        var hits = Physics.OverlapSphere(point, 2.5f, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            var container = hit ? hit.GetComponentInParent<Container>() : null;
            if (container) return container;
        }
        return null;
    }

    private void Persist()
    {
        if (_progression == null) return;
        var data = _progression.Data; data.BalanceMode = _config.BalanceMode.Value;
        data.HasUnloadPoint = _markers.HasUnloadPoint; data.UnloadPoint = ToArray(_markers.UnloadPoint);
        data.UnloadPointType = _markers.UnloadType;
        data.UnloadPoints = _markers.ExportUnloadPoints();
        data.HasWorkZone = _markers.HasWorkZone; data.WorkZone = ToArray(_markers.WorkZoneCentre);
        data.WorkZoneMode = _markers.ZoneMode;
        data.HasForbiddenZone = _markers.HasForbiddenZone; data.ForbiddenZone = ToArray(_markers.ForbiddenZoneCentre);
        data.ResourceFilterMode = _resourceSelection.Mode; data.ResourceCategory = _resourceSelection.Category;
        data.SelectedResource = _resourceSelection.ExactName;
        _saveManager.Save(_saveKey, data);
    }

    private void TrackRecentDrops()
    {
        if (!_config.AutoCarry.Value || Time.time > _dropTrackingUntil) return;
        if (!_carry.TryCollect(_lastWorkPosition, EffectiveCarryStacks, EffectiveCarryWeight,
                _resourceSelection, IsResourceForCurrentJob)) return;
        if (_carry.IsFull(EffectiveCarryStacks, EffectiveCarryWeight)) _forceDelivery = true;
    }

    private void Award(float spiritXp, float professionXp, Profession profession)
    {
        var oldLevel = _progression.Level;
        var leveled = _progression.Award(spiritXp, professionXp, profession,
            _config.SpiritXpMultiplier.Value, _config.ProfessionXpMultiplier.Value);
        if (!leveled) return;
        _celebrateUntil = Time.time + 2.2f;
        _ui.Notify(_progression.Level > oldLevel
            ? $"Уровень духа повышен: {_progression.Level}!"
            : $"Уровень профессии повышен: {profession} {_progression.ProfessionLevel(profession)}!");
        _audio?.Play(_progression.Level > oldLevel ? SpiritAudioEvent.SpiritLevelUp : SpiritAudioEvent.ProfessionLevelUp);
        AddJournal(_progression.Level > oldLevel ? $"Достигнут уровень духа {_progression.Level}." : $"{profession}: уровень {_progression.ProfessionLevel(profession)}.");
    }

    private void Celebrate(Player player, float deltaTime)
    {
        SetState(SpiritState.Celebrate);
        var angle = Time.time * 6f;
        var point = player.transform.position + new Vector3(Mathf.Cos(angle) * 2.2f, 2.3f + Mathf.Sin(angle * 2f) * 0.5f,
            Mathf.Sin(angle) * 2.2f);
        _visual!.Move(point, 15f, deltaTime);
    }

    private bool IsJobUnlocked(SpiritJob job, out int requiredLevel)
    {
        requiredLevel = job switch
        {
            SpiritJob.Transport => 3,
            SpiritJob.Scout => 10,
            SpiritJob.Guard => 10,
            SpiritJob.Courier or SpiritJob.Sort => 15,
            SpiritJob.Production => 25,
            SpiritJob.Expedition => 20,
            _ => 1
        };
        return _progression.Level >= requiredLevel;
    }

    private ISet<int> ActiveBlacklist() => new HashSet<int>(_targetBlacklist.Keys);

    private void RemoveExpiredBlacklistEntries()
    {
        if (_targetBlacklist.Count == 0) return;
        var expired = new List<int>();
        foreach (var entry in _targetBlacklist) if (entry.Value <= Time.time) expired.Add(entry.Key);
        foreach (var key in expired) _targetBlacklist.Remove(key);
    }

    private int TalentTier(string branch)
    {
        var tier = 0;
        for (var candidate = 1; candidate <= 4; candidate++)
            if (_progression.HasTalent($"{branch}.{candidate}")) tier = candidate;
        return tier;
    }

    private float ActiveSpeedMultiplier => Time.time < _shrineBuffUntil ? _shrineSpeedMultiplier : 1f;

    private void SuggestNextWork(Player player)
    {
        if (!_progression.Data.Automation.SuggestionsEnabled || Time.time < _lastSuggestion + 30f) return;
        _lastSuggestion = Time.time;
        var remembered = _knowledge.FindNearest(_resourceSelection.ExactName, player.transform.position);
        if (remembered != null)
        {
            var direction = SpiritKnowledge.ToVector(remembered.Position) - player.transform.position;
            _ui.Notify($"✦ Я помню {ResourceName(remembered.Resource)} на {DirectionName(direction)}. Продолжить там?", 6f);
        }
        else _ui.Notify("✦ В этой зоне больше нет выбранных ресурсов. Можно перенести рабочую зону.", 5f);
    }

    private void EnsureIdentity(Player player)
    {
        if (!string.IsNullOrWhiteSpace(_progression.Data.SpiritName)) return;
        var names = new[] { "Эйра", "Луми", "Нива", "Соль", "Вейл", "Ирис" };
        var personalities = new[] { "Любопытный", "Трудолюбивый", "Осторожный", "Спокойный" };
        var seed = player.GetPlayerID().GetHashCode() & int.MaxValue;
        _progression.Data.SpiritName = names[seed % names.Length];
        _progression.Data.Personality = personalities[(seed / names.Length) % personalities.Length];
        AddJournal($"{_progression.Data.SpiritName} присоединился к игроку.");
    }

    private void TrackFlight()
    {
        if (_visual == null) return;
        var distance = Vector3.Distance(_lastVisualPosition, _visual.Transform.position);
        if (distance < 20f) _progression.Data.DistanceFlown += distance;
        _lastVisualPosition = _visual.Transform.position;
    }

    private void RecordWork(ResourceDefinition definition)
    {
        if (definition.Category == ResourceCategory.Wood) _progression.Data.TreesWorked++;
        else if (definition.Category is ResourceCategory.Ore or ResourceCategory.Stone) _progression.Data.OreWorked++;
        else _progression.Data.PlantsGathered++;
        var resourceCount = _knowledge.RecordResource(definition.Name);
        if (resourceCount == 500)
        {
            _ui.Notify($"{_progression.Data.SpiritName} получил сродство: {ResourceName(definition.Name)}.", 6f);
            AddJournal($"Открыто сродство с ресурсом {definition.Name}.");
        }
        _progression.Data.Bond += 0.1f;
    }

    private Profession DominantProfession()
    {
        var dominant = Profession.Exploration;
        var level = 0;
        foreach (Profession profession in Enum.GetValues(typeof(Profession)))
        {
            var candidate = _progression.ProfessionLevel(profession);
            if (candidate <= level) continue;
            dominant = profession;
            level = candidate;
        }
        return dominant;
    }

    private void AddJournal(string entry)
    {
        var day = EnvMan.instance ? EnvMan.instance.GetDay() : 0;
        _progression.Data.Journal.Add($"Day {day}: {entry}");
        while (_progression.Data.Journal.Count > 50) _progression.Data.Journal.RemoveAt(0);
    }

    private bool IsResourceForCurrentJob(ResourceDefinition definition) => _job switch
    {
        SpiritJob.Woodcutting => definition.Category == ResourceCategory.Wood,
        SpiritJob.Mining => definition.Category is ResourceCategory.Ore or ResourceCategory.Stone,
        SpiritJob.Gathering => definition.Category is ResourceCategory.Plant or ResourceCategory.Food,
        SpiritJob.Assist or SpiritJob.Cleanup => _assistWorkJob switch
        {
            SpiritJob.Woodcutting => definition.Category == ResourceCategory.Wood,
            SpiritJob.Mining => definition.Category is ResourceCategory.Ore or ResourceCategory.Stone,
            _ => definition.Category is ResourceCategory.Plant or ResourceCategory.Food
        },
        _ => true
    };

    private void ResetTargetAndClaims()
    {
        _target = null;
        _workAttempts = 0;
        _carry.CancelPending();
    }

    private void CycleFloat(BepInEx.Configuration.ConfigEntry<float> entry, float[] values, string label, string format)
    {
        var index = Array.FindIndex(values, value => Mathf.Approximately(value, entry.Value));
        entry.Value = values[(index + 1 + values.Length) % values.Length];
        _ui.Notify($"{label}: {entry.Value.ToString(format)}.");
    }

    private void CycleColor(BepInEx.Configuration.ConfigEntry<string> entry, string[] values, string label)
    {
        var index = Array.FindIndex(values, value => string.Equals(value, entry.Value, StringComparison.OrdinalIgnoreCase));
        entry.Value = values[(index + 1 + values.Length) % values.Length];
        _ui.Notify($"{label}: {entry.Value}.");
    }
    private static float[] ToArray(Vector3 value) => new[] { value.x, value.y, value.z };

    private void OnGUI()
    {
        if (_progression == null) return;
        var target = _target?.Definition.Name ?? "—";
        var debug = $"State: {_state}\nJob: {_job}\nAssignment: {_assignmentProgress}/{QuantityName(_assignmentQuantity)}; queue: {_assignments.Count}\nTarget position: {_lastTargetPosition}\nBalance: {_config.BalanceMode.Value}\nEnergy: {_progression.Data.Energy:0.0}\nAttempts: {_workAttempts}\nLast error: {_lastError}";
        _ui.Draw(_config.EnableHud.Value, _progression, _state, _job, _progression.Data.Energy,
            ResourceName(target), _carry.Summary, FilterName(), _config.DebugMode.Value, debug);
    }

    private string FilterName() => _resourceSelection.Mode switch
    {
        ResourceFilterMode.Category => _resourceSelection.Category switch
        {
            ResourceCategory.Wood => "Древесина", ResourceCategory.Stone => "Камень",
            ResourceCategory.Ore => "Руда", ResourceCategory.Plant => "Растения",
            ResourceCategory.Food => "Еда", _ => _resourceSelection.Category.ToString()
        },
        ResourceFilterMode.Exact => ResourceName(_resourceSelection.ExactName),
        _ => "Все ресурсы"
    };
}
