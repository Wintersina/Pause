using System.Collections.Generic;
using UnityEngine;

// FRIENDLY FIRE: everything hostile can hurt everything else hostile.
//
//   * boss shots and lasers destroy the rocks, enemies and rail mines they
//     touch, and take a heart off an elite (BossProjectile, BossBeam);
//   * elite shots already do (EliteShot), and elites crash into hazards
//     (EliteShip.Collide);
//   * a rail mine that bursts -- shot, rammed, caught by friendly fire --
//     blasts everything within MineBlastRadius a beat later (so mines
//     chain, one beat per link);
//   * the free movers (chasers, the weaving rocks and aliens) break on
//     whatever hazard they run into, and it breaks with them.
// None of it pays the pilot (EliteShip.FriendlyKill: blast and sound, no
// score, dust or codex); an elite that dies to it still pays its fixed
// reward (EliteRewards, the elite rules). The boss is never hurt by its own
// attacks, and takes only a sliver (BossFriendlyFireWeight, at most once a
// BossFriendlyFireGap) from an ordinary enemy's shot or beam; nothing here runs during a player death (the domino
// chain, DeathCrashDomino, owns the board then).
//
// HOSTILE FIRE (the weapon half of the above, one rule for every hostile
// projectile and beam -- roster enemies' shots, elite shots and pools, boss
// shots and lasers, any future beam such as the rail-mine laser):
//   * a shot that touches another hazard hits it as a player shot would be
//     treated by that target -- rocks, enemies of every kind, mines (which
//     then burst and blast), rail mines -- and is spent (a siege shell
//     pierces two); a beam hits every target along its length once per
//     pulse (HostileBeam, BeamHits) and burns on;
//   * an elite loses ONE heart (EliteDamage.FriendlyFire, its grace
//     respected: never more than a heart a hit);
//   * never touched: its own shooter (an elite's own shots, a roster enemy's
//     own volley), the boss (Immune), elite shot hitboxes, pickups / atoms /
//     star dust (not hazards), the player (its own rules, unchanged);
//   * spawn-in protection: a target is only fair game once it has been
//     inside the playfield -- the camera view, between the rails
//     (BossRails.DrawnInnerEdge, which follows RailInset) -- for
//     HostileFireSpawnInProtectionSeconds (TrackPlayfield, gameplay time:
//     frozen while paused);
//   * at most HostileFireMaxKillsPerFrame kills a frame (a shot that would
//     kill past the cap is not spent and tries again next frame);
//   * pays the pilot nothing (DamageSource.HostileFire: no score, dust,
//     codex, kill chain or DEATH COMBO); an elite it brings down still pays
//     its fixed reward like every elite death (EliteRewards, the elite rules);
//   * off in the tutorial and during a player death.
public enum DamageSource { Player, HostileFire }

public static class FriendlyFire
{
    // ---- HOSTILE FIRE tuning: the one place ----
    public static bool HostileFireEnabled = true;                     // static (not const): previews / tests may switch it
    public static readonly bool HostileFireElitesLoseOneHeart = true;  // false: elites ignore hostile fire
    public static readonly bool HostileFireAwardsPlayerCredit = false; // true: a hostile-fire kill pays like a weapon kill
    public const int HostileFireMaxKillsPerFrame = 2;
    public const float HostileFireSpawnInProtectionSeconds = .1f;     // just long enough for a spawn-in not to pop; short so a laser / flame already burning still catches an enemy that flies in
    public const float RosterShotGraceSeconds = 1f;                    // a roster enemy's shot spares an enemy for its first second on the board, so a fresh formation cannot shoot itself apart; lasers, flames, blasts and elite fire keep the short grace
    public const float BossFriendlyFireWeight = .05f;                   // a boss hit by an ORDINARY enemy's fire loses this share of one ship hit (never by its own attacks)
    public const float BossFriendlyFireGap = 1f;                        // seconds between two such hits
    public const float HostileFireReach = .8f;                         // x a target's radius (the shots' hit tests)
    public const float HostileFirePopupGap = 1.5f;                     // seconds between two "FRIENDLY FIRE" words
    public const int BossPopupsPerFrame = 6;                           // during a boss: every kill gets its own word, up to this many a frame (a mass kill / the landing shockwave is still bounded)
    public const int MaxBeamTargets = 24;                              // per beam pulse

