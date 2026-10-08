using UnityEngine;

// DEATH COMBO -- the domino, in play and in the death crash. Every number is
// here (one place to tune); docs/enemy-behaviours.md ("Death combo") says
// the same in words.
//
// IN PLAY. A hazard the pilot destroys (collisionDetection.
// AwardDestroyedTarget: weapons, the ultimate, secret powers, a shielded
// ram, a blink; or an elite brought down by a weapon, a blink or a shielded
// ram, EliteShip.Die) sets off a COMBO TriggerChance of the time: a hard-edged
// shockwave ring and flash at the wreck (magenta / amber, never the pilot's
// red). Every other hazard on screen within BlastRadius of it (plus half its
// own size) is then rolled for on its own: ChainChance of them are hit,
// HopDelay later (a domino wave, not a mass delete). A hit kills a rock,
// enemy or rail mine like a weapon kill -- its own blast, sound, score with
// the kill chain, star dust, codex, achievements; a mine still bursts on its
// own friendly-fire rules -- and costs an elite ONE heart (EliteDamage.Combo,
// which respects the elite's grace; elites are never one-shot). Each combo
// KILL is a combo death of its own: its own burst, its own ChainChance roll
// per hazard in range, one hop deeper. An elite that only loses a heart does
// not chain on. The chain stops at MaxDepth hops, at MaxPending queued hits,
// and at hazards farther apart than the radius.
//   * Chain victims never roll TriggerChance again (Resolving): they only
//     spread through the chain roll.
//   * Never touched: the player, the boss (body, shots, beams), elite shots,
//     pickups, anything already destroyed this frame.
//   * Not in the tutorial; not during the death crash (it owns the board);
//     frozen while the world is paused (gameplay time, TargetExplosion.Delta,
//     stepped by HazardRuntime).
//   * Pays like any kill: one AwardDestroyedTarget per victim (no extra
//     multiplier, nothing paid twice); unpaid at an open portal past its
//     grace like every other kill (RunScore).
//
// THE DEATH CRASH (DeathCrashDomino), the same rule: the ship's wreckage
// breaks an enemy it touches DeathHitChance of the time (rolled once per
// enemy, on the first touch; a miss is a glancing spark and the enemy is
// spared by the wreckage), and each chain kill's own pieces are live (can
// break more) DeathChainChance of the time, otherwise harmless debris. Before,
// every touch killed and every kill threw more killing pieces, so a busy
// board was nearly always wiped (DeathComboTest / the old probe: 83% of
// deaths with 6 hazards on screen, 93% with 10). MEGA DOMINO keeps its own
// rare rule (DeathCrash.MegaChance).
public static class DeathCombo
{
    // ---- tuning ----
    public const float TriggerChance = .40f;    // a pilot kill sets off a combo
    public const float ChainChance = .40f;      // per hazard in range of a combo death
    public const float DeathHitChance = .40f;   // death crash: the wreckage breaks an enemy it touches
    public const float DeathChainChance = .40f; // death crash: a chain kill's pieces are live
    public const float BlastRadius = 1.4f;      // world units, plus half the target's radius
    public const int MaxDepth = 6;              // hops from the pilot's kill
    public const float HopDelay = .1f;          // seconds between links (gameplay time)
    public const int MaxPending = 32;           // queued bursts + hits at once
    // FX
    public const float RingSize = BlastRadius * 1.6f;
    public static readonly Color RingColour = AkiraPalette.Magenta;
    public static readonly Color FlashColour = AkiraPalette.Amber;

    // ---- switches (tests, previews) ----
    public static bool Enabled = true;
    // Edit mode only runs combos when a test asks (the suites kill things
    // by hand and must not see random chains).
    public static bool ForceInEditor;
    // >= 0: replaces the configured chance (tests); -1: roll.
    public static float ForceTrigger = -1f, ForceChain = -1f, ForceDeathHit = -1f, ForceDeathChain = -1f;

    // ---- read-outs (tests) ----
    public static int TriggerRolls, Triggers, Bursts, ChainRolls, ChainWins, ChainHits, Kills, EliteHits, Deepest, Dropped;
    public static int DeathHitRolls, DeathHits, DeathChainRolls, DeathChainLive;
    public static bool Resolving { get; private set; }

