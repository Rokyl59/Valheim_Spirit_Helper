using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpiritHelper.Logistics.Production;

public sealed class ProductionLogisticsSystem
{
    private sealed class ProductionOrder
    {
        public Smelter Machine = null!;
        public Container Source = null!;
        public ItemDrop ItemPrefab = null!;
        public Switch Input = null!;
        public bool IsFuel;
    }

    private const float InteractionDistance = 3.5f;
    private const float RescanInterval = 3f;
    private const float OwnershipTimeout = 5f;
    private const int MaximumFailures = 3;
    private const float FailureCooldown = 30f;

    private readonly Collider[] _hits = new Collider[1024];
    private readonly HashSet<int> _seen = new HashSet<int>();
    private readonly Dictionary<int, float> _machineCooldowns = new Dictionary<int, float>();
    private ProductionOrder? _order;
    private ItemDrop.ItemData? _reservedItem;
    private GameObject? _cargoVisual;
    private float _nextScanAt;
    private float _claimStartedAt;
    private int _failures;

    public ProductionTaskState State { get; private set; }
    public bool HasReservedCargo => _reservedItem != null;
    public string ReservedItemName => _reservedItem?.m_shared.m_name ?? string.Empty;

    public ProductionTickResult Tick(Player player, Vector3 spiritPosition, Vector3 centre, float radius)
    {
        CleanupInvalidCooldowns();
        UpdateCargoVisual(spiritPosition);

        if (_order == null)
        {
            if (Time.time < _nextScanAt)
                return Result(ProductionTaskState.WaitingForWork, null, "Нет срочной производственной задачи.");

            _nextScanAt = Time.time + RescanInterval;
            _order = FindOrder(centre, radius);
            if (_order == null)
                return Result(ProductionTaskState.WaitingForWork, null, "Печи заполнены или в доступных сундуках нет сырья.");
        }

        if (!_order.Machine || !_order.Source || !_order.ItemPrefab || !_order.Input)
            return FailOrder(spiritPosition, "Производственная точка больше недоступна.");

        if (_reservedItem == null)
        {
            var sourcePoint = _order.Source.transform.position + Vector3.up;
            if (Vector3.Distance(spiritPosition, sourcePoint) > InteractionDistance)
                return Result(ProductionTaskState.MovingToSource, sourcePoint, $"Забираю: {DisplayName(_order.ItemPrefab)}.");

            return ClaimFromSource(spiritPosition);
        }

        var inputPoint = _order.Input.transform.position + Vector3.up * 0.5f;
        if (Vector3.Distance(spiritPosition, inputPoint) > InteractionDistance)
            return Result(ProductionTaskState.MovingToMachine, inputPoint, $"Несу {DisplayName(_order.ItemPrefab)} к {_order.Machine.m_name}.");

        return ServiceMachine(player, spiritPosition);
    }

    public void Cancel(Vector3 fallbackPoint)
    {
        RestoreReservedItem(fallbackPoint);
        ResetOrder();
        State = ProductionTaskState.Idle;
    }

    private ProductionTickResult ClaimFromSource(Vector3 spiritPosition)
    {
        State = ProductionTaskState.ClaimingSource;
        var source = _order!.Source;
        if (!PrivateArea.CheckAccess(source.transform.position, 0f, false, false))
            return FailOrder(spiritPosition, "Нет доступа к сундуку с сырьём.");

        var view = source.GetComponent<ZNetView>();
        if (!view || !view.IsValid()) return FailOrder(spiritPosition, "Сундук недоступен по сети.");
        if (!view.IsOwner())
        {
            if (_claimStartedAt <= 0f) _claimStartedAt = Time.time;
            view.ClaimOwnership();
            if (Time.time - _claimStartedAt > OwnershipTimeout)
                return FailOrder(spiritPosition, "Не удалось получить владение сундуком.");
            return Result(ProductionTaskState.ClaimingSource, source.transform.position, "Ожидаю доступ к сундуку.");
        }

        _claimStartedAt = 0f;
        var inventory = source.GetInventory();
        var prefabName = _order.ItemPrefab.m_itemData.m_shared.m_name;
        var item = FindItem(inventory, prefabName);
        if (item == null) return FailOrder(spiritPosition, "Сырьё в сундуке закончилось.");

        _reservedItem = item.Clone();
        _reservedItem.m_stack = 1;
        if (!inventory.RemoveItem(item, 1))
        {
            _reservedItem = null;
            return FailOrder(spiritPosition, "Не удалось забрать сырьё из сундука.");
        }

        CreateCargoVisual(_order.IsFuel);
        return Result(ProductionTaskState.MovingToMachine, _order.Input.transform.position,
            $"Сырьё получено: {DisplayName(_order.ItemPrefab)}.");
    }

