using System;
using UnityEngine;

// RICOCHETS (BossBounce, docs/boss-bullet-waves.md): the unit half of BossWaveTest's ricochet checks (the whole-song
// flood fills live in BossWaveTest.RicochetSong). The pick, the reflection geometry, the bounce limit, what ends a
// bouncy shot, the exclusions, the cue, game time, pooling and the per-frame cost.
public static class Ricochet
{
    static Action<string, bool> check;
    static void Check(string what, bool ok) => check(what, ok);

    public static void Run(Action<string, bool> c)
    {
        check = c;
        Selector();
        TraceMatchesSim();
        TimedRunner();
        WallsNeverBounce();
        using (new TestHarness.Sandbox())
        {
            var boss = BossCatalog.ForWorld(1);
            var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
            try
            {
                Geometry(pool, boss);
                Ends(pool, boss);
                Exclusions(pool, boss);
                Cue(pool);
                Perf(pool, boss);
            }
            finally { pool.Dispose(); }
        }
    }

    // ---- the pick: exactly the fraction, spread evenly (never a whole group, never none of 7+)
    static void Selector()
    {
        var s = new BounceSelector();
        s.Reset();
        int allOrNone = 0;
        for (int wave = 0; wave < 300; wave++)
        {
            int n = 7 + wave % 20, got = 0;
            for (int i = 0; i < n; i++) if (s.Offer(BossBounce.BouncyFraction)) { s.Take(); got++; }
            if (got == 0 || got == n) allOrNone++;
        }
        float frac = s.picked / (float)s.eligible;
        Check("ricochet pick: " + s.picked + " of " + s.eligible + " bullets (" + (frac * 100f).ToString("F2") + " %, target " + BossBounce.BouncyFraction * 100f + " %), " + allOrNone + " of 300 waves all or none",
              Mathf.Abs(frac - BossBounce.BouncyFraction) < .002f && allOrNone == 0);
        // a veto keeps the credit: the next eligible bullet is picked instead, and a veto streak never banks a burst
        s.Reset();
        int picked = 0;
        for (int i = 0; i < 100; i++)
        {
            if (!s.Offer(.3f)) continue;
            if (i < 50) s.Veto(); else { s.Take(); picked++; }
        }
        Check("ricochet pick: vetoed picks pass the credit on, capped at " + BossBounce.MaxCredit + " (" + picked + " picked in the 50 after a veto streak)", picked >= 15 && picked <= 17);
        bool table = BossBounce.PerBoss.Length == 7;
        for (int w = 0; w < 7; w++) table &= Mathf.Approximately(BossBounce.FractionFor(w), .30f);
        Check("ricochet fraction: BouncyFraction " + BossBounce.BouncyFraction + ", a per-boss table (7 bosses, all .30 by default), MaxBounces " + BossBounce.MaxBounces + ", speed kept " + BossBounce.SpeedKeep,
              table && BossBounce.MaxBounces == 2 && BossBounce.SpeedKeep >= .9f && BossBounce.SpeedKeep < 1f);
    }

    // ---- BossBounce.Trace (what fairness is planned on) follows the same course the flight model flies
    static void TraceMatchesSim()
    {
        var sim = new PatternSim { cap = 8, railEdge = 2.85f, zoneBottom = -3.9f, zoneTop = -.7f, floorY = -9f };
        float worst = 0f; int cases = 0;
        for (int k = 0; k < 24; k++)
        {
            float deg = -100f - k * 3f;                       // heading left and down, shallow to steep
            var v = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * (2.6f + k * .08f);
            var from = new Vector2(.3f * (k % 3), 3f);
            float r = .12f, R = PatternSim.ShipRadius + r + BossBounce.ShadowMargin;
            if (!BossBounce.Trace(from, v, r, sim.railEdge, sim.zoneBottom - R, sim.zoneTop + R, out float t0, out float t1, out float x0, out float x1)) continue;
            sim.Reset();
            var ps = new PShot { style = BossShotStyle.Shard, rail = BossRailMode.Bounce, bounces = BossBounce.MaxBounces, bouncy = true };
            sim.Fire(from, v, ref ps);
            float dt = 1f / 240f, t = 0f, lo = 99f, hi = -99f, first = -1f, last = -1f;
            while (sim.live > 0 && t < 12f)
            {
                sim.Advance(dt); t += dt;
                var q = sim.shots[0];
                if (!q.alive) break;
                if (q.bounces < BossBounce.MaxBounces && q.p.y <= sim.zoneTop + R && q.p.y >= sim.zoneBottom - R)
                {
                    lo = Mathf.Min(lo, q.p.x); hi = Mathf.Max(hi, q.p.x);
                    if (first < 0f) first = t;
                    last = t;
                }
            }
            if (first < 0f) continue;
            cases++;
            float err = Mathf.Max(Mathf.Max(Mathf.Abs(lo - x0), Mathf.Abs(hi - x1)), Mathf.Max(Mathf.Abs(first - t0), Mathf.Abs(last - t1)) * v.magnitude);
            worst = Mathf.Max(worst, err);
        }
        Check("ricochet trace: the planned window and sweep of " + cases + " ricochets match the flown course (worst " + worst.ToString("F3") + " u)", cases >= 8 && worst < .05f);
    }

