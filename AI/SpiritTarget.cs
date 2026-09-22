using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.AI;

public sealed class SpiritTarget
{
    public Component Component { get; }
    public Collider? HitCollider { get; }
    public ResourceDefinition Definition { get; }
    public Vector3 HitPoint { get; }
    public int ComponentKey { get; }
    private readonly bool _requiresCollider;
    public Vector3 Position => HitCollider ? HitCollider.bounds.center : Component ? Component.transform.position : HitPoint;
    public int Key => ComponentKey;

    public bool IsValid
    {
        get
        {
            if (!Component || _requiresCollider && !HitCollider || HitCollider && (!HitCollider.enabled || !HitCollider.gameObject.activeInHierarchy)) return false;
            return Component is not Pickable pickable || pickable.CanBePicked();
        }
    }

    public SpiritTarget(Component component, Collider? hitCollider, Vector3 hitPoint, ResourceDefinition definition)
    {
        Component = component;
        ComponentKey = component.GetInstanceID();
        HitCollider = hitCollider;
        _requiresCollider = hitCollider;
        HitPoint = hitPoint;
        Definition = definition;
    }
}
