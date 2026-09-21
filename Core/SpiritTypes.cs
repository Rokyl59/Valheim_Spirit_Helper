namespace SpiritHelper.Core;

public enum SpiritState
{
    Idle, FollowPlayer, SearchTarget, MoveToTarget, Work, WaitForDrop,
    CollectDrops, CarryDrops, Unload, ReturnToPlayer, Rest, Scout, Guard,
    Recharge, Celebrate
}

public enum SpiritJob { Follow, Woodcutting, Mining, Gathering, Transport, Scout, Guard, Rest, Stopped }
public enum BalanceMode { Vanilla, Balanced, Free }
public enum WorkZoneMode { Circle, FollowPlayer, UnloadPointCentered, CustomArea }
public enum UnloadPointType { Universal, Wood, Ore, Stone, Plants, Food, Custom }
public enum SpiritLightMode { Off, Spirit, CameraForward }
public enum Profession { Woodcutting, Mining, Gathering, Logistics, Exploration }
