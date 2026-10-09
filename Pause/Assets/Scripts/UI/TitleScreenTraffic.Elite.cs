using UnityEngine;

// Home-screen traffic, part 4: the elite sniper.
//
// Every eliteInterval seconds or so (36-46 s, the first after a calm start
// of EliteFirst) an enemy elite -- a random one of the real elites
// (EliteCatalog's defs, drawn from EliteArt's strip in its own cells, with
// its own exhaust plumes and engine lights, its shot and engine colours) --
// streaks in from off-screen and pulls up at a perch clear of the menu.
// Then it snipes every ship on the screen, one after another, any depth:
// it turns onto a mark, paints it (a blinking sight line from its muzzle
// and a lock-on ring closing on the target, its tell cell or a charge glow
// at the muzzle), fires (its action cell or a muzzle flash) and a fast
// shot in its own colours runs the mark down -- a sniper doesn't miss. The
// hit is the home screen's usual shoot-down: an impact flash, then the
// hull's wreck fragments slam into the screen-edge walls (or, past the
// wreck cap, the crash explosion and debris). When nothing is left on the
// screen it zooms away (a brief back-off, then hard acceleration with its
// plumes flared). About EliteReturnDelay later the traffic starts coming
// back, quickly at first (EliteRefillDelay instead of respawnDelay) until
// every layer's cruiser target is met again.
//
// While it is on (entry to the return) nothing else starts: no spawns,
// zoomers, crashes, pursuits, logo dives or ultimates, and it only starts
// once no dive or ultimate is under way. Ships that come into view while
// it is perched are sniped too; the rest ease off (EliteSpookSpeed) and
// start no boosts, zips or tricks while it hunts; any still off-screen when it leaves are
// quietly sent back to the pool, so the sky really is clear for a beat.
// With nothing on the screen it just flies by.
//
// Positions are kept as fractions of the view, so a rotation or a new
// aspect / safe area mid-event just moves the perch with the screen.
// Purely cosmetic like the rest of the traffic: no gameplay elite object,
// brain, attack or director is created, nothing scores, pays out or counts
// towards anything; it runs on the traffic's own unscaled clock (frozen
// timeScale on the menu doesn't stop it, a backgrounded app resumes where
// it was), every renderer is pooled up front under the traffic object
// (Ignore Raycast), and Shutdown / leaving the scene drops it with
// everything else. The elite strip is loaded when the next event is
// picked (one load, counted as heavy SkinWork), never mid-event.
public partial class TitleScreenTraffic
{
    public enum ElitePhase { None, Enter, Snipe, Exit, Return }

    [Tooltip("Seconds between elite snipe runs (start to start).")]
    public Vector2 eliteInterval = new Vector2(36f, 46f);

    [Tooltip("Seconds of calm before the first elite run.")]
    public Vector2 eliteFirst = new Vector2(28f, 36f);

    public const float EliteScale = .8f;            // x the def's cell size
    public const float EliteEnterTime = 1.1f;       // off-screen -> perch
    public const float EliteSnipeBudget = 3f;     // seconds of aiming for a full sky
    public const float EliteAimMin = .17f, EliteAimMax = .4f;   // per mark
    public const float EliteFireHold = .1f;         // action cell / muzzle flash after a shot
    public const float EliteShotSpeed = 24f;        // world units per second
    public const float EliteClearHold = .35f;       // empty sky this long -> it leaves
    public const float EliteSnipeMax = 9f;          // never perches longer than this
    public const float EliteBackOff = .2f;          // exit anticipation
    public const float EliteExitAccel = 34f;        // world units / s^2 on the way out
    public const float EliteExitMax = 2.5f;         // seconds, safety
    public const float EliteReturnDelay = 1f;       // gone -> traffic starts back
    public const float EliteRefillWindow = 3f;      // fast respawns for this long after
    public static readonly Vector2 EliteRefillDelay = new Vector2(.06f, .16f);
    public const float EliteRetry = .5f;            // busy (a dive / ultimate): try again in
    public const float EliteSpookSpeed = .4f;       // ships ease off while it hunts (so they don't just fly out of its sights)
    public const int EliteShotPool = 4;
    public const int EliteSort = 104;               // over the front band and its fx (95..99)
    const float EliteTurnRate = 720f;                // degrees/s it swings onto a mark

