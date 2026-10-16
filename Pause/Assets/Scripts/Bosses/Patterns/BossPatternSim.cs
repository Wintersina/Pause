using System.Collections.Generic;
using UnityEngine;

// A flat flight model of the boss patterns, for measuring them (BossLadder) and proving them fair (the survivable-path
// solver). It plays BossPatternRunner through the IPatternHost interface exactly as the game does, and moves the shots
// by the same rules as BossProjectile.Step: straight lines, rails that mirror (Bounce) or take (Absorb) a shot, a
// bomb's fuse and burst, a seed's homing, a mine standing still. No engine objects, no allocation after construction.
public sealed class PatternSim : IMusicPatternHost
{
    public struct Shot
    {
        public Vector2 p, v;
        public float r, age, fuse, homeLeft, homeDeg;
        public int bounces, burst;
        public float burstSpeed, burstRot;
        public float turnLeft;          // a deploying shot: seconds until it takes `then`
        public Vector2 then;
        public BossRailMode rail;
        public bool alive;
        public bool bouncy;             // a ricochet shot (BossBounce): keeps SpeedKeep of its speed per bounce
    }

    // ---- the stage (defaults: the authored 10 u view, the narrowest ship reach) ----
    public float railEdge = 2.6f;
    public float shipHalf = 2.25f;
    public float spawnTop = 5.4f;
    public float zoneBottom = -3.9f, zoneTop = -.7f;
    public float shotScale = 1f;
    public Vector2 player = new Vector2(0f, -3.2f);
    public Vector2[] emitters = { new Vector2(0f, 3.4f), new Vector2(-1.4f, 3.2f), new Vector2(1.4f, 3.2f), new Vector2(0f, 3.0f) };
    public float boltR = .13f, shardR = .12f;
    public float floorY = -6.5f;
    // Where shots start (tests): every Fire is measured against the emitters as they are at that instant and the view.
    public float viewTop = 5f;
    public int fires, offView;
    public float worstEmitterDist;
    public int[] emittedParts = new int[16];

    public Shot[] shots = new Shot[256];
    public int live;
    public int fired, dropped;
    public float time;
    public int maxLive;
    public float minSpeed = float.MaxValue, maxSpeed;
    public int cap = 256;
    // Ricochets (BossBounce): shots fired bouncy, ricochets made, and bouncy shots alive now / at most.
    public int bouncyFired, ricochets, bouncyLive, maxBouncyLive;
    // The boss-fight weather (WorldHazards.BossShotsDrift): every live shot drifts sideways at this speed (u/s), kept
    // inside the rails like ShotShove.
    public float driftVx;

    public Vector2 Player => player;
    public float ShotSpeedScale => shotScale;
    public int ActiveMusicShots => live;
    public int MusicShotCapacity => cap;
    public float RailEdge => railEdge;
    public float ShipHalfWidth => shipHalf;
    public float SpawnTop => spawnTop;
    public float ZoneBottom => zoneBottom;
    public float ZoneTop => zoneTop;
    public Vector2 Emitter(int part) => emitters[Mathf.Clamp(part, 0, emitters.Length - 1)];
    public void Emitted() { }
    public void EmittedFrom(int part, float holdSeconds) { if (part >= 0 && part < emittedParts.Length) emittedParts[part]++; }
    public int marks;
    public void Mark(Vector2 a, Vector2 b, float halfWidth, float seconds) { marks++; }

    public void Reset()
    {
        for (int i = 0; i < shots.Length; i++) shots[i].alive = false;
        live = fired = dropped = marks = 0;
        bouncyFired = ricochets = bouncyLive = maxBouncyLive = 0;
        fires = offView = 0; worstEmitterDist = 0f;
        time = 0f;
        maxLive = 0;
        minSpeed = float.MaxValue; maxSpeed = 0f;
    }

    public void Fire(Vector2 at, Vector2 velocity, ref PShot ps)
    {
        // as BossActor: a fused shot (bomb, mine) flies on its own clock, everything else scales with the view
        float k = ps.fuse > 0f ? 1f : shotScale;
        fires++;
        float best = float.MaxValue;
        for (int i = 0; i < emitters.Length; i++) best = Mathf.Min(best, (emitters[i] - at).magnitude);
        if (best > worstEmitterDist) worstEmitterDist = best;
        if (at.y > viewTop || Mathf.Abs(at.x) > CameraFit.GameplayHalfWidth) offView++;
        var then = ps.then;
        ps.then *= k;
        Spawn(at, velocity * k, ps.style == BossShotStyle.Bolt ? boltR : shardR, ref ps);
        ps.then = then;
    }

