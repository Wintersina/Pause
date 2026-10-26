using System.Collections.Generic;
using UnityEngine;

// The attack roster v3 verbs of the three top-price hulls and the premium Paragon (the rest of the
// runner is ShipAttacks.cs):
//
//   Ion Lancer   ION LASER      a thin laser leaves the nose and sweeps the screen, burning what it crosses
//   Jade Phantom FROST NOVA     a frost ring grows over the screen; what it reaches freezes, then shatters
//   Gold Warden  WARDEN SENTRIES sentry pods launch from the ship, deploy across the screen and shoot
//   Paragon      NUKE           a warhead drops in onto the impact point, blinks and detonates: a red shockwave kills
//                               what it reaches and a mushroom cloud rises behind the action. Its timed mini-warheads (ParagonMissiles) are the small version.
//
// The three top-tier verbs are the cinematic attacks (ShipPowerController): the runner steps on gameplay
// time and the world is never slowed. A red atom's free shot plays the same verb, shortened (AttackRun.quick).
//
// Every hit goes through ShipAttackHits like the other attacks. A boss (or an elite) takes a whole attack as
// roughly one hit: the laser and the nova hit it once per pass / once, the nuke once, the sentries' bolts
// weigh 1 / (shots over a sentry's life). Everything here is preallocated and pooled; nothing allocates per frame.
public partial class ShipAttackRunner
{
    // ---------------------------------------------------------------- shared

    // One contact: `go` is hit once per `seen` list, a capped target (an elite) taking it in full.
    bool HitOnce(List<GameObject> seen, GameObject go, float weight)
    {
        if (go == null || seen.Contains(go)) return false;
        seen.Add(go);
        float own = weight;
        return ShipAttackHits.Hit(go, ship, weight, ref own);
    }

    static bool IsSpecial(GameObject go) { return go.GetComponent<IShipAttackTarget>() != null; }

    // How far from `from` the farthest corner of the view is (plus a margin): what an expanding ring has to cover.
    static float FarthestCorner(Vector3 from)
    {
        Rect v = ShipTargets.View();
        if (v.width > 1e4f) return 18f;   // no camera (tests): a generous screen
        float dx = Mathf.Max(Mathf.Abs(v.xMin - from.x), Mathf.Abs(v.xMax - from.x));
        float dy = Mathf.Max(Mathf.Abs(v.yMin - from.y), Mathf.Abs(v.yMax - from.y));
        return Mathf.Sqrt(dx * dx + dy * dy) + .6f;
    }

    static float RayExit(Vector3 o, Vector2 d, Rect view)
    {
        float t = float.MaxValue;
        if (d.y > 1e-4f) t = Mathf.Min(t, (view.yMax + .3f - o.y) / d.y);
        if (d.x > 1e-4f) t = Mathf.Min(t, (view.xMax - o.x) / d.x);
        else if (d.x < -1e-4f) t = Mathf.Min(t, (view.xMin - o.x) / d.x);
        return Mathf.Max(.5f, t);
    }

    void StartTop(AttackRun run, Vector3 nose)
    {
        switch (run.loadout.attack)
        {
            case ShipAttack.IonLaser:       StartIon(run, nose); break;
            case ShipAttack.FrostNova:      StartNova(run); break;
            case ShipAttack.WardenSentries: StartSentries(run, nose); break;
            case ShipAttack.Nuke:           StartNuke(run, nose); break;
        }
    }

    void StepTop(AttackRun run, float dt)
    {
        switch (run.loadout.attack)
        {
            case ShipAttack.IonLaser:       StepIon(run); break;
            case ShipAttack.FrostNova:      StepNova(run); break;
            case ShipAttack.WardenSentries: StepSentriesRun(run); break;
            case ShipAttack.Nuke:           StepNukeRun(run); break;
        }
    }

    // Every frame, whatever the runs are doing: the things the attacks left behind.
    void StepTopObjects(float dt)
    {
        if (frozenCount > 0) StepFrozen(dt);
        if (sentries != null) StepSentries(dt);
        if (bolts != null) StepBolts(dt);
        if (warheads != null) StepWarheads(dt);
    }

    // True while a cinematic (top tier) attack still holds the cinematic state.
    public bool CinematicHold
    {
        get
        {
            for (int i = 0; i < runs.Length; i++)
            {
                var r = runs[i];
                if (r != null && r.active && !r.quick && !r.slowDone && ShipLoadoutTable.IsCinematic(r.loadout.attack)) return true;
            }
            return false;
        }
    }

