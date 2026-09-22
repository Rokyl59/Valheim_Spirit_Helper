using System;
using SpiritHelper.Audio;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using SpiritHelper.Visuals;
using UnityEngine;

namespace SpiritHelper.Core;

public sealed partial class SpiritController
{
    private SpiritOrderData CaptureOrder(SpiritJob job) => new SpiritOrderData
    {
        Job = job,
        Quantity = job is SpiritJob.Woodcutting or SpiritJob.Mining or SpiritJob.Gathering or SpiritJob.Bring
            ? _selectedQuantity : 0,
        FilterMode = _resourceSelection.Mode,
        Category = _resourceSelection.Category,
        Resource = _resourceSelection.ExactName,
        HasWorkZone = _markers.HasWorkZone,
        WorkZone = ToArray(_markers.WorkZoneCentre),
        Radius = _config.WorkRadius.Value,
        ZoneMode = _markers.ZoneMode,
        HasDeliveryPoint = _orderDeliveryPoint.HasValue,
        DeliveryPoint = ToArray(_orderDeliveryPoint ?? Vector3.zero),
        CompletionJob = _progression.Data.Automation.CompletionJob
    };

    private void ActivateOrder(SpiritOrderData order)
    {
        _visual?.ResetNavigation();
        _followCloseToPlayer = false;
        _restNearPlayer = false;
        _job = order.Job;
        _assignmentQuantity = Math.Max(0, order.Quantity);
        _assignmentProgress = Math.Max(0, order.Collected);
        _resourceSelection.Restore(order.FilterMode, order.Category, order.Resource);
        _config.WorkRadius.Value = Mathf.Max(5f, order.Radius);
        _markers.ZoneMode = order.ZoneMode;
        if (order.HasWorkZone) _markers.SetWorkZone(SpiritKnowledge.ToVector(order.WorkZone), WorkRadius, _config.ShowWorkZone.Value);
        else _markers.RemoveWorkZone();
        _orderDeliveryPoint = order.HasDeliveryPoint ? SpiritKnowledge.ToVector(order.DeliveryPoint) : (Vector3?)null;
        _progression.Data.Automation.CurrentOrder = order;
        _target = null;
        _workAttempts = 0;
        _forceDelivery = false;
        _finishingOrder = _assignmentQuantity > 0 && _assignmentProgress >= _assignmentQuantity;
        _carry.CancelPending();
    }

    private void OnResourceCollected(ResourceDefinition definition, int amount)
    {
        if (!_resourceSelection.Allows(definition) || !IsResourceForCurrentJob(definition)) return;
        var count = _knowledge.RecordResource(definition.Name, amount);
        if (count >= 500 && count - amount < 500)
        {
            _ui.Notify($"{_progression.Data.SpiritName} получил сродство: {ResourceName(definition.Name)}.");
            AddJournal($"Открыто сродство с ресурсом {definition.Name}.");
        }
        if (_assignmentQuantity <= 0 || _finishingOrder) return;
        _assignmentProgress += amount;
        var order = _progression.Data.Automation.CurrentOrder;
        if (order != null) order.Collected = _assignmentProgress;
        if (_assignmentProgress < _assignmentQuantity) return;
        _finishingOrder = true;
        _target = null;
        _collectDropsUntil = 0f;
        _dropTrackingUntil = 0f;
        _carry.CancelPending();
        _ui.Notify($"Количество собрано: {_assignmentProgress}/{_assignmentQuantity}. Доставляю груз.");
    }

    private void FinishOrder(Player player, float deltaTime)
    {
        if (_carry.Count > 0)
        {
            if (_job == SpiritJob.Bring) Bring(player, deltaTime);
            else DeliverCarried(player, deltaTime);
            return;
        }
        if (_carry.PendingCount > 0) return;
        _finishingOrder = false;
        var completed = _progression.Data.Automation.CurrentOrder;
        if (completed != null && completed.Delivered < _assignmentQuantity)
        {
            _assignmentProgress = completed.Delivered;
            completed.Collected = _assignmentProgress;
            _lastError = "Часть груза потеряна. Собираю недостающее количество.";
            return;
        }
        _ui.Notify($"Поручение выполнено: {_assignmentProgress}/{_assignmentQuantity}.");
        _audio?.Play(SpiritAudioEvent.JobComplete);
        _visual?.ShowEmotion(SpiritEmotion.Happy);
        _celebrateUntil = Time.time + 2.2f;
        if (_progression.Data.Automation.RepeatOrders && completed != null)
        {
            completed.Collected = 0;
            completed.Delivered = 0;
            _assignments.Enqueue(completed);
        }
        if (_assignments.Count > 0) { ActivateOrder(_assignments.Dequeue()); return; }
        _assignmentQuantity = 0;
        _assignmentProgress = 0;
        _progression.Data.Automation.CurrentOrder = null;
        _job = completed?.CompletionJob ?? SpiritJob.Follow;
        if (_job == SpiritJob.Guide)
        {
            _guideDestination = _orderDeliveryPoint ?? (_markers.HasUnloadPoint ? _markers.UnloadPoint : player.transform.position);
            _guideLabel = "место доставки";
        }
    }

    private void OnResourceDelivered(ResourceDefinition definition, int amount)
    {
        _progression.Data.ItemsDelivered += amount;
        var order = _progression.Data.Automation.CurrentOrder;
        if (order == null || order.Quantity <= 0 || !_resourceSelection.Allows(definition) || !IsResourceForCurrentJob(definition)) return;
        order.Delivered += amount;
    }

    private bool IsWorkPositionForbidden(Vector3 point)
    {
        if (_markers.IsForbidden(point)) return true;
        foreach (var ignored in _progression.Data.Automation.IgnoredTargets)
            if (Vector3.SqrMagnitude(SpiritKnowledge.ToVector(ignored.Position) - point) < 4f) return true;
        return false;
    }
}