    // What the kill in progress comes from (reward code reads it).
    public static DamageSource Source { get; private set; } = DamageSource.Player;
    public static bool HostileKillInProgress => Source == DamageSource.HostileFire;
    // Counters (tests, the probe).
    public static int HostileKills, HostileEliteHits, HostileCapped, HostileBossHits, Popups;

    public const float MineBlastRadius = 1.05f;
    public const float MineBlastDelay = .1f;
    public const int MaxPendingBlasts = 16;
    public const float CrashOverlap = .7f;    // (rA + rB) * this: a solid hit, not a graze

    // Counters (tests).
    public static int Kills, EliteHits, MineBlasts, Crashes;

    public static void ResetCounters() { Kills = EliteHits = MineBlasts = Crashes = 0; }

    // Things friendly fire never touches: the boss (and its shots and
    // lasers, which are handled as projectiles), elite shot hitboxes.
    public static bool Immune(GameObject go)
    {
        if (go == null) return true;
        if (go.TryGetComponent(out BossTarget _)) return true;
        if (go.TryGetComponent(out ClearTarget c) && c.Scenery) return true;   // the backdrop rocks
        return go.TryGetComponent(out EliteShotHitbox _);
    }

    public static bool CanHit(ClearTarget t) =>
        t != null && t.isActiveAndEnabled && ClearTarget.IsHazard(t.gameObject) && !Immune(t.gameObject);

    static bool IsBossBody(ClearTarget t) => t != null && t.isActiveAndEnabled && t.TryGetComponent(out BossTarget _);

    // The boss takes a sliver of damage from an ORDINARY enemy's fire (a rail
    // mine's laser, a roster / elite shot); never from its own attacks (the
    // boss shooter is null in the shot calls, or a BossActor body part).
    static bool BossCanBeHurtBy(GameObject shooter) =>
        shooter != null && shooter.GetComponentInParent<BossActor>() == null &&
        clock - lastBossHit >= BossFriendlyFireGap && BossEncounter.Instance != null;

    // A hostile hit on `go`: an elite loses a heart, anything else is
    // destroyed with its blast, unpaid.
    public static void Hit(GameObject go, Vector3 at)
    {
        if (go == null || Immune(go)) return;
        Popup(go.transform.position);
        if (go.TryGetComponent(out EliteShip elite))
        {
            EliteHits++;
            if (EliteShip.HitBy == null) EliteShip.HitBy = "boss shot";
            elite.TakeHit(EliteDamage.FriendlyFire, at);
            return;
        }
        Kills++;
        EliteShip.FriendlyKill(go);
    }

    // ---- hostile fire ----

    static bool tutorialScene;
    static int frameKey = int.MinValue, killsThisFrame, editStep, popupFrame = int.MinValue, popupsThisFrame;
    static float lastPopup = -999f, lastBossHit = -999f, clock;

    public static void ResetHostileCounters() { HostileKills = HostileEliteHits = HostileCapped = HostileBossHits = Popups = ShockwaveKills = ShockwaveEliteKills = 0; killsThisFrame = 0; frameKey = int.MinValue; lastPopup = -999f; lastBossHit = -999f; popupFrame = int.MinValue; popupsThisFrame = 0; }

    // The scene loader's note (HazardRuntime.Boot): Scene.name allocates, so it is read once a load.
    public static void OnSceneLoaded(string sceneName) { tutorialScene = sceneName == score.TutorialScene; }

    public static bool HostileFireAllowed =>
        HostileFireEnabled && !startMenu.youAreInTutorial && !tutorialScene && !DeathCrash.Running && !buttonClicks.playerDied;