    private ProductionTickResult ServiceMachine(Player player, Vector3 spiritPosition)
    {
        State = ProductionTaskState.ServicingMachine;
        if (!PrivateArea.CheckAccess(_order!.Machine.transform.position, 0f, false, false))
            return FailOrder(spiritPosition, "Нет доступа к производственной постройке.");
        if (!StillNeedsItem(_order)) return FailOrder(spiritPosition, "Постройка уже заполнена.", false);

        var inventory = player.GetInventory();
        var loan = _reservedItem!.Clone();
        loan.m_stack = 1;
        if (!inventory.AddItem(loan))
            return FailOrder(spiritPosition, "В инвентаре игрока нет места для безопасного взаимодействия.");

        var accepted = _order.Input.Interact(player, false, false);
        var loanRemains = inventory.GetAllItems().Contains(loan);
        if (loanRemains) inventory.RemoveItem(loan, 1);
        if (!accepted || loanRemains)
            return FailOrder(spiritPosition, "Постройка отклонила сырьё.");

        var delivered = DisplayName(_order.ItemPrefab);
        _reservedItem = null;
        DestroyCargoVisual();
        ResetOrder();
        _nextScanAt = Time.time + 0.25f;
        State = ProductionTaskState.Idle;
        return new ProductionTickResult(State, null, $"Загружено в производство: {delivered}.", true);
    }

    private ProductionOrder? FindOrder(Vector3 centre, float radius)
    {
        var machines = FindComponents<Smelter>(centre, radius);
        var containers = FindComponents<Container>(centre, radius);
        ProductionOrder? best = null;
        var bestScore = float.MaxValue;

        foreach (var machine in machines)
        {
            if (!machine || IsCoolingDown(machine) || !PrivateArea.CheckAccess(machine.transform.position, 0f, false, false)) continue;
            foreach (var request in Requests(machine))
            foreach (var container in containers)
            {
                if (!container || !PrivateArea.CheckAccess(container.transform.position, 0f, false, false)) continue;
                if (FindItem(container.GetInventory(), request.Item.m_itemData.m_shared.m_name) == null) continue;
                var score = Vector3.Distance(centre, container.transform.position) +
                            Vector3.Distance(container.transform.position, machine.transform.position);
                if (!request.IsFuel) score -= 5f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = new ProductionOrder
                {
                    Machine = machine,
                    Source = container,
                    ItemPrefab = request.Item,
                    Input = request.Input,
                    IsFuel = request.IsFuel
                };
            }
        }
        return best;
    }

    private static IEnumerable<(ItemDrop Item, Switch Input, bool IsFuel)> Requests(Smelter machine)
    {
        if (machine.m_fuelItem && machine.m_addWoodSwitch && machine.GetFuel() + 0.01f < machine.m_maxFuel)
            yield return (machine.m_fuelItem, machine.m_addWoodSwitch, true);

        if (!machine.m_addOreSwitch || machine.GetQueueSize() >= machine.m_maxOre) yield break;
        foreach (var conversion in machine.m_conversion)
            if (conversion?.m_from) yield return (conversion.m_from, machine.m_addOreSwitch, false);
    }

    private static bool StillNeedsItem(ProductionOrder order) => order.IsFuel
        ? order.Machine.GetFuel() + 0.01f < order.Machine.m_maxFuel
        : order.Machine.GetQueueSize() < order.Machine.m_maxOre && order.Machine.GetItemConversion(order.ItemPrefab.m_itemData.m_shared.m_name) != null;