    void StopTop()
    {
        if (frozen != null)
            for (int i = 0; i < frozen.Length; i++) if (frozen[i].on) ReleaseFrozen(frozen[i], true);
        frozenCount = 0;
        if (sentries != null)
            for (int i = 0; i < sentries.Length; i++)
            {
                var s = sentries[i];
                if (s.sprite != null && s.sprite.Active) s.sprite.Stop();
                s.on = false;
                s.sprite = null;
            }
        if (bolts != null)
            for (int i = 0; i < bolts.Length; i++)
            {
                if (bolts[i].sprite != null && bolts[i].sprite.Active) bolts[i].sprite.Stop();
                bolts[i].on = false;
                bolts[i].sprite = null;
            }
        if (warheads != null)
            for (int i = 0; i < warheads.Length; i++)
            {
                var w = warheads[i];
                if (w.sprite != null && w.sprite.Active) w.sprite.Stop();
                if (w.ring != null && w.ring.Active) w.ring.Stop();
                if (w.mush != null && w.mush.Active) w.mush.Stop();
                w.on = false;
                w.sprite = w.ring = w.mush = null;
            }
    }

    // ============================================================ ION LASER
    //
    // A held laser leaves the nose and SWEEPS from about 70 degrees to one side of straight up to 70 to the
    // other (l.width is the half angle) over l.duration, on the side with more targets first. It reaches the
    // top of the view (never beyond l.range) and stops at the first boss in its way. Everything it crosses
    // is hit once per pass: a burst at the contact. Upgrades: a longer sweep, a second pass back.

    public const float IonBeamHalfWidth = .2f;
    public const float IonQuickShare = .6f;   // a free shot sweeps in this share of the time, one pass

    public int IonPasses(AttackRun run) { return run.quick ? 1 : Mathf.Max(1, run.loadout.shots); }
    public float IonPassSeconds(AttackRun run) { return Mathf.Max(.1f, run.loadout.duration * (run.quick ? IonQuickShare : 1f)); }

    // The laser's angle from straight up (right is positive) at `seconds` into a firing.
    public static float IonAngle(float half, int startSide, int pass, float prog)
    {
        float dir = -startSide * (pass % 2 == 0 ? 1f : -1f);
        return -dir * half + dir * 2f * half * Mathf.Clamp01(prog);
    }

