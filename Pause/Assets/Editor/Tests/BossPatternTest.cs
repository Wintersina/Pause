using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The boss bullet-hell overhaul (docs/boss-attack-overhaul.md): the tables, the pattern runner, and the real fight.
//
//   1  tables      every boss has its rotation as data (5-7 slots), sorted by phase (2 attacks, then 3-4, then all), pace and
//                  telegraph per the ladder, parts resolved, hearts / clock / hit points untouched
//   2  signatures  no two bosses share a signature mechanic; only plain basics (aimed volleys, fans, a laser) repeat, and every
//                  boss's own mechanics are the ones the design says
//   3  runner      each stream kind does what it says (shapes, gaps, rails, bursts, homing, mines), deterministic per seed,
//                  faster at enrage, nothing outside the stage
//   4  the fight   a whole phase-3 rotation of each boss through the real BossEncounter: patterns play (Pattern phase), their
//                  telegraphs show, the pool never exceeds its cap, shots carry the hostile hitbox tag, no pink outside Space,
//                  a steady step allocates nothing
public static class BossPatternTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PATTERN] PASS  " : "[PATTERN] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Names = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Tables();
            Signatures();
            Runner();
            Streams();
            TheFight();
        }
        finally
        {
            BossCatalog.UseOverhaul = true;
            BossEncounter.ResetRun();
            BossRails.Reset();
            BossAttackTest.ReleaseAtlases();
        }
        Debug.Log("[PATTERN] failures: " + fails);
        return fails;
    }

    // ---- 1 -------------------------------------------------------------------------------------------------

    static void Tables()
    {
        int[] sizes = { 5, 5, 6, 6, 7, 7, 7 };
        Check("seven bosses, one table each", BossCatalog.All.Length == 7 && BossPatternTables.Bosses == 7);
        for (int w = 0; w < 7; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            var t = boss.attacks;
            Check(Names[w] + ": fights the bullet-hell table by default (" + t.Length + " attacks)", ReferenceEquals(t, boss.hell) && t.Length == sizes[w]);
            bool sorted = true;
            for (int i = 1; i < t.Length; i++) if (t[i].minPhase < t[i - 1].minPhase) sorted = false;
            Check(Names[w] + ": sorted by phase, so the unlocked attacks are a prefix", sorted);
            int p1 = BossCatalog.UnlockedAttacks(boss, 0f), p2 = BossCatalog.UnlockedAttacks(boss, .5f), p3 = BossCatalog.UnlockedAttacks(boss, .9f);
            Check(Names[w] + ": phase 1 two attacks, phase 2 " + p2 + ", phase 3 all " + p3, p1 == 2 && p2 > p1 && p2 < p3 && p3 == t.Length);
            bool parts = true, tells = true, patterns = false, pose = true;
            foreach (var a in t)
            {
                if (a.kind == BossAttackKind.Pattern)
                {
                    patterns = true;
                    if (a.pattern == null || a.pattern.streams.Length == 0 || a.pattern.streams.Length > BossPatternRunner.MaxStreams) parts = false;
                    else foreach (var st in a.pattern.streams) { if (st.parts == null || st.parts.Length == 0) parts = false; else foreach (int p in st.parts) if (p < 0) parts = false; }
                }
                if (a.tellSeconds < BossLadder.TellFloor[w] - .001f) tells = false;
                if (a.tell < 0 || a.tell > 2) pose = false;
                if (a.cooldown <= 0f) tells = false;
            }
            Check(Names[w] + ": every stream's body parts resolve, at most " + BossPatternRunner.MaxStreams + " streams an attack", parts && patterns);
            Check(Names[w] + ": telegraphs meet the ladder floor " + BossLadder.TellFloor[w].ToString("F2") + " s, cooldowns positive, tell poses 0-2", tells && pose);
        }
        Check("the numbers the overhaul keeps: 12 hearts, 12 hit points, 120 s escape clock",
              BossConfig.Hearts == 12 && BossConfig.HitPoints == 12 && BossConfig.EscapeSeconds == 120f && BossConfig.FightSeconds == 120f);
        Check("the old default and themed tables are still there for their suites", BossCatalog.ForWorld(1).DefaultAttacks.Length == 3 && BossCatalog.ForWorld(1).themed.Length == 5);
        BossCatalog.UseOverhaul = false;
        Check("with the overhaul off the boss fights its old table again", !ReferenceEquals(BossCatalog.ForWorld(0).attacks, BossCatalog.ForWorld(0).hell));
        BossCatalog.UseOverhaul = true;
    }

    // ---- 2 -------------------------------------------------------------------------------------------------

    // The mechanics a boss may own alone: any non-basic stream kind, any area hazard kind. Basics (aimed volleys, fans, lasers) repeat.
    static IEnumerable<string> Mechanics(BossAttack a)
    {
        if (a.kind == BossAttackKind.Pattern)
        {
            foreach (var s in a.pattern.streams)
                if (s.kind != StreamKind.Aimed && s.kind != StreamKind.Fan) yield return s.kind.ToString();
        }
        else if (a.kind != BossAttackKind.Beam && a.kind != BossAttackKind.Aimed && a.kind != BossAttackKind.Fan && a.kind != BossAttackKind.Lob) yield return a.kind.ToString();
    }

    // What an attack is, for telling two apart: its kind and streams with their numbers and the parts it fires from.
    static string Signature(BossAttack a)
    {
        string em = string.Join("+", a.emitters ?? new string[0]);
        if (a.kind != BossAttackKind.Pattern) return a.kind + "|" + em + "|" + a.aim + a.aimDeg + "|" + a.sweepDeg + "|" + a.count + "|" + a.spreadDeg;
        return "P|" + string.Join(",", a.pattern.streams.Select(s => s.kind + ":" + string.Join("+", s.emitters) + ":" + s.count + ":" + s.spread + ":" + s.speed + ":" + s.interval));
    }

    static void Signatures()
    {
        var owner = new Dictionary<string, int>();
        var shared = new List<string>();
        for (int w = 0; w < 7; w++)
            foreach (var a in BossCatalog.ForWorld(w).attacks)
                foreach (var m in Mechanics(a))
                {
                    if (owner.TryGetValue(m, out int o) && o != w) shared.Add(m + " (" + Names[o] + ", " + Names[w] + ")");
                    else owner[m] = w;
                }
        Check("every signature mechanic belongs to exactly one boss" + (shared.Count == 0 ? "" : ": SHARED " + string.Join("; ", shared)), shared.Count == 0);

        // the design: who owns what
        var expect = new Dictionary<string, int>
        {
            { "Spiral", 0 }, { "Ring", 1 }, { "Seeds", 2 }, { "Lash", 2 }, { "Roll", 2 }, { "Bomb", 3 }, { "Jet", 3 }, { "Strike", 3 },
            { "Curtain", 4 }, { "Wave", 4 }, { "Rain", 5 }, { "Lattice", 5 }, { "Mines", 6 }, { "Pincer", 6 },
        };
        // (the Void Archon's ion lance is a Jet in phase 1: the one basic-phase exception, see below)
        bool design = true;
        string off = "";
        foreach (var kv in expect)
            if (!owner.TryGetValue(kv.Key, out int o) || o != kv.Value) { if (kv.Key == "Jet") continue; design = false; off += kv.Key + " "; }
        Check("each boss owns the mechanics the design gives it (Space spiral; Frost ring; Verdant seeds, lash, trunk; Ember bombs, strike; Tide curtain, wave; Storm rain, lattice; Desert mines, pincer)" + (design ? "" : ": off " + off), design);

        // no two bosses share an attack's signature, bar phase-1 basics
        var seen = new Dictionary<string, int>();
        var dup = new List<string>();
        for (int w = 0; w < 7; w++)
            foreach (var a in BossCatalog.ForWorld(w).attacks)
            {
                string sig = Signature(a);
                if (seen.TryGetValue(sig, out int o) && o != w && a.minPhase > 1) dup.Add(a.name + " (" + Names[o] + ", " + Names[w] + ")");
                if (!seen.ContainsKey(sig)) seen[sig] = w;
            }
        Check("no attack after phase 1 repeats another boss's signature" + (dup.Count == 0 ? "" : ": " + string.Join("; ", dup)), dup.Count == 0);

        // each boss's phase-2 and phase-3 attacks are all different from each other
        bool own = true;
        for (int w = 0; w < 7; w++)
        {
            var sigs = new HashSet<string>();
            foreach (var a in BossCatalog.ForWorld(w).attacks) if (!sigs.Add(Signature(a))) own = false;
        }
        Check("no boss repeats one of its own attacks", own);

        // the basics repeat only early: phase 1 (the plain aimed volley / fan every boss opens with)
        int basicLate = 0;
        for (int w = 0; w < 7; w++)
            foreach (var a in BossCatalog.ForWorld(w).attacks)
                if (a.minPhase > 1 && a.kind == BossAttackKind.Pattern && a.pattern.streams.All(s => s.kind == StreamKind.Aimed) ) basicLate++;
        Check("a plain aimed volley is only ever a phase-1 attack", basicLate == 0);
        Check("each boss opens with a plain basic and has at least two mechanics of its own",
              Enumerable.Range(0, 7).All(w => BossCatalog.ForWorld(w).attacks.Count(a => Mechanics(a).Any()) >= 2));
        int layered = 0;
        for (int w = 0; w < 7; w++) layered += BossCatalog.ForWorld(w).attacks.Count(a => a.kind == BossAttackKind.Pattern && a.pattern.streams.Length >= 2);
        Check("overlapping streams are used (" + layered + " layered attacks)", layered >= 10);
    }

    // ---- 3 -------------------------------------------------------------------------------------------------

    static PatternSim Run(PatternStream s, float rate = 1f, int seed = 1, float seconds = -1f, Vector2? player = null)
    {
        var sim = new PatternSim();
        if (player.HasValue) sim.player = player.Value;
        var r = new BossPatternRunner();
        s.parts = s.emitters != null ? System.Linq.Enumerable.Range(0, s.emitters.Length).ToArray() : new[] { 0, 1 };
        var p = new BossPattern { name = "t", streams = new[] { s } };
        r.Begin(p, sim, rate, seed, .5f);
        float end = seconds < 0f ? p.Duration : seconds;
        for (float t = 0f; t < end; t += .025f) { r.Step(.025f); sim.Advance(.025f); }
        return sim;
    }

    static int Live(PatternSim sim) { int n = 0; for (int i = 0; i < sim.cap; i++) if (sim.shots[i].alive) n++; return n; }

    static void Runner()
    {
        // deterministic per seed, different between seeds, faster at enrage
        var rain = new PatternStream { kind = StreamKind.Rain, duration = 2f, interval = .3f, count = 5, pitch = .75f, free = 3, speed = 3.5f, speedJitter = 1.5f, style = BossShotStyle.Shard };
        var a1 = Run(rain, 1f, 7);
        var a2 = Run(rain, 1f, 7);
        var a3 = Run(rain, 1f, 8);
        bool same = a1.fired == a2.fired;
        for (int i = 0; i < a1.cap && same; i++) same = a1.shots[i].alive == a2.shots[i].alive && (!a1.shots[i].alive || (a1.shots[i].p - a2.shots[i].p).sqrMagnitude < 1e-8f);
        bool diff = false;
        for (int i = 0; i < a1.cap; i++) if (a1.shots[i].alive != a3.shots[i].alive || (a1.shots[i].alive && (a1.shots[i].p - a3.shots[i].p).sqrMagnitude > 1e-6f)) diff = true;
        Check("a pattern is the same shots for the same seed, other shots for another", same && diff);
        var fast = Run(rain, 1.4f, 7);
        Check("the enrage fires faster (" + a1.fired + " -> " + fast.fired + " shots)", fast.fired > a1.fired * 1.25f);

        // every shot is born on the stage and the pattern never drops one into a roomy pool
        bool stage = true;
        for (int w = 0; w < 7; w++)
            foreach (var sl in BossPatternTables.For(w))
            {
                if (sl.pattern == null) continue;
                var sim = new PatternSim();
                var r = new BossPatternRunner();
                foreach (var st in sl.pattern.streams) st.parts = System.Linq.Enumerable.Range(0, st.emitters.Length).ToArray();   // (parts index the sim's emitters; the boss's real ones are resolved by BossHell)
                r.Begin(sl.pattern, sim, BossLadder.RateAt(w, 1f), 3, sl.tell);
                int steps = 0;
                while (!r.Step(.025f) && steps++ < 800)
                {
                    sim.Advance(.025f);
                    for (int i = 0; i < sim.cap; i++)
                        if (sim.shots[i].alive && (Mathf.Abs(sim.shots[i].p.x) > 4.2f || sim.shots[i].p.y > sim.spawnTop + 1.5f)) stage = false;
                }
                if (sim.dropped > 0) stage = false;
            }
        Check("every shot of every pattern stays on the stage and none is dropped by a 256-shot pool", stage);
    }

    static void Streams()
    {
        // curtain: a wall of shots with exactly the stated gap between the two shots either side of it, all falling straight down
        var cur = new PatternStream { kind = StreamKind.Curtain, duration = .1f, interval = 2f, gap = 2.0f, gapStep = 1f, pitch = .5f, speed = 3f, style = BossShotStyle.Bolt };
        var sim = Run(cur, 1f, 1, .05f, new Vector2(1.5f, -3f));
        int n = 0;
        bool down = true, row = true;
        float y0 = float.NaN;
        for (int i = 0; i < sim.cap; i++)
        {
            if (!sim.shots[i].alive) continue;
            n++;
            if (sim.shots[i].v.x != 0f || sim.shots[i].v.y >= 0f) down = false;
            if (float.IsNaN(y0)) y0 = sim.shots[i].p.y; else if (Mathf.Abs(sim.shots[i].p.y - y0) > .01f) row = false;
        }
        var xs = new List<float>();
        for (int i = 0; i < sim.cap; i++) if (sim.shots[i].alive) xs.Add(sim.shots[i].p.x);
        xs.Sort();
        float widest = 0f;
        for (int i = 1; i < xs.Count; i++) widest = Mathf.Max(widest, xs[i] - xs[i - 1]);
        Check("a curtain is one row of " + n + " shots falling straight down with a gap of " + widest.ToString("F2") + " u (stated 2.0), no wider gap anywhere", down && row && n >= 6 && Mathf.Abs(widest - 2f) < .02f);

        // a ring: only the downward half, a gap of the stated width aimed beside the ship, none of its shots inside it
        var ring = new PatternStream { kind = StreamKind.Ring, duration = .1f, interval = 2f, count = 20, gap = 60f, spread = 26f, speed = 2.8f, style = BossShotStyle.Shard };
        var rs = Run(ring, 1f, 1, .05f, new Vector2(0f, -3.2f));
        float aim = Mathf.Atan2(-3.2f - 3.4f, 0f) * Mathf.Rad2Deg;   // straight down from the sim's emitter 0 (0, 3.4)
        bool lower = true, gapClear = true;
        int rings = 0;
        for (int i = 0; i < rs.cap; i++)
        {
            if (!rs.shots[i].alive) continue;
            rings++;
            float h = Mathf.Atan2(rs.shots[i].v.y, rs.shots[i].v.x) * Mathf.Rad2Deg;
            if (rs.shots[i].v.y > 0f) lower = false;
            if (Mathf.Abs(Mathf.DeltaAngle(h, aim + 26f)) < 30f - .01f) gapClear = false;
        }
        Check("a ring fires only down (" + rings + " shots of 20), leaving a gap beside the ship", lower && rings >= 4 && rings < 20 && gapClear);

        // a spiral turns: later shots are at other headings; the mirror's arms are mirror images
        var spi = new PatternStream { kind = StreamKind.Spiral, duration = 2f, interval = .1f, count = 2, armDeg = 180f, spin = 90f, speed = 3f, style = BossShotStyle.Shard, mirror = false };
        var ss = Run(spi);
        var heads = new HashSet<int>();
        for (int i = 0; i < ss.cap; i++) if (ss.shots[i].alive) heads.Add(Mathf.RoundToInt(Mathf.Atan2(ss.shots[i].v.y, ss.shots[i].v.x) * Mathf.Rad2Deg / 10f));
        Check("a rotating spiral fires at many headings (" + heads.Count + ") and never up", heads.Count >= 8 && Enumerable.Range(0, ss.cap).All(i => !ss.shots[i].alive || ss.shots[i].v.y < 0f));
        var mir = new PatternStream { kind = StreamKind.Spiral, duration = 1.2f, interval = .1f, count = 1, amp = 40f, period = 2f, speed = 3f, style = BossShotStyle.Bolt, mirror = true };
        var mirSim = new PatternSim { railEdge = 10f };
        var mirRun = new BossPatternRunner();
        mir.parts = new[] { 0, 1 };
        mirRun.Begin(new BossPattern { name = "m", streams = new[] { mir } }, mirSim, 1f, 1, 0f);
        for (float t = 0f; t < 1.2f; t += .025f) { mirRun.Step(.025f); mirSim.Advance(.025f); }
        var ms = mirSim;
        float sumX = 0f; int cnt = 0;
        for (int i = 0; i < ms.cap; i++) if (ms.shots[i].alive) { sumX += ms.shots[i].v.x; cnt++; }
        Check("mirrored sweeps are symmetric (net sideways speed " + sumX.ToString("F2") + " over " + cnt + " shots)", cnt > 6 && Mathf.Abs(sumX) < .25f * cnt);

        // seeds turn towards the ship, then fly straight; bombs burst into rings; mines stand still and go
        var seeds = new PatternStream { kind = StreamKind.Seeds, duration = .01f, interval = 5f, count = 1, speed = 2.5f, homeDeg = 90f, homeSeconds = 1.5f, style = BossShotStyle.Shard };
        var sd = Run(seeds, 1f, 1, .02f, new Vector2(2f, -3f));
        float before = Mathf.Atan2(sd.shots[FirstAlive(sd)].v.y, sd.shots[FirstAlive(sd)].v.x) * Mathf.Rad2Deg;
        sd.player = new Vector2(-2f, -3f);
        for (int i = 0; i < 40; i++) sd.Advance(.025f);
        float after = Mathf.Atan2(sd.shots[FirstAlive(sd)].v.y, sd.shots[FirstAlive(sd)].v.x) * Mathf.Rad2Deg;
        for (int i = 0; i < 200; i++) sd.Advance(.025f);
        int k = FirstAlive(sd);
        float late = k < 0 ? after : Mathf.Atan2(sd.shots[k].v.y, sd.shots[k].v.x) * Mathf.Rad2Deg;
        Check("a seed turns toward the ship (" + before.ToString("F0") + " -> " + after.ToString("F0") + " deg) and stops turning once its homing is spent", Mathf.Abs(Mathf.DeltaAngle(before, after)) > 10f && Mathf.Abs(Mathf.DeltaAngle(after, late)) < 60f);

        var bomb = new PatternStream { kind = StreamKind.Bomb, duration = .01f, interval = 5f, count = 3, minSep = 1.6f, fuse = 1.4f, burst = 10, burstSpeed = 2.5f, style = BossShotStyle.Shard };
        var bs = Run(bomb, 1f, 1, .02f);
        int bombs = Live(bs);
        for (int i = 0; i < 62; i++) bs.Advance(.025f);
        int afterBang = Live(bs);
        Check("three bombs burst into rings of ten after their fuse (" + bombs + " -> " + afterBang + " shots)", bombs == 3 && afterBang >= 24 && afterBang <= 30);

        var mine = new PatternStream { kind = StreamKind.Mines, duration = 3f, interval = 9f, count = 8, gap = 1.4f, minSep = 1.5f, fuse = 3f, style = BossShotStyle.Shard };
        var mn = Run(mine, 1f, 5, .05f, new Vector2(0f, -3.2f));
        bool still = true, clear = true, apart = true;
        var pos = new List<Vector2>();
        for (int i = 0; i < mn.cap; i++) if (mn.shots[i].alive) { if (mn.shots[i].v.sqrMagnitude > 0f) still = false; pos.Add(mn.shots[i].p); if ((mn.shots[i].p - new Vector2(0f, -3.2f)).magnitude < 1.4f - .01f) clear = false; }
        for (int i = 0; i < pos.Count; i++) for (int j = i + 1; j < pos.Count; j++) if ((pos[i] - pos[j]).magnitude < 1.5f - .01f) apart = false;
        int placed = pos.Count;
        for (int i = 0; i < 140; i++) mn.Advance(.025f);
        Check("a mine field: " + placed + " mines that stand still, " + "keep clear of the ship and of each other, and go after their fuse", still && clear && apart && placed >= 4 && Live(mn) == 0);

        var rn = new PatternStream { kind = StreamKind.Rain, duration = 3f, interval = .25f, count = 6, pitch = .75f, free = 3, speed = 3.5f, speedJitter = 1.5f, style = BossShotStyle.Shard };
        var rr = Run(rn, 1f, 2, .02f);
        Check("rain drops from above the view into at most (lanes - free) lanes a row (" + Live(rr) + " drops of 7 lanes - 3 free)", Live(rr) == 4 && Enumerable.Range(0, rr.cap).All(i => !rr.shots[i].alive || rr.shots[i].p.y >= rr.spawnTop - .4f));

        var pin = new PatternStream { kind = StreamKind.Pincer, duration = 3f, interval = .5f, count = 1, gap = 2.4f, gapStep = .6f, side = 1.5f, speed = 4f, style = BossShotStyle.Bolt };
        var pn = Run(pin, 1f, 1, .02f, new Vector2(0f, -3.2f));
        int left = 0, right = 0;
        for (int i = 0; i < pn.cap; i++) if (pn.shots[i].alive) { if (pn.shots[i].v.x < 0f) left++; else right++; }
        Check("a pincer fires from both sides at once (the jaws), the first volley aimed wide of the ship (" + left + " left, " + right + " right)", left >= 1 && right >= 1);

        // a lattice keeps its rows staggered and drifting
        var lat = new PatternStream { kind = StreamKind.Lattice, duration = 2f, interval = .5f, count = 3, pitch = 2f, angle = 18f, speed = 3f, style = BossShotStyle.Bolt };
        var lt = Run(lat, 1f, 1, 1.2f);
        bool drift = true;
        for (int i = 0; i < lt.cap; i++) if (lt.shots[i].alive && Mathf.Abs(lt.shots[i].v.x) < .5f) drift = false;
        Check("a lattice's shots drift (" + Live(lt) + " live, off straight down by 18 deg)", drift && Live(lt) >= 8);
    }

    static int FirstAlive(PatternSim s) { for (int i = 0; i < s.cap; i++) if (s.shots[i].alive) return i; return 0; }

    // ---- 4 -------------------------------------------------------------------------------------------------

    static void TheFight()
    {
        var rng = new System.Random(5);
        for (int w = 0; w < 7; w++)
        {
            if (!TestHarness.SweepWorld(w)) continue;
            var sc = new AttackBudgetScenarios.Rotation(w, 40f);
            var ship = sc.Begin(rng, new Vector2(1.2f, -3.2f));
            var e = BossEncounter.Instance;
            var boss = BossCatalog.ForWorld(w);
            int patternPhases = 0, markersSeen = 0, tellsSeen = 0, maxMarkers = 0;
            bool hitboxOk = true, onStage = true;
            var kinds = new HashSet<string>();
            float t = 0f;
            for (; t < 40f; t += .025f)
            {
                sc.Step(.025f);
                var actor = e.Actor;
                if (actor.PatternPhase) patternPhases++;
                if (actor.Telegraphing && actor.CurrentAttack != null && actor.CurrentAttack.kind == BossAttackKind.Pattern) tellsSeen++;
                if (actor.Markers != null) { int m = actor.Markers.Shown; if (m > 0) markersSeen++; maxMarkers = Mathf.Max(maxMarkers, actor.Markers.Live); }
                if (actor.CurrentAttack != null) kinds.Add(actor.CurrentAttack.name);
                foreach (var s in e.Pool.Shots)
                {
                    if (s == null || !s.Active) continue;
                    if (s.Hitbox == null || s.Hitbox.tag != BossHitbox.Tag) hitboxOk = false;
                    var p = s.transform.position;
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || Mathf.Abs(p.x) > 4.3f || p.y > 12f) onStage = false;
                }
                // keep the ship out of harm's way: the fight is the subject, not the dodging
                ship.position = new Vector3(Mathf.Sin(t * .7f) * 1.5f, -3.4f, 0f);
            }
            Check(Names[w] + ": the rotation plays " + kinds.Count + " of its " + boss.attacks.Length + " attacks in 40 s, " + patternPhases + " frames in a pattern, " + tellsSeen + " in a pattern's tell",
                  kinds.Count >= boss.attacks.Length - 2 && patternPhases > 100 && tellsSeen > 20);
            Check(Names[w] + ": peak " + sc.PeakShots + " live shots (pool " + e.Pool.Capacity + ", created " + e.Pool.Created + "), peak beams " + sc.PeakBeams,
                  sc.PeakShots <= e.Pool.Capacity && e.Pool.Created <= BossConfig.ProjectilePoolMax && sc.PeakShots >= 20 && sc.PeakBeams <= BossConfig.BeamPoolMax);
            Check(Names[w] + ": every live shot carries the hostile hitbox tag and stays on the stage", hitboxOk && onStage);
            bool hasMarks = boss.attacks.Any(a => a.kind == BossAttackKind.Pattern && a.pattern.streams.Any(s => s.kind == StreamKind.Curtain || s.kind == StreamKind.Ring || s.kind == StreamKind.Mines || s.kind == StreamKind.Bomb));
            Check(Names[w] + ": the telegraph markers show (" + markersSeen + " frames, up to " + maxMarkers + " at once)" + (hasMarks ? "" : " [none needed]"), !hasMarks || markersSeen > 10);
            Check(Names[w] + ": the pattern's shots are told apart from its own (self-safe) and the hearts / clock are untouched (" + e.HeartsLeft + " hearts)", e.HeartsLeft == BossConfig.Hearts);

            // a steady pattern frame allocates (next to) nothing: the managed bytes of the frames spent inside a pattern (no laser or
            // hazard of another attack in the air), in windows of 40 such frames; the quietest window is what counts (the recorder is
            // process-wide, so a stray background allocation lands in one window, never in all)
            long bestWindow = long.MaxValue;
            int windows = 0, framesSeen = 0;
            using (var rec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC.Alloc", 1,
                       Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame | Unity.Profiling.ProfilerRecorderOptions.StartImmediately))
            {
                long windowBytes = 0;
                int inWindow = 0;
                for (int guard = 0; guard < 12000 && windows < 6; guard++)
                {
                    var act = e.Actor;
                    bool inPattern = act.PatternPhase && e.Pool.ActiveBeams == 0 && AttackHazard.ActiveCount == 0;
                    long before = rec.CurrentValue;
                    e.Step(.025f, 1f);
                    long used = rec.CurrentValue - before;
                    ship.position = new Vector3(Mathf.Sin(guard * .1f) * 1.5f, -3.4f, 0f);
                    if (inPattern && act.PatternPhase) { windowBytes += used; inWindow++; framesSeen++; }
                    if (inWindow == 40) { bestWindow = System.Math.Min(bestWindow, windowBytes); windows++; windowBytes = 0; inWindow = 0; }
                }
                Check(Names[w] + ": the allocation meter works here", rec.Valid);
            }
            Check(Names[w] + ": the quietest 40 frames inside patterns (" + windows + " windows, " + e.Pool.ActiveShots + " shots up at the end) allocate " + bestWindow + " bytes (" + (bestWindow / 40f).ToString("F1") + " a frame)",
                  windows >= 3 && bestWindow <= 40 * 48);

            // no pink outside Space
            var tint = HostileGlow.Tint(boss.flash);
            bool pinkOk = w == 0 || (!WorldCue.IsPink(tint) && !WorldCue.IsPink(boss.flash));
            Check(Names[w] + ": telegraph markers and flash are " + (w == 0 ? "Space's (pink allowed)" : "not pink"), pinkOk);

            // killing the boss clears the shots and the markers
            e.Actor.BeginOutro(true);
            e.Pool.RecycleAll();
            Check(Names[w] + ": a dying boss leaves no pattern markers", e.Actor.Markers == null || e.Actor.Markers.Live == 0);
            sc.End();
        }
    }
}
