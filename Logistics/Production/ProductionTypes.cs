using UnityEngine;

namespace SpiritHelper.Logistics.Production;

public enum ProductionTaskState
{
    Idle,
    MovingToSource,
    ClaimingSource,
    MovingToMachine,
    ServicingMachine,
    WaitingForWork
}

public readonly struct ProductionTickResult
{
    public ProductionTickResult(ProductionTaskState state, Vector3? destination, string status, bool completed)
    {
        State = state;
        Destination = destination;
        Status = status;
        Completed = completed;
    }

    public ProductionTaskState State { get; }
    public Vector3? Destination { get; }
    public string Status { get; }
    public bool Completed { get; }
}