    // Once a step (HazardRuntime): how long each hazard has been inside the
    // playfield -- the view, between the rails. Gameplay time, no allocation.
    public static void TrackPlayfield(float dt, Rect view)
    {
        if (dt <= 0f) return;
        clock += dt;
        float edge = BossRails.DrawnInnerEdge;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null) continue;
            Vector2 p = t.transform.position;
            if (view.Contains(p) && Mathf.Abs(p.x) <= edge) t.PlayfieldSeconds += dt;
            else t.PlayfieldSeconds = 0f;
        }
    }

    // Past its spawn-in protection (tests, fixtures: a target that has been on the board a while).
    public static void Settle(GameObject go)
    {
        if (go != null && go.TryGetComponent(out ClearTarget t)) t.PlayfieldSeconds = Mathf.Max(HostileFireSpawnInProtectionSeconds, RosterShotGraceSeconds) + 1f;
    }

    public static bool SpawnProtected(ClearTarget t, float minSeconds = 0f) => t.PlayfieldSeconds < Mathf.Max(HostileFireSpawnInProtectionSeconds, minSeconds);

    // May a hostile shot fired by `shooter` (null: the boss, a test) hit `t`?
    public static bool HostileFireCanHit(ClearTarget t, GameObject shooter, float graceSeconds = 0f)
    {
        if (!HostileFireAllowed) return false;
        if (IsBossBody(t)) return BossCanBeHurtBy(shooter);
        if (!CanHit(t)) return false;
        var go = t.gameObject;
        if (go == shooter || t.IsShotHitbox || !t.enabled) return false;
        // (an elite's own tow or drone: its shots do not spend it)
        if (t.Mother != null && t.Mother.gameObject == shooter) return false;
        // An elite's own gate is joining the play (parked / lifting off it
        // has no collider for anyone) and its grace after a heart.
        var elite = t.Elite;
        if (elite != null || go.TryGetComponent(out elite))
            return HostileFireElitesLoseOneHeart && elite.InPlay;
        return !SpawnProtected(t, graceSeconds);
    }

    // The kill cap's "frame": Time.frameCount in play; in edit mode (tests
    // stepping by hand) every pool / runtime step counts as one.
    public static void HostileStep() { editStep++; }

    static bool KillBudget()
    {
        int key = Application.isPlaying ? Time.frameCount : editStep;
        if (key != frameKey) { frameKey = key; killsThisFrame = 0; }
        return killsThisFrame < HostileFireMaxKillsPerFrame;
    }

    // A hostile weapon hits `t` (HostileFireCanHit said yes): an elite loses
    // a heart, anything else is destroyed with its blast, unpaid. False when
    // the frame's kill cap held it back (the shot is not spent).
    public static bool HostileHit(ClearTarget t, Vector3 at, string by)
    {
        var go = t.gameObject;
        if (IsBossBody(t))
        {
            lastBossHit = clock;
            HostileBossHits++;
            Popup(go.transform.position);
            BossEncounter.Instance?.OnShipAttackHit(BossFriendlyFireWeight, at);
            return true;
        }
        var elite = t.Elite;
        if (elite != null || go.TryGetComponent(out elite))
        {
            HostileEliteHits++;
            Popup(go.transform.position);
            EliteHits++;
            EliteShip.HitBy = by;
            Source = DamageSource.HostileFire;
            try { elite.TakeHit(EliteDamage.FriendlyFire, at); }
            finally { Source = DamageSource.Player; EliteShip.HitBy = null; }
            return true;
        }
        if (!KillBudget()) { HostileCapped++; return false; }
        killsThisFrame++;
        HostileKill(go);
        return true;
    }

    // The kill itself, as hostile fire: counted, the FRIENDLY FIRE word, its
    // blast and death cue, no pilot credit (HostileFireAwardsPlayerCredit).
    static void HostileKill(GameObject go)
    {
        HostileKills++;
        Kills++;
        Popup(go.transform.position);
        Source = DamageSource.HostileFire;
        try
        {
            if (HostileFireAwardsPlayerCredit) collisionDetection.AwardDestroyedTarget(go);
            EliteShip.FriendlyKill(go);
        }
        finally { Source = DamageSource.Player; }
    }

    // ---- the boss's landing shockwave (BossShockwave) ----
    // It clears the board, and everything it kills is FRIENDLY FIRE exactly
    // as a hostile weapon's kill is (HostileKill: the counters, the word, no
    // pilot credit) -- but with no per-frame kill cap and no spawn-in
    // protection, so nothing is left behind. An elite in play loses every
    // heart under the hostile-fire source (it dies as an elite does, its
    // fixed reward included); one in its grace, or still parked / rising, is
    // simply removed. Never the boss, never an elite shot's hitbox (shots are
    // popped by the wave itself). True when something was removed.
    public static int ShockwaveKills, ShockwaveEliteKills;
    public const string ShockwaveBy = "boss shockwave";

    public static bool ShockwaveHit(ClearTarget t, Vector3 at)
    {
        if (t == null || !t.isActiveAndEnabled) return false;
        var go = t.gameObject;
        if (!ClearTarget.IsHazard(go) || Immune(go) || t.IsShotHitbox) return false;
        var elite = t.Elite;
        if (elite != null || go.TryGetComponent(out elite))
        {
            if (elite.State == EliteState.Dead) return false;
            if (!elite.InPlay)
            {
                // still parked / rising in the backdrop: it is the board's, it simply goes
                ClearTarget.Release(elite.gameObject);
                BossUtil.Kill(elite.gameObject);
                return true;
            }
            Popup(go.transform.position);
            EliteShip.HitBy = ShockwaveBy;
            Source = DamageSource.HostileFire;
            try
            {
                // every heart it has; one its grace or armour refused still dies, the same way
                bool landed = elite.TakeHit(EliteDamage.FriendlyFire, at, Mathf.Max(1, elite.Hearts));
                if (elite != null && elite.State != EliteState.Dead) { EliteShip.HitBy = ShockwaveBy; elite.ForceDie(EliteDamage.FriendlyFire); landed = true; }
                if (landed) { HostileEliteHits++; EliteHits++; ShockwaveEliteKills++; }
            }
            finally { Source = DamageSource.Player; EliteShip.HitBy = null; }
            return true;
        }
        ShockwaveKills++;
        HostileKill(go);
        return true;
    }

    // The small "FRIENDLY FIRE" word: every friendly-fire hit or kill from any source (shots, beams, flames,
    // blasts, crashes) calls it; at most one word every HostileFirePopupGap seconds of gameplay time.
    public static System.Action<string, Vector3> WordHook;   // edit-mode previews / tests: stands in for the HUD word
    public static void Popup(Vector3 at)
    {
        if (BossEncounter.Running)
        {
            // BOSS: no shared gap -- every object destroyed shows its own word above itself (the pool recycles the oldest),
            // bounded per frame so a mass kill (shockwave, mine chain) cannot flood the HUD.
            int key = Application.isPlaying ? Time.frameCount : editStep;
            if (key != popupFrame) { popupFrame = key; popupsThisFrame = 0; }
            if (popupsThisFrame >= BossPopupsPerFrame) return;
            popupsThisFrame++;
        }
        else
        {
            if (clock - lastPopup < HostileFirePopupGap) return;
            lastPopup = clock;
        }
        Popups++;
        if (!Application.isPlaying) { WordHook?.Invoke("FRIENDLY FIRE", at); return; }
        var hud = ScoreHud.Current;
        if (hud != null) hud.ShowWord("FRIENDLY FIRE", at, DeathCombo.FlashColour, 22);
    }

    // A beam's targets this pulse: each is hit once.
    public sealed class BeamHits
    {
        readonly int[] ids = new int[MaxBeamTargets];
        int n;
        public int Count => n;
        public void NewPulse() { n = 0; }
        public bool Has(int id) { for (int i = 0; i < n; i++) if (ids[i] == id) return true; return false; }
        public void Add(int id) { if (n < ids.Length) ids[n++] = id; }
    }

    static readonly List<ClearTarget> beamScratch = new List<ClearTarget>(32);
    static readonly Vector2[] beamAt = new Vector2[MaxBeamTargets];

    // A live beam from `o` along unit `d`, `length` long, `half` wide each
    // side: every eligible target it crosses that this pulse has not hit yet
    // is hit (the cap permitting). Returns the hits landed this call.
    public static int HostileBeam(BeamHits hits, Vector2 o, Vector2 d, float length, float half, GameObject shooter,
                                  string by = "laser")
    {
        if (hits == null || length <= 0f || !HostileFireAllowed) return 0;
        beamScratch.Clear();
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count && beamScratch.Count < MaxBeamTargets; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            Vector2 p = t.transform.position;
            float along = Mathf.Clamp(Vector2.Dot(p - o, d), 0f, length);
            float R = half + t.Radius * HostileFireReach;
            Vector2 q = o + d * along;
            if ((q - p).sqrMagnitude > R * R) continue;
            if (!HostileFireCanHit(t, shooter) || hits.Has(t.GetInstanceID())) continue;
            beamAt[beamScratch.Count] = q;
            beamScratch.Add(t);
        }
        int landed = 0;
        for (int i = 0; i < beamScratch.Count; i++)
        {
            var t = beamScratch[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            int id = t.GetInstanceID();
            if (!HostileHit(t, beamAt[i], by)) continue;   // capped: next frame
            hits.Add(id);
            landed++;
        }
        beamScratch.Clear();
        return landed;
    }

    // ---- rail mine blasts ----

    static readonly Vector3[] blastAt = new Vector3[MaxPendingBlasts];
    static readonly float[] blastIn = new float[MaxPendingBlasts];
    static readonly GameObject[] blastMine = new GameObject[MaxPendingBlasts];
    static int blasts;

    public static int PendingBlasts => blasts;

    // A rail mine is bursting (RailBombAnimator.Burst): its blast lands
    // MineBlastDelay later.
    public static void MineBlast(GameObject mine)
    {
        if (mine == null || DeathCrash.Running || blasts >= MaxPendingBlasts) return;
        blastAt[blasts] = mine.transform.position;
        blastIn[blasts] = MineBlastDelay;
        blastMine[blasts] = mine;
        blasts++;
        if (Application.isPlaying) HazardRuntime.Ensure();
    }

    public static void ClearPending()
    {
        for (int i = 0; i < blasts; i++) blastMine[i] = null;
        blasts = 0;
    }

    static readonly List<ClearTarget> scratch = new List<ClearTarget>(64);

    public static void StepBlasts(float dt)
    {
        if (blasts == 0) return;
        if (DeathCrash.Running) { ClearPending(); return; }
        for (int i = 0; i < blasts; i++) blastIn[i] -= dt;
        int k = 0;
        while (k < blasts)
        {
            if (blastIn[k] > 0f) { k++; continue; }
            Vector3 at = blastAt[k];
            GameObject mine = blastMine[k];
            blasts--;
            blastAt[k] = blastAt[blasts]; blastIn[k] = blastIn[blasts]; blastMine[k] = blastMine[blasts];
            blastMine[blasts] = null;
            Detonate(at, mine);
        }
    }

    static void Detonate(Vector3 at, GameObject mine)
    {
        MineBlasts++;
        scratch.Clear();
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!CanHit(t) || t.gameObject == mine) continue;
            float r = MineBlastRadius + t.Radius * .5f;
            if (((Vector2)(t.transform.position - at)).sqrMagnitude > r * r) continue;
            scratch.Add(t);
        }
        for (int i = 0; i < scratch.Count; i++)
            if (scratch[i] != null && scratch[i].isActiveAndEnabled)
            {
                EliteShip.HitBy = "mine blast";
                Hit(scratch[i].gameObject, at);
                EliteShip.HitBy = null;
            }
        scratch.Clear();
    }

    // ---- free movers running into things ----

    // (Pilots ride a moveEnimes too, so they are free movers: with soft avoidance nothing stops one meeting a neighbour.)
    public static bool FreeMover(GameObject go) =>
        go.TryGetComponent(out ChaserEnemy _) || go.TryGetComponent(out moveEnimes _);

    // One crash a step at most (the registry changes under a kill).
    public static bool StepCrashes(Rect view)
    {
        if (DeathCrash.Running) return false;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var a = live[i];
            if (!CanHit(a) || a.TryGetComponent(out EliteShip _)) continue;
            Vector2 pa = a.transform.position;
            if (!view.Contains(pa) || !FreeMover(a.gameObject)) continue;
            for (int j = 0; j < live.Count; j++)
            {
                if (j == i) continue;
                var b = live[j];
                if (!CanHit(b) || b.TryGetComponent(out EliteShip _)) continue;
                Vector2 pb = b.transform.position;
                float r = (a.Radius + b.Radius) * CrashOverlap;
                if ((pa - pb).sqrMagnitude > r * r || !view.Contains(pb)) continue;
                Crashes++;
                var ga = a.gameObject;
                var gb = b.gameObject;
                Hit(gb, pa);
                Hit(ga, pb);
                return true;
            }
        }
        return false;
    }
}

