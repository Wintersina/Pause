using UnityEngine;

// A shielded (or Cloaked) ship flying into a boss's body (collisionDetection).
//
// The player's zone reaches the HUD, where the boss rests, so a ship with a
// blue-atom shield can fly up and ram it. Before this the body hitbox was
// simply destroyed (it came back after BossConfig.BodyRespawnSeconds), paid
// as a kill and did no damage. Now:
//   * a ram that lands (RamCooldown seconds since the last one) is one hit of
//     RamWeight on the boss -- the same path as the ship's weapons
//     (BossTarget.TakeShipAttack: blast, sound, heart / hit point bookkeeping);
//     3 hit points and 5 hearts, so RamWeight .3 is half a heart (.6) a ram;
//   * it costs the shield RamShieldCost seconds of its timer, so one blue atom
//     (5.8 s) buys about four rams at most, never a melted boss;
//   * the body hitbox stays (nothing is destroyed, nothing paid as a kill);
//   * inside the cooldown the shield just shrugs the contact off.
// An unshielded touch is unchanged: a heart, and the hitbox is knocked out
// for BodyRespawnSeconds.
public static class BossRam
{
    // ---- tunables ----
    // Share of one full ultimate hit a ram deals.
    public static float RamWeight = .3f;
    // Seconds of shield (blue atom timer / Cloak) a landed ram uses up.
    public static float RamShieldCost = 1.2f;
    // Minimum gap between two landed rams (world seconds).
    public static float RamCooldown = 1f;

    public static int Rams;           // tests: landed rams
    public static float TestClock;    // tests: the clock outside play mode
    static float lastRam = -1000f;

    static float Now => Application.isPlaying ? Time.time : TestClock;

    public static void Reset() { lastRam = -1000f; Rams = 0; }

    // True when `body` is a boss's body hitbox: the contact is handled here
    // (damage or a shrugged-off touch) and the caller leaves it alone.
    public static bool Ram(GameObject body, int ship, Vector3 at)
    {
        if (body == null || !body.TryGetComponent(out BossTarget target)) return false;
        if (Now - lastRam < RamCooldown) return true;
        var enc = BossEncounter.Instance;
        if (enc == null || enc.State != BossEncounter.Phase.Fight) return true;
        lastRam = Now;
        Rams++;
        target.TakeShipAttack(ship, RamWeight, at);
        SpendShield(RamShieldCost);
        return true;
    }

    static void SpendShield(float seconds)
    {
        if (collisionDetection.atomCheck) collisionDetection.invTimer -= seconds;
        else if (collisionDetection.Cloaked) collisionDetection.cloakTimer = Mathf.Max(0f, collisionDetection.cloakTimer - seconds);
    }
}