    // ---- the timed rotation's streams: the same fraction of the rail-meeting shots, certified clear of the ship
    static void TimedRunner()
    {
        var sim = new PatternSim { cap = 256, railEdge = 2.85f, shipHalf = 2.5f };
        var stream = new PatternStream { kind = StreamKind.Fan, parts = new[] { 0 }, duration = 30f, interval = .5f, count = 9, spread = 150f, speed = 3f, style = BossShotStyle.Shard };
        var runner = new BossPatternRunner();
        sim.Reset();
        sim.player = new Vector2(0f, -3.2f);
        runner.Begin(new BossPattern { name = "fan", streams = new[] { stream } }, sim, 1f, 3, 0f);
        for (int f = 0; f < 60 * 30; f++) { runner.Step(1f / 60f); sim.Advance(1f / 60f); }
        var b = runner.bounce;
        float frac = b.eligible > 0 ? b.picked / (float)b.eligible : 0f;
        Check("timed rotation: " + b.picked + " of " + b.eligible + " rail-meeting fan shots ricochet (" + (frac * 100f).ToString("F1") + " %, " + b.vetoed + " vetoed near the ship), " + sim.ricochets + " ricochets flown",
              b.eligible > 100 && Mathf.Abs(frac - .3f) < .04f && sim.bouncyFired == b.picked && sim.ricochets > 0);
        // a bomb / mine / homing seed stream is never bouncy
        foreach (var kind in new[] { StreamKind.Bomb, StreamKind.Mines, StreamKind.Seeds })
        {
            var s2 = new PatternStream { kind = kind, parts = new[] { 0 }, duration = 8f, interval = .6f, count = 3, speed = 3f, fuse = 1.2f, burst = 6, style = BossShotStyle.Shard };
            var r2 = new BossPatternRunner();
            sim.Reset();
            r2.Begin(new BossPattern { name = kind.ToString(), streams = new[] { s2 } }, sim, 1f, 5, .5f);
            for (int f = 0; f < 60 * 9; f++) { r2.Step(1f / 60f); sim.Advance(1f / 60f); }
            Check("timed rotation: " + kind + " (and its burst shards) never ricochet (" + sim.fired + " shots, " + sim.bouncyFired + " bouncy)", sim.fired > 0 && sim.bouncyFired == 0);
        }
    }

    // ---- a wall / slant falls straight down its lane: it never meets a rail, so it is never bouncy
    static void WallsNeverBounce()
    {
        var sim = new PatternSim { cap = 256 };
        var voice = new BossWaveVoice(4, sim, BossWaveSources.For(4, BossCatalog.ForWorld(4), 0));
        float t = 0f; int fired = 0;
        for (int i = 0; i < 30; i++)
        {
            fired += voice.EmitMove(i % 2 == 0 ? WaveMove.Wall(0, true, 2) : WaveMove.Slant(0, 12f), .6f, .4, t, false);
            for (int f = 0; f < 60; f++) { sim.Advance(1f / 60f); t += 1f / 60f; }
        }
        Check("walls and slants never ricochet (" + fired + " bullets, " + voice.bounce.eligible + " meet a rail, " + sim.bouncyFired + " bouncy)", fired > 300 && voice.bounce.eligible == 0 && sim.bouncyFired == 0);
    }

    static BossProjectile Bouncy(BossProjectilePool pool, BossDef boss, Vector2 at, Vector2 v)
    {
        var ps = new PShot { style = BossShotStyle.Shard, rail = BossRailMode.Bounce, bounces = BossBounce.MaxBounces, bouncy = true };
        return pool.FireShot(boss, at, v, ref ps, false);
    }

