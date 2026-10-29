using System.Collections.Generic;
using UnityEngine;

// SIGNATURE UPGRADE EFFECTS, group 2: Jade Phantom, Gold Warden, Paranoid, Saboteur.
//
// On top of the plain tracks in ShipWeaponUpgrades (which are unchanged: same steps, numbers, 4-step
// structure) every one of these ships unlocks a visible effect with a weapon level. The unlock rows are the
// tables below, indexed by weapon level 0..4 (ShipLoadout.weaponLevel); the dock and the codex show them because the
// label of the step that unlocks them is extended (ShipSignatures2.Tag in the tracks).
//
//   Jade Phantom   FROST NOVA        level 2 CHILL: a shatter also chills what is just outside its splash (it stops
//                                    for 1.5 s under a pale ice overlay, then thaws, no damage);
//                                    level 4 DEEP CHILL: a wider chill that holds for 3 s.
//   Gold Warden    WARDEN SENTRIES   level 2 PIERCE: a sentry bolt carries on to one more enemy near the one it hit;
//                                    level 4 DRIFT: the sentries also drift along with the ship, and bolts carry on twice.
//   Paranoid       ARCANE EYE        level 2 BIG BLAST: the detonation radius 1.2 -> 1.5; level 4 HUGE BLAST: 1.8
//                                    (the burst drawing is sized to it).
//   Saboteur       PROXIMITY MINES   level 2 BIG BLAST: the mine blast radius 1.1 -> 1.35;
//                                    level 4 CHAIN BLAST: a blowing mine sets off the armed mines near it, a beat apart.
//
// Balance: nothing here adds a hit. A chill is a stop, not a hit; a pierce / chain / bigger blast only ever
// reaches ordinary enemies (a boss or an elite is never pierced, chilled or splashed by a bigger radius than the
// level 0 one, and a mine or mark still counts its own weight once), so a firing is still worth about one hit
// on a boss or an elite, paced by their own hit windows. Every hit still goes through ShipAttackHits.
public static class ShipSignatures2
{
    const int Levels = 5;   // levels 0..ShipWeaponUpgrades.MaxLevel

    static int At(int level) { return Mathf.Clamp(level, 0, Levels - 1); }

    // Appends the effect to the label of the step that unlocks it ("-15% COOLDOWN + CHILL"); the step's numbers stay.
    public static WeaponStep Tag(WeaponStep s, string effect)
    {
        s.label += " + " + effect;
        return s;
    }

    // ---------------------------------------------------------- Jade Phantom

    // Extra reach of the chill past the shatter splash (world units) and how long a chilled enemy stays stopped, by level.
    public static readonly float[] ChillReach = { 0f, 0f, 1f, 1f, 1.8f };
    public static readonly float[] ChillSeconds = { 0f, 0f, 1.5f, 1.5f, 3f };
    public static float ChillReachAt(int level) { return ChillReach[At(level)]; }
    public static float ChillSecondsAt(int level) { return ChillSeconds[At(level)]; }
    public static bool ChillsAt(int level) { return ChillReachAt(level) > 0f; }
    public const float ChillOverlayAlpha = .5f;   // thin and low-glow

    // ---------------------------------------------------------- Gold Warden

    // How many more enemies a sentry bolt carries on to, by level, and how far it looks for the next one.
    public static readonly int[] SentryPierce = { 0, 0, 1, 1, 2 };
    public const float SentryPierceReach = 3f;
    public static int SentryPierceAt(int level) { return SentryPierce[At(level)]; }
    // From this level the sentries drift along with the ship, at most SentryDriftSpeed units a second.
    public const int SentryDriftLevel = 4;
    public const float SentryDriftSpeed = 2.4f;
    public static bool SentriesDriftAt(int level) { return level >= SentryDriftLevel; }

    // ---------------------------------------------------------- Paranoid