// SPLIT INTO PIECES, SOMETIMES: a hazard that dies (any cause) breaks into
// spinning fragments of its own drawing -- cut by the death crash's cutter
// (DeathCrash.FragmentsOf, 2-4 pieces by size, cached per drawing) -- with
// a smaller blast under them, instead of the full blast. Bigger things
// split more often. The fragments are cosmetic (no collider, never part of
// the death domino), pooled (MaxFragments on screen), stepped on gameplay
// time. Never during a player death: the domino breaks things its own way.
public static class EnemySplit
{
    public const float SmallChance = .30f, MediumChance = .35f, LargeChance = .40f, EliteChance = .40f;
    public const int MaxSplitsPerStep = 2;   // a new drawing is cut on the GPU once; bounds the hitch

    // Edit mode only splits when a test asks.
    public static bool ForceInEditor;
    // Previews: a fixed chance for every kill (-1: the configured ones).
    public static float ChanceOverride = -1f;
    public static bool Enabled => Application.isPlaying || ForceInEditor;

    public static int Rolls, Splits, Capped;
    static int splitsThisStep;
    static uint rng = 0x9E3779B9u;
    static readonly System.Func<float> Rand = Next01;   // cached: no delegate per split

    public static void Seed(uint seed) { rng = seed == 0 ? 0x9E3779B9u : seed; }
    public static void ResetCounters() { Rolls = Splits = Capped = 0; splitsThisStep = 0; }
    internal static void NewStep() { splitsThisStep = 0; }