    void Spawn(Vector2 at, Vector2 v, float r, ref PShot ps)
    {
        int slot = -1;
        for (int i = 0; i < cap; i++) if (!shots[i].alive) { slot = i; break; }
        fired++;
        if (slot < 0) { dropped++; return; }
        ref var s = ref shots[slot];
        s.alive = true;
        s.p = at; s.v = v; s.r = r; s.age = 0f;
        s.fuse = ps.fuse; s.burst = ps.burst; s.burstSpeed = ps.burstSpeed; s.burstRot = ps.burstRot;
        s.rail = ps.rail; s.bounces = ps.rail == BossRailMode.Bounce ? ps.bounces : 0;
        s.homeLeft = ps.homeSeconds > 0f && ps.homeDeg > 0f ? ps.homeSeconds : 0f; s.homeDeg = ps.homeDeg;
        s.turnLeft = ps.turnAt > 0f ? ps.turnAt : 0f; s.then = ps.then;
        s.bouncy = ps.bouncy && s.bounces > 0;
        if (s.bouncy) bouncyFired++;
        live++;
        if (live > maxLive) maxLive = live;
        // a deploying shot's speed is the one it flies down its lane at (the spray out of the muzzle is not an attack)
        float sp = ps.turnAt > 0f ? ps.then.magnitude : v.magnitude;
        if (sp > .01f) { if (sp < minSpeed) minSpeed = sp; if (sp > maxSpeed) maxSpeed = sp; }
    }

    // Advances every shot by dt (the pool's Step).
    public void Advance(float dt)
    {
        time += dt;
        for (int i = 0; i < cap; i++)
        {
            ref var s = ref shots[i];
            if (!s.alive) continue;
            s.age += dt;
            if (s.fuse > 0f && s.age >= s.fuse)
            {
                s.alive = false; live--;
                int burstN = s.burst;
                float burstSp = s.burstSpeed, burstRt = s.burstRot;
                Vector2 at = s.p;   // (Spawn may reuse this very slot: read it all first)
                if (burstN > 0)
                {
                    var ps = new PShot { style = BossShotStyle.Shard, rail = BossRailMode.Absorb };
                    for (int b = 0; b < burstN; b++)
                    {
                        float h = burstRt + 360f * b / burstN;
                        var v = new Vector2(Mathf.Cos(h * Mathf.Deg2Rad), Mathf.Sin(h * Mathf.Deg2Rad)) * burstSp;
                        Spawn(at, v, shardR, ref ps);
                    }
                }
                continue;
            }
            if (s.homeLeft > 0f)
            {
                s.homeLeft -= dt;
                float sp = s.v.magnitude;
                if (sp > .01f)
                {
                    float cur = Mathf.Atan2(s.v.y, s.v.x) * Mathf.Rad2Deg;
                    float want = Mathf.Atan2(player.y - s.p.y, player.x - s.p.x) * Mathf.Rad2Deg;
                    float d = Mathf.DeltaAngle(cur, want);
                    float turn = Mathf.Clamp(d, -s.homeDeg * dt, s.homeDeg * dt);
                    float h = (cur + turn) * Mathf.Deg2Rad;
                    s.v = new Vector2(Mathf.Cos(h), Mathf.Sin(h)) * sp;
                }
            }
            float move = dt;
            var p = s.p;
            if (s.turnLeft > 0f)
            {
                if (s.turnLeft <= dt) { p += s.v * s.turnLeft; move = dt - s.turnLeft; s.turnLeft = 0f; s.v = s.then; }
                else s.turnLeft -= dt;
            }
            p += s.v * move;
            if (driftVx != 0f)
            {
                float dx = driftVx * dt;
                if (s.rail != BossRailMode.Pass)
                {
                    float lim = Mathf.Max(Mathf.Abs(p.x), railEdge - s.r);
                    p.x = Mathf.Clamp(p.x + dx, -lim, lim);
                }
                else p.x += dx;
            }
            if (s.rail != BossRailMode.Pass)
            {
                float edge = railEdge - s.r;
                int side = p.x > edge && s.v.x > 0f ? 1 : p.x < -edge && s.v.x < 0f ? -1 : 0;
                if (side != 0)
                {
                    if (s.bounces > 0)
                    {
                        s.bounces--; p.x = side * edge - (p.x - side * edge); s.v.x = -s.v.x;
                        if (s.bouncy) { s.v *= BossBounce.SpeedKeep; ricochets++; }
                    }
                    else { s.alive = false; live--; continue; }
                }
            }
            s.p = p;
            if (p.y < floorY || p.y > spawnTop + 1.5f || Mathf.Abs(p.x) > 4.2f) { s.alive = false; live--; }
        }
        bouncyLive = 0;
        for (int i = 0; i < cap; i++) if (shots[i].alive && shots[i].bouncy) bouncyLive++;
        if (bouncyLive > maxBouncyLive) maxBouncyLive = bouncyLive;
    }

    // ---- the survivable-path solver ----
    //
    // A time-expanded flood fill: the set of places the ship can be (a grid over its zone) after each step is the set it
    // could reach (at `shipSpeed`) from the places it could be a step ago, minus every cell a shot touches (the ship's
    // radius + the shot's + `margin`). The pattern is SURVIVABLE from a start when the set is never empty. Omniscient
    // (it knows every shot in advance), so it proves a way exists, not that a human finds it; the dodge bot (DodgeBot) and
    // the greedy-bot ladder test cover reaction. Shots that aim at the ship (aimed, pincer, seeds, bombs) depend on where
    // the ship goes, so a caller who wants the static field only leaves those streams out of the pattern it passes.
    public const float ShipRadius = .28f;
    public const float Cell = .14f;

