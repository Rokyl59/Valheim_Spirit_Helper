namespace SpiritHelper.Core;

public enum SpiritState
{
    Idle, FollowPlayer, SearchTarget, MoveToTarget, Work, WaitForDrop,
    CollectDrops, CarryDrops, Unload, ReturnToPlayer, Rest, Scout, Guard,
    Recharge, Celebrate, Assist, Guide, Expedition, Observe, Production
}

public enum SpiritJob
{
    Follow, Woodcutting, Mining, Gathering, Transport, Scout, Guard, Rest, Stopped,
    Observe, Assist, Cleanup, Courier, Caravan, Bring, Sort, Patrol, Guide,
    ExploreDungeon, Expedition, Production
}
public enum BalanceMode { Vanilla, Balanced, Free }
public enum WorkZoneMode { Circle, FollowPlayer, UnloadPointCentered, CustomArea }
public enum UnloadPointType { Universal, Wood, Ore, Stone, Plants, Food, Custom }
public enum SpiritLightMode { Off, Spirit, CameraForward }
public enum Profession { Woodcutting, Mining, Gathering, Logistics, Exploration }
public enum SpiritPersonality { Curious, Industrious, Cautious, Calm }
public enum SpiritRuleTrigger { CargoEightyPercent, PlayerInventoryNinetyPercent, EnemyNearby, EnergyBelowTwenty }
public enum SpiritRuleAction { DeliverAndResume, CleanupNearby, ReturnToPlayer, RechargeAndResume }
public enum NamedZoneType { Home, Forest, Mine, Farm, Port, Custom }