    public static float ChanceFor(TargetExplosion.Size size) =>
        size == TargetExplosion.Size.Large ? LargeChance : size == TargetExplosion.Size.Medium ? MediumChance : SmallChance;

    public static TargetExplosion.Size Smaller(TargetExplosion.Size size) =>
        size == TargetExplosion.Size.Large ? TargetExplosion.Size.Medium : TargetExplosion.Size.Small;

    static float Next01()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFF) / 16777216f;
    }

    // Rolls for `target` (a hazard about to be destroyed); true when it broke
    // into fragments (the caller then plays a smaller blast).
    public static bool TrySplit(GameObject target, TargetExplosion.Size size) =>
        TrySplit(target, null, ChanceFor(size));

    public static bool TrySplitElite(EliteShip elite) => elite != null && TrySplit(elite.gameObject, elite.Hull, EliteChance);

    static bool TrySplit(GameObject target, SpriteRenderer sr, float chance)
    {
        if (target == null || !Enabled || DeathCrash.Running) return false;
        Rolls++;
        if (Next01() >= (ChanceOverride >= 0f ? ChanceOverride : chance)) return false;
        if (splitsThisStep >= MaxSplitsPerStep || HazardRuntime.FreeFragments < 2) { Capped++; return false; }
        if (sr == null) sr = target.GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null || !sr.enabled) return false;
        float radius = target.TryGetComponent(out ClearTarget ct) ? ct.Radius : ClearTarget.MeasureRadius(target);
        if (!DeathCrash.FragmentsOf(sr.sprite, DeathCrash.CountFor(radius), out var sprites, out var local, out var rad))
            return false;
        int n = HazardRuntime.Ensure().Shatter(sr, sprites, local, rad, Rand);
        if (n < 2) return false;
        splitsThisStep++;
        Splits++;
        return true;
    }
}

