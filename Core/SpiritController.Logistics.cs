using System;
using System.Linq;
using SpiritHelper.Logistics;
using SpiritHelper.Resources;
using SpiritHelper.Zones;
using UnityEngine;

namespace SpiritHelper.Core;

public sealed partial class SpiritController
{
    private const float CourierInteractionDistance = 3f;
    private const float CaravanDangerCooldown = 8f;
    private const float CourierFailureTimeout = 5f;
    private bool _buildAssistEnabled;
    private float _lastCaravanHealth = -1f;
    private float _caravanCollectAfter;
    private float _courierSourceBlockedSince;
    private float _courierDestinationBlockedSince;

    private void Courier(Player player, float deltaTime)
    {
        var route = _progression.Data.Automation.CourierRoute;
        var source = new SavedContainerTarget(route.SourceContainerUserId, route.SourceContainerId).Resolve();
        var destination = new SavedContainerTarget(route.DestinationContainerUserId, route.DestinationContainerId).Resolve();
        var definition = _resources.BySelection(route.Resource);
        if (!route.Enabled || !source || !destination || definition == null ||
            Vector3.Distance(player.transform.position, source.transform.position) > _config.MaxDistanceFromPlayer.Value ||
            Vector3.Distance(player.transform.position, destination.transform.position) > _config.MaxDistanceFromPlayer.Value)
        {
            Follow(player, deltaTime);
            return;
        }
        var selection = new ResourceSelection();
        selection.SelectExact(definition.Name);
        if (_carry.Count == 0)
        {
            SetState(SpiritState.CollectDrops);
            _visual!.Move(source.transform.position + Vector3.up * 1.5f, _config.FollowSpeed.Value, deltaTime);
            if (Vector3.Distance(_visual.Transform.position, source.transform.position) > CourierInteractionDistance) return;
            if (!CarrySystem.CanAccessContainer(source) || !source.IsOwner())
            {
                if (BeginCourierFailure(ref _courierSourceBlockedSince, "Source container is unavailable or owned by another peer."))
                    route.Enabled = false;
                return;
            }
            _courierSourceBlockedSince = 0f;
            _carry.TryWithdraw(source, source.transform.position + Vector3.up * 1.2f,
                EffectiveCarryStacks, EffectiveCarryWeight, selection, IsWorkPositionForbidden);
            return;
        }
        SetState(SpiritState.CarryDrops);
        _visual!.Move(destination.transform.position + Vector3.up * 1.5f, _config.FollowSpeed.Value, deltaTime);
        if (Vector3.Distance(_visual.Transform.position, destination.transform.position) > CourierInteractionDistance) return;
        if (CarrySystem.CanAccessContainer(destination) && destination.IsOwner())
        {
            var delivered = _carry.UnloadToContainer(destination, selection.Allows);
            if (delivered > 0) { _courierDestinationBlockedSince = 0f; return; }
        }
        if (BeginCourierFailure(ref _courierDestinationBlockedSince, "Destination container rejected cargo; route stopped and cargo released nearby."))
        {
            _carry.Release(destination.transform.position + Vector3.up);
            route.Enabled = false;
        }
    }

