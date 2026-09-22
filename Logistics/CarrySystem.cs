using System;
using System.Collections.Generic;
using System.Reflection;
using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.Logistics;

public sealed class CarrySystem
{
    private sealed class PendingClaim
    {
        public ItemDrop Drop = null!;
        public ResourceDefinition Definition = null!;
        public float StartedAt;
        public int RequestedStack;
    }

    private sealed class Cargo
    {
        public ItemDrop Drop = null!;
        public ResourceDefinition Definition = null!;
        public GameObject Visual = null!;
        public Renderer[] Renderers = Array.Empty<Renderer>();
        public bool[] RendererStates = Array.Empty<bool>();
        public bool AutoPickup;
    }

    private sealed class DeliveredDrop
    {
        public ItemDrop Drop = null!;
        public Vector3 Position;
    }

    private const float OwnershipTimeout = 8f;
    private const float DeliveryExclusionDistance = 2.5f;
    private static readonly MethodInfo? ContainerCheckAccess = typeof(Container).GetMethod("CheckAccess",
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(long) }, null);
    private readonly ResourceDatabase _database;
    private readonly List<Cargo> _carried = new List<Cargo>();
    private readonly Dictionary<int, PendingClaim> _pending = new Dictionary<int, PendingClaim>();
    private readonly Dictionary<int, DeliveredDrop> _recentDeliveries = new Dictionary<int, DeliveredDrop>();
    private readonly Dictionary<int, int> _splitRemainders = new Dictionary<int, int>();
    private readonly Collider[] _hits = new Collider[512];
    private bool _blockedByCapacity;

    public CarrySystem(ResourceDatabase database) => _database = database;
    public Func<int>? RemainingQuantity { get; set; }
    public Func<Vector3, bool>? IsForbidden { get; set; }
    public event Action<ResourceDefinition, int>? Collected;
    public event Action<ResourceDefinition, int>? Delivered;
    public string LastFailure { get; private set; } = string.Empty;
    public int Count => _carried.Count;
    public int PendingCount => _pending.Count;
    public string Summary => _carried.Count == 0 ? (_pending.Count == 0 ? "—" : $"получение: {_pending.Count}") : $"{_carried.Count} стоп., {CurrentWeight:0.#} кг";
    public float CurrentWeight
    {
        get
        {
            var weight = 0f;
            foreach (var cargo in _carried) if (cargo.Drop) weight += Weight(cargo.Drop);
            foreach (var claim in _pending.Values) if (claim.Drop) weight += Weight(claim.Drop, claim.RequestedStack);
            return weight;
        }
    }

    public bool IsFull(int maxStacks, float maxWeight) =>
        _carried.Count + _pending.Count >= maxStacks || CurrentWeight >= maxWeight || _blockedByCapacity;

    public bool TryCollect(Vector3 position, int maxStacks, float maxWeight, ResourceSelection selection,
        Func<ResourceDefinition, bool>? jobFilter = null, Func<Vector3, bool>? isForbidden = null)
    {
        LastFailure = string.Empty;
        PruneDropTracking();
        TickClaims(maxStacks, maxWeight);
        if (GetRemainingQuantity() <= 0) return _carried.Count > 0 || _pending.Count > 0;
        _blockedByCapacity = false;
        var count = Physics.OverlapSphereNonAlloc(position, 10f, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        var occupied = _carried.Count + _pending.Count;
        var weight = CurrentWeight;
        for (var index = 0; index < count; index++)
        {
            if (occupied >= maxStacks) { _blockedByCapacity = true; break; }
            var drop = _hits[index] ? _hits[index].GetComponentInParent<ItemDrop>() : null;
            if (!drop || Contains(drop)) continue;
            if (IsRecentlyDelivered(drop) || IsSplitRemainder(drop)) continue;
            if (IsProtectedOrForbidden(drop.transform.position, isForbidden)) continue;
            var definition = _database.ByDrop(drop.gameObject.name);
            if (definition == null || !definition.CanAutoTransport || !selection.Allows(definition) || jobFilter != null && !jobFilter(definition)) continue;
            var requestedStack = FitStack(drop, Mathf.Min(drop.m_itemData.m_stack, AvailableQuantity()), maxWeight - weight);
            if (requestedStack <= 0)
            {
                LastFailure = AvailableQuantity() <= 0 ? "Requested quantity is already reserved." : "Cargo weight capacity reached.";
                break;
            }
            var dropWeight = Weight(drop, requestedStack);
            if (weight + dropWeight > maxWeight) { _blockedByCapacity = true; continue; }
            var view = drop.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) continue;
            _pending[drop.GetInstanceID()] = new PendingClaim
            {
                Drop = drop, Definition = definition, StartedAt = Time.time, RequestedStack = requestedStack
            };
            drop.RequestOwn();
            occupied++;
            weight += dropWeight;
        }
        TickClaims(maxStacks, maxWeight);
        return _carried.Count > 0 || _pending.Count > 0;
    }

    public Vector3? FindNearest(Vector3 centre, float radius, ResourceSelection selection,
        Func<Vector3, bool>? isForbidden = null, int maxStacks = int.MaxValue, float maxWeight = float.MaxValue)
    {
        LastFailure = string.Empty;
        PruneDropTracking();
        if (GetRemainingQuantity() <= 0) return null;
        if (_carried.Count + _pending.Count >= maxStacks || CurrentWeight >= maxWeight)
        {
            LastFailure = "Cargo capacity reached.";
            return null;
        }
        var count = Physics.OverlapSphereNonAlloc(centre, radius, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        ItemDrop? nearest = null;
        var nearestDistance = float.MaxValue;
        for (var index = 0; index < count; index++)
        {
            var drop = _hits[index] ? _hits[index].GetComponentInParent<ItemDrop>() : null;
            if (!drop || Contains(drop)) continue;
            if (IsRecentlyDelivered(drop) || IsSplitRemainder(drop)) continue;
            if (IsProtectedOrForbidden(drop.transform.position, isForbidden)) continue;
            var definition = _database.ByDrop(drop.gameObject.name);
            if (definition == null || !definition.CanAutoTransport || !selection.Allows(definition)) continue;
            var stack = FitStack(drop, Mathf.Min(drop.m_itemData.m_stack, AvailableQuantity()), maxWeight - CurrentWeight);
            if (stack <= 0) continue;
            var distance = Vector3.SqrMagnitude(drop.transform.position - centre);
            if (distance >= nearestDistance) continue;
            nearest = drop;
            nearestDistance = distance;
        }
        return nearest ? nearest.transform.position : null;
    }

    public void TickClaims(int maxStacks, float maxWeight)
    {
        PruneDropTracking();
        if (_pending.Count == 0) return;
        var keys = new List<int>(_pending.Keys);
        foreach (var key in keys)
        {
            if (!_pending.TryGetValue(key, out var claim)) continue;
            if (!claim.Drop || Time.time - claim.StartedAt > OwnershipTimeout)
            {
                _pending.Remove(key);
                continue;
            }
            var view = claim.Drop.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) { _pending.Remove(key); continue; }
            if (IsProtectedOrForbidden(claim.Drop.transform.position, null)) { _pending.Remove(key); continue; }
            if (!view.IsOwner()) { claim.Drop.RequestOwn(); continue; }
            var stack = Mathf.Min(claim.Drop.m_itemData.m_stack, claim.RequestedStack, AvailableQuantity(claim));
            if (stack <= 0) { _pending.Remove(key); continue; }
            if (claim.Drop.m_itemData.m_stack > stack)
            {
                var carriedItem = claim.Drop.m_itemData.Clone();
                carriedItem.m_stack = stack;
                var split = ItemDrop.DropItem(carriedItem, stack, claim.Drop.transform.position, claim.Drop.transform.rotation);
                if (!split) { LastFailure = "Could not split the requested item stack."; _pending.Remove(key); continue; }
                claim.Drop.SetStack(claim.Drop.m_itemData.m_stack - stack);
                _splitRemainders[claim.Drop.GetInstanceID()] = split.GetInstanceID();
                claim.Drop = split;
                claim.StartedAt = Time.time;
                _pending.Remove(key);
                _pending[split.GetInstanceID()] = claim;
                split.RequestOwn();
                continue;
            }
            if (_carried.Count >= maxStacks || CarriedWeight() + Weight(claim.Drop) > maxWeight)
            {
                LastFailure = "Cargo capacity reached.";
                _pending.Remove(key);
                continue;
            }
            _pending.Remove(key);
            _carried.Add(CreateCargo(claim.Drop, claim.Definition));
            Collected?.Invoke(claim.Definition, claim.Drop.m_itemData.m_stack);
        }
    }

    public void Follow(Vector3 spiritPosition)
    {
        for (var index = _carried.Count - 1; index >= 0; index--)
        {
            var cargo = _carried[index];
            if (!cargo.Drop)
            {
                if (cargo.Visual) UnityEngine.Object.Destroy(cargo.Visual);
                _carried.RemoveAt(index);
                continue;
            }
            var view = cargo.Drop.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner())
            {
                Restore(cargo);
                _carried.RemoveAt(index);
                continue;
            }
            var angle = Time.time * 1.8f + index * Mathf.PI * 2f / Mathf.Max(1, _carried.Count);
            var cargoPosition = spiritPosition + new Vector3(Mathf.Cos(angle) * 0.55f, -0.55f - index * 0.08f,
                Mathf.Sin(angle) * 0.55f);
            cargo.Drop.transform.position = cargoPosition;
            StopBody(cargo.Drop);
            cargo.Visual.transform.position = cargoPosition;
        }
    }

    public bool HasAccepted(Func<ResourceDefinition, bool> accepts)
    {
        foreach (var cargo in _carried) if (cargo.Drop && accepts(cargo.Definition)) return true;
        return false;
    }

    public ResourceDefinition? FirstDefinition
    {
        get
        {
            foreach (var cargo in _carried) if (cargo.Drop) return cargo.Definition;
            return null;
        }
    }

    public int Unload(Vector3 point, float radius, Func<ResourceDefinition, bool> accepts,
        Func<Vector3, bool>? isForbidden = null)
    {
        if (IsProtectedOrForbidden(point, isForbidden)) return 0;
        var delivered = 0;
        for (var index = _carried.Count - 1; index >= 0; index--)
        {
            var cargo = _carried[index];
            if (!cargo.Drop)
            {
                Restore(cargo);
                _carried.RemoveAt(index);
                continue;
            }
            if (!accepts(cargo.Definition)) continue;
            var view = cargo.Drop.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) continue;
            if (!view.IsOwner()) { cargo.Drop.RequestOwn(); continue; }
            Restore(cargo);
            var angle = delivered * 2.399963f;
            cargo.Drop.transform.position = point + new Vector3(Mathf.Cos(angle), 0.45f, Mathf.Sin(angle)) * radius;
            StopBody(cargo.Drop);
            TrackDelivered(cargo.Drop);
            delivered++;
            Delivered?.Invoke(cargo.Definition, cargo.Drop.m_itemData.m_stack);
            _carried.RemoveAt(index);
        }
        _blockedByCapacity = false;
        return delivered;
    }

    public int UnloadToContainer(Container container, Func<ResourceDefinition, bool> accepts)
    {
        if (!CanAccessContainer(container)) return 0;
        var containerView = container.GetComponent<ZNetView>();
        if (!containerView || !containerView.IsValid()) return 0;
        if (!containerView.IsOwner()) return 0;
        var inventory = container.GetInventory();
        var delivered = 0;
        for (var index = _carried.Count - 1; index >= 0; index--)
        {
            var cargo = _carried[index];
            if (!cargo.Drop || !accepts(cargo.Definition)) continue;
            var dropView = cargo.Drop.GetComponent<ZNetView>();
            if (!dropView || !dropView.IsValid()) continue;
            if (!dropView.IsOwner()) { cargo.Drop.RequestOwn(); continue; }
            var item = cargo.Drop.m_itemData;
            if (!inventory.CanAddItem(item, item.m_stack) || !inventory.AddItem(item.Clone())) continue;
            var stack = item.m_stack;
            Restore(cargo);
            DestroyDrop(cargo.Drop);
            _carried.RemoveAt(index);
            delivered++;
            Delivered?.Invoke(cargo.Definition, stack);
        }
        _blockedByCapacity = false;
        return delivered;
    }

    public bool TryWithdraw(Container container, Vector3 dropPosition, int maxStacks, float maxWeight,
        ResourceSelection selection, Func<Vector3, bool>? isForbidden = null)
    {
        LastFailure = string.Empty;
        if (!CanAccessContainer(container) || IsFull(maxStacks, maxWeight) || IsProtectedOrForbidden(dropPosition, isForbidden))
            return false;
        var remaining = GetRemainingQuantity();
        if (remaining <= 0) return false;
        var view = container.GetComponent<ZNetView>();
        if (!view || !view.IsValid()) return false;
        if (!view.IsOwner()) return false;
        var inventory = container.GetInventory();
        foreach (var item in inventory.GetAllItems())
        {
            if (!item.m_dropPrefab) continue;
            var definition = _database.ByDrop(item.m_dropPrefab.name);
            if (definition == null || !definition.CanAutoTransport || !selection.Allows(definition)) continue;
            var clone = item.Clone();
            clone.m_stack = Mathf.Min(clone.m_stack, remaining);
            clone.m_stack = FitStack(clone, clone.m_stack, maxWeight - CurrentWeight);
            if (_carried.Count + _pending.Count >= maxStacks || clone.m_stack <= 0)
            {
                LastFailure = "Cargo capacity reached.";
                return false;
            }
            var drop = ItemDrop.DropItem(clone, clone.m_stack, dropPosition, Quaternion.identity);
            if (!drop) return false;
            if (!inventory.RemoveItem(item, clone.m_stack))
            {
                DestroyDrop(drop);
                return false;
            }
            drop.RequestOwn();
            return TryCollect(dropPosition, maxStacks, maxWeight, selection);
        }
        return false;
    }

    public void CancelPending() => _pending.Clear();

    public void Release()
    {
        foreach (var cargo in _carried)
        {
            if (cargo.Drop)
            {
                var view = cargo.Drop.GetComponent<ZNetView>();
                if (view && view.IsValid() && view.IsOwner()) StopBody(cargo.Drop);
            }
            Restore(cargo);
        }
        _carried.Clear();
        _pending.Clear();
        _blockedByCapacity = false;
    }

    public void Release(Vector3 point)
    {
        var index = 0;
        foreach (var cargo in _carried)
        {
            var view = cargo.Drop ? cargo.Drop.GetComponent<ZNetView>() : null;
            if (cargo.Drop && view && view.IsValid() && view.IsOwner())
            {
                var angle = index++ * 2.399963f;
                cargo.Drop.transform.position = point + new Vector3(Mathf.Cos(angle), 0.4f, Mathf.Sin(angle));
                StopBody(cargo.Drop);
            }
            Restore(cargo);
        }
        _carried.Clear();
        _pending.Clear();
        _blockedByCapacity = false;
    }

    private bool Contains(ItemDrop drop)
    {
        if (_pending.ContainsKey(drop.GetInstanceID())) return true;
        foreach (var cargo in _carried) if (cargo.Drop == drop) return true;
        return false;
    }

    private int GetRemainingQuantity() => Mathf.Max(0, RemainingQuantity?.Invoke() ?? int.MaxValue);

    private int AvailableQuantity(PendingClaim? excluded = null)
    {
        var reserved = 0;
        foreach (var claim in _pending.Values)
            if (claim != excluded) reserved += claim.RequestedStack;
        return Mathf.Max(0, GetRemainingQuantity() - reserved);
    }

    private static int FitStack(ItemDrop drop, int requestedStack, float availableWeight)
    {
        return FitStack(drop.m_itemData, requestedStack, availableWeight);
    }

    private static int FitStack(ItemDrop.ItemData item, int requestedStack, float availableWeight)
    {
        if (requestedStack <= 0 || availableWeight < 0f) return 0;
        var unitWeight = item.m_shared.m_weight;
        if (unitWeight <= 0f) return requestedStack;
        return Mathf.Min(requestedStack, Mathf.FloorToInt(availableWeight / unitWeight));
    }

    private bool IsRecentlyDelivered(ItemDrop drop)
    {
        var id = drop.GetInstanceID();
        if (!_recentDeliveries.TryGetValue(id, out var delivery)) return false;
        if (!delivery.Drop || Vector3.SqrMagnitude(drop.transform.position - delivery.Position) >
            DeliveryExclusionDistance * DeliveryExclusionDistance)
        {
            _recentDeliveries.Remove(id);
            return false;
        }
        return true;
    }

    private bool IsSplitRemainder(ItemDrop drop)
    {
        return _splitRemainders.TryGetValue(drop.GetInstanceID(), out var splitId) && _pending.ContainsKey(splitId);
    }

    private void TrackDelivered(ItemDrop drop)
    {
        _recentDeliveries[drop.GetInstanceID()] = new DeliveredDrop { Drop = drop, Position = drop.transform.position };
    }

    private void PruneDropTracking()
    {
        var deliveredKeys = new List<int>();
        foreach (var entry in _recentDeliveries)
            if (!entry.Value.Drop || Vector3.SqrMagnitude(entry.Value.Drop.transform.position - entry.Value.Position) >
                DeliveryExclusionDistance * DeliveryExclusionDistance) deliveredKeys.Add(entry.Key);
        foreach (var key in deliveredKeys) _recentDeliveries.Remove(key);
        var remainderKeys = new List<int>();
        foreach (var entry in _splitRemainders)
            if (!_pending.ContainsKey(entry.Value)) remainderKeys.Add(entry.Key);
        foreach (var key in remainderKeys) _splitRemainders.Remove(key);
    }

    public static bool CanAccessContainer(Container container)
    {
        var player = Player.m_localPlayer;
        return container && player && !container.IsInUse() &&
               PrivateArea.CheckAccess(container.transform.position, 0f, false, false) &&
               ContainerCheckAccess?.Invoke(container, new object[] { player.GetPlayerID() }) is true;
    }

    private bool IsProtectedOrForbidden(Vector3 position, Func<Vector3, bool>? isForbidden)
    {
        return !PrivateArea.CheckAccess(position, 0f, false, false) ||
               IsForbidden != null && IsForbidden(position) || isForbidden != null && isForbidden(position);
    }

    private static void DestroyDrop(ItemDrop drop)
    {
        if (ZNetScene.instance) ZNetScene.instance.Destroy(drop.gameObject);
        else UnityEngine.Object.Destroy(drop.gameObject);
    }

    private static void StopBody(ItemDrop drop)
    {
        var body = drop.GetComponent<Rigidbody>();
        if (body) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
    }

    private static Cargo CreateCargo(ItemDrop drop, ResourceDefinition definition)
    {
        var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "SpiritHelper_CargoVisual";
        UnityEngine.Object.Destroy(visual.GetComponent<Collider>());
        visual.transform.localScale = Vector3.one * 0.22f;
        var renderer = visual.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        renderer.material.color = CategoryColor(definition.Category);
        var renderers = drop.GetComponentsInChildren<Renderer>(true);
        var states = new bool[renderers.Length];
        for (var index = 0; index < renderers.Length; index++) { states[index] = renderers[index].enabled; renderers[index].enabled = false; }
        var autoPickup = drop.m_autoPickup;
        drop.m_autoPickup = false;
        return new Cargo
        {
            Drop = drop, Definition = definition, Visual = visual, Renderers = renderers, RendererStates = states,
            AutoPickup = autoPickup
        };
    }

    private static void Restore(Cargo cargo)
    {
        for (var index = 0; index < cargo.Renderers.Length; index++)
            if (cargo.Renderers[index]) cargo.Renderers[index].enabled = cargo.RendererStates[index];
        if (cargo.Drop) cargo.Drop.m_autoPickup = cargo.AutoPickup;
        if (cargo.Visual) UnityEngine.Object.Destroy(cargo.Visual);
    }

    private float CarriedWeight()
    {
        var weight = 0f;
        foreach (var cargo in _carried) if (cargo.Drop) weight += Weight(cargo.Drop);
        return weight;
    }

    private static float Weight(ItemDrop drop) => Weight(drop, drop.m_itemData.m_stack);
    private static float Weight(ItemDrop drop, int stack) => drop.m_itemData.m_shared.m_weight * stack;

    private static Color CategoryColor(ResourceCategory category) => category switch
    {
        ResourceCategory.Wood => new Color(0.35f, 0.9f, 0.3f),
        ResourceCategory.Ore => new Color(0.65f, 0.65f, 1f),
        ResourceCategory.Stone => new Color(0.75f, 0.8f, 0.85f),
        ResourceCategory.Plant => new Color(0.25f, 1f, 0.55f),
        ResourceCategory.Food => new Color(1f, 0.4f, 0.55f),
        _ => new Color(0.3f, 0.85f, 1f)
    };
}
