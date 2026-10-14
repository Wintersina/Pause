// Shared enums of the boss shots (moved out of BossCatalog so the pure pattern core has no other dependency).
public enum BossShotStyle { Bolt, Shard }

// What a projectile does at a side rail.
public enum BossRailMode
{
    Absorb,  // splashes against the rail (a spark) and is gone
    Bounce,  // ricochets `bounces` times (a spark each), then splashes
    Pass,    // flies on past the rail and off the screen
}