    private void Caravan(Player player, float deltaTime)
    {
        var health = player.GetHealth();
        var danger = _lastCaravanHealth >= 0f && health < _lastCaravanHealth - 0.01f ||
            CountEnemies(player.transform.position, 12f) > 0;
        _lastCaravanHealth = health;
        if (danger)
        {
            if (_carry.Count > 0) _carry.Release(player.transform.position + player.transform.forward * 1.2f);
            _caravanCollectAfter = Time.time + CaravanDangerCooldown;
        }
        SetState(SpiritState.CarryDrops);
        if (Time.time >= _caravanCollectAfter)
            _carry.TryCollect(player.transform.position, EffectiveCarryStacks, EffectiveCarryWeight, _resourceSelection,
                isForbidden: IsWorkPositionForbidden);
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

    private void StartBuildAssist()
    {
        var player = Player.m_localPlayer;
        if (!player || !IsHammerActive(player) || !player.GetSelectedPiece())
        {
            _ui.Notify("Возьмите молот и выберите строительную деталь.");
            return;
        }
        SetJob(SpiritJob.Follow);
        _buildAssistEnabled = true;
        _ui.Notify("Строительный помощник активен: дух принесёт недостающие известные материалы.");
    }

    private void TickBuildAssist(Player player)
    {
        if (!_buildAssistEnabled || !IsHammerActive(player))
        {
            _buildAssistEnabled = false;
            return;
        }
        if (_job == SpiritJob.Bring && _assignmentQuantity > 0) return;
        if (_job is not SpiritJob.Follow and not SpiritJob.Bring) return;
        var piece = player.GetSelectedPiece();
        if (!piece || piece.m_resources == null) return;
        foreach (var requirement in piece.m_resources)
        {
            if (!requirement.m_resItem) continue;
            var definition = _resources.ByDrop(requirement.m_resItem.gameObject.name);
            if (definition == null) continue;
            var available = 0;
            foreach (var item in player.GetInventory().GetAllItems())
                if (item.m_dropPrefab && item.m_dropPrefab.name == requirement.m_resItem.gameObject.name) available += item.m_stack;
            var missing = Mathf.Max(0, requirement.m_amount - available);
            if (missing == 0) continue;
            _resourceSelection.SelectExact(definition.Name);
            _selectedQuantity = missing;
            SetJob(SpiritJob.Bring);
            _ui.Notify($"Строительный помощник несёт {ResourceName(definition.Name)} × {missing} для {piece.m_name}.");
            return;
        }
    }

    private void ConfigureCourier()
    {
        var player = Player.m_localPlayer;
        var container = ContainerAtAim();
        if (!player || !container || !CarrySystem.CanAccessContainer(container))
        {
            _ui.Notify("Наведитесь на доступный сундук.");
            return;
        }
        if (_resourceSelection.Mode != ResourceFilterMode.Exact)
        {
            _ui.Notify("Сначала выберите конкретный ресурс для маршрута.");
            return;
        }
        var target = SavedContainerTarget.From(container);
        if (!target.IsBound) { _ui.Notify("Не удалось привязать сундук к маршруту."); return; }
        var route = _progression.Data.Automation.CourierRoute;
        if (!_courierAwaitingDestination)
        {
            route.Source = ToArray(container.transform.position);
            route.SourceContainerUserId = target.UserId;
            route.SourceContainerId = target.Id;
            route.Resource = _resourceSelection.ExactName;
            route.Enabled = false;
            _courierAwaitingDestination = true;
            _ui.Notify("Источник маршрута A установлен. Наведитесь на точку B и выберите команду ещё раз.", 6f);
            return;
        }
        route.Destination = ToArray(container.transform.position);
        route.DestinationContainerUserId = target.UserId;
        route.DestinationContainerId = target.Id;
        route.Enabled = true;
        _courierAwaitingDestination = false;
        _ui.Notify("Маршрут почтальона A → B активирован.");
        SetJob(SpiritJob.Courier);
    }

    private static Container? ContainerAtAim()
    {
        var camera = Camera.main;
        if (!camera || !Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit, 45f,
                Physics.AllLayers, QueryTriggerInteraction.Collide)) return null;
        return hit.collider ? hit.collider.GetComponentInParent<Container>() : null;
    }

    private static bool IsHammerActive(Player player)
    {
        return player.GetSelectedPiece();
    }

    private bool BeginCourierFailure(ref float blockedSince, string message)
    {
        if (blockedSince <= 0f)
        {
            blockedSince = Time.time;
            return false;
        }
        if (Time.time - blockedSince < CourierFailureTimeout) return false;
        _ui.Notify(message, 6f);
        _knowledge.RecordDecision($"Courier stopped: {message}");
        blockedSince = 0f;
        return true;
    }

}