    private List<T> FindComponents<T>(Vector3 centre, float radius) where T : Component
    {
        var found = new List<T>();
        _seen.Clear();
        var count = Physics.OverlapSphereNonAlloc(centre, radius, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (var index = 0; index < count; index++)
        {
            var component = _hits[index] ? _hits[index].GetComponentInParent<T>() : null;
            if (component && _seen.Add(component.GetInstanceID())) found.Add(component);
        }
        return found;
    }

    private ProductionTickResult FailOrder(Vector3 fallbackPoint, string message, bool countFailure = true)
    {
        RestoreReservedItem(fallbackPoint);
        if (countFailure && _order?.Machine)
        {
            _failures++;
            if (_failures >= MaximumFailures)
            {
                _machineCooldowns[_order.Machine.GetInstanceID()] = Time.time + FailureCooldown;
                _failures = 0;
            }
        }
        ResetOrder();
        _nextScanAt = Time.time + RescanInterval;
        State = ProductionTaskState.WaitingForWork;
        return Result(State, null, message);
    }

    private void RestoreReservedItem(Vector3 fallbackPoint)
    {
        if (_reservedItem == null) return;
        if (_order?.Source && PrivateArea.CheckAccess(_order.Source.transform.position, 0f, false, false))
        {
            var view = _order.Source.GetComponent<ZNetView>();
            if (view && view.IsValid() && view.IsOwner() && _order.Source.GetInventory().AddItem(_reservedItem))
            {
                _reservedItem = null;
                DestroyCargoVisual();
                return;
            }
        }
        ItemDrop.DropItem(_reservedItem, _reservedItem.m_stack, fallbackPoint + Vector3.up, Quaternion.identity);
        _reservedItem = null;
        DestroyCargoVisual();
    }

    private static ItemDrop.ItemData? FindItem(Inventory inventory, string sharedName)
    {
        foreach (var item in inventory.GetAllItems())
            if (item.m_shared.m_name == sharedName && item.m_stack > 0) return item;
        return null;
    }

    private void CreateCargoVisual(bool fuel)
    {
        DestroyCargoVisual();
        _cargoVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _cargoVisual.name = "SpiritHelper_ProductionCargo";
        UnityEngine.Object.Destroy(_cargoVisual.GetComponent<Collider>());
        _cargoVisual.transform.localScale = Vector3.one * 0.2f;
        var renderer = _cargoVisual.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        renderer.material.color = fuel ? new Color(1f, 0.55f, 0.15f) : new Color(0.55f, 0.75f, 1f);
    }

    private void UpdateCargoVisual(Vector3 spiritPosition)
    {
        if (_cargoVisual) _cargoVisual.transform.position = spiritPosition + Vector3.down * 0.55f;
    }

    private void DestroyCargoVisual()
    {
        if (_cargoVisual) UnityEngine.Object.Destroy(_cargoVisual);
        _cargoVisual = null;
    }

    private bool IsCoolingDown(Smelter machine) =>
        _machineCooldowns.TryGetValue(machine.GetInstanceID(), out var until) && Time.time < until;

    private void CleanupInvalidCooldowns()
    {
        if (_machineCooldowns.Count == 0) return;
        var expired = new List<int>();
        foreach (var pair in _machineCooldowns) if (Time.time >= pair.Value) expired.Add(pair.Key);
        foreach (var key in expired) _machineCooldowns.Remove(key);
    }

    private void ResetOrder()
    {
        _order = null;
        _claimStartedAt = 0f;
    }

    private ProductionTickResult Result(ProductionTaskState state, Vector3? destination, string status)
    {
        State = state;
        return new ProductionTickResult(state, destination, status, false);
    }

    private static string DisplayName(ItemDrop item) =>
        Localization.instance ? Localization.instance.Localize(item.m_itemData.m_shared.m_name) : item.m_itemData.m_shared.m_name;
}
