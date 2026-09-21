using SpiritHelper.Resources;
using UnityEngine;

namespace SpiritHelper.AI;

public sealed class SpiritTarget
{
    public Component Component { get; }
    public Collider? HitCollider { get; }
    public ResourceDefinition Definition { get; }
    public Vector3 HitPoint { get; }
    public Vector3 Position => HitCollider ? HitCollider.bounds.center : Component ? Component.transform.position : HitPoint;
    public int Key => HitCollider ? HitCollider.GetInstanceID() : Component ? Component.GetInstanceID() : 0;
    public bool IsValid => Component && (!HitCollider || HitCollider.enabled && HitCollider.gameObject.activeInHierarchy);

    public SpiritTarget(Component component, Collider? hitCollider, Vector3 hitPoint, ResourceDefinition definition)
    {
        Component = component;
        HitCollider = hitCollider;
        HitPoint = hitPoint;
        Definition = definition;
    }
}