    class EliteShot
    {
        public SpriteRenderer body, core;
        public bool on;
        public Vector2 pos, dir, last;
        public Flyer target;
        public float age;
    }

    // ---- state
    ElitePhase elitePhase;
    float eliteT, nextEliteAt, eliteReturnAt, refillUntil = -1f, eliteStartedAt;
    EliteDef eliteDef, nextEliteDef;
    Sprite[] eliteFrames;
    Vector2 elitePos, eliteVel, eliteStartUV, elitePerchUV, eliteCtrlUV, eliteExitDir;
    float eliteFacing, eliteSide, eliteFire, eliteBob, eliteAimDur, eliteClearT;
    Flyer eliteAim;
    float eliteAimT;
    int eliteNozzles;

    // ---- pooled drawings
    Transform eliteRoot, eliteHullTf;
    SpriteRenderer eliteHull, eliteMuzzle, eliteSight, eliteReticle;
    SpriteRenderer[] elitePlumes = new SpriteRenderer[0], eliteGlows = new SpriteRenderer[0];
    readonly EliteShot[] eliteShots = new EliteShot[EliteShotPool];
    EliteDef[] eliteDefs = new EliteDef[0];

    // ---- stats, read by the tests
    public int EliteEvents { get; private set; }
    public int EliteSnipes { get; private set; }
    public int EliteShotsFired { get; private set; }
    public int EliteFlybys { get; private set; }
    public int EliteStragglers { get; private set; }
    public float LastEliteStartAt { get; private set; } = -1f;
    public float LastEliteGoneAt { get; private set; } = -1f;
    public float LastEliteReturnAt { get; private set; } = -1f;
    public int LastEliteOnScreen { get; private set; }
    public ElitePhase ElitePhaseNow => elitePhase;
    public bool EliteBusy => elitePhase != ElitePhase.None;
    public bool EliteRefilling => elitePhase == ElitePhase.None && now < refillUntil;
    public EliteDef EliteDefNow => eliteDef;
    public EliteDef NextEliteDef => nextEliteDef;
    public Vector2 ElitePosition => elitePos;
    public bool EliteVisible => eliteRoot != null && eliteRoot.gameObject.activeSelf && eliteHull.enabled;
    public bool EliteSightShown => eliteSight != null && eliteSight.enabled;
    public float NextEliteAt { get => nextEliteAt; set => nextEliteAt = value; }
    public int EliteDefCount => eliteDefs.Length;

    public int ActiveEliteShots
    {
        get { int n = 0; for (int i = 0; i < EliteShotPool; i++) if (eliteShots[i].on) n++; return n; }
    }

    // Ships an elite would snipe right now: in the air and on the screen.
    public int ShipsOnScreen
    {
        get { int n = 0; for (int i = 0; i < pool.Length; i++) if (OnScreen(pool[i])) n++; return n; }
    }

    bool OnScreen(Flyer f)
    {
        return f != null && f.active && Overlaps(view, f.pos, -.15f);
    }

    // Close enough to the screen to be marked (a hull half out still shows).
    bool InReach(Flyer f)
    {
        return f != null && f.active && Overlaps(view, f.pos, .25f);
    }

    // ------------------------------------------------------------- setup

    void BuildElite()
    {
        // every elite def (tiny JSON); a strip is only loaded when its run
        // is picked (PickNextElite), so they never all sit in memory at once
        var all = EliteCatalog.All;
        int n = 0, maxNoz = 0;
        for (int i = 0; i < all.Length; i++) if (all[i] != null) n++;
        eliteDefs = new EliteDef[n];
        n = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            eliteDefs[n++] = all[i];
            maxNoz = Mathf.Max(maxNoz, all[i].nozzles.Length);
        }

