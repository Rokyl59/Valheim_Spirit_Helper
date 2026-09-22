using System;
using System.Linq;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using SpiritHelper.UI;
using UnityEngine;

namespace SpiritHelper.Core;

public sealed partial class SpiritController
{
    private const float PingChainWindow = 12f;
    private float _lastResourcePingAt = float.NegativeInfinity;
    private float _lastGroundPingAt = float.NegativeInfinity;
    private Vector3 _lastGroundPing;

    public void HandleSpiritPing(Vector3 position, bool local)
    {
        if (!local || !_initialized) return;
        var player = Player.m_localPlayer;
        if (!player || player.IsDead()) return;
        if (Vector3.Distance(player.transform.position, position) < 2f)
        { SetJob(SpiritJob.Follow); return; }
        if (Vector3.Distance(player.transform.position, position) > _config.MaxDistanceFromPlayer.Value)
        { _ui.Notify("Метка за пределами доступной области. Подойдите ближе."); return; }
        var hits = Physics.OverlapSphere(position, 2f, Physics.AllLayers, QueryTriggerInteraction.Collide);
        var nearest = hits.Where(hit => hit).OrderBy(hit => Vector3.SqrMagnitude(hit.ClosestPoint(position) - position)).ToArray();
        foreach (var hit in nearest)
        {
            var container = hit.GetComponentInParent<Container>();
            if (container) { AssignContainer(container); return; }
            var enemy = hit.GetComponentInParent<Character>();
            if (enemy && enemy != player && BaseAI.IsEnemy(player, enemy))
            { _guardThreat = enemy; SetJob(SpiritJob.Guard); return; }
            var drop = hit.GetComponentInParent<ItemDrop>();
            if (drop && _resources.ByDrop(drop.gameObject.name) is { } dropResource)
            {
                SetExactFilter(dropResource.Name, ResourceName(dropResource.Name));
                SetJob(SpiritJob.Bring);
                return;
            }
            Component? resource = hit.GetComponentInParent<Pickable>();
            resource ??= hit.GetComponentInParent<TreeLog>();
            resource ??= hit.GetComponentInParent<TreeBase>();
            resource ??= hit.GetComponentInParent<MineRock5>();
            resource ??= hit.GetComponentInParent<MineRock>();
            resource ??= hit.GetComponentInParent<Destructible>();
            var definition = resource ? _resources.ByTarget(resource) : null;
            if (definition == null) continue;
            SetExactFilter(definition.Name, ResourceName(definition.Name));
            if (_progression.Level >= 5)
                _markers.SetWorkZone(Time.unscaledTime - _lastGroundPingAt <= PingChainWindow ? _lastGroundPing : position,
                    WorkRadius, _config.ShowWorkZone.Value);
            _lastResourcePingAt = Time.unscaledTime;
            SetJob(JobFor(definition));
            return;
        }
        if (_progression.Level < 5) { _ui.Notify("Рабочая область откроется на уровне 5."); return; }
        if (IsWorkPositionForbidden(position)) { _ui.Notify("Это место находится в запретной области."); return; }
        _lastGroundPingAt = Time.unscaledTime;
        _lastGroundPing = position;
        _markers.SetWorkZone(position, WorkRadius, _config.ShowWorkZone.Value);
        if (_progression.Data.Automation.CurrentOrder is { } order)
        { order.HasWorkZone = true; order.WorkZone = ToArray(position); }
        _ui.Notify("Работать здесь. Следующая метка ресурса выберет добычу.");
    }

    private void AssignContainer(Container container)
    {
        if (_progression.Level < 7) { _ui.Notify("Разгрузка открывается на уровне 7."); return; }
        if (!PrivateArea.CheckAccess(container.transform.position, 0f, false, false))
        { _ui.Notify("Нет доступа к этому сундуку."); return; }
        var maximumPoints = _progression.Level >= 25 ? 6 : _progression.Level >= 15 ? 3 : 1;
        var position = container.transform.position;
        _markers.SetUnload(position, maximumPoints, container, _resourceSelection.ExactName);
        if (Time.unscaledTime - _lastResourcePingAt <= PingChainWindow)
        {
            _orderDeliveryPoint = position;
            if (_progression.Data.Automation.CurrentOrder is { } order)
            { order.HasDeliveryPoint = true; order.DeliveryPoint = ToArray(position); }
        }
        _ui.Notify("Назначен склад. Дух использует этот сундук; при отказе оставит груз рядом.");
    }