    // The detonation radius of an Arcane Eye mark, by level (the level 0 value is ShipAttackRunner.EyeBlast).
    public static readonly float[] EyeBlast =
        { ShipAttackRunner.EyeBlast, ShipAttackRunner.EyeBlast, 1.5f, 1.5f, 1.8f };
    public static float EyeBlastAt(int level) { return EyeBlast[At(level)]; }

    // ---------------------------------------------------------- Saboteur

    // The proximity mine blast radius, by level (the level 0 value is ShipAttackRunner.MineBlast).
    public static readonly float[] MineBlast =
        { ShipAttackRunner.MineBlast, ShipAttackRunner.MineBlast, 1.35f, 1.35f, 1.35f };
    public static float MineBlastAt(int level) { return MineBlast[At(level)]; }
    // From this level a blowing mine sets off every armed mine within MineChainReach, MineChainDelay later.
    public const int MineChainLevel = 4;
    public const float MineChainReach = 2.4f, MineChainDelay = .12f;
    public static bool MinesChainAt(int level) { return level >= MineChainLevel; }
}

public partial class ShipAttackRunner
{
    // ============================================ FROST NOVA: CHILL (Jade Phantom)
    //
    // A shatter (Shatter) chills the ordinary enemies just outside its splash: they stop (FrozenState, with a
    // failsafe as long as the chill) under a pale ice overlay and thaw by themselves. No damage, so a boss or
    // an elite (never freezable) and the nova's own frozen enemies are left alone.

    public const int MaxChilled = 24;

    class ChillEntry
    {
        public bool on;
        public GameObject go;
        public ClearTarget target;
        public FrozenState state;
        public AttackSprite overlay;
        public float left;
    }

    ChillEntry[] chilled;
    int chilledCount;
    public int ChilledNow { get { return chilledCount; } }
    public int ChillCount { get; private set; }   // enemies chilled so far (tests)

    bool FrozenByNova(GameObject go)
    {
        if (frozen == null) return false;
        for (int i = 0; i < frozen.Length; i++) if (frozen[i].on && frozen[i].go == go) return true;
        return false;
    }