    public sealed class SolveResult
    {
        public bool survivable;
        public float minReach;          // the smallest share of the ship's zone still reachable at any step (0..1)
        public float worstAt;           // when
        public int stepsRun;
    }

    bool[] reach, blocked, tmp;
    int gw, gh;

    // One step of the flood fill: the shots as they are now block cells, the reachable set grows by `rad` cells and loses
    // the blocked ones. Returns how many cells are still reachable (Solve's loop body, shared with the streaming tracker
    // that plays a whole song through the musical director).
    int ReachStep(int rad, float margin)
    {
        int cells = gw * gh;
        // blocked cells
        for (int i = 0; i < cells; i++) blocked[i] = false;
        for (int k = 0; k < cap; k++)
        {
            if (!shots[k].alive) continue;
            float R = ShipRadius + shots[k].r + margin;
            float cx = (shots[k].p.x + shipHalf) / Cell, cy = (shots[k].p.y - zoneBottom) / Cell;
            float rc = R / Cell;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rc)), x1 = Mathf.Min(gw - 1, Mathf.CeilToInt(cx + rc));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - rc)), y1 = Mathf.Min(gh - 1, Mathf.CeilToInt(cy + rc));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= rc * rc) blocked[y * gw + x] = true;
                }
        }
        // dilate, then remove the blocked
        for (int i = 0; i < cells; i++) tmp[i] = false;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                if (!reach[y * gw + x]) continue;
                for (int dy = -rad; dy <= rad; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= gh) continue;
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= gw) continue;
                        if (dx * dx + dy * dy > rad * rad) continue;
                        tmp[yy * gw + xx] = true;
                    }
                }
            }
        int n = 0;
        for (int i = 0; i < cells; i++) { reach[i] = tmp[i] && !blocked[i]; if (reach[i]) n++; }
        return n;
    }

    // Streaming use: TrackBegin from the ship's start, then TrackStep once per simulated frame after Advance.
    public void TrackBegin(Vector2 start)
    {
        gw = Mathf.CeilToInt(shipHalf * 2f / Cell) + 1;
        gh = Mathf.CeilToInt((zoneTop - zoneBottom) / Cell) + 1;
        int cells = gw * gh;
        if (reach == null || reach.Length != cells) { reach = new bool[cells]; blocked = new bool[cells]; tmp = new bool[cells]; }
        for (int i = 0; i < cells; i++) reach[i] = false;
        int sx = Mathf.Clamp(Mathf.RoundToInt((start.x + shipHalf) / Cell), 0, gw - 1);
        int sy = Mathf.Clamp(Mathf.RoundToInt((start.y - zoneBottom) / Cell), 0, gh - 1);
        reach[sy * gw + sx] = true;
    }

    // The mean x of the reachable cells: where a ship that has been dodging well could be standing.
    public float ReachCentroid()
    {
        float sum = 0f; int n = 0;
        for (int i = 0; i < reach.Length; i++) if (reach[i]) { sum += (i % gw) * Cell - shipHalf; n++; }
        return n > 0 ? sum / n : 0f;
    }

    // Cells still reachable (0 = no survivable path is left).
    public int TrackStep(float dt, float shipSpeed, float margin)
    {
        return ReachStep(Mathf.Max(1, Mathf.CeilToInt(shipSpeed * dt / Cell)), margin);
    }

    public SolveResult Solve(BossPattern pattern, float rate, int seed, Vector2 start, float shipSpeed, float margin, float tail, float dt = .05f)
    {
        Reset();
        player = start;
        gw = Mathf.CeilToInt(shipHalf * 2f / Cell) + 1;
        gh = Mathf.CeilToInt((zoneTop - zoneBottom) / Cell) + 1;
        int cells = gw * gh;
        if (reach == null || reach.Length != cells) { reach = new bool[cells]; blocked = new bool[cells]; tmp = new bool[cells]; }
        var runner = new BossPatternRunner();
        runner.Begin(pattern, this, rate, seed, 0f);
        for (int i = 0; i < cells; i++) reach[i] = false;
        int sx = Mathf.Clamp(Mathf.RoundToInt((start.x + shipHalf) / Cell), 0, gw - 1);
        int sy = Mathf.Clamp(Mathf.RoundToInt((start.y - zoneBottom) / Cell), 0, gh - 1);
        reach[sy * gw + sx] = true;
        var res = new SolveResult { survivable = true, minReach = 1f };
        int rad = Mathf.Max(1, Mathf.CeilToInt(shipSpeed * dt / Cell));
        float total = pattern.Duration + tail;
        for (float t = 0f; t < total; t += dt)
        {
            runner.Step(dt);
            Advance(dt);
            res.stepsRun++;
            int n = ReachStep(rad, margin);
            float share = n / (float)cells;
            if (t >= 1.2f && share < res.minReach) { res.minReach = share; res.worstAt = t; }
            if (n == 0) { res.survivable = false; break; }
        }
        return res;
    }
}