    void StartIon(AttackRun run, Vector3 nose)
    {
        // the side with more targets goes first
        ShipTargets.Collect(targets);
        int left = 0, right = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;
            if (targets[i].transform.position.x < nose.x) left++; else right++;
        }
        run.hops = left > right ? -1 : 1;
        run.fired = 0;
        run.visual = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose, new Vector3(1f, .9f, 1f), 90f,
            ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 69).Owned(run);
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, nose, Vector3.one * .45f, 0f,
            ShipFxArt.BurstTicks, false, 0f, transform, Vector3.up * noseOffset, 71);
    }

    void StepIon(AttackRun run)
    {
        var l = run.loadout;
        int passes = IonPasses(run);
        float passSeconds = IonPassSeconds(run);
        if (run.t >= passSeconds * passes) { run.slowDone = true; run.Stop(); return; }
        int pass = Mathf.Min(passes - 1, (int)(run.t / passSeconds));
        if (pass != run.fired) { run.fired = pass; run.hit.Clear(); }   // a new pass can hit everything again
        float angle = IonAngle(l.width, run.hops, pass, (run.t - pass * passSeconds) / passSeconds);
        float rad = angle * Mathf.Deg2Rad;
        Vector2 d = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        Vector3 nose = Nose;
        Rect view = ShipTargets.View();
        float len = Mathf.Min(l.range, RayExit(nose, d, view));
        ShipTargets.Collect(targets);

        // a boss in the way ends the beam at it
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled || !ShipTargets.IsBossTarget(t)) continue;
            Vector2 to = t.transform.position - nose;
            float along = Vector2.Dot(to, d);
            if (along <= 0f) continue;
            float side = Mathf.Abs(to.x * d.y - to.y * d.x);
            if (side > IonBeamHalfWidth + t.Radius * .55f) continue;
            len = Mathf.Min(len, Mathf.Max(.6f, along));
        }

        float weight = 1f / passes;
        Vector3 end = nose + (Vector3)(d * len);
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled || run.hit.Contains(t.gameObject)) continue;
            float reach = IonBeamHalfWidth + t.Radius * .55f;
            if (HostileShots.SegmentDistanceSq(nose, end, t.transform.position) > reach * reach) continue;
            if (Vector2.Dot((Vector2)(t.transform.position - nose), d) > len + t.Radius) continue;
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, t.transform.position, Vector3.one * .5f, 0f,
                ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
            HitOnce(run.hit, t.gameObject, weight);
        }

        // the beam itself: art is a horizontal strip, so it is stretched along x and turned onto the line
        if (run.visual != null && run.visual.Active && run.visual.Run == run)
        {
            var b = run.visual.Body;
            b.position = nose + (Vector3)(d * len * .5f);
            b.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            b.localScale = new Vector3(len / .6f, .9f, 1f);
        }
    }

    // ============================================================ FROST NOVA
    //
    // A thin frost ring grows from the ship over NovaSeconds until it has covered the screen. Everything it
    // reaches FREEZES (FrozenState: movers and brain suspended, its shots on screen cancelled, an ice overlay
    // sized to it) and SHATTERS l.duration later: an ice burst, then the kill. A boss or an elite does not
    // freeze: it takes one hit and the world stalls for a moment. Upgrade: the shatter also hits whatever is
    // within l.width of the shattered one.

    public const float NovaSeconds = 1f;
    public const float NovaQuickShare = .7f;
    public const float CrackSeconds = .33f;     // the crust cracks over the last of the freeze
    public const float ShatterSplashWeight = .5f;
    public const int MaxFrozen = 32;
    const float NovaRingArtRadius = .35f;       // the ring drawing's radius in world units at scale 1

    class FrozenEntry
    {
        public bool on, cracking;
        public GameObject go;
        public ClearTarget target;
        public FrozenState state;
        public AttackSprite overlay;
        public AttackRun run;
        public float t, shatterAt, radius;
    }

    FrozenEntry[] frozen;
    int frozenCount;
    public int FrozenNow { get { return frozenCount; } }
    public int ShatterCount { get; private set; }

    void StartNova(AttackRun run)
    {
        if (frozen == null)
        {
            frozen = new FrozenEntry[MaxFrozen];
            for (int i = 0; i < frozen.Length; i++) frozen[i] = new FrozenEntry();
        }
        run.point = new Vector3(FarthestCorner(transform.position), 0f, 0f);
        run.visual = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, transform.position, Vector3.one * .3f, 0f,
            HoldTicks, true, 0f, null, Vector3.zero, 66).Strip("attack", 7, true).Owned(run);
    }

    static readonly int[] HoldTicks = { 1000 };   // one drawing held

    void StepNova(AttackRun run)
    {
        float seconds = NovaSeconds * (run.quick ? NovaQuickShare : 1f);
        float maxR = run.point.x;
        float p = Mathf.Clamp01(run.t / seconds);
        float r = maxR * p;
        Vector3 center = transform.position;
        bool growing = run.t < seconds;

        if (run.visual != null && run.visual.Active && run.visual.Run == run)
        {
            run.visual.Body.position = center;
            run.visual.Body.localScale = Vector3.one * Mathf.Max(.3f, r / NovaRingArtRadius);
            run.visual.SetColor(new Color(1f, 1f, 1f, 1f - .6f * p));
        }
        if (growing)
        {
            HostileShots.ClearWithin(center, r);
            ShipTargets.Collect(targets);
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.isActiveAndEnabled || run.hit.Contains(t.gameObject)) continue;
                Vector2 dv = t.transform.position - center;
                float reach = r + t.Radius * .5f;
                if (dv.sqrMagnitude > reach * reach) continue;
                if (IsSpecial(t.gameObject))
                {
                    // bosses and elites don't freeze: one hit
                    HitOnce(run.hit, t.gameObject, 1f);
                    continue;
                }
                if (Freeze(run, t)) run.hit.Add(t.gameObject);
            }
        }
        else
        {
            if (run.visual != null) { if (run.visual.Active && run.visual.Run == run) run.visual.Stop(); run.visual = null; }
            if (!HasFrozenOf(run)) { run.slowDone = true; run.Stop(); }
        }
    }

    bool HasFrozenOf(AttackRun run)
    {
        if (frozen == null) return false;
        for (int i = 0; i < frozen.Length; i++) if (frozen[i].on && frozen[i].run == run) return true;
        return false;
    }

    bool Freeze(AttackRun run, ClearTarget t)
    {
        FrozenEntry e = null;
        for (int i = 0; i < frozen.Length; i++) if (!frozen[i].on) { e = frozen[i]; break; }
        if (e == null) return false;
        var st = FrozenState.Freeze(t.gameObject);
        if (st == null) return false;
        e.on = true;
        e.cracking = false;
        e.go = t.gameObject;
        e.target = t;
        e.state = st;
        e.run = run;
        e.t = 0f;
        e.radius = t.Radius;
        e.shatterAt = Mathf.Max(.3f, run.loadout.duration * (run.quick ? NovaQuickShare : 1f));
        frozenCount++;
        e.overlay = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, t.transform.position,
            Vector3.one * Mathf.Max(.7f, e.radius * 2.6f), 0f, ShipFxArt.LoopTicks, true, 0f, t.transform, Vector3.zero, 67)
            .Strip("attack_ice", 0, true);
        return true;
    }

    void ReleaseFrozen(FrozenEntry e, bool thaw)
    {
        if (!e.on) return;
        e.on = false;
        if (e.overlay != null && e.overlay.Active) e.overlay.Stop();
        e.overlay = null;
        if (thaw && e.state != null) e.state.Thaw();
        e.state = null;
        e.go = null;
        e.target = null;
        frozenCount = Mathf.Max(0, frozenCount - 1);
    }

    void StepFrozen(float dt)
    {
        for (int i = 0; i < frozen.Length; i++)
        {
            var e = frozen[i];
            if (!e.on) continue;
            // destroyed (shot down by something else) or despawned: nothing left to shatter
            if (e.go == null || e.target == null || !e.target.isActiveAndEnabled || !e.go.activeInHierarchy)
            {
                ReleaseFrozen(e, false);
                continue;
            }
            e.t += dt;
            if (!e.cracking && e.t >= e.shatterAt - CrackSeconds)
            {
                e.cracking = true;
                if (e.overlay != null && e.overlay.Active) e.overlay.Stop();
                e.overlay = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, e.go.transform.position,
                    Vector3.one * Mathf.Max(.7f, e.radius * 2.6f), 0f, ShipFxArt.BurstTicks, false, 0f, e.go.transform,
                    Vector3.zero, 67).Strip("attack_ice", 8, false);
            }
            if (e.t >= e.shatterAt) Shatter(e);
        }
    }

    void Shatter(FrozenEntry e)
    {
        Vector3 at = e.go.transform.position;
        float splash = e.run != null ? e.run.loadout.width : 0f;
        var go = e.go;
        var run = e.run;
        float radius = e.radius;
        ReleaseFrozen(e, false);
        ShatterCount++;
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, at, Vector3.one * Mathf.Max(.8f, radius * 2.6f), 0f,
            ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        ShipAttackHits.Hit(go, ship);
        if (splash > 0f)
        {
            ShipTargets.Collect(targets);
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.isActiveAndEnabled || t.gameObject == go) continue;
                Vector2 dv = t.transform.position - at;
                float reach = splash + t.Radius * .5f;
                if (dv.sqrMagnitude > reach * reach) continue;
                if (run != null) HitOnce(run.hit, t.gameObject, ShatterSplashWeight);
                else ShipAttackHits.Hit(t.gameObject, ship);
            }
        }
    }

    // ===================================================== WARDEN SENTRIES
    //
    // l.shots sentry pods launch from the nose to hover points spread across the lower-middle of the screen,
    // unfold on arrival (the cinematic slow motion ends once they are deployed), then shoot slim bolts at the
    // nearest target in range at l.speed shots a second for l.duration of normal time, and fold away. They
    // hold their screen position and nothing hits them. A bolt weighs 1 / (shots over a sentry's life), so a
    // sentry is worth about one boss hit.

    public const int MaxSentries = 8, MaxBolts = 16;
    public const float SentryFlightSeconds = .4f, SentryStagger = .08f, SentryBoltSpeed = 30f, SentrySpacing = 2.6f;
    public const float SentryQuickShare = .5f;
    static readonly int[] DeployTicks = { 1, 1, 1, 1, 1, 1, 1, 1000 };   // unfolds, then holds the open turret
    static readonly int[] FoldTicks = { 1, 1, 1, 1, 1, 1, 1, 1 };

    public static float DeploySeconds { get { return 8 * WeaponArt.Tick; } }

    class Sentry
    {
        public bool on;
        public int stage;   // 0 waiting to launch, 1 flying, 2 unfolding, 3 firing, 4 folding
        public AttackRun run;
        public AttackSprite sprite;
        public Vector3 from, to;
        public float t, delay, fireClock, life, rate, weight, reach;
    }

    class Bolt
    {
        public bool on;
        public AttackRun run;
        public AttackSprite sprite;
        public GameObject target;
        public ClearTarget clear;
        public Vector3 from;
        public float t, dur, weight;
    }

    Sentry[] sentries;
    Bolt[] bolts;
    public int SentriesNow { get { int n = 0; if (sentries != null) for (int i = 0; i < sentries.Length; i++) if (sentries[i].on) n++; return n; } }
    public int SentryBolts { get; private set; }

    // Where sentry i of n hovers: spread across the screen, 3-5 u above the ship, centre ones higher.
    public static Vector3 SentryPoint(int i, int n, Vector3 shipPos, Rect view)
    {
        float k = i - (n - 1) * .5f;
        float spacing = Mathf.Min(SentrySpacing, view.width * .3f);
        float cx = view.width > 1e4f ? shipPos.x : view.center.x;
        float x = Mathf.Clamp(cx + k * spacing, view.xMin + .8f, view.xMax - .8f);
        float edge = 1f - Mathf.Abs(k) / Mathf.Max(1f, (n - 1) * .5f);   // 1 centre, 0 outermost
        float y = Mathf.Min(shipPos.y + 3.4f + 1.4f * edge, view.yMax - 1.2f);
        return new Vector3(x, y, 0f);
    }

    void StartSentries(AttackRun run, Vector3 nose)
    {
        if (sentries == null)
        {
            sentries = new Sentry[MaxSentries];
            for (int i = 0; i < sentries.Length; i++) sentries[i] = new Sentry();
            bolts = new Bolt[MaxBolts];
            for (int i = 0; i < bolts.Length; i++) bolts[i] = new Bolt();
        }
        var l = run.loadout;
        int n = Mathf.Clamp(l.shots, 1, 4);
        float life = l.duration * (run.quick ? SentryQuickShare : 1f);
        float rate = Mathf.Max(.5f, l.speed);
        run.budget = run.quick ? 1f : n;   // an elite takes about one hit from each sentry (a free shot: one in all)
        Rect view = ShipTargets.View();
        int made = 0;
        for (int i = 0; i < sentries.Length && made < n; i++)
        {
            var s = sentries[i];
            if (s.on) continue;
            s.on = true;
            s.stage = 0;
            s.run = run;
            s.sprite = null;
            s.from = nose;
            s.to = SentryPoint(made, n, transform.position, view);
            s.t = 0f;
            s.delay = made * SentryStagger;
            s.fireClock = 0f;
            s.life = life;
            s.rate = rate;
            s.weight = (run.quick ? 1f / n : 1f) / (rate * life);
            s.reach = l.range;
            made++;
        }
        if (made == 0) run.Stop();
    }

    void StepSentriesRun(AttackRun run)
    {
        // the run lives as long as its sentries; the slow motion ends once they are all deployed
        bool any = false, allDeployed = true;
        for (int i = 0; i < sentries.Length; i++)
        {
            var s = sentries[i];
            if (!s.on || s.run != run) continue;
            any = true;
            if (s.stage < 3) allDeployed = false;
        }
        if (allDeployed) run.slowDone = true;
        if (!any) { run.slowDone = true; run.Stop(); }
    }

    void StepSentries(float dt)
    {
        for (int i = 0; i < sentries.Length; i++)
        {
            var s = sentries[i];
            if (!s.on) continue;
            s.t += dt;
            switch (s.stage)
            {
                case 0:
                    if (s.t < s.delay) break;
                    s.stage = 1;
                    s.t = 0f;
                    s.sprite = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, s.from, Vector3.one * .9f, 0f,
                        ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 68).Owned(s.run).Travel(s.to, SentryFlightSeconds, .4f);
                    break;
                case 1:
                    if (s.t < SentryFlightSeconds) break;
                    s.stage = 2;
                    s.t = 0f;
                    if (s.sprite != null && s.sprite.Active) s.sprite.Stop();
                    s.sprite = AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, s.to, Vector3.one * .9f, 0f,
                        DeployTicks, false, 0f, null, Vector3.zero, 68).Owned(s.run);
                    break;
                case 2:
                    if (s.t < DeploySeconds) break;
                    s.stage = 3;
                    s.t = 0f;
                    break;
                case 3:
                    s.fireClock += dt;
                    float gap = 1f / s.rate;
                    while (s.fireClock >= gap)
                    {
                        s.fireClock -= gap;
                        FireBolt(s);
                    }
                    if (s.t < s.life) break;
                    s.stage = 4;
                    s.t = 0f;
                    if (s.sprite != null && s.sprite.Active) s.sprite.Stop();
                    s.sprite = AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, s.to, Vector3.one * .9f, 0f,
                        FoldTicks, false, 0f, null, Vector3.zero, 68).Reverse().Owned(s.run);
                    break;
                default:
                    if (s.t < DeploySeconds) break;
                    if (s.sprite != null && s.sprite.Active) s.sprite.Stop();
                    s.sprite = null;
                    s.on = false;
                    break;
            }
        }
    }

    public const float SentryBurstAlphaK = .5f;   // a sentry bolt's impact burst is half as opaque (they fire all the time)

    void FireBolt(Sentry s)
    {
        Vector3 muzzle = s.to + Vector3.up * .35f;
        var t = ShipTargets.NearestHoming(muzzle, s.reach, null, true);   // real bodies only, never backdrop rocks
        if (t == null) return;
        Bolt b = null;
        for (int i = 0; i < bolts.Length; i++) if (!bolts[i].on) { b = bolts[i]; break; }
        if (b == null) return;
        Vector3 to = t.transform.position;
        b.on = true;
        b.run = s.run;
        b.target = t.gameObject;
        b.clear = t;
        b.from = muzzle;
        b.t = 0f;
        b.dur = Mathf.Max(.05f, Vector2.Distance(muzzle, to) / SentryBoltSpeed);
        b.weight = s.weight;
        b.sprite = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, muzzle, Vector3.one * .7f, BoltAngle(muzzle, to),
            ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 70).Strip("attack_shot", 0, true);
        SentryBolts++;
    }

    static float BoltAngle(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;   // the drawing points up
    }

    void StepBolts(float dt)
    {
        for (int i = 0; i < bolts.Length; i++)
        {
            var b = bolts[i];
            if (!b.on) continue;
            if (b.target == null || b.clear == null || !b.clear.isActiveAndEnabled)
            {
                // its mark died on the way: the bolt fizzles
                EndBolt(b);
                continue;
            }
            b.t += dt;
            Vector3 to = b.target.transform.position;
            float k = Mathf.Clamp01(b.t / b.dur);
            if (b.sprite != null && b.sprite.Active)
            {
                b.sprite.Body.position = Vector3.Lerp(b.from, to, k);
                b.sprite.Body.rotation = Quaternion.Euler(0f, 0f, BoltAngle(b.from, to));
            }
            if (k < 1f) continue;
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, to, Vector3.one * .45f, 0f,
                ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71).Strip("attack_shot", 8, false)
                .Alpha(ShipFxArt.AttackBurstAlpha * SentryBurstAlphaK);
            var go = b.target;
            var run = b.run;
            float w = b.weight;
            EndBolt(b);
            ShipAttackHits.Hit(go, ship, w, ref run.budget);
        }
    }

    void EndBolt(Bolt b)
    {
        if (b.sprite != null && b.sprite.Active) b.sprite.Stop();
        b.sprite = null;
        b.target = null;
        b.clear = null;
        b.on = false;
    }

    // ================================================================ NUKE
    //
    // A warhead leaves the nose and flies up to ~55% of the screen height (or towards the densest cluster),
    // blinks its fuse (l.duration), detonates: a core flash, a red shockwave ring and a mushroom cloud; the ring grows to cover the
    // screen and kills every non-boss target as the ring reaches it. The boss and elites take one hit each.
    // The premium hull's timed mini-warheads (FireMiniWarheads) are the
    // same thing small: straight ahead, detonating at MiniRange with a MiniRadius shockwave.

    public const float MiniRange = 4f, MiniRadius = 1.4f, MiniSpeed = 16f, MiniStagger = .12f, MiniSpread = .35f;
    public const int MaxWarheads = 8;
    // Every warhead, big or small, is SEEN falling nose-down onto its impact point for this long before it
    // detonates (damage lands on the detonation). Independent of the loadout's speed so it is never a blink.
    public const float FallSeconds = .4f, MiniFallSeconds = .3f, FallHeight = 3.2f, MiniFallHeight = 1.6f;
    public const float FallRotation = 180f;   // the warhead drawing points up; turned over it falls nose first
    static readonly int[] ShockTicks = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };   // all 16 cells, one animation
    // Mean radius of the shockwave drawing per cell, as a share of its full radius (measured from the art).
    static readonly float[] ShockRadius =
        { .13f, .18f, .27f, .36f, .50f, .59f, .68f, .70f, .80f, .84f, .90f, .95f, .96f, .97f, 1f, 1f };
    const float ShockArtRadius = .39f;
    public const float MushroomSize = 3.6f, MushroomAlpha = .8f;
    const int MushroomFadeCells = 3;   // the last cell still has faint smoke: fade to nothing over the last three   // the drawing's full ring radius in world units at scale 1

    public static float ShockFrac(float seconds)
    {
        float f = Mathf.Clamp(seconds / WeaponArt.Tick, 0f, ShockRadius.Length - 1f);
        int a = (int)f;
        int b = Mathf.Min(a + 1, ShockRadius.Length - 1);
        return Mathf.Lerp(ShockRadius[a], ShockRadius[b], f - a);
    }

    public static float ShockSeconds { get { return WeaponArt.Seconds(ShockTicks); } }

    class Warhead
    {
        public bool on, mini;
        public int stage;   // 0 waiting, 1 flying, 2 fuse, 3 shockwave
        public AttackRun run;
        public AttackSprite sprite, ring, mush;
        public Vector3 from, to, center;
        public bool fell;
        public float t, delay, flight, fuse, maxRadius, weight;
        public readonly List<GameObject> hit = new List<GameObject>(24);
    }

    Warhead[] warheads;
    public int Detonations { get; private set; }
    public int MiniDetonations { get; private set; }
    public int WarheadFalls { get; private set; }   // falling-warhead sprites spawned (every detonation has one first)
    public int FallsShown { get; private set; }     // detonations that were preceded by a visible fall
    public int WarheadsNow { get { int n = 0; if (warheads != null) for (int i = 0; i < warheads.Length; i++) if (warheads[i].on) n++; return n; } }

    Warhead FreeWarhead()
    {
        if (warheads == null)
        {
            warheads = new Warhead[MaxWarheads];
            for (int i = 0; i < warheads.Length; i++) warheads[i] = new Warhead();
        }
        for (int i = 0; i < warheads.Length; i++) if (!warheads[i].on) return warheads[i];
        return null;
    }

    void StartNuke(AttackRun run, Vector3 nose)
    {
        var l = run.loadout;
        var w = FreeWarhead();
        if (w == null) { run.Stop(); return; }
        Rect view = ShipTargets.View();
        float topY = view.width > 1e4f ? nose.y + 6f : Mathf.Lerp(view.yMin, view.yMax, l.range);
        topY = Mathf.Max(topY, nose.y + 2f);
        float x = nose.x;
        // the densest cluster, if there is one worth the trip
        ShipTargets.Collect(targets);
        int best = 1;
        for (int i = 0; i < targets.Count; i++)
        {
            var a = targets[i];
            if (a == null || !a.isActiveAndEnabled) continue;
            if (ShipTargets.IsBossTarget(a)) { x = a.transform.position.x; best = int.MaxValue; break; }
            int n = 0;
            for (int j = 0; j < targets.Count; j++)
                if (targets[j] != null && ((Vector2)(targets[j].transform.position - a.transform.position)).sqrMagnitude < 2.5f * 2.5f) n++;
            if (n > best) { best = n; x = a.transform.position.x; }
        }
        w.on = true;
        w.mini = false;
        w.stage = 1;
        w.run = run;
        w.to = new Vector3(Mathf.Clamp(x, view.xMin + .8f, view.xMax - .8f), topY, 0f);
        // the bomb drops in from above the impact point (the muzzle puff below marks the launch at the nose)
        w.from = w.to + Vector3.up * (view.width > 1e4f ? FallHeight : Mathf.Min(FallHeight + 2.5f, Mathf.Max(FallHeight, view.yMax + .6f - topY)));
        w.t = 0f;
        w.delay = 0f;
        w.flight = FallSeconds;
        w.fell = false;
        w.fuse = l.duration * (run.quick ? .7f : 1f);
        w.weight = 1f;
        w.hit.Clear();
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, nose, Vector3.one * .4f, 0f,
            ShipFxArt.BurstTicks, false, 0f, transform, Vector3.up * noseOffset, 71);
        SpawnFalling(w, .6f);
    }

    // The falling warhead drawing (nose down), at its start point. The one place a warhead sprite is made.
    void SpawnFalling(Warhead w, float size)
    {
        w.sprite = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, w.from, Vector3.one * size, FallRotation,
            ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 69).Owned(w.run);
        w.fell = true;
        WarheadFalls++;
    }

    void StepNukeRun(AttackRun run)
    {
        if (warheads != null)
            for (int i = 0; i < warheads.Length; i++) if (warheads[i].on && warheads[i].run == run) return;
        run.slowDone = true;
        run.Stop();
    }

    // A premium hull's timed mini-warheads (ParagonMissiles): `count` small warheads, one after another and a
    // little apart, straight ahead from the nose; each detonates at MiniRange with a MiniRadius shockwave.
    // Their own run (budget, hit lists), never one of the attack slots. Returns how many left; 0 (and nothing
    // launched) when there is nothing on screen to hit.
    public int MiniVolleys { get; private set; }
    AttackRun miniRun;

    public int FireMiniWarheads(int count)
    {
        if (count <= 0) return 0;
        if (runs[0] == null) Setup(ship);
        ShipTargets.Collect(targets);
        if (targets.Count == 0) return 0;
        if (miniRun == null) miniRun = new AttackRun();
        miniRun.Begin(loadout, ship, Time.unscaledTime);
        miniRun.active = false;
        Vector3 nose = Nose;
        int launched = 0;
        for (int i = 0; i < count; i++)
        {
            var w = FreeWarhead();
            if (w == null) break;
            Vector3 from = nose + Vector3.right * ((i - (count - 1) * .5f) * MiniSpread);
            w.on = true;
            w.mini = true;
            w.stage = 0;
            w.run = miniRun;
            w.to = from + Vector3.up * MiniRange;
            w.from = w.to + Vector3.up * MiniFallHeight;
            w.fell = false;
            w.t = 0f;
            w.delay = i * MiniStagger;
            w.flight = MiniFallSeconds;
            w.fuse = 0f;
            w.weight = 1f / count;
            w.maxRadius = MiniRadius;
            w.hit.Clear();
            w.sprite = null;
            launched++;
        }
        if (launched > 0)
        {
            MiniVolleys++;
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, nose, Vector3.one * .4f, 0f,
                ShipFxArt.BurstTicks, false, 0f, transform, Vector3.up * noseOffset, 71);
        }
        return launched;
    }

    void StepWarheads(float dt)
    {
        for (int i = 0; i < warheads.Length; i++)
        {
            var w = warheads[i];
            if (!w.on) continue;
            w.t += dt;
            switch (w.stage)
            {
                case 0:
                    if (w.t < w.delay) break;
                    w.stage = 1;
                    w.t = 0f;
                    SpawnFalling(w, .3f);
                    break;
                case 1:
                    {
                        float k = Mathf.Clamp01(w.t / w.flight);
                        float e = k * k;   // gravity: it speeds up as it drops
                        Vector3 p = Vector3.Lerp(w.from, w.to, e);
                        if (w.sprite == null && !w.fell) SpawnFalling(w, w.mini ? .3f : .6f);   // never skip the fall
                        if (w.sprite != null && w.sprite.Active) w.sprite.Body.position = p;
                        if (k < 1f) break;
                        w.center = w.to;
                        w.t = 0f;
                        if (w.fuse > 0f) w.stage = 2; else Detonate(w);
                    }
                    break;
                case 2:
                    // the fuse: the warhead blinks, faster as it burns down
                    if (w.sprite != null && w.sprite.Active)
                    {
                        float rate = Mathf.Lerp(8f, 22f, w.t / w.fuse);
                        w.sprite.SetColor(((int)(w.t * rate) & 1) == 0 ? Color.white : new Color(1f, 1f, 1f, .3f));
                    }
                    if (w.t >= w.fuse) Detonate(w);
                    break;
                default:
                    StepShockwave(w);
                    break;
            }
        }
    }

    void Detonate(Warhead w)
    {
        if (!w.fell) SpawnFalling(w, w.mini ? .3f : .6f);   // belt and braces: no detonation without a fall
        FallsShown++;
        if (w.sprite != null && w.sprite.Active) w.sprite.Stop();
        w.sprite = null;
        w.stage = 3;
        w.t = 0f;
        if (!w.mini) w.maxRadius = FarthestCorner(w.center);
        float scale = w.maxRadius / ShockArtRadius;
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, w.center, Vector3.one * (w.mini ? .7f : 1.6f), 0f,
            ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        w.ring = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, w.center, Vector3.one * scale, 0f,
            ShockTicks, false, 0f, null, Vector3.zero, 70).Strip("attack_shockwave", 0, true);
        w.mush = null;
        if (!w.mini)
        {
            // the mushroom cloud: the one deliberate exception to the restraint rule (the cheat ship). Its stem
            // base is the bottom-centre of the cell, so it stands on the detonation point; behind enemies and
            // shots (their sorting orders start at 2) and at 0.8 alpha, so it never hides a threat.
            w.mush = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, w.center + Vector3.up * MushroomSize * .5f,
                Vector3.one * MushroomSize, 0f, ShockTicks, false, 0f, null, Vector3.zero, 1)
                .Strip("attack_mushroom", 0, false).Alpha(MushroomAlpha);
        }
        if (w.mini) MiniDetonations++;
        else
        {
            Detonations++;
            CameraKick.Kick(.04f);
        }
    }

    void StepShockwave(Warhead w)
    {
        float r = w.maxRadius * ShockFrac(w.t);
        if (!w.mini) HostileShots.ClearWithin(w.center, r);
        if (w.mush != null && w.mush.Active)
        {
            int cell = Mathf.Min(ShockTicks.Length - 1, (int)(w.t / WeaponArt.Tick));
            float k = Mathf.Clamp01((ShockTicks.Length - cell) / (float)(MushroomFadeCells + 1));
            w.mush.SetColor(new Color(1f, 1f, 1f, k));
        }
        ShipTargets.Collect(targets);
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled || w.hit.Contains(t.gameObject)) continue;
            Vector2 dv = t.transform.position - w.center;
            float reach = r + t.Radius * .5f;
            if (dv.sqrMagnitude > reach * reach) continue;
            HitOnce(w.hit, t.gameObject, w.weight);
        }
        if (w.t < ShockSeconds) return;
        if (w.ring != null && w.ring.Active) w.ring.Stop();
        if (w.mush != null && w.mush.Active) w.mush.Stop();
        w.ring = w.mush = null;
        w.on = false;
    }
}