    public static void ResetCounters()
    {
        TriggerRolls = Triggers = Bursts = ChainRolls = ChainWins = ChainHits = Kills = EliteHits = Deepest = Dropped = 0;
        DeathHitRolls = DeathHits = DeathChainRolls = DeathChainLive = 0;
    }

    // ---- the game's own RNG for the rolls (seedable) ----
    static uint rng = 0x2545F491u;
    public static void Seed(uint seed) { rng = seed == 0 ? 0x2545F491u : seed; }
    static float Next01()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFF) / 16777216f;
    }
    static bool Roll(float chance, float force) => Next01() < (force >= 0f ? force : chance);

    // ---- the queue (fixed arrays: no allocation) ----
    struct Entry
    {
        public bool burst;          // a burst (a combo death) or a hit on `target`
        public Vector3 at;
        public float delay;
        public int depth;
        public GameObject target;   // hit: the victim; burst: the wreck (skipped)
    }
    static readonly Entry[] queue = new Entry[MaxPending];
    const float DueSlack = 1e-4f;   // float dust: six 1/60 s steps make one .1 s hop
    static int count;
    public static int Pending => count;

    public static void ClearPending()
    {
        for (int i = 0; i < count; i++) queue[i].target = null;
        count = 0;
    }

    static bool Queued(GameObject go)
    {
        for (int i = 0; i < count; i++) if (!queue[i].burst && queue[i].target == go) return true;
        return false;
    }

    static bool Enqueue(bool burst, Vector3 at, float delay, int depth, GameObject target)
    {
        if (count >= MaxPending) { Dropped++; return false; }
        queue[count++] = new Entry { burst = burst, at = at, delay = delay, depth = depth, target = target };
        if (Application.isPlaying) HazardRuntime.Ensure();
        return true;
    }

    // Combos may run here and now.
    public static bool Allowed
    {
        get
        {
            if (!Enabled || buttonClicks.playerDied || DeathCrash.Running) return false;
            if (!Application.isPlaying && !ForceInEditor) return false;
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != score.TutorialScene;
        }
    }

    // ---- entry points ----

    // The pilot destroyed `target` (it is still there; it goes this frame).
    // TriggerChance of the time it sets off a combo at its position. True
    // when it did.
    public static bool OnPlayerKill(GameObject target)
    {
        if (target == null || Resolving || !Allowed || FriendlyFire.HostileKillInProgress) return false;
        if (BossPart(target)) return false;
        TriggerRolls++;
        if (!Roll(TriggerChance, ForceTrigger)) return false;
        if (!Enqueue(true, target.transform.position, 0f, 0, target)) return false;
        Triggers++;
        return true;
    }

    // The death crash: does the ship's wreckage break this enemy (rolled
    // once per enemy, on its first touch)?
    public static bool RollDeathHit()
    {
        DeathHitRolls++;
        bool hit = Roll(DeathHitChance, ForceDeathHit);
        if (hit) DeathHits++;
        return hit;
    }

    // The death crash: are the pieces of this chain kill live?
    public static bool RollDeathChain()
    {
        DeathChainRolls++;
        bool live = Roll(DeathChainChance, ForceDeathChain);
        if (live) DeathChainLive++;
        return live;
    }

    // ---- per step (HazardRuntime, gameplay time) ----

    public static void Step(float dt)
    {
        if (count == 0) return;
        if (buttonClicks.playerDied || DeathCrash.Running || !Enabled) { ClearPending(); return; }
        if (dt <= 0f) return;   // paused: frozen
        for (int i = 0; i < count; i++) queue[i].delay -= dt;
        // Due entries resolve in queue order; what they add waits its hop.
        int k = 0;
        while (k < count)
        {
            if (queue[k].delay > DueSlack) { k++; continue; }
            var e = queue[k];
            for (int j = k + 1; j < count; j++) queue[j - 1] = queue[j];   // keep the order
            count--;
            queue[count].target = null;
            if (e.burst) Burst(e.at, e.depth, e.target);
            else Strike(e.target, e.at, e.depth);
        }
    }

    // A combo death at `at`: the ring, and each hazard in range rolls.
    static void Burst(Vector3 at, int depth, GameObject wreck)
    {
        Bursts++;
        BurstFx(at, depth);
        if (depth >= MaxDepth) return;
        Rect view = ShipTargets.View(.2f);
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            Vector3 p = t.transform.position;
            float r = BlastRadius + t.Radius * .5f;
            float dx = p.x - at.x, dy = p.y - at.y;
            if (dx * dx + dy * dy > r * r || !view.Contains(p)) continue;
            if (!CanChainTo(t, wreck)) continue;
            ChainRolls++;
            if (!Roll(ChainChance, ForceChain)) continue;
            ChainWins++;
            Enqueue(false, at, HopDelay, depth + 1, t.gameObject);
        }
    }

    // The boss (body, shots, beams) and elite shot hitboxes: never part of a
    // combo (no strings: GameObject.name allocates).
    static bool BossPart(GameObject go) =>
        FriendlyFire.Immune(go) || RamKill.NotAHazardBody(go) ||
        go.TryGetComponent(out BossProjectile _) || go.TryGetComponent(out BossBeam _) ||
        go.GetComponentInParent<BossActor>() != null;

    static bool CanChainTo(ClearTarget t, GameObject wreck)
    {
        if (!FriendlyFire.CanHit(t)) return false;
        var go = t.gameObject;
        if (go == wreck || ShipAttackHits.AlreadyHit(go) || BossPart(go)) return false;
        if (go.TryGetComponent(out EliteShip elite) && !elite.InPlay) return false;
        return !Queued(go);
    }

    // The link lands: a kill, or a heart off an elite.
    static void Strike(GameObject go, Vector3 from, int depth)
    {
        if (go == null || !go.activeInHierarchy || ShipAttackHits.AlreadyHit(go)) return;
        Vector3 at = go.transform.position;
        LinkFx(at);
        if (go.TryGetComponent(out EliteShip elite))
        {
            if (!elite.InPlay) return;
            if (!elite.TakeHit(EliteDamage.Combo, from)) return;   // in its grace
            ChainHits++;
            EliteHits++;
            if (depth > Deepest) Deepest = depth;
            if (elite.State == EliteState.Dead) Enqueue(true, at, 0f, depth, null);
            return;
        }
        ChainHits++;
        Kills++;
        if (depth > Deepest) Deepest = depth;
        Resolving = true;
        try
        {
            int ship = ShipId.Equipped();
            TargetExplosion.Spawn(go, ship);
            EnemyDeathAudio.Play(go);
            collisionDetection.AwardDestroyedTarget(go);
        }
        finally { Resolving = false; }
        ClearTarget.Release(go);
        if (Application.isPlaying) Object.Destroy(go);
        else Object.DestroyImmediate(go);
        Enqueue(true, at, 0f, depth, null);
    }

    // ---- FX: a pixel shockwave ring and a flash star (pooled flipbooks) ----

    static readonly string[] Words = { "COMBO!", "CHAIN x2", "CHAIN x3", "CHAIN x4", "CHAIN x5", "CHAIN x6", "CHAIN x7" };
    public static string WordFor(int depth) => Words[Mathf.Clamp(depth, 0, Words.Length - 1)];

    static void BurstFx(Vector3 at, int depth)
    {
        if (!Application.isPlaying) return;
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Ring, at, RingSize, ShipId.None, TargetExplosion.Kind.Metal, RingColour, 1.3f, 67);
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Flash, at, .9f, ShipId.None, TargetExplosion.Kind.Metal, FlashColour, 1f, 68);
        var hud = ScoreHud.Current;
        if (hud != null) hud.ShowWord(WordFor(depth), at, depth == 0 ? FlashColour : RingColour, 28 + Mathf.Min(depth, 5) * 2);
    }

    static void LinkFx(Vector3 at)
    {
        if (!Application.isPlaying) return;
        WeaponFx.Flipbook().Play(FlipbookFx.Mode.Ring, at, .7f, ShipId.None, TargetExplosion.Kind.Metal, FlashColour, 1f, 67);
    }
}