// The scene's one stepper for the cosmetic and friendly-fire bits above:
// split fragments, shot-vs-shot pops, pending mine blasts, movers crashing,
// and the DEATH COMBO's queued links (DeathCombo).
// Made on demand in play mode (and on every gameplay scene load); tests
// call Step() themselves.
public class HazardRuntime : MonoBehaviour
{
    public const int MaxFragments = 40, MaxPops = 16;
    public const float PopSeconds = .2f;

    public static HazardRuntime Instance { get; private set; }

    sealed class Frag
    {
        public SpriteRenderer sr;
        public Vector3 vel;
        public float spin, age, life, scale;
        public bool active;
    }

    sealed class Pop
    {
        public SpriteRenderer ring, core;
        public float age;
        public Color tint;
        public bool active;
    }

    readonly Frag[] frags = new Frag[MaxFragments];
    readonly Pop[] pops = new Pop[MaxPops];
    int fragsBuilt, popsBuilt;

    public static int FreeFragments => Instance == null ? MaxFragments : MaxFragments - Instance.ActiveFragments;

    public int ActiveFragments
    {
        get
        {
            int n = 0;
            for (int i = 0; i < fragsBuilt; i++) if (frags[i].active) n++;
            return n;
        }
    }

    public int ActivePops
    {
        get
        {
            int n = 0;
            for (int i = 0; i < popsBuilt; i++) if (pops[i].active) n++;
            return n;
        }
    }

