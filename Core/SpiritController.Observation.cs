using System;
using System.Collections.Generic;
using System.Linq;
using SpiritHelper.Audio;
using SpiritHelper.Progression;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.Core;

public sealed partial class SpiritController
{
    private const float LearningRadius = 12f;
    private const float LearningActionTimeout = 30f;
    private const float LearningDeliveryTimeout = 30f;
    private readonly Collider[] _learningHits = new Collider[256];
    private Component? _learningTarget;
    private bool _learningArmed;
    private bool _learningFinalized;
    private Vector3 _learningPosition;
    private float _learningEndsAt;
    private RequiredToolType _learningTool;
    private int _learningToolTier;
    private bool _learningToolCaptured;
    private string _learningPrefab = string.Empty;
    private bool _learningTargetIsPickable;
    private ResourceDefinition? _learningKnownDefinition;
    private readonly HashSet<int> _learningKnownDropIds = new HashSet<int>();
    private readonly HashSet<string> _learningObservedDrops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _learningInventory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private Container? _learningDeliveryContainer;
    private int _learningDeliveryBaseline;
    private Dictionary<string, int>? _learningInventoryAfterWork;

    private void Observe(Player player, float deltaTime)
    {
        SetState(SpiritState.Observe);
        Follow(player, deltaTime);
        if (_ui.MenuOpen) return;
        if (!_learningArmed)
        {
            if (Input.GetMouseButton(0) || Input.GetKey(KeyCode.E)) ArmLearning(player, LookComponent());
            return;
        }

        if (!_learningToolCaptured && (Input.GetMouseButton(0) || Input.GetKey(KeyCode.E)))
        {
            _learningTool = DetectTool(player, out _learningToolTier);
            _learningToolCaptured = true;
        }
        CaptureNewDrops(player);
        if (!_learningFinalized)
        {
            if (_learningToolCaptured && _learningObservedDrops.Count > 0) FinalizeLearning(player);
            else if (Time.time >= _learningEndsAt)
            {
                _ui.Notify("Дух не увидел новый дроп после действия. Ресурс не добавлен.", 6f);
                ResetLearning();
            }
            return;
        }

        if (DeliveryObserved(player) || Time.time >= _learningEndsAt) CompleteObservedOrder(player);
    }

    private void LearnLookTarget()
    {
        var player = Player.m_localPlayer;
        if (!player) return;
        ArmLearning(player, LookComponent(), true);
    }

    private void ArmLearning(Player player, Component? component, bool notify = false)
    {
        if (!component)
        {
            if (notify) _ui.Notify("Объект для обучения не найден.");
            return;
        }
        _learningTarget = component;
        _learningArmed = true;
        _learningFinalized = false;
        _learningPosition = component.transform.position;
        _learningEndsAt = Time.time + LearningActionTimeout;
        _learningPrefab = component.gameObject.name;
        _learningTargetIsPickable = component is Pickable;
        _learningKnownDefinition = _resources.ByTarget(component);
        _learningTool = DetectTool(player, out _learningToolTier);
        _learningToolCaptured = !notify;
        _learningKnownDropIds.Clear();
        _learningObservedDrops.Clear();
        _learningInventory.Clear();
        _learningDeliveryContainer = null;
        _learningInventoryAfterWork = null;
        SnapshotDrops(_learningPosition, _learningKnownDropIds, null);
        SnapshotInventory(player, _learningInventory);
        SetJob(SpiritJob.Observe);
        if (notify) _ui.Notify("Дух наблюдает действие и ждёт новый дроп от выбранной цели.", 6f);
    }

    private void CaptureNewDrops(Player player)
    {
        SnapshotDrops(_learningPosition, _learningKnownDropIds, _learningObservedDrops);
        var currentInventory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        SnapshotInventory(player, currentInventory);
        foreach (var entry in currentInventory)
        {
            _learningInventory.TryGetValue(entry.Key, out var previousCount);
            if (entry.Value > previousCount) _learningObservedDrops.Add(entry.Key);
        }
    }

    private void FinalizeLearning(Player player)
    {
        var drops = _learningObservedDrops.ToArray();
        var definition = _learningKnownDefinition ?? _resources.Learn(_learningPrefab, _learningTool, _learningTargetIsPickable, drops, _learningToolTier);
        _ui.SetResources(_resources.All);
        _resourceSelection.SelectExact(definition.Name);
        _knowledge.Remember(definition.Name, _learningPosition, CurrentDay());
        _knowledge.RecordDecision($"Observed resource: {definition.Name} -> {string.Join(", ", drops)}");
        AddJournal($"Наблюдал ресурс: {definition.Name}; дроп: {string.Join(", ", drops)}.");
        _audio?.Play(SpiritAudioEvent.TargetFound);
        _learningFinalized = true;
        _learningEndsAt = Time.time + LearningDeliveryTimeout;
        _learningInventoryAfterWork = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        SnapshotInventory(player, _learningInventoryAfterWork);
        _learningDeliveryContainer = FindNearbyContainer(player.transform.position, 8f);
        _learningDeliveryBaseline = _learningDeliveryContainer ? InventoryCount(_learningDeliveryContainer, drops) : 0;
        _ui.Notify($"Дух увидел дроп {string.Join(", ", drops)}. Положите его в сундук рядом в течение 30 секунд, чтобы сохранить доставку.", 7f);
    }