        var root = new GameObject("~elite");
        root.layer = 2;
        root.transform.SetParent(transform, false);
        eliteRoot = root.transform;
        eliteHull = Child(eliteRoot, "~elite_hull", null, EliteSort);
        eliteHullTf = eliteHull.transform;
        elitePlumes = new SpriteRenderer[maxNoz];
        eliteGlows = new SpriteRenderer[maxNoz];
        for (int i = 0; i < maxNoz; i++)
        {
            elitePlumes[i] = Child(eliteHullTf, "~elite_plume", null, EliteSort - 2);
            eliteGlows[i] = Child(eliteHullTf, "~elite_glow", EliteFxArt.Glow, EliteSort + 1);
        }
        eliteMuzzle = Child(eliteRoot, "~elite_muzzle", EliteFxArt.Glow, EliteSort + 2);
        eliteSight = Child(eliteRoot, "~elite_sight", WeaponFx.Solid, EliteSort - 1);
        eliteReticle = Child(eliteRoot, "~elite_lock", EliteFxArt.Ring, EliteSort + 3);
        for (int i = 0; i < EliteShotPool; i++)
            eliteShots[i] = new EliteShot
            {
                body = Child(eliteRoot, "~elite_shot", EliteFxArt.Bolt, EliteSort + 3),
                core = Child(eliteRoot, "~elite_shot", EliteFxArt.Bolt, EliteSort + 4),
            };
        if (spriteMaterial != null)
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true)) sr.sharedMaterial = spriteMaterial;
        root.SetActive(false);
    }

    SpriteRenderer Child(Transform parent, string name, Sprite sprite, int order)
    {
        var sr = Renderer(name, sprite);
        sr.transform.SetParent(parent, false);
        sr.sortingOrder = order;
        return sr;
    }

    void StartElite()
    {
        elitePhase = ElitePhase.None;
        nextEliteAt = Random.Range(eliteFirst.x, eliteFirst.y);
        PickNextElite(true);
    }

    // Choose the next event's elite and load its strip now (one heavy
    // step, long before it flies), so the event itself never loads art.
    void PickNextElite(bool force)
    {
        if (eliteDefs.Length == 0) { nextEliteDef = null; return; }
        if (SkinWorkPaused && !force && nextEliteDef != null) return;   // keep what's loaded
        var def = eliteDefs[Random.Range(0, eliteDefs.Length)];
        if (EliteArt.Frames(def) == null) def = nextEliteDef;
        nextEliteDef = def;
        SkinWork++;
    }

    // ------------------------------------------------------------- loop

    void TickElite(float dt)
    {
        switch (elitePhase)
        {
            case ElitePhase.None:
                if (now >= nextEliteAt) TryStartElite();
                break;
            case ElitePhase.Enter: EliteEnter(dt); break;
            case ElitePhase.Snipe: EliteSnipe(dt); break;
            case ElitePhase.Exit: EliteExit(dt); break;
            case ElitePhase.Return:
                if (now >= eliteReturnAt) EndElite();
                break;
        }
        TickEliteShots(dt);
        DrawElite(dt);
    }

    // Starts a run now if nothing else is going on (tests call it too).
    public bool TryStartElite()
    {
        if (elitePhase != ElitePhase.None) return false;
        if (plunger != null || ActiveUlts > 0) { nextEliteAt = now + EliteRetry; return false; }
        var def = nextEliteDef;
        var frames = def != null ? EliteArt.Frames(def) : null;
        if (frames == null) { nextEliteAt = now + Random.Range(eliteInterval.x, eliteInterval.y); return false; }

        eliteDef = def;
        eliteFrames = frames;
        eliteNozzles = Mathf.Min(def.nozzles.Length, elitePlumes.Length);
        EndPursuit();
        EliteEvents++;
        eliteStartedAt = now;
        LastEliteStartAt = now;
        LastEliteOnScreen = ShipsOnScreen;
        refillUntil = -1f;

        // a perch on one side, clear of the menu column (and the logo if it can)
        eliteSide = Random.value < .5f ? -1f : 1f;
        Vector2 perch = safe.center;
        for (int tries = 0; tries < 12; tries++)
        {
            float u = .5f + eliteSide * Random.Range(.2f, .3f);
            float v = Random.Range(.38f, .72f);
            perch = new Vector2(safe.xMin + safe.width * u, safe.yMin + safe.height * v);
            if (tries < 10 && Overlaps(menu, perch, .55f)) continue;
            if (tries < 6 && Overlaps(logo, perch, .3f)) continue;
            break;
        }
        float m = def.cellWorldSize * EliteScale + .6f;
        Vector2 start = new Vector2(eliteSide > 0f ? view.xMax + m : view.xMin - m, perch.y + Random.Range(.8f, 1.8f));
        Vector2 ctrl = new Vector2(Mathf.Lerp(start.x, perch.x, .55f), perch.y + Random.Range(-.6f, .2f));
        elitePerchUV = ToUV(perch);
        eliteStartUV = ToUV(start);
        eliteCtrlUV = ToUV(ctrl);
        elitePos = start;
        eliteVel = Vector2.zero;
        eliteFacing = Mathf.Atan2(perch.y - start.y, perch.x - start.x) * Mathf.Rad2Deg;
        eliteFire = 0f;
        eliteAim = null;
        eliteAimT = 0f;
        eliteClearT = 0f;
        eliteBob = Random.Range(0f, 6.283f);

        elitePhase = ElitePhase.Enter;
        eliteT = 0f;
        ApplyEliteLook();
        eliteRoot.gameObject.SetActive(true);
        DrawElite(0f);
        return true;
    }

    Vector2 ToUV(Vector2 p) { return new Vector2((p.x - view.x) / view.width, (p.y - view.y) / view.height); }
    Vector2 FromUV(Vector2 uv) { return new Vector2(view.x + uv.x * view.width, view.y + uv.y * view.height); }

    void EliteEnter(float dt)
    {
        eliteT += dt;
        float g = Mathf.Clamp01(eliteT / EliteEnterTime);
        float e = DockTween.OutCubic(g);
        Vector2 p0 = FromUV(eliteStartUV), p1 = FromUV(eliteCtrlUV), p2 = FromUV(elitePerchUV);
        Vector2 prev = elitePos;
        elitePos = (Vector2)DockTween.Bezier(p0, p1, p2, e);
        eliteVel = dt > 0f ? (elitePos - prev) / dt : Vector2.zero;
        if (g < .92f && eliteVel.sqrMagnitude > .01f) TurnElite(Mathf.Atan2(eliteVel.y, eliteVel.x) * Mathf.Rad2Deg, dt);
        if (g >= 1f)
        {
            elitePhase = ElitePhase.Snipe;
            eliteT = 0f;
            int n = ShipsOnScreen;
            eliteAimDur = Mathf.Clamp(EliteSnipeBudget / Mathf.Max(1, n), EliteAimMin, EliteAimMax);
            if (n == 0) EliteFlybys++;
        }
    }

    void EliteSnipe(float dt)
    {
        eliteT += dt;
        // hover at the perch with a little bob
        Vector2 perch = FromUV(elitePerchUV) + new Vector2(0f, Mathf.Sin(now * 2.4f + eliteBob) * .05f);
        Vector2 prev = elitePos;
        elitePos = Vector2.Lerp(elitePos, perch, 1f - Mathf.Exp(-8f * dt));
        eliteVel = dt > 0f ? (elitePos - prev) / dt : Vector2.zero;

        if (eliteAim != null && !InReach(eliteAim)) eliteAim = null;   // flew off: pick again
        if (eliteAim == null && eliteFire <= 0f)
        {
            eliteAim = NextMark();
            eliteAimT = 0f;
        }
        if (eliteAim != null)
        {
            eliteClearT = 0f;
            eliteAimT += dt;
            Vector2 m = EliteMuzzle();
            Vector2 d = eliteAim.pos - m;
            TurnElite(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, dt);
            if (eliteAimT >= eliteAimDur && FireEliteShot(eliteAim)) eliteAim = null;
        }
        else if (ActiveEliteShots == 0 && eliteFire <= 0f) eliteClearT += dt;

        if (eliteClearT >= EliteClearHold || eliteT >= EliteSnipeMax) BeginEliteExit();
    }

    // The next mark: whoever is about to slip off the screen first, then the
    // one it has to turn least for.
    Flyer NextMark()
    {
        Flyer best = null;
        float bestScore = float.MaxValue;
        Vector2 from = elitePos;
        for (int i = 0; i < pool.Length; i++)
        {
            var f = pool[i];
            if (!InReach(f) || f.sniped) continue;
            Vector2 d = f.pos - from;
            float turn = Mathf.Abs(Mathf.DeltaAngle(eliteFacing, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg));
            float edge = Mathf.Min(Mathf.Min(f.pos.x - view.xMin, view.xMax - f.pos.x), Mathf.Min(f.pos.y - view.yMin, view.yMax - f.pos.y));
            Vector2 fwd = Dir(f.heading);
            bool leaving = Vector2.Dot(fwd, f.pos - view.center) > 0f || f.state == State.ZipOut;
            float score = turn * .5f + d.magnitude * 3f + Mathf.Max(0f, edge) * (leaving ? 22f : 45f);
            if (score < bestScore) { bestScore = score; best = f; }
        }
        return best;
    }

    void TurnElite(float wantDeg, float dt)
    {
        eliteFacing = Mathf.MoveTowardsAngle(eliteFacing, wantDeg, EliteTurnRate * dt);
    }

    bool FireEliteShot(Flyer target)
    {
        EliteShot s = null;
        for (int i = 0; i < EliteShotPool; i++) if (!eliteShots[i].on) { s = eliteShots[i]; break; }
        if (s == null) return false;   // all four in the air: hold the aim a moment
        Vector2 m = EliteMuzzle();
        s.on = true;
        s.pos = s.last = m;
        s.target = target;
        s.age = 0f;
        Vector2 d = target.pos - m;
        s.dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up;
        s.body.color = eliteDef.ShotColor;
        s.core.color = eliteDef.ShotCore;
        target.sniped = true;
        eliteFire = EliteFireHold;
        EliteShotsFired++;
        DrawEliteShot(s);
        return true;
    }

    void TickEliteShots(float dt)
    {
        for (int i = 0; i < EliteShotPool; i++)
        {
            var s = eliteShots[i];
            if (!s.on) continue;
            s.age += dt;
            var t = s.target;
            if (t != null && (!t.active || !t.sniped)) t = s.target = null;
            Vector2 goal = t != null ? t.pos : s.last + s.dir * 30f;
            Vector2 to = goal - s.pos;
            float step = EliteShotSpeed * dt;
            float dist = to.magnitude;
            if (dist > 1e-5f) s.dir = to / dist;
            if (t != null && dist <= step + t.radius * .5f)
            {
                s.pos = t.pos;
                Snipe(t, s.dir);
                StopEliteShot(s);
                continue;
            }
            s.last = s.pos;
            s.pos += s.dir * Mathf.Min(step, dist + 1f);
            if (!Overlaps(view, s.pos, 1f) || s.age > 1.5f)
            {
                if (t != null) t.sniped = false;
                StopEliteShot(s);
                continue;
            }
            DrawEliteShot(s);
        }
    }

    void StopEliteShot(EliteShot s)
    {
        s.on = false;
        s.target = null;
        s.body.enabled = false;
        s.core.enabled = false;
    }

    // The home screen's shoot-down, by the elite: impact flash, then the
    // wreck into the walls (or the crash explosion past the wreck cap).
    void Snipe(Flyer victim, Vector2 dir)
    {
        if (victim == null || !victim.active) return;
        EliteSnipes++;
        var spec = Depths[(int)victim.layer];
        PlayFx(FlipbookFx.Mode.WeaponImpact, victim.pos, .75f * victim.scale, victim.id, spec.fxTint, spec.fxSort + 4);
        if (victim.state == State.Plunge && plunger == victim) plunger = null;
        if (pursuerA == victim || pursuerB == victim) EndPursuit();
        victim.state = State.Cruise;
        if (!Wreck(victim, dir)) Explode(victim, .9f);
        Retire(victim);
    }

    void BeginEliteExit()
    {
        eliteAim = null;
        elitePhase = ElitePhase.Exit;
        eliteT = 0f;
        // out over the near side, rising
        eliteExitDir = new Vector2(eliteSide, Random.Range(.35f, .75f)).normalized;
        eliteVel = Vector2.zero;
    }

    void EliteExit(float dt)
    {
        eliteT += dt;
        if (eliteT < EliteBackOff)
        {
            // a held back-off, nose already swinging to the way out
            TurnElite(Mathf.Atan2(eliteExitDir.y, eliteExitDir.x) * Mathf.Rad2Deg, dt);
            elitePos -= eliteExitDir * (.6f * dt);
        }
        else
        {
            TurnElite(Mathf.Atan2(eliteExitDir.y, eliteExitDir.x) * Mathf.Rad2Deg, dt * 2f);
            eliteVel += eliteExitDir * (EliteExitAccel * dt);
            elitePos += eliteVel * dt;
        }
        float m = eliteDef.cellWorldSize * EliteScale + .5f;
        bool gone = elitePos.x < view.xMin - m || elitePos.x > view.xMax + m || elitePos.y < view.yMin - m || elitePos.y > view.yMax + m;
        if (gone || eliteT >= EliteExitMax) EliteGone();
    }

    void EliteGone()
    {
        // stragglers still off-screen go back to the pool unseen: a clear beat
        for (int i = 0; i < pool.Length; i++)
        {
            var f = pool[i];
            if (!f.active || f.state == State.Plunge) continue;
            if (OnScreen(f)) continue;
            EliteStragglers++;
            Retire(f);
        }
        for (int i = 0; i < EliteShotPool; i++)
        {
            if (!eliteShots[i].on) continue;
            if (eliteShots[i].target != null) eliteShots[i].target.sniped = false;
            StopEliteShot(eliteShots[i]);
        }
        HideElite();
        LastEliteGoneAt = now;
        eliteReturnAt = now + EliteReturnDelay;
        elitePhase = ElitePhase.Return;
        eliteT = 0f;
    }

    void EndElite()
    {
        elitePhase = ElitePhase.None;
        LastEliteReturnAt = now;
        refillUntil = now + EliteRefillWindow;
        for (int d = 0; d < 3; d++) nextSpawnAt[d] = now + Random.Range(0f, EliteRefillDelay.y);
        for (int i = 0; i < pool.Length; i++) pool[i].sniped = false;
        nextEliteAt = Mathf.Max(eliteStartedAt + Random.Range(eliteInterval.x, eliteInterval.y), now + eliteInterval.x * .5f);
        nextZoomAt = Mathf.Max(nextZoomAt, now + 2f);
        nextPlungeAt = Mathf.Max(nextPlungeAt, now + 4f);
        PickNextElite(false);
    }

    void HideElite()
    {
        if (eliteRoot != null) eliteRoot.gameObject.SetActive(false);
        if (eliteReticle != null) eliteReticle.enabled = false;
        if (eliteSight != null) eliteSight.enabled = false;
    }

    // Drops a run in progress at once (Shutdown; tests).
    public void CancelElite()
    {
        if (elitePhase == ElitePhase.None) return;
        for (int i = 0; i < EliteShotPool; i++) if (eliteShots[i] != null && eliteShots[i].on) StopEliteShot(eliteShots[i]);
        for (int i = 0; pool != null && i < pool.Length; i++) pool[i].sniped = false;
        HideElite();
        elitePhase = ElitePhase.None;
        eliteAim = null;
    }

    // Cruise speed factor: ships ease off, spooked, while the elite hunts.
    float EliteSpook => elitePhase == ElitePhase.Enter || elitePhase == ElitePhase.Snipe ? EliteSpookSpeed : 1f;

    // Spawning pace while the traffic refills after a run.
    float RespawnGap()
    {
        return EliteRefilling ? Random.Range(EliteRefillDelay.x, EliteRefillDelay.y) : Random.Range(respawnDelay.x, respawnDelay.y);
    }

    // ------------------------------------------------------------- drawing

    float EliteDrawingDeg => eliteDef.turnsToFace ? eliteFacing - eliteDef.noseDeg : EliteBank();

    float EliteBank()
    {
        float side = Mathf.Clamp(eliteVel.x / 3f, -1f, 1f);
        return -side * eliteDef.maxBank;
    }

    Vector2 EliteMuzzle()
    {
        var def = eliteDef;
        Vector2 local = def.muzzles.Length > 0
            ? def.PixelToLocal(def.muzzles[0].x, def.muzzles[0].y)
            : Dir(def.noseDeg * Mathf.Deg2Rad) * def.cellWorldSize * .38f;
        return elitePos + Rotate(local * EliteScale, EliteDrawingDeg * Mathf.Deg2Rad);
    }

    void ApplyEliteLook()
    {
        var def = eliteDef;
        for (int i = 0; i < elitePlumes.Length; i++)
        {
            bool on = i < eliteNozzles;
            elitePlumes[i].enabled = false;
            eliteGlows[i].enabled = on;
            if (!on) continue;
            var nz = def.nozzles[i];
            Vector2 local = def.PixelToLocal(nz.x, nz.y);
            float ang = nz.dir >= 0f ? nz.dir : def.noseDeg + 180f;
            elitePlumes[i].transform.localPosition = local;
            elitePlumes[i].transform.localRotation = Quaternion.Euler(0f, 0f, ang + 90f);
            eliteGlows[i].transform.localPosition = local;
        }
        eliteHull.color = Color.white;
        eliteHull.sprite = eliteFrames[Mathf.Clamp(def.cells.Flight0, 0, eliteFrames.Length - 1)];
        eliteHull.enabled = eliteHull.sprite != null;
        eliteMuzzle.enabled = false;
        eliteSight.enabled = false;
        eliteReticle.enabled = false;
    }

    void DrawElite(float dt)
    {
        if (elitePhase == ElitePhase.None || elitePhase == ElitePhase.Return || eliteDef == null) return;
        var def = eliteDef;
        var c = def.cells;
        if (eliteFire > 0f) eliteFire = Mathf.Max(0f, eliteFire - dt);
        bool aiming = elitePhase == ElitePhase.Snipe && eliteAim != null;
        bool exiting = elitePhase == ElitePhase.Exit;
        bool burning = exiting && eliteT >= EliteBackOff;

        // the drawing: its flight loop (or banks), tell while it aims, action as it fires
        int cell;
        if (eliteFire > 0f && c.action >= 0) cell = c.action;
        else if (aiming && c.tell >= 0) cell = c.tell;
        else
        {
            cell = FlightCell(c, now);
            if (!def.turnsToFace && c.Banks && Mathf.Abs(eliteVel.x) > .8f) cell = eliteVel.x < 0f ? c.bankLeft : c.bankRight;
        }
        var sprite = eliteFrames[Mathf.Clamp(cell, 0, eliteFrames.Length - 1)];
        if (sprite != null && eliteHull.sprite != sprite) eliteHull.sprite = sprite;

        // squash on the back-off, stretch along the way out once it burns
        float sx = 1f, sy = 1f;
        if (exiting && !burning) { sx = 1.05f; sy = .94f; }
        else if (burning) { sx = .94f; sy = 1.07f; }
        else if (aiming && c.tell < 0) { float k = Mathf.Clamp01(eliteAimT / Mathf.Max(.05f, eliteAimDur)); sx = 1f + .05f * k; sy = 1f - .05f * k; }
        float deg = EliteDrawingDeg;
        eliteHullTf.position = new Vector3(elitePos.x, elitePos.y, 0f);
        eliteHullTf.rotation = Quaternion.Euler(0f, 0f, deg);
        eliteHullTf.localScale = new Vector3(EliteScale * sx, EliteScale * sy, 1f);

        // engines: idle plumes, flared on the way in and out
        float plume = elitePhase == ElitePhase.Enter ? Mathf.Lerp(1.6f, .8f, Mathf.Clamp01(eliteT / EliteEnterTime))
                    : burning ? 2.1f : exiting ? .35f : .7f;
        int ticks = Mathf.FloorToInt(now * 24f);
        float len = def.cellWorldSize * def.exhaustScale * plume;
        for (int i = 0; i < eliteNozzles; i++)
        {
            var p = elitePlumes[i];
            bool show = len > .02f && def.exhaustScale > 0f;
            Sprite s = show ? ShipExhaust.Frame(def.exhaustShip, false, ShipExhaust.FrameAt(def.exhaustShip, ticks + i * 3)) : null;
            p.enabled = s != null;
            if (s != null)
            {
                if (p.sprite != s) p.sprite = s;
                p.transform.localScale = new Vector3(len * .42f / Mathf.Max(.01f, s.bounds.size.x), len / Mathf.Max(.01f, s.bounds.size.y), 1f);
            }
            var g = eliteGlows[i];
            Color gc = def.EngineColor;
            gc.a = Mathf.Clamp01(.6f + .3f * plume);
            g.color = gc;
            g.transform.localScale = Vector3.one * def.cellWorldSize * def.glowScale * (.7f + .4f * Mathf.Min(plume, 1.5f));
        }

        // the muzzle: a stepped charge glow while it aims (strips without a
        // tell cell), a flash as it fires (strips without an action cell)
        Vector2 muzzle = EliteMuzzle();
        bool flash = eliteFire > 0f && c.action < 0;
        bool charge = aiming && c.tell < 0;
        eliteMuzzle.enabled = flash || charge;
        if (eliteMuzzle.enabled)
        {
            float k = Mathf.Clamp01(eliteAimT / Mathf.Max(.05f, eliteAimDur));
            float size = flash ? (eliteFire > EliteFireHold * .5f ? .34f : .22f) : (k < .34f ? .1f : k < .67f ? .16f : .22f);
            Color mc = flash ? def.ShotCore : def.ShotColor;
            if (!flash) mc.a = Mathf.FloorToInt(eliteAimT / ((k > .7f ? 2f : 4f) * Tick)) % 2 == 0 ? 1f : .55f;
            eliteMuzzle.color = mc;
            var mt = eliteMuzzle.transform;
            mt.position = new Vector3(muzzle.x, muzzle.y, 0f);
            mt.localScale = Vector3.one * def.cellWorldSize * EliteScale * size;
        }

        // the tell: a blinking sight line to the mark, a lock ring closing on it
        if (aiming)
        {
            float k = Mathf.Clamp01(eliteAimT / Mathf.Max(.05f, eliteAimDur));
            bool blink = Mathf.FloorToInt(eliteAimT / ((k > .6f ? 1f : 2f) * Tick)) % 2 == 0;
            Vector3 a = new Vector3(muzzle.x, muzzle.y, 0f), b = new Vector3(eliteAim.pos.x, eliteAim.pos.y, 0f);
            Color sc = def.ShotColor;
            eliteSight.color = sc;
            Segment(eliteSight, true, a, b, k > .6f ? .045f : .028f, blink ? .85f : .4f);
            var rt = eliteReticle.transform;
            float r = eliteAim.radius * 2f * (k < .34f ? 2.2f : k < .67f ? 1.7f : 1.25f);
            rt.position = b;
            rt.localScale = new Vector3(r, r, 1f);
            Color lc = def.ShotCore;
            lc.a = blink ? 1f : .6f;
            eliteReticle.color = lc;
            eliteReticle.enabled = true;
        }
        else
        {
            eliteSight.enabled = false;
            eliteReticle.enabled = false;
        }
    }

    // The flight loop's drawing at time t (EliteArt's idle rhythm).
    static int FlightCell(EliteCells c, float t)
    {
        var loop = c.flight;
        if (loop == null || loop.Length == 0) return 0;
        if (loop.Length == 1) return loop[0];
        int total = 0;
        for (int i = 0; i < loop.Length; i++) total += EliteArt.IdleTicks[i % EliteArt.IdleTicks.Length];
        int tick = Mathf.FloorToInt(t * 24f) % Mathf.Max(1, total);
        for (int i = 0; i < loop.Length; i++)
        {
            tick -= EliteArt.IdleTicks[i % EliteArt.IdleTicks.Length];
            if (tick < 0) return loop[i];
        }
        return loop[0];
    }

    void DrawEliteShot(EliteShot s)
    {
        float deg = Mathf.Atan2(s.dir.y, s.dir.x) * Mathf.Rad2Deg - 90f;
        float w = Mathf.Max(.12f, eliteDef.shotSize * .55f), l = w * 3.2f;
        var bt = s.body.transform;
        bt.position = new Vector3(s.pos.x, s.pos.y, 0f);
        bt.rotation = Quaternion.Euler(0f, 0f, deg);
        // the capsule sprite is 1 unit tall, half as wide
        bt.localScale = new Vector3(w * 2f, l, 1f);
        var ct = s.core.transform;
        ct.position = bt.position;
        ct.rotation = bt.rotation;
        ct.localScale = new Vector3(w, l * .7f, 1f);
        s.body.enabled = true;
        s.core.enabled = true;
    }
}
