using System;
using System.Collections.Generic;
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
    }

    private sealed class Cargo
    {
        public ItemDrop Drop = null!;
        public ResourceDefinition Definition = null!;
        public GameObject Visual = null!;
        public Renderer[] Renderers = Array.Empty<Renderer>();
        public bool[] RendererStates = Array.Empty<bool>();
    }

    private const float OwnershipTimeout = 8f;
    private readonly ResourceDatabase _database;
    private readonly List<Cargo> _carried = new List<Cargo>();
    private readonly Dictionary<int, PendingClaim> _pending = new Dictionary<int, PendingClaim>();
    private readonly Collider[] _hits = new Collider[512];
    private bool _blockedByCapacity;

    public CarrySystem(ResourceDatabase database) => _database = database;
    public int Count => _carried.Count;
    public int PendingCount => _pending.Count;
    public string Summary => _carried.Count == 0 ? (_pending.Count == 0 ? "—" : $"получение: {_pending.Count}") : $"{_carried.Count} стоп., {CurrentWeight:0.#} кг";
    public float CurrentWeight
    {
        get
        {
            var weight = 0f;
            foreach (var cargo in _carried) if (cargo.Drop) weight += Weight(cargo.Drop);
            foreach (var claim in _pending.Values) if (claim.Drop) weight += Weight(claim.Drop);
            return weight;
        }
    }

    public bool IsFull(int maxStacks, float maxWeight) =>
        _carried.Count + _pending.Count >= maxStacks || CurrentWeight >= maxWeight || _blockedByCapacity;

    public bool TryCollect(Vector3 position, int maxStacks, float maxWeight, ResourceSelection selection,
        Func<ResourceDefinition, bool>? jobFilter = null)
    {
        TickClaims(maxStacks, maxWeight);
        _blockedByCapacity = false;
        var count = Physics.OverlapSphereNonAlloc(position, 10f, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        var occupied = _carried.Count + _pending.Count;
        var weight = CurrentWeight;
        for (var index = 0; index < count; index++)
        {
            if (occupied >= maxStacks) { _blockedByCapacity = true; break; }
            var drop = _hits[index] ? _hits[index].GetComponentInParent<ItemDrop>() : null;
            if (!drop || Contains(drop)) continue;
            var definition = _database.ByDrop(drop.gameObject.name);
            if (definition == null || !definition.CanAutoTransport || !selection.Allows(definition) || jobFilter != null && !jobFilter(definition)) continue;
            var dropWeight = Weight(drop);
            if (weight + dropWeight > maxWeight) { _blockedByCapacity = true; continue; }
            var view = drop.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) continue;
            _pending[drop.GetInstanceID()] = new PendingClaim { Drop = drop, Definition = definition, StartedAt = Time.time };
            drop.RequestOwn();
            occupied++;
            weight += dropWeight;
        }
        TickClaims(maxStacks, maxWeight);
        return _carried.Count > 0 || _pending.Count > 0;
    }

    public Vector3? FindNearest(Vector3 centre, float radius, ResourceSelection selection)
    {
        var count = Physics.OverlapSphereNonAlloc(centre, radius, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        ItemDrop? nearest = null;
        var nearestDistance = float.MaxValue;
        for (var index = 0; index < count; index++)
        {
            var drop = _hits[index] ? _hits[index].GetComponentInParent<ItemDrop>() : null;
            if (!drop || Contains(drop)) continue;
            var definition = _database.ByDrop(drop.gameObject.name);
            if (definition == null || !definition.CanAutoTransport || !selection.Allows(definition)) continue;
            var distance = Vector3.SqrMagnitude(drop.transform.position - centre);
            if (distance >= nearestDistance) continue;
            nearest = drop;
            nearestDistance = distance;
        }
        return nearest ? nearest.transform.position : null;
    }

    public void TickClaims(int maxStacks, float maxWeight)
    {
        if (_pending.Count == 0) return;
        var keys = new List<int>(_pending.Keys);
        foreach (var key in keys)
        {
            var claim = _pending[key];
            if (!claim.Drop || Time.time - claim.StartedAt > OwnershipTimeout)
            {
                _pending.Remove(key);
                continue;
            }
            var view = claim.Drop.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) { _pending.Remove(key); continue; }
            if (!view.IsOwner()) { claim.Drop.RequestOwn(); continue; }
            if (_carried.Count >= maxStacks || CarriedWeight() + Weight(claim.Drop) > maxWeight) { _pending.Remove(key); continue; }
            _pending.Remove(key);
            _carried.Add(CreateCargo(claim.Drop, claim.Definition));
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
            var angle = Time.time * 1.8f + index * Mathf.PI * 2f / Mathf.Max(1, _carried.Count);
            cargo.Visual.transform.position = spiritPosition + new Vector3(Mathf.Cos(angle) * 0.55f, -0.55f - index * 0.08f, Mathf.Sin(angle) * 0.55f);
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

    public int Unload(Vector3 point, float radius, Func<ResourceDefinition, bool> accepts)
    {
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
            var body = cargo.Drop.GetComponent<Rigidbody>();
            if (body) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            delivered++;
            _carried.RemoveAt(index);
        }
        _blockedByCapacity = false;
        return delivered;
    }

    public int UnloadToContainer(Container container, Func<ResourceDefinition, bool> accepts)
    {
        if (!container || !PrivateArea.CheckAccess(container.transform.position, 0f, false, false)) return 0;
        var containerView = container.GetComponent<ZNetView>();
        if (!containerView || !containerView.IsValid()) return 0;
        if (!containerView.IsOwner()) { containerView.ClaimOwnership(); return 0; }
        var inventory = container.GetInventory();
        var delivered = 0;
        for (var index = _carried.Count - 1; index >= 0; index--)
        {
            var cargo = _carried[index];
            if (!cargo.Drop || !accepts(cargo.Definition)) continue;
            var dropView = cargo.Drop.GetComponent<ZNetView>();
            if (!dropView || !dropView.IsValid()) continue;
            if (!dropView.IsOwner()) { cargo.Drop.RequestOwn(); continue; }
            if (!inventory.AddItem(cargo.Drop.m_itemData.Clone())) continue;
            if (cargo.Visual) UnityEngine.Object.Destroy(cargo.Visual);
            if (ZNetScene.instance) ZNetScene.instance.Destroy(cargo.Drop.gameObject);
            else UnityEngine.Object.Destroy(cargo.Drop.gameObject);
            _carried.RemoveAt(index);
            delivered++;
        }
        _blockedByCapacity = false;
        return delivered;
    }

    public bool TryWithdraw(Container container, Vector3 dropPosition, int maxStacks, float maxWeight,
        ResourceSelection selection)
    {
        if (!container || !PrivateArea.CheckAccess(container.transform.position, 0f, false, false) || IsFull(maxStacks, maxWeight))
            return false;
        var view = container.GetComponent<ZNetView>();
        if (!view || !view.IsValid()) return false;
        if (!view.IsOwner()) { view.ClaimOwnership(); return false; }
        var inventory = container.GetInventory();
        foreach (var item in inventory.GetAllItems())
        {
            if (!item.m_dropPrefab) continue;
            var definition = _database.ByDrop(item.m_dropPrefab.name);
            if (definition == null || !definition.CanAutoTransport || !selection.Allows(definition)) continue;
            var clone = item.Clone();
            if (_carried.Count + _pending.Count >= maxStacks || CurrentWeight + clone.m_shared.m_weight * clone.m_stack > maxWeight)
                return false;
            if (!inventory.RemoveItem(item)) return false;
            var drop = ItemDrop.DropItem(clone, clone.m_stack, dropPosition, Quaternion.identity);
            if (!drop) return false;
            return TryCollect(dropPosition, maxStacks, maxWeight, selection);
        }
        return false;
    }

    public void CancelPending() => _pending.Clear();

    public void Release()
    {
        foreach (var cargo in _carried) Restore(cargo);
        _carried.Clear();
        _pending.Clear();
        _blockedByCapacity = false;
    }

    public void Release(Vector3 point)
    {
        var index = 0;
        foreach (var cargo in _carried)
        {
            if (cargo.Drop)
            {
                var angle = index++ * 2.399963f;
                cargo.Drop.transform.position = point + new Vector3(Mathf.Cos(angle), 0.4f, Mathf.Sin(angle));
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
        return new Cargo { Drop = drop, Definition = definition, Visual = visual, Renderers = renderers, RendererStates = states };
    }

    private static void Restore(Cargo cargo)
    {
        for (var index = 0; index < cargo.Renderers.Length; index++)
            if (cargo.Renderers[index]) cargo.Renderers[index].enabled = cargo.RendererStates[index];
        if (cargo.Visual) UnityEngine.Object.Destroy(cargo.Visual);
    }

    private float CarriedWeight()
    {
        var weight = 0f;
        foreach (var cargo in _carried) if (cargo.Drop) weight += Weight(cargo.Drop);
        return weight;
    }

    private static float Weight(ItemDrop drop) => drop.m_itemData.m_shared.m_weight * drop.m_itemData.m_stack;

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