    // ---- angle in = angle out, a little speed lost, never through the rail, MaxBounces then splashed
    static void Geometry(BossProjectilePool pool, BossDef boss)
    {
        pool.RecycleAll();
        var v0 = new Vector2(Mathf.Cos(-12f * Mathf.Deg2Rad), Mathf.Sin(-12f * Mathf.Deg2Rad)) * 4f;
        var s = Bouncy(pool, boss, new Vector2(0f, 3f), v0);
        bool angles = true, speed = true, inside = true;
        int sparks0 = pool.RicochetSparks, bounces = 0;
        float edge = BossRails.InnerEdge - s.Radius;
        Vector2 before = s.Velocity;
        for (int f = 0; f < 60 * 8 && s.Active; f++)
        {
            before = s.Velocity;
            pool.Step(1f / 60f);
            if (!s.Active) break;
            inside &= Mathf.Abs(s.transform.position.x) <= edge + 1e-3f;
            if (s.Bounces != bounces)
            {
                bounces = s.Bounces;
                var after = s.Velocity;
                // mirrored: the same angle to the rail's normal, the other way; the speed SpeedKeep of what it was
                float aIn = Mathf.Atan2(before.y, Mathf.Abs(before.x)), aOut = Mathf.Atan2(after.y, Mathf.Abs(after.x));
                angles &= Mathf.Abs(aIn - aOut) < 1e-4f && Mathf.Sign(after.x) == -Mathf.Sign(before.x);
                speed &= Mathf.Abs(after.magnitude / before.magnitude - BossBounce.SpeedKeep) < 1e-4f;
            }
        }
        Check("ricochet geometry: angle in = angle out, " + ((1f - BossBounce.SpeedKeep) * 100f).ToString("F0") + " % speed lost per bounce, never past the rail's inner face", angles && speed && inside && bounces >= 1);
        Check("ricochet limit: " + bounces + " ricochets (" + (pool.RicochetSparks - sparks0) + " cue-tinted sparks), then splashed on the next rail (end reason " + s.EndReason + ")",
              bounces == BossBounce.MaxBounces && s.EndReason == 2 && pool.RicochetSparks - sparks0 == BossBounce.MaxBounces);
        // the same hit size, speed class and damage tag as a plain shot
        var plainPs = new PShot { style = BossShotStyle.Shard, rail = BossRailMode.Absorb };
        var plain = pool.FireShot(boss, new Vector2(0f, 3f), v0, ref plainPs, false);
        var b = Bouncy(pool, boss, new Vector2(0f, 3f), v0);
        var cp = plain.Hitbox.GetComponent<CircleCollider2D>(); var cb = b.Hitbox.GetComponent<CircleCollider2D>();
        Check("ricochet shots keep the shot's hitbox (" + b.Radius + " u), speed and damage tag",
              Mathf.Approximately(plain.Radius, b.Radius) && Mathf.Approximately(cp.radius, cb.radius) && b.Velocity == plain.Velocity && b.Hitbox.tag == BossHitbox.Tag && b.Hitbox.name == plain.Hitbox.name);
        pool.RecycleAll();
    }

    // ---- what still ends a bouncy shot: the bottom, the top, the ship / shield (its hitbox), a pop; pause freezes it
    static void Ends(BossProjectilePool pool, BossDef boss)
    {
        pool.RecycleAll();
        var down = Bouncy(pool, boss, new Vector2(0f, 0f), new Vector2(.4f, -6f));
        for (int f = 0; f < 600 && down.Active; f++) pool.Step(1f / 60f);
        int downEnd = down.EndReason;
        var hit = Bouncy(pool, boss, new Vector2(0f, 2f), new Vector2(2f, -2f));
        pool.Step(1f / 60f);
        UnityEngine.Object.DestroyImmediate(hit.Hitbox);
        pool.Step(1f / 60f);
        int hitEnd = hit.EndReason;
        var pop = Bouncy(pool, boss, new Vector2(0f, 2f), new Vector2(2f, -2f));
        pop.ShotPop(pop.transform.position);
        int popEnd = pop.EndReason;
        var frozen = Bouncy(pool, boss, new Vector2(1f, 2f), new Vector2(3f, -1f));
        var at = frozen.transform.position;
        for (int f = 0; f < 120; f++) pool.Step(0f);
        bool still = frozen.Active && frozen.transform.position == at;
        // slow motion: the same course in game time (a quarter speed covers a quarter of the way)
        var slow = Bouncy(pool, boss, new Vector2(-1f, 2f), new Vector2(3f, -1f));
        for (int f = 0; f < 60; f++) pool.Step(.25f / 60f);
        float slowDx = slow.transform.position.x - -1f;
        Check("a bouncy shot still leaves through the bottom (" + downEnd + "), dies on the ship / shield (" + hitEnd + ") and to a pop (" + popEnd + "); a pause freezes it, slow motion slows it (" + slowDx.ToString("F2") + " u in a quarter second of game time)",
              downEnd == 1 && hitEnd == 3 && popEnd == 5 && still && Mathf.Abs(slowDx - .75f) < .02f);
        pool.RecycleAll();
    }