    private string MenuActionValue(SpiritMenuAction action) => action switch
    {
        SpiritMenuAction.OpenFilter or SpiritMenuAction.OpenExactFilter => FilterName(),
        SpiritMenuAction.OpenShrine => _progression.Data.Automation.ShrineSet
            ? $"ур. {_progression.Data.Automation.ShrineLevel}" : "не установлен",
        SpiritMenuAction.ToggleWorkZoneVisibility => OnOff(_config.ShowWorkZone.Value),
        SpiritMenuAction.ToggleAutoCarry => OnOff(_config.AutoCarry.Value),
        SpiritMenuAction.ToggleHud => OnOff(_config.EnableHud.Value),
        SpiritMenuAction.CycleWorkRadius => $"{WorkRadius:0} м",
        SpiritMenuAction.CycleBalance => BalanceName(_config.BalanceMode.Value),
        SpiritMenuAction.CycleUnloadType => UnloadTypeName(_markers.UnloadType),
        SpiritMenuAction.CycleZoneMode => ZoneModeName(_markers.ZoneMode),
        SpiritMenuAction.CycleQuantity => QuantityName(_selectedQuantity),
        SpiritMenuAction.ToggleQueueMode => OnOff(_queueMode),
        SpiritMenuAction.CycleOrbSize => $"{_config.OrbSize.Value:0.00}",
        SpiritMenuAction.CycleOrbColor => _config.OrbColor.Value,
        SpiritMenuAction.CycleFollowDistance => $"{_config.FollowDistance.Value:0.0} м",
        SpiritMenuAction.CycleFollowHeight => $"{_config.FollowHeight.Value:0.0} м",
        SpiritMenuAction.CycleFollowSpeed => $"{_config.FollowSpeed.Value:0}",
        SpiritMenuAction.CycleInertia => $"{_config.MovementInertia.Value:0.00}",
        SpiritMenuAction.CycleBobbing => $"{_config.BobbingAmount.Value:0.00}",
        SpiritMenuAction.CycleLightMode or SpiritMenuAction.ToggleTorch => LightModeName(_config.LightMode.Value),
        SpiritMenuAction.CycleLightIntensity => $"{_config.LightIntensity.Value:0.0}",
        SpiritMenuAction.CycleLightRange => $"{_config.LightRange.Value:0} м",
        SpiritMenuAction.CycleLightDistance => $"{_config.CameraLightDistance.Value:0} м",
        SpiritMenuAction.CycleLightColor => _config.LightColor.Value,
        SpiritMenuAction.CycleSafeRadius => $"{_config.SafeBaseRadius.Value:0} м",
        SpiritMenuAction.ToggleProtectTrees => OnOff(_config.ProtectPlantedTrees.Value),
        SpiritMenuAction.ToggleProtectCrops => OnOff(_config.ProtectCrops.Value),
        SpiritMenuAction.ToggleRules => OnOff(_rulesEnabled),
        SpiritMenuAction.ToggleSchedule => OnOff(_scheduleEnabled),
        SpiritMenuAction.ToggleSuggestions => OnOff(_progression.Data.Automation.SuggestionsEnabled),
        SpiritMenuAction.ToggleRepeatOrders => OnOff(_progression.Data.Automation.RepeatOrders),
        SpiritMenuAction.CycleMaintainStock => _progression.Data.Automation.MaintainStock.ToString(),
        SpiritMenuAction.BaseAlarm => OnOff(_baseAlarmEnabled),
        SpiritMenuAction.DetectSecret => OnOff(_secretDetectorEnabled),
        _ => string.Empty
    };

    private static int MenuRequiredLevel(SpiritMenuAction action) => action switch
    {
        SpiritMenuAction.Transport or SpiritMenuAction.Caravan or SpiritMenuAction.Bring or SpiritMenuAction.BuildAssist => 3,
        SpiritMenuAction.PlaceWorkZone or SpiritMenuAction.SaveNamedZone or SpiritMenuAction.CycleNamedZone => 5,
        SpiritMenuAction.PlaceUnload => 7,
        SpiritMenuAction.Scout or SpiritMenuAction.Guard or SpiritMenuAction.ExploreDungeon => 10,
        SpiritMenuAction.Courier or SpiritMenuAction.Sort => 15,
        SpiritMenuAction.ToggleQueueMode or SpiritMenuAction.Expedition => 20,
        SpiritMenuAction.Production or SpiritMenuAction.ToggleRules or SpiritMenuAction.ToggleSchedule or SpiritMenuAction.CycleMaintainStock => 25,
        SpiritMenuAction.ReleaseSpirit => 30,
        _ => 1
    };
}