    // Called from Shatter after its splash: chills what is within the splash radius + the level's reach.
    void ChillAround(Vector3 at, float splash, ShipLoadout lo)
    {
        float reach = ShipSignatures2.ChillReachAt(lo.weaponLevel);
        if (reach <= 0f) return;
        if (chilled == null)
        {
            chilled = new ChillEntry[MaxChilled];
            for (int i = 0; i < chilled.Length; i++) chilled[i] = new ChillEntry();
        }
        float seconds = ShipSignatures2.ChillSecondsAt(lo.weaponLevel);
        float outer = splash + reach;
        // a pale puff the size of the chill, so its reach reads
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, at, Vector3.one * outer * 2f, 0f,
            ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 66).Strip("attack_ice", 8, false)
            .Alpha(ShipSignatures2.ChillOverlayAlpha);
        ShipTargets.Collect(targets);
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled || !ShipTargets.IsThreat(t) || t.IsShotHitbox) continue;
            if (ShipAttackHits.AlreadyHit(t.gameObject) || IsSpecial(t.gameObject)) continue;
            var existing = t.GetComponent<FrozenState>();
            if (existing != null && existing.IsFrozen) continue;   // already held (by the nova, or by a chill)
            Vector2 d = t.transform.position - at;
            float r = outer + t.Radius * .5f;
            if (d.sqrMagnitude > r * r) continue;
            Chill(t, seconds);
        }
    }

    bool Chill(ClearTarget t, float seconds)
    {
        ChillEntry e = null;
        for (int i = 0; i < chilled.Length; i++) if (!chilled[i].on) { e = chilled[i]; break; }
        if (e == null) return false;
        var st = FrozenState.Freeze(t.gameObject, seconds);
        if (st == null) return false;
        e.on = true;
        e.go = t.gameObject;
        e.target = t;
        e.state = st;
        e.left = seconds;
        chilledCount++;
        ChillCount++;
        e.overlay = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, t.transform.position,
            Vector3.one * Mathf.Max(.7f, t.Radius * 2.6f), 0f, ShipFxArt.LoopTicks, true, 0f, t.transform, Vector3.zero, 67)
            .Strip("attack_ice", 0, true).Alpha(ShipSignatures2.ChillOverlayAlpha);
        return true;
    }

    void EndChill(ChillEntry e, bool thaw)
    {
        if (!e.on) return;
        e.on = false;
        if (e.overlay != null && e.overlay.Active) e.overlay.Stop();
        e.overlay = null;
        // the nova may have frozen it since: that freeze is the nova's to end
        if (thaw && e.state != null && e.go != null && !FrozenByNova(e.go)) e.state.Thaw();
        e.state = null;
        e.go = null;
        e.target = null;
        chilledCount = Mathf.Max(0, chilledCount - 1);
    }

    void StepChill(float dt)
    {
        for (int i = 0; i < chilled.Length; i++)
        {
            var e = chilled[i];
            if (!e.on) continue;
            if (e.go == null || e.target == null || !e.target.isActiveAndEnabled || !e.go.activeInHierarchy)
            {
                EndChill(e, false);   // shot down or gone: nothing to thaw
                continue;
            }
            e.left -= dt;
            if (e.left <= 0f) EndChill(e, true);
        }
    }

    void StopChill()
    {
        if (chilled == null) return;
        for (int i = 0; i < chilled.Length; i++) if (chilled[i].on) EndChill(chilled[i], true);
        chilledCount = 0;
    }

    // ============================================ WARDEN SENTRIES: PIERCE AND DRIFT (Gold Warden)

    // The x of the first sentry still deployed (NaN when none): tests watch it drift.
    public float FirstSentryX { get { return sentries != null && sentries[0].on ? sentries[0].to.x : float.NaN; } }

    readonly List<GameObject> pierceSkip = new List<GameObject>(2);
    public int PierceCount { get; private set; }   // bolts carried on to a second enemy (tests)

    // A bolt just killed an ordinary enemy at `at`: if it still has pierce left it carries on to the nearest other
    // ordinary enemy within reach (never a boss or an elite: their damage is not stacked), same weight.
    void ContinueBolt(AttackRun run, Vector3 at, GameObject through, int pierceLeft, float weight)
    {
        if (pierceLeft <= 0 || bolts == null) return;
        pierceSkip.Clear();
        pierceSkip.Add(through);
        var t = ShipTargets.Nearest(at, ShipSignatures2.SentryPierceReach, pierceSkip, true);
        if (t == null || t.IsShotHitbox || IsSpecial(t.gameObject) || ShipAttackHits.AlreadyHit(t.gameObject)) return;
        Bolt b = null;
        for (int i = 0; i < bolts.Length; i++) if (!bolts[i].on) { b = bolts[i]; break; }
        if (b == null) return;
        Vector3 to = t.transform.position;
        b.on = true;
        b.run = run;
        b.target = t.gameObject;
        b.clear = t;
        b.from = at;
        b.t = 0f;
        b.dur = Mathf.Max(.05f, Vector2.Distance(at, to) / SentryBoltSpeed);
        b.weight = weight;
        b.pierce = pierceLeft - 1;
        b.sprite = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, at, Vector3.one * .7f, BoltAngle(at, to),
            ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 70).Strip("attack_shot", 0, true);
        PierceCount++;
    }

    // A drifting sentry (level 4) keeps its place relative to the ship: it slides toward where it would hover
    // if the ship's sideways move since the deploy were added, at most SentryDriftSpeed, kept inside the view.
    void DriftSentry(Sentry s, float dt)
    {
        Rect view = ShipTargets.View();
        float want = Mathf.Clamp(s.home.x + (transform.position.x - s.anchorX), view.xMin + .8f, view.xMax - .8f);
        float x = Mathf.MoveTowards(s.to.x, want, ShipSignatures2.SentryDriftSpeed * dt);
        if (Mathf.Approximately(x, s.to.x)) return;
        s.to.x = x;
        if (s.sprite != null && s.sprite.Active) s.sprite.Body.position = s.to;
    }
}