    public int FragmentsBuilt => fragsBuilt;

    public static HazardRuntime Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("~HazardRuntime");
        Instance = go.AddComponent<HazardRuntime>();
        return Instance;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) =>
        {
            FriendlyFire.OnSceneLoaded(s.name);
            if (Application.isPlaying) Ensure();
        };
        FriendlyFire.OnSceneLoaded(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        Ensure();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        FriendlyFire.ClearPending();
        DeathCombo.ClearPending();
    }

    void Update()
    {
        Step(TargetExplosion.Delta());
    }

    public void Step(float dt)
    {
        EnemySplit.NewStep();
        FriendlyFire.HostileStep();
        FriendlyFire.TrackPlayfield(dt, ShipTargets.View());   // hostile fire's spawn-in protection
        FriendlyFire.StepBlasts(dt);
        DeathCombo.Step(dt);
        if (dt > 0f && Application.isPlaying && TargetExplosion.WorldScrolling) FriendlyFire.StepCrashes(ShipTargets.View());
        float fall = TargetExplosion.WorldScrolling ? moveBackGround.speed * 30f : 0f;
        for (int i = 0; i < fragsBuilt; i++) StepFrag(frags[i], dt, fall);
        for (int i = 0; i < popsBuilt; i++) StepPop(pops[i], dt);
    }

    // ---- fragments ----

    Frag FreeFrag()
    {
        for (int i = 0; i < fragsBuilt; i++) if (!frags[i].active) return frags[i];
        if (fragsBuilt >= MaxFragments) return null;
        var go = new GameObject("SplitFragment");
        go.transform.SetParent(transform, false);
        var f = new Frag { sr = go.AddComponent<SpriteRenderer>() };
        go.SetActive(false);
        frags[fragsBuilt++] = f;
        return f;
    }

    // The drawing on `from` breaks into its pieces, flung out from its
    // centre, spinning. Returns how many pieces flew.
    public int Shatter(SpriteRenderer from, Sprite[] sprites, Vector2[] local, float[] radius, System.Func<float> rand)
    {
        var tf = from.transform;
        Vector3 centre = tf.position;
        Vector3 scale = tf.lossyScale;
        float flipX = from.flipX ? -1f : 1f, flipY = from.flipY ? -1f : 1f;
        Quaternion rot = tf.rotation;
        int n = 0;
        for (int k = 0; k < sprites.Length; k++)
        {
            if (sprites[k] == null) continue;
            var f = FreeFrag();
            if (f == null) break;
            Vector2 l = local[k];
            Vector3 off = rot * new Vector3(l.x * scale.x * flipX, l.y * scale.y * flipY, 0f);
            Vector3 dir = off.sqrMagnitude > 1e-6f ? off.normalized : (Vector3)Random.insideUnitCircle.normalized;
            float speed = Mathf.Lerp(1.3f, 2.6f, rand());
            f.vel = dir * speed + new Vector3((rand() - .5f) * .6f, (rand() - .2f) * .6f, 0f);
            f.spin = Mathf.Lerp(220f, 560f, rand()) * (rand() < .5f ? -1f : 1f);
            f.life = Mathf.Lerp(.75f, 1.15f, rand());
            f.age = 0f;
            f.scale = 1f;
            f.active = true;
            var sr = f.sr;
            sr.sprite = sprites[k];
            sr.flipX = from.flipX;
            sr.flipY = from.flipY;
            sr.color = from.color;
            sr.sortingLayerID = from.sortingLayerID;
            sr.sortingOrder = from.sortingOrder + 1;
            sr.transform.SetPositionAndRotation(centre + off, rot);
            sr.transform.localScale = scale;
            sr.gameObject.SetActive(true);
            n++;
        }
        return n;
    }

    static void StepFrag(Frag f, float dt, float fall)
    {
        if (!f.active || dt <= 0f) return;
        f.age += dt;
        if (f.age >= f.life) { f.active = false; f.sr.gameObject.SetActive(false); return; }
        var t = f.sr.transform;
        f.vel *= 1f - Mathf.Min(1f, 1.4f * dt);
        Vector3 p = t.position + f.vel * dt;
        p.y -= fall * dt;
        t.position = p;
        t.Rotate(0f, 0f, f.spin * dt);
        float k = f.age / f.life;
        var c = f.sr.color;
        c.a = k < .55f ? 1f : 1f - (k - .55f) / .45f;
        f.sr.color = c;
    }

    // ---- shot-vs-shot pops ----

    public static void PopFx(Vector2 at, Color tint)
    {
        if (!Application.isPlaying && Instance == null) return;
        Ensure().PlayPop(at, tint);
    }

    void PlayPop(Vector2 at, Color tint)
    {
        Pop p = null;
        for (int i = 0; i < popsBuilt; i++) if (!pops[i].active) { p = pops[i]; break; }
        if (p == null)
        {
            if (popsBuilt >= MaxPops)
            {
                // the oldest gives way
                p = pops[0];
                for (int i = 1; i < popsBuilt; i++) if (pops[i].age > p.age) p = pops[i];
            }
            else
            {
                var go = new GameObject("ShotPop");
                go.transform.SetParent(transform, false);
                p = new Pop { ring = go.AddComponent<SpriteRenderer>() };
                var c = new GameObject("Core");
                c.transform.SetParent(go.transform, false);
                p.core = c.AddComponent<SpriteRenderer>();
                p.ring.sprite = HostileGlow.Halo;
                p.core.sprite = HostileGlow.Halo;
                p.ring.sortingOrder = 33;
                p.core.sortingOrder = 34;
                pops[popsBuilt++] = p;
            }
        }
        p.age = 0f;
        p.tint = HostileGlow.Tint(tint);
        p.active = true;
        p.ring.transform.position = new Vector3(at.x, at.y, 0f);
        p.ring.gameObject.SetActive(true);
        StepPop(p, 0f);
    }

    static void StepPop(Pop p, float dt)
    {
        if (!p.active) return;
        p.age += dt;
        float k = p.age / PopSeconds;
        if (k >= 1f) { p.active = false; p.ring.gameObject.SetActive(false); return; }
        float ring = Mathf.Lerp(.18f, .62f, 1f - (1f - k) * (1f - k));
        p.ring.transform.localScale = Vector3.one * ring;
        p.ring.transform.rotation = Quaternion.Euler(0f, 0f, 45f * Mathf.Floor(k * 4f));
        var c = p.tint;
        c.a = 1f - k;
        p.ring.color = c;
        // a white-hot point for the first beat
        p.core.transform.localScale = Vector3.one * (k < .4f ? .45f : .25f);
        var w = Color.white;
        w.a = k < .4f ? 1f : Mathf.Max(0f, 1f - (k - .4f) / .3f);
        p.core.color = w;
    }

    public void ClearAll()
    {
        for (int i = 0; i < fragsBuilt; i++) { frags[i].active = false; frags[i].sr.gameObject.SetActive(false); }
        for (int i = 0; i < popsBuilt; i++) { pops[i].active = false; pops[i].ring.gameObject.SetActive(false); }
    }
}