    // ---- never a bomb, a mine, a homing seed, a burst shard; beams / lashes / hazards are no shots at all
    static void Exclusions(BossProjectilePool pool, BossDef boss)
    {
        pool.RecycleAll();
        var bomb = new PShot { style = BossShotStyle.Shard, rail = BossRailMode.Bounce, bounces = 2, bouncy = true, fuse = 1f, burst = 6, burstSpeed = 2.5f };
        var mine = new PShot { style = BossShotStyle.Shard, rail = BossRailMode.Bounce, bounces = 2, bouncy = true, fuse = 2f, turnAt = .45f };
        var seed = new PShot { style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 2, bouncy = true, homeDeg = 60f, homeSeconds = 1f };
        bool none = !pool.FireShot(boss, Vector2.zero, Vector2.down, ref bomb, false).Bouncy
                 && !pool.FireShot(boss, Vector2.zero, Vector2.down, ref mine, false).Bouncy
                 && !pool.FireShot(boss, Vector2.zero, Vector2.down, ref seed, false).Bouncy;
        pool.Burst(boss, Vector3.zero, 8, 2.5f, 0f);
        var beam = pool.Beam(boss, null, -1, new Vector3(0f, 3f, 0f), -90f, 0f, .2f, .5f, .3f);
        for (int f = 0; f < 120; f++) pool.Step(1f / 60f);
        bool beams = typeof(BossBeam).GetProperty("Bouncy") == null && beam != null;
        Check("never bouncy: bombs, mines, homing seeds, a bomb's burst shards (" + pool.ActiveBouncy + " bouncy in the pool); beams have no ricochet at all", none && pool.ActiveBouncy == 0 && beams);
        pool.RecycleAll();
    }

    // ---- the cue: a ring of its own (not the shot's drawing), the world's cue colour, pink only in Space
    static void Cue(BossProjectilePool pool)
    {
        bool ok = true, pinkOk = true;
        string why = "";
        for (int w = 0; w < 7; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            pool.RecycleAll();
            var b = Bouncy(pool, boss, new Vector2(0f, 2f), new Vector2(1f, -2f));
            var plainPs = new PShot { style = BossShotStyle.Shard, rail = BossRailMode.Absorb };
            var p = pool.FireShot(boss, new Vector2(.5f, 2f), new Vector2(1f, -2f), ref plainPs, false);
            pool.Step(1f / 60f);
            // another agent's projectile art swaps the drawing: the cue is untouched
            b.GetComponent<SpriteRenderer>().sprite = null;
            pool.Step(1f / 60f);
            var c = b.Cue.color;
            bool family = Hued(c, w);
            if (!(b.Cue.enabled && b.Cue.sprite == BossRicochetCue.Ring && !p.Cue.enabled && c.a > .2f && b.Cue.sortingOrder > b.GetComponent<SpriteRenderer>().sortingOrder))
            { ok = false; why += " " + w; }
            if (w > 0 && WorldCue.IsPink(c)) pinkOk = false;
            ok &= family;
        }
        Check("ricochet cue: its own dashed ring over the shot in the world's cue colour (not the shot's sprite: survives an art swap), plain shots without" + (why.Length > 0 ? " -- wrong in worlds" + why : ""), ok);
        Check("ricochet cue: no pink outside Space", pinkOk);
        pool.RecycleAll();
    }

    // The cue is the world's pale trace: its hue is the world's cue hue (or it is near white).
    static bool Hued(Color c, int w)
    {
        Color.RGBToHSV(c, out float h, out float sat, out _);
        var t = BossRicochetCue.Tint(w);
        return sat < .06f || Mathf.Abs(Mathf.DeltaAngle(h * 360f, WorldCue.Hue[w])) < 20f && Mathf.Abs(c.r - t.r) + Mathf.Abs(c.g - t.g) + Mathf.Abs(c.b - t.b) < .01f;
    }

    // ---- pooled, no allocation, the cost of a screen of ricochets
    static void Perf(BossProjectilePool pool, BossDef boss)
    {
        pool.RecycleAll();
        pool.Warm(boss, BossConfig.ProjectilePoolMax);
        int created = pool.Created;
        var sw = new System.Diagnostics.Stopwatch();
        long bytes = 0; int frames = 0;
        for (int f = 0; f < 600; f++)
        {
            if (f % 20 == 0)
                for (int i = 0; i < 12; i++)
                {
                    float deg = -15f - i * 12.5f;
                    Bouncy(pool, boss, new Vector2(0f, 3.5f), new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * 3.2f);
                }
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Start();
            pool.Step(1f / 60f);
            sw.Stop();
            if (f > 60) { bytes += GC.GetAllocatedBytesForCurrentThread() - a0; frames++; }
        }
        double us = sw.Elapsed.TotalMilliseconds * 1000.0 / 600;
        Check("ricochets pooled: " + pool.ActiveShots + " live (" + pool.ActiveBouncy + " bouncy), pool " + pool.Created + " / " + pool.Capacity + " objects (none made mid-fight), " + us.ToString("F0") + " us / frame stepping them at 60 fps (editor), " + (bytes / Math.Max(1, frames)) + " B / frame",
              pool.Created == created && bytes == 0 && us < 2000);
        pool.RecycleAll();
    }
}

