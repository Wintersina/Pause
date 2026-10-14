using System.Collections.Generic;
using System.Text;
using UnityEngine;

// THE DIFFICULTY LADDER of the boss bullet-hell overhaul (docs/boss-attack-overhaul.md): boss 1 (Void Archon) easiest to
// boss 7 (Dune-Eater) hardest, in the order the game visits the worlds, defined by numbers MEASURED on the real tables
// (BossPatternTables played through PatternSim; no engine objects, no scene), so tuning a boss in the table cannot quietly
// break the ladder.
//
//   * bullets / second strictly increases up the ladder (the headline), never under its floor
//   * max simultaneous shots, fastest shot, pattern count, max overlapping streams, enrage: non-decreasing
//   * the shortest telegraph and the narrowest safe gap: non-increasing (and every stream's forced gap is >= its boss's target)
//   * everything fits the projectile pool, nothing is dropped
//   * the solver (a flood fill over the ship's reach) finds a survivable path through every static pattern, at enrage
//     rate, from several starts, at several screen scales; and it FAILS an impossible wall (the regression injection)
public static class BossLadderTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LADDER] PASS  " : "[LADDER] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Names = { "Void Archon", "Hoarfrost Leviathan", "Bloom Queen", "Cinder Drake", "Iron Kraken", "Tempest Engine", "Dune-Eater" };

    public static int Execute()
    {
        fails = 0;
        var rows = new LadderRow[BossPatternTables.Bosses];
        for (int w = 0; w < rows.Length; w++) rows[w] = BossLadder.Measure(w);
        Table(rows);
        Order(rows);
        Floors(rows);
        FitsThePool(rows);
        Gaps();
        Fairness();
        SolverCatchesAnImpossibleWall();
        Debug.Log("[LADDER] failures: " + fails);
        return fails;
    }

    static void Table(LadderRow[] rows)
    {
        var sb = new StringBuilder("boss                 attacks patterns  shots/s  maxLive  speed       minTell  safeGap  overlap  enrage\n");
        for (int w = 0; w < rows.Length; w++)
        {
            var r = rows[w];
            sb.Append(Names[w].PadRight(21)).Append(r.attacks.ToString().PadRight(8)).Append(r.patterns.ToString().PadRight(9))
              .Append(r.bulletsPerSecond.ToString("F1").PadRight(9)).Append(r.maxSimultaneous.ToString().PadRight(9))
              .Append((r.minSpeed.ToString("F1") + "-" + r.maxSpeed.ToString("F1")).PadRight(12))
              .Append(r.minTell.ToString("F2").PadRight(9)).Append((r.minGap > 50f ? "-" : r.minGap.ToString("F2")).PadRight(9))
              .Append(r.maxOverlap.ToString().PadRight(9)).Append(r.enrage.ToString("F2")).Append('\n');
        }
        Debug.Log("[LADDER] the measured ladder (phase-3 rotation, enrage on):\n" + sb);
    }

    static void Order(LadderRow[] rows)
    {
        bool bullets = true, live = true, speed = true, attacks = true, overlap = true, enrage = true, tell = true, gapT = true;
        for (int w = 1; w < rows.Length; w++)
        {
            var a = rows[w - 1];
            var b = rows[w];
            if (!(b.bulletsPerSecond > a.bulletsPerSecond)) { bullets = false; Debug.Log("[LADDER]   shots/s " + Names[w] + " " + b.bulletsPerSecond.ToString("F2") + " vs " + a.bulletsPerSecond.ToString("F2")); }
            if (b.maxSimultaneous < a.maxSimultaneous) live = false;
            if (b.maxSpeed < a.maxSpeed - .01f) speed = false;
            if (b.attacks < a.attacks) attacks = false;
            if (b.maxOverlap < a.maxOverlap) overlap = false;
            if (b.enrage < a.enrage) enrage = false;
            if (b.minTell > a.minTell + .001f) tell = false;
            if (BossLadder.GapTarget[w] > BossLadder.GapTarget[w - 1]) gapT = false;
        }
        Check("shots per second STRICTLY increases up the ladder (the headline knob)", bullets);
        Check("max simultaneous shots is non-decreasing up the ladder", live);
        Check("the fastest shot is non-decreasing up the ladder", speed);
        Check("the number of attacks in the rotation is non-decreasing", attacks);
        Check("the most streams running at once inside an attack is non-decreasing", overlap);
        Check("the enrage multiplier of the last third is non-decreasing", enrage);
        Check("the shortest telegraph is non-increasing up the ladder", tell);
        Check("the safe-gap target (hitbox widths) is non-increasing up the ladder", gapT);
        Check("the last boss beats the first on every headline number (" + rows[6].bulletsPerSecond.ToString("F1") + " vs " + rows[0].bulletsPerSecond.ToString("F1") + " shots/s, " +
              rows[6].maxSimultaneous + " vs " + rows[0].maxSimultaneous + " live, " + rows[6].maxOverlap + " vs " + rows[0].maxOverlap + " overlapping streams)",
              rows[6].bulletsPerSecond > 2.5f * rows[0].bulletsPerSecond && rows[6].maxSimultaneous > 2 * rows[0].maxSimultaneous && rows[6].maxOverlap > rows[0].maxOverlap);
    }

    static void Floors(LadderRow[] rows)
    {
        for (int w = 0; w < rows.Length; w++)
        {
            var r = rows[w];
            Check(Names[w] + ": " + r.bulletsPerSecond.ToString("F1") + " shots/s meets its floor " + BossLadder.BulletsFloor[w].ToString("F1"), r.bulletsPerSecond >= BossLadder.BulletsFloor[w]);
            Check(Names[w] + ": its fastest shot " + r.maxSpeed.ToString("F1") + " meets the speed floor " + BossLadder.SpeedFloor[w].ToString("F1"), r.maxSpeed >= BossLadder.SpeedFloor[w] - .01f);
            Check(Names[w] + ": its shortest telegraph " + r.minTell.ToString("F2") + " is its floor " + BossLadder.TellFloor[w].ToString("F2"), Mathf.Abs(r.minTell - BossLadder.TellFloor[w]) < .011f);
            Check(Names[w] + ": the enrage is " + r.enrage.ToString("F2") + " (> 1)", r.enrage > 1.05f && Mathf.Approximately(r.enrage, BossLadder.EnrageFor(w)));
        }
    }

    static void FitsThePool(LadderRow[] rows)
    {
        for (int w = 0; w < rows.Length; w++)
            Check(Names[w] + ": " + rows[w].maxSimultaneous + " live shots fit the pool of " + BossConfig.ProjectilePoolMax + " with room to spare, none dropped",
                  rows[w].maxSimultaneous <= BossConfig.ProjectilePoolMax - 8 && rows[w].dropped == 0);
    }

    // every stream of a boss's forced gap (a wall's opening, a ring's, a lattice corridor, a mine field's lanes) is at least its target
    static void Gaps()
    {
        for (int w = 0; w < BossPatternTables.Bosses; w++)
        {
            float worst = float.MaxValue;
            string name = "";
            foreach (var sl in BossPatternTables.For(w))
            {
                if (sl.pattern == null) continue;
                foreach (var st in sl.pattern.streams)
                {
                    float g = BossLadder.GapMultiple(st);
                    if (g < worst) { worst = g; name = sl.name; }
                }
            }
            Check(Names[w] + ": its narrowest forced gap is " + (worst > 50f ? "none" : worst.ToString("F2") + " hitboxes (" + name + ")") + ", target " + BossLadder.GapTarget[w].ToString("F1"),
                  worst >= BossLadder.GapTarget[w] - .001f);
        }
        // a wall's gap never moves faster than the ship can follow it (7 u/s top speed, 60 % of it asked)
        bool slow = true;
        string fast = "";
        for (int w = 0; w < BossPatternTables.Bosses; w++)
            foreach (var sl in BossPatternTables.For(w))
            {
                if (sl.pattern == null) continue;
                foreach (var st in sl.pattern.streams)
                {
                    if (st.kind != StreamKind.Curtain) continue;
                    float v = st.gapStep / (st.interval / BossLadder.RateAt(w, 1f));
                    if (v > 4.2f) { slow = false; fast = sl.name + " " + v.ToString("F1"); }
                }
            }
        Check("a wall's gap never moves faster than 4.2 u/s (60 % of the ship's 7 u/s) even at enrage" + (slow ? "" : " (" + fast + ")"), slow);
    }

    // The solver over every pattern: its static streams (the ones that do not aim at the ship) at enrage rate.
    static void Fairness()
    {
        var sim = new PatternSim();
        float[] starts = { -2.1f, 0f, 2.1f };
        float[] scales = TestHarness.FullLane ? new[] { .75f, 1f, 1.25f, 1.6f } : new[] { .75f, 1.6f };
        int seeds = TestHarness.FullLane ? 4 : 1;
        for (int w = 0; w < BossPatternTables.Bosses; w++)
        {
            int solved = 0, total = 0;
            float worstReach = 1f;
            string worstName = "";
            foreach (var sl in BossPatternTables.For(w))
            {
                if (sl.pattern == null) continue;
                var stat = StaticOnly(sl.pattern);
                if (stat == null) continue;
                foreach (float scale in scales)
                    foreach (float x in starts)
                        for (int seed = 1; seed <= seeds; seed++)
                        {
                            sim.shotScale = scale;
                            var r = sim.Solve(stat, BossLadder.RateAt(w, 1f), seed, new Vector2(x, -3.2f), 5f, .04f, 2.5f);
                            total++;
                            if (r.survivable) solved++;
                            if (r.minReach < worstReach) { worstReach = r.minReach; worstName = sl.name; }
                        }
            }
            sim.shotScale = 1f;
            Check(Names[w] + ": a survivable path exists through every static pattern (" + solved + "/" + total + " solves), the tightest leaves " + (worstReach * 100f).ToString("F0") + "% of the ship's zone (" + worstName + ")",
                  solved == total && worstReach >= .1f);
        }
    }

    // The pattern without the streams that aim at the ship (their shots depend on where the ship goes: the dodge bot covers them), or null if nothing is left.
    public static BossPattern StaticOnly(BossPattern p)
    {
        var keep = new List<PatternStream>();
        foreach (var s in p.streams)
        {
            if (s.kind == StreamKind.Aimed || s.kind == StreamKind.Pincer || s.kind == StreamKind.Seeds) continue;
            keep.Add(s);
        }
        return keep.Count == 0 ? null : new BossPattern { name = p.name, streams = keep.ToArray() };
    }

    // The injection: a wall whose gap is a third of the ship's width cannot be flown through, and the solver must say so.
    static void SolverCatchesAnImpossibleWall()
    {
        var wall = new BossPattern
        {
            name = "impossible",
            streams = new[]
            {
                new PatternStream { kind = StreamKind.Curtain, duration = 3f, interval = .8f, gap = .2f, gapStep = 0f, pitch = .3f, speed = 3f, style = BossShotStyle.Bolt },
                new PatternStream { kind = StreamKind.Curtain, start = .1f, duration = 3f, interval = .8f, gap = .2f, gapStep = 0f, pitch = .3f, speed = 3f, style = BossShotStyle.Bolt },
            },
        };
        var sim = new PatternSim();
        var r = sim.Solve(wall, 1f, 1, new Vector2(0f, -3.2f), 5f, .04f, 2.5f);
        Check("regression injection: the solver reports a wall with a gap narrower than the ship as NOT survivable", !r.survivable);
        var ok = new BossPattern { name = "fair", streams = new[] { new PatternStream { kind = StreamKind.Curtain, duration = 3f, interval = .9f, gap = 2.2f, gapStep = 1.2f, pitch = .5f, speed = 3f, style = BossShotStyle.Bolt } } };
        var r2 = sim.Solve(ok, 1f, 1, new Vector2(0f, -3.2f), 5f, .04f, 2.5f);
        Check("... and the same wall with a 2.2 u gap is survivable (the solver is not just always saying no)", r2.survivable);
    }
}
