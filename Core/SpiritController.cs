using System;
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
    private SpiritConfig _config = null!;
    private readonly SaveManager _saveManager = new SaveManager();
    private SpiritProgression _progression = null!;
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

    public void Initialize(SpiritConfig config)
    {
        _config = config;
        _resources = new ResourceDatabase();
        _scanner = new TargetScanner(_resources);
        _interaction = new VanillaInteractionHelper();
        _carry = new CarrySystem(_resources);
        _ui.ActionSelected += HandleMenuAction;
    }

    public void Shutdown()
    {
        if (_initialized) Persist();
        _carry?.Release();
        _markers.Dispose();
        _visual?.Dispose();
    }

    private float WorkRadius => Mathf.Min(_config.WorkRadius.Value, _config.MaxWorkRadius.Value);

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
            _config.BalanceMode.Value = data.BalanceMode;
            _resourceSelection.Restore(data.ResourceFilterMode, data.ResourceCategory, data.SelectedResource);
            _markers.Restore(data.HasUnloadPoint, data.UnloadPoint, data.UnloadPointType,
                data.HasWorkZone, data.WorkZone, data.WorkZoneMode, WorkRadius);
            _initialized = true;
        }
        if (_visual != null) return;
        _visual = new SpiritVisualController(_config);
        _visual.Spawn(player.transform.position + player.transform.right * 1.5f + Vector3.up * 2f);
        _audio = new SpiritAudioController(_visual.Transform, _config.EnableSounds.Value, _config.MasterVolume.Value,
            _config.EnableAmbientSounds.Value, _config.AmbientVolume.Value,
            _config.AmbientIntervalMin.Value, _config.AmbientIntervalMax.Value);
        _audio.RegisterProceduralDefaults();
        _audio.Play(SpiritAudioEvent.Spawn);
        _ui.Notify("Дух-помощник пробудился.");
    }

    private void HandleInput()
    {
        if (_config.MenuKey.Value.IsDown()) { _ui.ToggleMenu(); _audio?.Play(SpiritAudioEvent.MenuOpen); }
    }

    private void TickAi(Player player, float deltaTime)
    {
        if (_visual == null) return;
        _carry.TickClaims(_config.MaxCarryStacks.Value, _config.MaxCarryWeight.Value);
        TrackRecentDrops();
        _markers.Tick(player, WorkRadius);
        if (_job == SpiritJob.Stopped) { SetState(SpiritState.Idle); _visual.Tick(_state, EnergyRatio, deltaTime); return; }
        if (_progression.Data.Energy <= 0f && _config.EnergyEnabled.Value) { _job = SpiritJob.Rest; SetState(SpiritState.Rest); }
        switch (_job)
        {
            case SpiritJob.Follow: Follow(player, deltaTime); break;
            case SpiritJob.Rest: Rest(player, deltaTime); break;
            case SpiritJob.Transport: TransportOnly(player, deltaTime); break;
            case SpiritJob.Scout: Scout(player, deltaTime); break;
            case SpiritJob.Guard: Guard(player, deltaTime); break;
            default: WorkJob(player, deltaTime); break;
        }
        _carry.Follow(_visual.Transform.position);
        _visual.Tick(_state, EnergyRatio, deltaTime);
    }

    private void Follow(Player player, float deltaTime)
    {
        SetState(SpiritState.FollowPlayer);
        var side = player.transform.right * _config.FollowDistance.Value + player.transform.forward * -0.7f;
        var bob = Vector3.up * (_config.FollowHeight.Value + Mathf.Sin(Time.time * 1.8f) * _config.BobbingAmount.Value);
        _visual!.Move(player.transform.position + side + bob, _config.FollowSpeed.Value, deltaTime);
    }

    private void Rest(Player player, float deltaTime)
    {
        SetState(SpiritState.Rest);
        _visual!.Move(player.transform.position + Vector3.up * 1.1f, 5f, deltaTime);
        if (_progression.Data.Energy >= 99.9f) { _job = SpiritJob.Follow; _ui.Notify("Энергия духа восстановлена."); }
    }

    private void WorkJob(Player player, float deltaTime)
    {
        if (_carry.Count > 0 && (_forceDelivery || _carry.IsFull(_config.MaxCarryStacks.Value, _config.MaxCarryWeight.Value)))
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
            if (_config.AutoCarry.Value) _carry.TryCollect(_completedTargetPosition, _config.MaxCarryStacks.Value,
                _config.MaxCarryWeight.Value, _resourceSelection, IsResourceForCurrentJob);
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
            SetState(SpiritState.SearchTarget); _nextScan = Time.time + _config.SearchInterval.Value;
            var centre = _markers.GetWorkCentre(player);
            _target = _scanner.Find(centre, WorkRadius, _job, _resourceSelection, _config.SafeBaseRadius.Value,
                _config.ProtectPlantedTrees.Value, _config.ProtectCrops.Value);
            if (_target == null)
            {
                if (_carry.Count > 0) DeliverCarried(player, deltaTime);
                else FollowNearWorkCentre(player, deltaTime);
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
        SetState(SpiritState.Work); _nextWork = Time.time + (_config.BalanceMode.Value == BalanceMode.Free ? 0.35f : _config.WorkInterval.Value);
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
        _audio?.Play(_job == SpiritJob.Woodcutting ? SpiritAudioEvent.WoodHit : _job == SpiritJob.Mining ? SpiritAudioEvent.MiningHitOre : SpiritAudioEvent.GatherPlant, 0.35f);
        if (_target.Definition.DamageType == ResourceDamageType.Interact) CompleteTarget();
    }

    private void CompleteTarget()
    {
        if (_target == null) return;
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
            Award(definition.XpReward, definition.Profession);
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
                    _carry.TryCollect(dropPosition.Value, _config.MaxCarryStacks.Value, _config.MaxCarryWeight.Value, _resourceSelection);
                return;
            }
        }
        if (_carry.Count == 0) { Follow(player, deltaTime); return; }
        DeliverCarried(player, deltaTime);
    }

    private void DeliverCarried(Player player, float deltaTime)
    {
        var useUnloadPoint = _markers.HasUnloadPoint && _carry.HasAccepted(definition => _markers.Accepts(definition, _resourceSelection));
        var deliveryPoint = useUnloadPoint ? _markers.UnloadPoint : player.transform.position + player.transform.forward * 1.5f;
        Func<ResourceDefinition, bool> accepts = useUnloadPoint
            ? definition => _markers.Accepts(definition, _resourceSelection)
            : _ => true;
        SetState(SpiritState.CarryDrops);
        _visual!.Move(deliveryPoint + Vector3.up * 1.7f, 8f, deltaTime);
        if (Vector3.Distance(_visual.Transform.position, deliveryPoint) >= 2f) return;
        var delivered = _carry.Unload(deliveryPoint, 1.4f, accepts);
        if (delivered <= 0) return;
        Award(_config.LogisticsXp.Value, Profession.Logistics);
        _forceDelivery = _carry.IsFull(_config.MaxCarryStacks.Value, _config.MaxCarryWeight.Value);
        _audio?.Play(SpiritAudioEvent.Unload);
        _ui.Notify(useUnloadPoint ? $"Дух доставил груз к точке разгрузки: {delivered}." : $"Дух принёс груз к игроку: {delivered}.", 2f);
        SetState(SpiritState.Unload);
    }

    private void Scout(Player player, float deltaTime)
    {
        SetState(SpiritState.Scout);
        var point = player.transform.position + player.transform.forward * 22f + Vector3.up * 8f;
        _visual!.Move(point, 13f, deltaTime); SpendEnergy(0.3f * deltaTime);
        if (Time.time < _nextScoutReport) return;
        _nextScoutReport = Time.time + 6f;
        SpiritTarget? nearest = null;
        foreach (var job in new[] { SpiritJob.Woodcutting, SpiritJob.Mining, SpiritJob.Gathering })
        {
            var candidate = _scanner.Find(point, Mathf.Min(30f, WorkRadius), job, _resourceSelection,
                _config.SafeBaseRadius.Value, _config.ProtectPlantedTrees.Value, _config.ProtectCrops.Value);
            if (candidate != null && (nearest == null || Vector3.SqrMagnitude(candidate.Position - point) < Vector3.SqrMagnitude(nearest.Position - point))) nearest = candidate;
        }
        if (nearest != null)
            _ui.Notify($"Разведка: {ResourceName(nearest.Definition.Name)}, {Vector3.Distance(player.transform.position, nearest.Position):0} м.", 3f);
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
            _ui.Notify($"Охрана: враг в {Mathf.Sqrt(nearestDistance):0} м!", 3f);
            _audio?.Play(SpiritAudioEvent.ScanPulse);
        }
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
        _job = job; _target = null; _workAttempts = 0; _awaitingTreeLog = false;
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
        }
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
                _config.SafeBaseRadius.Value, _config.ProtectPlantedTrees.Value, _config.ProtectCrops.Value);
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
        if (!player || !TryGroundPoint(player, out var point)) { _ui.Notify("Не удалось определить точку на земле."); return; }
        _markers.SetUnload(point); _ui.Notify("Точка разгрузки установлена."); _audio?.Play(SpiritAudioEvent.UnloadPointPlaced);
    }

    private void PlaceWorkZone()
    {
        var player = Player.m_localPlayer;
        if (!player || !TryGroundPoint(player, out var point)) { _ui.Notify("Не удалось определить точку на земле."); return; }
        _markers.SetWorkZone(point, WorkRadius); _ui.Notify("Рабочая зона установлена."); _audio?.Play(SpiritAudioEvent.WorkZonePlaced);
    }

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
        _lastError = reason; _ui.Notify(reason); _target = null; _workAttempts = 0; SetState(SpiritState.ReturnToPlayer);
    }

    private void TickEnergy(float deltaTime)
    {
        if (!_config.EnergyEnabled.Value) { _progression.Data.Energy = 100f; return; }
        if (_state is SpiritState.Idle or SpiritState.FollowPlayer or SpiritState.Rest or SpiritState.Recharge)
            _progression.Data.Energy = Mathf.Min(100f, _progression.Data.Energy + _config.EnergyRegenRate.Value * deltaTime);
    }
    private void SpendEnergy(float value) { if (_config.EnergyEnabled.Value) _progression.Data.Energy = Mathf.Max(0f, _progression.Data.Energy - value); }
    private float EnergyRatio => _progression.Data.Energy / 100f;
    private void SetState(SpiritState state) => _state = state;

    private static bool TryGroundPoint(Player player, out Vector3 point)
    {
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

    private void Persist()
    {
        if (_progression == null) return;
        var data = _progression.Data; data.BalanceMode = _config.BalanceMode.Value;
        data.HasUnloadPoint = _markers.HasUnloadPoint; data.UnloadPoint = ToArray(_markers.UnloadPoint);
        data.UnloadPointType = _markers.UnloadType;
        data.HasWorkZone = _markers.HasWorkZone; data.WorkZone = ToArray(_markers.WorkZoneCentre);
        data.WorkZoneMode = _markers.ZoneMode;
        data.ResourceFilterMode = _resourceSelection.Mode; data.ResourceCategory = _resourceSelection.Category;
        data.SelectedResource = _resourceSelection.ExactName;
        _saveManager.Save(_saveKey, data);
    }

    private void TrackRecentDrops()
    {
        if (!_config.AutoCarry.Value || Time.time > _dropTrackingUntil) return;
        if (!_carry.TryCollect(_lastWorkPosition, _config.MaxCarryStacks.Value, _config.MaxCarryWeight.Value,
                _resourceSelection, IsResourceForCurrentJob)) return;
        if (_carry.IsFull(_config.MaxCarryStacks.Value, _config.MaxCarryWeight.Value)) _forceDelivery = true;
    }

    private void Award(float xp, Profession profession) =>
        _progression.Award(xp, profession, _config.SpiritXpMultiplier.Value, _config.ProfessionXpMultiplier.Value);

    private bool IsResourceForCurrentJob(ResourceDefinition definition) => _job switch
    {
        SpiritJob.Woodcutting => definition.Category == ResourceCategory.Wood,
        SpiritJob.Mining => definition.Category is ResourceCategory.Ore or ResourceCategory.Stone,
        SpiritJob.Gathering => definition.Category is ResourceCategory.Plant or ResourceCategory.Food,
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
        var debug = $"State: {_state}\nJob: {_job}\nTarget position: {_lastTargetPosition}\nBalance: {_config.BalanceMode.Value}\nEnergy: {_progression.Data.Energy:0.0}\nAttempts: {_workAttempts}\nLast error: {_lastError}";
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
