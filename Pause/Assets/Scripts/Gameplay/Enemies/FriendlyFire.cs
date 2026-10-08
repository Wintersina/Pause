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
// reward (EliteRewards, the elite rules). The boss itself is never hurt by
// friendly fire, and nothing here runs during a player death (the domino
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
    public const float HostileFireSpawnInProtectionSeconds = 1f;
    public const float HostileFireReach = .8f;                         // x a target's radius (the shots' hit tests)
    public const float HostileFirePopupGap = 1.5f;                     // seconds between two "FRIENDLY FIRE" words
    public const int MaxBeamTargets = 24;                              // per beam pulse

    // What the kill in progress comes from (reward code reads it).
    public static DamageSource Source { get; private set; } = DamageSource.Player;
    public static bool HostileKillInProgress => Source == DamageSource.HostileFire;
    // Counters (tests, the probe).
    public static int HostileKills, HostileEliteHits, HostileCapped;

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
        return go.TryGetComponent(out EliteShotHitbox _);
    }

    public static bool CanHit(ClearTarget t) =>
        t != null && t.isActiveAndEnabled && ClearTarget.IsHazard(t.gameObject) && !Immune(t.gameObject);

    // A hostile hit on `go`: an elite loses a heart, anything else is
    // destroyed with its blast, unpaid.
    public static void Hit(GameObject go, Vector3 at)
    {
        if (go == null || Immune(go)) return;
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
    static int frameKey = int.MinValue, killsThisFrame, editStep;
    static float lastPopup = -999f, clock;

    public static void ResetHostileCounters() { HostileKills = HostileEliteHits = HostileCapped = 0; killsThisFrame = 0; frameKey = int.MinValue; }

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
        if (go != null && go.TryGetComponent(out ClearTarget t)) t.PlayfieldSeconds = HostileFireSpawnInProtectionSeconds + 1f;
    }

    public static bool SpawnProtected(ClearTarget t) => t.PlayfieldSeconds < HostileFireSpawnInProtectionSeconds;

    // May a hostile shot fired by `shooter` (null: the boss, a test) hit `t`?
    public static bool HostileFireCanHit(ClearTarget t, GameObject shooter)
    {
        if (!CanHit(t) || !HostileFireAllowed) return false;
        var go = t.gameObject;
        if (go == shooter || t.IsShotHitbox || !t.enabled) return false;
        // An elite's own gate is joining the play (parked / lifting off it
        // has no collider for anyone) and its grace after a heart.
        var elite = t.Elite;
        if (elite != null || go.TryGetComponent(out elite))
            return HostileFireElitesLoseOneHeart && elite.InPlay;
        return !SpawnProtected(t);
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
        var elite = t.Elite;
        if (elite != null || go.TryGetComponent(out elite))
        {
            HostileEliteHits++;
            EliteHits++;
            EliteShip.HitBy = by;
            Source = DamageSource.HostileFire;
            try { elite.TakeHit(EliteDamage.FriendlyFire, at); }
            finally { Source = DamageSource.Player; EliteShip.HitBy = null; }
            return true;
        }
        if (!KillBudget()) { HostileCapped++; return false; }
        killsThisFrame++;
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
        return true;
    }

    static void Popup(Vector3 at)
    {
        if (!Application.isPlaying || clock - lastPopup < HostileFirePopupGap) return;
        lastPopup = clock;
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