    private bool DeliveryObserved(Player player)
    {
        if (!_learningDeliveryContainer || _learningInventoryAfterWork == null) return false;
        var current = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        SnapshotInventory(player, current);
        var playerDelivered = false;
        foreach (var drop in _learningObservedDrops)
        {
            _learningInventoryAfterWork.TryGetValue(drop, out var before);
            current.TryGetValue(drop, out var after);
            if (after < before) { playerDelivered = true; break; }
            if (after > before) _learningInventoryAfterWork[drop] = after;
        }
        return playerDelivered && InventoryCount(_learningDeliveryContainer, _learningObservedDrops) > _learningDeliveryBaseline;
    }

    private void CompleteObservedOrder(Player player)
    {
        var definition = _learningKnownDefinition ?? _resources.ByPrefab(_learningPrefab);
        if (definition == null) { ResetLearning(); return; }
        var hasDelivery = DeliveryObserved(player);
        if (hasDelivery && _learningDeliveryContainer && _progression.Level >= 7)
            _markers.SetUnload(_learningDeliveryContainer.transform.position, _progression.Level >= 25 ? 6 : _progression.Level >= 15 ? 3 : 1,
                _learningDeliveryContainer, definition.Name);
        _progression.Data.Automation.ObservedOrder = new SpiritOrderData
        {
            Job = JobFor(definition), FilterMode = ResourceFilterMode.Exact, Resource = definition.Name,
            HasWorkZone = _markers.HasWorkZone, WorkZone = _markers.HasWorkZone ? ToArray(_markers.WorkZoneCentre) : new float[3],
            Radius = _config.WorkRadius.Value, ZoneMode = _markers.ZoneMode, HasDeliveryPoint = hasDelivery,
            DeliveryPoint = hasDelivery && _learningDeliveryContainer ? ToArray(_learningDeliveryContainer.transform.position) : new float[3],
            CompletionJob = SpiritJob.Follow
        };
        _ui.Notify(hasDelivery
            ? $"Поручение сохранено: {definition.Name} с доставкой в наблюдаемый сундук."
            : $"Поручение сохранено: {definition.Name}. Доставка не задана.", 7f);
        ResetLearning();
    }

    private bool TryRepeatObservedOrder()
    {
        var order = _progression.Data.Automation.ObservedOrder;
        if (order == null || string.IsNullOrWhiteSpace(order.Resource)) return false;
        _resourceSelection.Restore(order.FilterMode, order.Category, order.Resource);
        if (order.HasWorkZone) _markers.SetWorkZone(SpiritKnowledge.ToVector(order.WorkZone), order.Radius, _config.ShowWorkZone.Value);
        _orderDeliveryPoint = order.HasDeliveryPoint ? SpiritKnowledge.ToVector(order.DeliveryPoint) : null;
        SetJob(order.Job);
        _ui.Notify($"Повторяю изученное поручение: {ResourceName(order.Resource)}.", 5f);
        return true;
    }

    private void ResetLearning()
    {
        _learningTarget = null; _learningArmed = false; _learningFinalized = false; _learningPrefab = string.Empty;
        _learningToolCaptured = false;
        _learningKnownDefinition = null; _learningDeliveryContainer = null; _learningInventoryAfterWork = null;
        _learningKnownDropIds.Clear(); _learningObservedDrops.Clear(); _learningInventory.Clear();
    }

    private void SnapshotDrops(Vector3 centre, ISet<int> knownIds, ISet<string>? newDropNames)
    {
        var count = Physics.OverlapSphereNonAlloc(centre, LearningRadius, _learningHits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (var index = 0; index < count; index++)
        {
            var drop = _learningHits[index] ? _learningHits[index].GetComponentInParent<ItemDrop>() : null;
            if (!drop || !knownIds.Add(drop.GetInstanceID())) continue;
            if (newDropNames != null)
                newDropNames.Add(drop.m_itemData.m_dropPrefab ? drop.m_itemData.m_dropPrefab.name : drop.gameObject.name.Replace("(Clone)", string.Empty).Trim());
        }
    }

    private static void SnapshotInventory(Player player, IDictionary<string, int> inventory)
    {
        foreach (var item in player.GetInventory().GetAllItems())
        {
            if (!item.m_dropPrefab) continue;
            inventory.TryGetValue(item.m_dropPrefab.name, out var count);
            inventory[item.m_dropPrefab.name] = count + item.m_stack;
        }
    }

    private Container? FindNearbyContainer(Vector3 centre, float radius)
    {
        var count = Physics.OverlapSphereNonAlloc(centre, radius, _learningHits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        Container? nearest = null;
        var nearestDistance = float.MaxValue;
        var seen = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var container = _learningHits[index] ? _learningHits[index].GetComponentInParent<Container>() : null;
            if (!container || !seen.Add(container.GetInstanceID())) continue;
            var distance = Vector3.SqrMagnitude(container.transform.position - centre);
            if (distance >= nearestDistance) continue;
            nearest = container;
            nearestDistance = distance;
        }
        return nearest;
    }

    private static int InventoryCount(Container container, IEnumerable<string> prefabs) => container.GetInventory().GetAllItems()
        .Where(item => item.m_dropPrefab && prefabs.Contains(item.m_dropPrefab.name, StringComparer.OrdinalIgnoreCase)).Sum(item => item.m_stack);

    private static RequiredToolType DetectTool(Player player, out int tier)
    {
        var weapon = player.GetCurrentWeapon();
        tier = weapon?.m_shared.m_toolTier ?? 0;
        if (weapon == null) return RequiredToolType.None;
        var damage = weapon.GetDamage();
        if (damage.m_chop > 0f && damage.m_chop >= damage.m_pickaxe) return RequiredToolType.Axe;
        return damage.m_pickaxe > 0f ? RequiredToolType.Pickaxe : RequiredToolType.None;
    }
}