// THE REGISTRY OF DAMAGING ENEMY-OWNED SOURCES. Every component that makes a
// hitbox that can hurt the pilot, or registers a hostile shot / zone, is
// listed here with the call that sends it through the shared friendly-fire
// path (FriendlyFire.HostileFireCanHit / HostileHit / HostileBeam / MineBlast
// / Hit). FriendlyFireCoverageTest scans the source tree and fails when a
// class makes such a hitbox without being listed (or lists a marker it no
// longer calls), so a future attack cannot silently skip friendly fire.
// Bodies (EliteShip, the boss) are targets and crash through their own rules.
public static class FriendlyFireSources
{
    public readonly struct Source
    {
        public readonly string type, marker;
        public Source(string type, string marker) { this.type = type; this.marker = marker; }
    }

    public static readonly Source[] All =
    {
        new Source("EliteShot", "FriendlyFire.HostileHit("),          // every elite / roster / pool shot kind
        new Source("BossProjectile", "FriendlyFire.HostileHit("),     // boss shots (never the boss itself)
        new Source("BossBeam", "FriendlyFire.HostileBeam("),          // boss lasers
        new Source("AttackHazard", "FriendlyFire.HostileHit("),       // blast, strike, jet, wave, lash, log (the base burns)
        new Source("RailMineLaser", "FriendlyFire.HostileHit("),      // rail mine laser / flame
        new Source("RailBombAnimator", "FriendlyFire.MineBlast("),    // every mine burst blasts its neighbours
    };
}