// A hazard held frozen: its movers, its brain (so it neither flies nor shoots) and its tumble are switched
// off and switched back on by Thaw(), so the scripts are suspended, not broken. Reusable by anything that
// wants a "frozen" state (Frost Nova today). Safe by construction:
//   * destroyed or despawned while frozen: nothing to restore (the components went with it);
//   * the freezer vanishing (the ship died, the scene changed): a failsafe thaws it after FailSafeSeconds of
//     gameplay time, so nothing can stay frozen for good.
// Bosses and elites are never frozen (they take a hit instead): Freeze refuses an IShipAttackTarget.
[DisallowMultipleComponent]
public class FrozenState : MonoBehaviour
{
    public const float FailSafeSeconds = 4f;

    readonly Behaviour[] held = new Behaviour[6];
    int heldCount;
    float left;

    public bool IsFrozen { get; private set; }

    public static bool CanFreeze(GameObject go)
    {
        return go != null && go.GetComponent<IShipAttackTarget>() == null;
    }

    // Freezes `go` (null if it can't be). Freezing a frozen hazard just refreshes the failsafe.
    public static FrozenState Freeze(GameObject go, float failSafe = FailSafeSeconds)
    {
        if (!CanFreeze(go)) return null;
        var st = go.GetComponent<FrozenState>();
        if (st == null) st = go.AddComponent<FrozenState>();
        st.Hold(failSafe);
        return st;
    }

    void Hold(float failSafe)
    {
        left = failSafe;
        if (IsFrozen) return;
        IsFrozen = true;
        heldCount = HazardMovers.Disable(gameObject, held, 0);
        heldCount = Suspend(GetComponent<EnemyBrain>(), heldCount);
        heldCount = Suspend(GetComponent<AsteroidSpin>(), heldCount);
    }

    int Suspend(Behaviour b, int count)
    {
        if (b == null || !b.enabled || count >= held.Length) return count;
        b.enabled = false;
        held[count] = b;
        return count + 1;
    }

    // Switches everything it held back on (a component that has since been destroyed is skipped).
    public void Thaw()
    {
        for (int i = 0; i < heldCount; i++)
        {
            if (held[i] != null) held[i].enabled = true;
            held[i] = null;
        }
        heldCount = 0;
        IsFrozen = false;
    }

    void Update()
    {
        if (!IsFrozen) return;
        left -= ShipAttackRunner.ScaledDelta();
        if (left <= 0f) Thaw();
    }
}
