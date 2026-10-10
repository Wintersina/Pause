using System.Collections.Generic;
using UnityEngine;
using static AttackTestKit;

// THE PROJECTILE BEHAVIOURS (plan phase 1f; EliteShot, ShotMotion flags read from the shot's skin; docs/world-attacks-design.md section 3):
// Streak, Shatter, Flutter, Slash, Roll, Burst.
//
// Each behaviour: its path and numbers (the design doc's budgets: ShotMotions), its lifetime, its children (count, pooling, never again),
// the hit radius that does not depend on the drawn size, the rails, no allocation, a frozen world. The defaults stay: with no motion flag
// in the skin (every world until its phase calls ShotSkins.Enable) a shot is exactly what it was.
public static class ShotMotionTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SHOTMOT] PASS  " : "[SHOTMOT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Defaults();
                Streak();
                Shatter();
                Flutter();
                Slash();
                Roll();
                Burst();
                RadiusIsTheKinds();
                PoolAndPause();
                DrawnPixels();
            }
            finally { AttackTestKit.Cleanup(); }
        }
        Debug.Log("[SHOTMOT] failures: " + fails);
        return fails;
    }

    static EliteDef Def(string world = "verdant", float size = .22f)
    {
        var d = new EliteDef { key = "test_" + world, world = world, shotSize = size, shotColor = "#FF4FD8", shotCore = "#FFFFFF", lobSeconds = .8f, poolSeconds = 2.5f, hazardCount = 6, hazardSpeed = 1.5f };
        d.Resolve();
        return d;
    }

    static void SkinOn(int world, EliteShots.Kind k, ShotMotion m, float drawScale = 2f)
    {
        var s = ShotMotionArt.Skin(world, k, m);
        s.drawScale = drawScale;
        ShotSkins.Override(world, k, s);
    }

    static EliteShot Fire(EliteDef d, EliteShots.Kind k, Vector2 at, Vector2 v) => EliteSystem.Shots.Fire(null, d, k, at, v);

    static int Active()
    {
        int n = 0;
        foreach (var s in EliteSystem.Shots.All) if (s != null && s.Active) n++;
        return n;
    }

    static List<EliteShot> Live()
    {
        var l = new List<EliteShot>();
        foreach (var s in EliteSystem.Shots.All) if (s != null && s.Active) l.Add(s);
        return l;
    }

    // ---- the defaults -----------------------------------------------------------------------------------

    static void Defaults()
    {
        Fresh();
        int withMotion = 0, total = 0;
        for (int w = 0; w < ShotSkins.Worlds; w++)
            foreach (EliteShots.Kind k in System.Enum.GetValues(typeof(EliteShots.Kind)))
            {
                total++;
                if (ShotSkins.For(w, k).motion != ShotMotion.None) withMotion++;
            }
        ShotSkins.Enable(2);
        foreach (EliteShots.Kind k in System.Enum.GetValues(typeof(EliteShots.Kind)))
        {
            total++;
            if (ShotSkins.For(2, k).motion != ShotMotion.None) withMotion++;
        }
        ShotSkins.Enable(2, false);
        Check("no world's default skin carries a motion flag, enabled or not (" + withMotion + " of " + total + "): the shots are what they were until a phase gives a skin one", withMotion == 0);
        var d = Def();
        var s = Fire(d, EliteShots.Kind.Shell, new Vector2(0f, 2f), new Vector2(0f, -3f));
        Advance(.2f);
        Check("a plain shot has no ghosts, no sight line, no capsule, its own speed (" + s.Velocity.magnitude.ToString("F2") + ") and a circle hitbox",
              s.Motion == ShotMotion.None && s.GhostsShown == 0 && !s.SightShown && !s.SlashCapsule && Mathf.Abs(s.Velocity.magnitude - 3f) < .01f && s.Hitbox.GetComponent<CircleCollider2D>().enabled);
        EliteSystem.Clear();
    }

    // ---- Streak ----------------------------------------------------------------------------------------

    static void Streak()
    {
        Fresh();
        var d = Def("space");
        float plainR;
        {
            var p = Fire(d, EliteShots.Kind.Shell, new Vector2(0f, 3f), new Vector2(0f, -3f));
            plainR = p.Radius;
            p.Recycle();
        }
        SkinOn(0, EliteShots.Kind.Shell, ShotMotion.Streak);
        var s = Fire(d, EliteShots.Kind.Shell, new Vector2(.4f, 3.5f), new Vector2(.5f, -3.4f));
        Vector2 dir0 = new Vector2(.5f, -3.4f).normalized;
        Check("a Streak slug flies at " + ShotMotions.StreakSpeed + " u/s along the aimed line whatever speed it was fired with (" + s.Velocity.magnitude.ToString("F2") + ", heading off by " +
              Vector2.Angle(s.Velocity, dir0).ToString("F2") + " deg)", Mathf.Abs(s.Velocity.magnitude - ShotMotions.StreakSpeed) < .01f && Vector2.Angle(s.Velocity, dir0) < .1f && (s.Motion & ShotMotion.Streak) != 0);
        Check("... with " + s.GhostsShown + " afterimages behind it (" + ShotMotions.GhostCount + " x " + ShotMotions.GhostGap + " u = " + (ShotMotions.GhostCount * ShotMotions.GhostGap).ToString("F1") + " u) and a hairline sight line to the edge of the view (" +
              s.SightLength.ToString("F1") + " u)", s.GhostsShown == ShotMotions.GhostCount && s.SightShown && s.SightLength > 5f);
        var g0 = s.transform.GetChild(s.transform.childCount - ShotMotions.GhostCount);
        float gap = Vector2.Distance(g0.position, s.transform.position);
        Check("the first afterimage is " + ShotMotions.GhostGap + " u behind the slug (" + gap.ToString("F2") + ") on its line", Mathf.Abs(gap - ShotMotions.GhostGap) < .02f && Vector2.Dot((g0.position - s.transform.position).normalized, -dir0) > .99f);
        Check("the hit radius is the shell's own (" + s.Radius.ToString("F3") + " vs " + plainR.ToString("F3") + ") whatever is drawn", Mathf.Abs(s.Radius - plainR) < 1e-5f);
        Vector2 p0 = s.transform.position;
        float sight0 = s.SightLength;
        Advance(.5f);
        Check("after .5 s it has flown " + Vector2.Distance(p0, s.transform.position).ToString("F2") + " u (3.0 expected); the sight line is shorter now (" + s.SightLength.ToString("F1") + " < " + sight0.ToString("F1") + ")",
              Mathf.Abs(Vector2.Distance(p0, s.transform.position) - ShotMotions.StreakSpeed * .5f) < .1f && s.SightLength < sight0 - 2f);
        AdvanceUntil(() => !s.Active, 3f);
        Check("it is gone at the bottom of the view and its ghosts and line go with it (" + s.GhostsShown + ", " + s.SightShown + ")", !s.Active && s.GhostsShown == 0 && !s.SightShown);
        var again = Fire(d, EliteShots.Kind.Shard, new Vector2(0f, 3f), new Vector2(0f, -3f));
        Check("a pooled shot used again as something plain wears none of it", again.GhostsShown == 0 && !again.SightShown);
        EliteSystem.Clear();
    }

    // ---- Shatter ---------------------------------------------------------------------------------------

    static void Shatter()
    {
        Fresh();
        var d = Def("frost", .26f);
        float plainShard;
        {
            var p = Fire(d, EliteShots.Kind.Shard, new Vector2(0f, 3f), Vector2.zero);
            plainShard = p.Radius;
            p.Recycle();
        }
        SkinOn(1, EliteShots.Kind.Bolt, ShotMotion.Shatter);
        SkinOn(1, EliteShots.Kind.Shard, ShotMotion.Shatter);   // (the chips are shards: the same skin must not split them again)
        Vector2 at = new Vector2(0f, 4.2f), v = new Vector2(.6f, -2.8f);
        var s = Fire(d, EliteShots.Kind.Bolt, at, v);
        float parentR = s.Radius;
        int launched0 = EliteSystem.Shots.Launched;
        float flew = AdvanceUntil(() => !s.Active, 3f);
        var chips = Live();
        Check("an ice spear splits after " + flew.ToString("F2") + " s of flight (" + ShotMotions.ShatterSeconds + ") into " + chips.Count + " chips, pooled (" + (EliteSystem.Shots.Launched - launched0) + " launched, pool still " +
              EliteSystem.Shots.All.Count + "), and is gone", Mathf.Abs(flew - ShotMotions.ShatterSeconds) < .06f && chips.Count == ShotMotions.ShatterChips && EliteSystem.Shots.Launched - launched0 == ShotMotions.ShatterChips &&
              EliteSystem.Shots.All.Count == EliteShots.MaxShots && s.LastSpawned == ShotMotions.ShatterChips);
        // the fan: -14, 0, +14 round the heading, 90% of the speed
        Vector2 heading = v.normalized;
        var angles = new List<float>();
        float speed = 0f;
        foreach (var c in chips) { angles.Add(Vector2.SignedAngle(heading, c.Velocity)); speed += c.Velocity.magnitude / chips.Count; }
        angles.Sort();
        Check("the chips fan out " + angles[0].ToString("F1") + " / " + angles[1].ToString("F1") + " / " + angles[2].ToString("F1") + " deg round the heading at " + speed.ToString("F2") + " u/s (" +
              (v.magnitude * ShotMotions.ChipSpeedShare).ToString("F2") + " expected)", Mathf.Abs(angles[0] + ShotMotions.ChipSpreadDeg) < 1f && Mathf.Abs(angles[1]) < 1f && Mathf.Abs(angles[2] - ShotMotions.ChipSpreadDeg) < 1f &&
              Mathf.Abs(speed - v.magnitude * ShotMotions.ChipSpeedShare) < .1f);
        bool small = true, plain = true;
        foreach (var c in chips) { small &= Mathf.Abs(c.Radius - plainShard * ShotMotions.ChipScale) < .002f; plain &= c.IsChip && c.Motion == ShotMotion.None && c.GhostsShown == 0 && c.Kind == EliteShots.Kind.Shard; }
        Check("each chip is a small plain shard: " + (ShotMotions.ChipScale * 100f) + "% of a shard's hit radius (" + chips[0].Radius.ToString("F3") + " vs " + plainShard.ToString("F3") + "), no behaviour of its own", small && plain);
        int before = EliteSystem.Shots.Launched;
        Advance(2f);
        Check("a chip never splits again, though its skin says Shatter (nothing more launched: " + (EliteSystem.Shots.Launched - before) + ")", EliteSystem.Shots.Launched == before);
        EliteSystem.Clear();

        // on a rail
        Fresh();
        SkinOn(1, EliteShots.Kind.Bolt, ShotMotion.Shatter);
        float edge = EliteSystem.RailEdge;
        var r = Fire(d, EliteShots.Kind.Bolt, new Vector2(edge - .3f, 3f), new Vector2(2.4f, -.6f));
        AdvanceUntil(() => !r.Active, 1f);
        var rc = Live();
        bool inside = true;
        foreach (var c in rc) inside &= Mathf.Abs(c.transform.position.x) <= edge + .01f;
        Check("on a rail the spear shatters where it hit, inside the rails (" + rc.Count + " chips, " + r.Age.ToString("F2") + " s old, end reason " + r.EndReason + ")", rc.Count == ShotMotions.ShatterChips && inside && r.Age < .5f);
        EliteSystem.Clear();

        // on a hazard it hits
        Fresh();
        SkinOn(1, EliteShots.Kind.Bolt, ShotMotion.Shatter);
        var rock = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Rock), new Vector2(0f, 1.6f), Quaternion.identity);
        ClearTarget.Ensure(rock);
        FriendlyFire.Settle(rock);
        var h = Fire(d, EliteShots.Kind.Bolt, new Vector2(0f, 3.2f), new Vector2(0f, -3f));
        AdvanceUntil(() => !h.Active, 2f);
        Check("on a hazard it hits it shatters there: the rock is destroyed (" + (rock == null) + ") and " + Live().Count + " chips fly on", rock == null && Live().Count == ShotMotions.ShatterChips && h.Age < 1f);
        EliteSystem.Clear();

        // shot down: just pops
        Fresh();
        SkinOn(1, EliteShots.Kind.Bolt, ShotMotion.Shatter);
        var pop = Fire(d, EliteShots.Kind.Bolt, new Vector2(0f, 3f), new Vector2(0f, -2f));
        Advance(.2f);
        pop.Struck();
        Check("shot down by the player it just pops: no chips (" + Active() + " live)", !pop.Active && Active() == 0);

        // a full pool: fewer chips, no exception
        EliteSystem.Clear();
        Fresh();
        SkinOn(1, EliteShots.Kind.Bolt, ShotMotion.Shatter);
        var parent = Fire(d, EliteShots.Kind.Bolt, new Vector2(0f, 3f), new Vector2(0f, -1f));
        var filler = new List<EliteShot>();
        for (int i = 0; i < EliteShots.MaxShots + 4; i++) { var f = Fire(def_(d), EliteShots.Kind.Slab, new Vector2(-3f + i * .1f, 4f), Vector2.zero); if (f != null) filler.Add(f); }
        parent.Recycle();   // (one slot free again)
        var par2 = Fire(d, EliteShots.Kind.Bolt, new Vector2(0f, 3f), new Vector2(0f, -1f));
        int freeBefore = EliteShots.MaxShots - Active();
        Advance(ShotMotions.ShatterSeconds + .1f);
        Check("with the pool nearly full the spear splits into what fits (" + par2.LastSpawned + " of " + ShotMotions.ShatterChips + " chips) and nothing throws", !par2.Active && par2.LastSpawned <= ShotMotions.ShatterChips && par2.LastSpawned >= 0);
        EliteSystem.Clear();
    }

    static EliteDef def_(EliteDef d) => d;

    // ---- Flutter ---------------------------------------------------------------------------------------

    static void Flutter()
    {
        Fresh();
        var d = Def("verdant", .2f);
        float plainR;
        {
            var p = Fire(d, EliteShots.Kind.Shard, new Vector2(0f, 3f), Vector2.zero);
            plainR = p.Radius;
            p.Recycle();
        }
        SkinOn(2, EliteShots.Kind.Shard, ShotMotion.Flutter);
        var a = Fire(d, EliteShots.Kind.Shard, new Vector2(0f, 4f), new Vector2(0f, -1.8f));
        var b = Fire(d, EliteShots.Kind.Shard, new Vector2(0f, 4f), new Vector2(0f, -1.8f));
        float maxA = 0f, maxB = 0f, sumAbs = 0f;
        int crossings = 0;
        float lastOff = 0f;
        var rotations = new HashSet<int>();
        int frames = 0;
        for (; frames < 120 && a.Active; frames++)
        {
            EliteSystem.Step(Dt);
            float oa = a.transform.position.x, ob = b.transform.position.x;
            maxA = Mathf.Max(maxA, Mathf.Abs(oa)); maxB = Mathf.Max(maxB, Mathf.Abs(ob));
            sumAbs = Mathf.Max(sumAbs, Mathf.Abs(oa + ob));
            if (frames > 2 && oa * lastOff < 0f) crossings++;
            if (Mathf.Abs(oa) > 1e-4f) lastOff = oa;
            rotations.Add(Mathf.RoundToInt(a.transform.eulerAngles.z));
        }
        float t = frames * Dt;
        Check("a leaf weaves " + maxA.ToString("F3") + " u across its straight course (" + ShotMotions.FlutterAmp + " expected) at " + ShotMotions.FlutterHz + " Hz (" + crossings + " crossings in " + t.ToString("F1") + " s), the pair in opposite phase (the two never drift apart " + sumAbs.ToString("F3") + " from mirror)",
              Mathf.Abs(maxA - ShotMotions.FlutterAmp) < .01f && Mathf.Abs(maxB - ShotMotions.FlutterAmp) < .01f && sumAbs < .01f && crossings >= Mathf.FloorToInt(t * ShotMotions.FlutterHz * 2f) - 2);
        bool steps = rotations.Count > 4;
        foreach (int r in rotations) steps &= r % 45 == 0 || r % 45 == 44 || r % 45 == 1;
        Check("it spins in 45 degree steps (" + rotations.Count + " distinct angles, all multiples of 45: " + steps + ")", steps);
        Check("the straight course is the fired speed (the leaf has dropped " + (4f - a.transform.position.y).ToString("F2") + " u in " + t.ToString("F2") + " s at 1.8 u/s = " + (1.8f * t).ToString("F2") + ")", Mathf.Abs((4f - a.transform.position.y) - 1.8f * t) < .08f);
        Check("the hit radius is the shard's own (" + a.Radius.ToString("F3") + " vs " + plainR.ToString("F3") + ")", Mathf.Abs(a.Radius - plainR) < 1e-5f);
        EliteSystem.Clear();
    }

    // ---- Slash -----------------------------------------------------------------------------------------

    static void Slash()
    {
        Fresh();
        var d = Def("verdant", .2f);
        float plainR;
        {
            var p = Fire(d, EliteShots.Kind.Shard, new Vector2(0f, 3f), Vector2.zero);
            plainR = p.Radius;
            p.Recycle();
        }
        SkinOn(2, EliteShots.Kind.Shard, ShotMotion.Slash);
        float edge = EliteSystem.RailEdge;
        var s = Fire(d, EliteShots.Kind.Shard, new Vector2(-edge + .2f, 1f), new Vector2(9f, 0f));
        Check("a crescent crosses the lane at the " + ShotMotions.SlashSpeed + " u/s cap whatever it was fired with (" + s.Velocity.magnitude.ToString("F2") + "), its length along its heading (axis " + s.SlashAxis.ToString("F2") + ")",
              Mathf.Abs(s.Velocity.magnitude - ShotMotions.SlashSpeed) < .01f && Vector2.Dot(s.SlashAxis, Vector2.right) > .99f);
        float drawn = s.BodySprite.bounds.size.x * s.transform.lossyScale.x;
        Check("it is drawn " + drawn.ToString("F2") + " u long (" + ShotMotions.SlashLength + ") and hit as a capsule that long and " + (s.Radius * 2f).ToString("F3") + " thick (the kind's own radius " + plainR.ToString("F3") + " either side), the circle off",
              Mathf.Abs(drawn - ShotMotions.SlashLength) < .02f && s.SlashCapsule && !s.Hitbox.GetComponent<CircleCollider2D>().enabled && Mathf.Abs(s.Radius - plainR) < 1e-5f && s.ShotCollidable);
        Physics2D.SyncTransforms();
        var cap = s.Hitbox.GetComponent<CapsuleCollider2D>();
        Vector2 c = s.transform.position;
        Check("the capsule covers the crescent lengthwise and no more (a point .5 u along it is in, .62 u along is out, .12 u across it is out)",
              cap.OverlapPoint(c + Vector2.right * .5f) && !cap.OverlapPoint(c + Vector2.right * .62f) && !cap.OverlapPoint(c + Vector2.up * (plainR + .12f)) && cap.OverlapPoint(c));
        // friendly fire along its length: a rock .45 u ahead of its centre dies, one beside it on the row above does not
        var ahead = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Rock), new Vector2(c.x + 1.4f, 1f), Quaternion.identity);
        ClearTarget.Ensure(ahead); FriendlyFire.Settle(ahead);
        var above = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Rock), new Vector2(c.x + 1.4f, 2.6f), Quaternion.identity);
        ClearTarget.Ensure(above); FriendlyFire.Settle(above);
        float x0 = c.x;
        AdvanceUntil(() => !s.Active, 3f);
        Check("it cuts the hazard on its row (" + (ahead == null) + ") and leaves the one a row above (" + (above != null) + ")", ahead == null && above != null);
        EliteSystem.Clear();
        // the rail: a crescent ends when its end touches the rail
        Fresh();
        SkinOn(2, EliteShots.Kind.Shard, ShotMotion.Slash);
        var r = Fire(d, EliteShots.Kind.Shard, new Vector2(0f, 1f), new Vector2(4f, 0f));
        AdvanceUntil(() => !r.Active, 3f);
        float endX = r.transform.position.x;
        Check("it crosses the lane and ends when its centre reaches the rail (centre x " + endX.ToString("F2") + " vs rail " + EliteSystem.RailEdge.ToString("F2") + ")",
              Mathf.Abs(endX - (EliteSystem.RailEdge - plainR - ShotMotions.SlashRailLap)) < .4f && r.EndReason == 2);
        EliteSystem.Clear();
    }

    // ---- Roll --------------------------------------------------------------------------------------------------

    static void Roll()
    {
        Fresh();
        var d = Def("verdant", .7f);
        float plainGlob;
        {
            var p = Fire(d, EliteShots.Kind.Glob, new Vector2(0f, 3f), Vector2.zero);
            plainGlob = p.Radius;
            p.Recycle();
        }
        SkinOn(2, EliteShots.Kind.Glob, ShotMotion.Roll);
        var s = Fire(d, EliteShots.Kind.Glob, new Vector2(0f, 4f), Vector2.zero);
        Vector2 spot = new Vector2(.9f, 1.5f);
        s.Lob(spot, .8f);
        Check("a lobbed log is in the air with no hitbox and a marked spot, until it lands", s.Airborne && !s.ShotCollidable && s.MarkShown);
        AdvanceUntil(() => !s.Airborne, 2f);
        Check("it lands on the marked spot (" + ((Vector2)s.transform.position).ToString("F2") + " vs " + spot.ToString("F2") + ") and rolls: heavy mass, not a pool", s.Rolling && Vector2.Distance(s.transform.position, spot) < .08f &&
              s.ShotMass == HostileShots.Heavy && !s.Pooled && s.ShotCollidable && !s.MarkShown);
        Vector2 v = s.Velocity;
        Check("it rolls down the board at " + ShotMotions.RollSpeed + " u/s relative to the ground (" + v.magnitude.ToString("F2") + " on a still board), its hit radius the kind's own (" + s.Radius.ToString("F3") + " vs " + plainGlob.ToString("F3") + ")",
              Mathf.Abs(v.magnitude - ShotMotions.RollSpeed) < .06f && v.y < 0f && Mathf.Abs(s.Radius - plainGlob) < 1e-5f);
        var angles = new HashSet<int>();
        float landed = s.Age;
        int bounced = 0;
        float lastVx = v.x;
        int frames = 0;
        for (; frames < 60 * 8 && s.Active; frames++)
        {
            EliteSystem.Step(Dt);
            if (!s.Active) break;
            angles.Add(Mathf.RoundToInt(s.transform.eulerAngles.z));
            if (Mathf.Sign(s.Velocity.x) != Mathf.Sign(lastVx)) bounced++;
            lastVx = s.Velocity.x;
        }
        Check("it bounces off a rail exactly once (" + s.Bounced + " bounce, " + bounced + " turn of direction), spins in 30 degree steps (" + angles.Count + " angles) and is over after " + (landed + frames * Dt).ToString("F1") + " s (its life is " +
              ShotMotions.RollSeconds + " s)", s.Bounced == 1 && bounced == 1 && angles.Count >= 4 && frames * Dt <= ShotMotions.RollSeconds + .1f);
        // on a moving board the log keeps its own pace on top of the ground
        moveBackGround.speed = .4f;
        var m = Fire(d, EliteShots.Kind.Glob, new Vector2(0f, 4f), Vector2.zero);
        m.Lob(new Vector2(-1f, 2f), .5f);
        AdvanceUntil(() => !m.Airborne, 2f);
        Advance(.1f);
        Check("on a moving board it carries the board's pace plus its own (vy " + m.Velocity.y.ToString("F2") + ")", m.Rolling && m.Velocity.y < -ShotMotions.RollSpeed * .7f - EliteSystem.Scroll + .02f);
        moveBackGround.speed = 0f;
        EliteSystem.Clear();
    }

    // ---- Burst -------------------------------------------------------------------------------------------------

    static void Burst()
    {
        Fresh();
        var d = Def("verdant", .3f);
        float plainShard;
        {
            var p = Fire(d, EliteShots.Kind.Shard, new Vector2(0f, 3f), Vector2.zero);
            plainShard = p.Radius;
            p.Recycle();
        }
        SkinOn(2, EliteShots.Kind.Glob, ShotMotion.Burst);
        SkinOn(2, EliteShots.Kind.Bolt, ShotMotion.Burst);
        SkinOn(2, EliteShots.Kind.Shard, ShotMotion.Burst);   // (the spores are shards: the same skin must not burst them again)
        // a lobbed pod: lands, leaves its cloud and scatters spores
        var pod = Fire(d, EliteShots.Kind.Glob, new Vector2(0f, 4f), Vector2.zero);
        pod.Lob(new Vector2(0f, 1.2f), .8f);
        int launched0 = EliteSystem.Shots.Launched;
        AdvanceUntil(() => !pod.Airborne, 2f);
        var spores = Live();
        spores.Remove(pod);
        Check("a pod lands and leaves its cloud pool (pooled: " + pod.Pooled + ") and scatters " + spores.Count + " spores (" + ShotMotions.BurstSpores + "), all from the shot pool (" + (EliteSystem.Shots.Launched - launched0) +
              " launched, pool " + EliteSystem.Shots.All.Count + ")", pod.Pooled && spores.Count == ShotMotions.BurstSpores && EliteSystem.Shots.Launched - launched0 == ShotMotions.BurstSpores && EliteSystem.Shots.All.Count == EliteShots.MaxShots && pod.LastSpawned == ShotMotions.BurstSpores);
        bool ring = true, small = true;
        var dirs = new List<float>();
        foreach (var sp in spores)
        {
            ring &= Mathf.Abs(sp.Velocity.magnitude - ShotMotions.SporeSpeed) < .05f && sp.IsChip && sp.Motion == ShotMotion.None;
            small &= Mathf.Abs(sp.Radius - plainShard * ShotMotions.SporeScale) < .002f;
            dirs.Add(Mathf.Atan2(sp.Velocity.y, sp.Velocity.x) * Mathf.Rad2Deg);
        }
        dirs.Sort();
        float evenly = 0f;
        for (int i = 1; i < dirs.Count; i++) evenly = Mathf.Max(evenly, Mathf.Abs(dirs[i] - dirs[i - 1] - 360f / ShotMotions.BurstSpores));
        Check("the spores leave in a ring at " + ShotMotions.SporeSpeed + " u/s, evenly spaced (worst step " + evenly.ToString("F1") + " deg off), " + (ShotMotions.SporeScale * 100f) + "% of a shard's hit radius (" + spores[0].Radius.ToString("F3") + ")", ring && small && evenly < 1.5f);
        Advance(ShotMotions.SporeSeconds + .2f);
        bool gone = true;
        foreach (var sp in spores) gone &= !sp.Active;
        Check("the spores last " + ShotMotions.SporeSeconds + " s and never burst again; the cloud is still there (" + pod.Active + ")", gone && pod.Active && pod.Splits == 1);
        EliteSystem.Clear();

        // a pod in flight (not lobbed): bursts by itself at BurstSeconds, and on a rail
        Fresh();
        SkinOn(2, EliteShots.Kind.Bolt, ShotMotion.Burst);
        SkinOn(2, EliteShots.Kind.Shard, ShotMotion.Burst);
        var b = Fire(d, EliteShots.Kind.Bolt, new Vector2(0f, 4f), new Vector2(0f, -1.5f));
        float flew = AdvanceUntil(() => !b.Active, 3f);
        Check("a pod in flight bursts after " + flew.ToString("F2") + " s (" + ShotMotions.BurstSeconds + ") into " + Live().Count + " spores", Mathf.Abs(flew - ShotMotions.BurstSeconds) < .06f && Live().Count == ShotMotions.BurstSpores);
        EliteSystem.Clear();
        Fresh();
        SkinOn(2, EliteShots.Kind.Bolt, ShotMotion.Burst);
        SkinOn(2, EliteShots.Kind.Shard, ShotMotion.Burst);
        var r = Fire(d, EliteShots.Kind.Bolt, new Vector2(EliteSystem.RailEdge - .3f, 3f), new Vector2(2f, -.3f));
        AdvanceUntil(() => !r.Active, 1.5f);
        Check("a pod bursts on the rail (" + r.Age.ToString("F2") + " s, " + Live().Count + " spores)", Live().Count == ShotMotions.BurstSpores && r.Age < ShotMotions.BurstSeconds);
        EliteSystem.Clear();
    }

    // ---- the hit radius does not depend on what is drawn -----------------------------------------------------------

    static void RadiusIsTheKinds()
    {
        Fresh();
        var d = Def("verdant", .24f);
        var cases = new[]
        {
            (EliteShots.Kind.Shell, ShotMotion.Streak), (EliteShots.Kind.Bolt, ShotMotion.Shatter), (EliteShots.Kind.Shard, ShotMotion.Flutter),
            (EliteShots.Kind.Shard, ShotMotion.Slash), (EliteShots.Kind.Glob, ShotMotion.Roll), (EliteShots.Kind.Glob, ShotMotion.Burst),
        };
        int same = 0;
        string why = "";
        foreach (var (kind, motion) in cases)
        {
            float[] r = new float[3];
            float[] draw = new float[3];
            int i = 0;
            foreach (float scale in new[] { 1f, 2f, 3.5f })
            {
                ShotSkins.ResetForTests();
                SkinOn(2, kind, motion, scale);
                var s = Fire(d, kind, new Vector2(0f, 3f), new Vector2(0f, -1f));
                r[i] = s.Radius;
                draw[i] = s.HitRadiusOfCollider;
                i++;
                s.Recycle();
            }
            ShotSkins.ResetForTests();
            var plain = Fire(d, kind, new Vector2(0f, 3f), new Vector2(0f, -1f));
            bool ok = Mathf.Abs(r[0] - plain.Radius) < 1e-5f && Mathf.Abs(r[1] - plain.Radius) < 1e-5f && Mathf.Abs(r[2] - plain.Radius) < 1e-5f && Mathf.Abs(draw[0] - draw[2]) < .002f * (motion == ShotMotion.Slash ? 100f : 1f);
            plain.Recycle();
            if (ok) same++; else why += motion + " (" + r[0].ToString("F3") + "/" + r[1].ToString("F3") + "/" + r[2].ToString("F3") + " vs " + plain.Radius.ToString("F3") + "); ";
        }
        Check("for every behaviour the hit radius is the kind's own at draw scales 1, 2 and 3.5 (" + same + " of " + cases.Length + " identical: " + why + ")", same == cases.Length);
        EliteSystem.Clear();
    }

    // ---- pool, allocation, pause ---------------------------------------------------------------------------------------

    static void PoolAndPause()
    {
        Fresh();
        var d = Def("verdant", .3f);
        SkinOn(0 + 2, EliteShots.Kind.Shell, ShotMotion.Streak);
        SkinOn(2, EliteShots.Kind.Bolt, ShotMotion.Shatter);
        SkinOn(2, EliteShots.Kind.Shard, ShotMotion.Flutter);
        SkinOn(2, EliteShots.Kind.Glob, ShotMotion.Roll);
        System.Action life = () =>
        {
            Fire(d, EliteShots.Kind.Shell, new Vector2(.2f, 4f), new Vector2(0f, -3f));
            Fire(d, EliteShots.Kind.Bolt, new Vector2(-1f, 4f), new Vector2(.3f, -2.8f));
            Fire(d, EliteShots.Kind.Shard, new Vector2(1f, 4f), new Vector2(0f, -1.8f));
            var log = Fire(d, EliteShots.Kind.Glob, new Vector2(.5f, 4f), Vector2.zero);
            if (log != null) log.Lob(new Vector2(.9f, 1.5f), .6f);
            for (int i = 0; i < 400; i++) { EliteSystem.Step(Dt); }
        };
        life(); life();
        bool meter = TestHarness.AllocMeterWorks(out long ctl);
        long used = TestHarness.AllocatedBytes(life);
        Check("a streak, a splitting spear (and its chips), a leaf and a rolling log launch, fly, split and end allocating nothing after warm-up (" + used + " bytes; meter " + (meter ? "ok" : "blind") + ")", meter && used == 0);
        Check("the pool is back to idle (" + Active() + " live)", Active() == 0);
        EliteSystem.Clear();

        // a frozen world freezes every behaviour
        Fresh();
        SkinOn(2, EliteShots.Kind.Shard, ShotMotion.Flutter);
        SkinOn(2, EliteShots.Kind.Shell, ShotMotion.Streak);
        var f = Fire(d, EliteShots.Kind.Shard, new Vector2(0f, 3f), new Vector2(0f, -1.8f));
        var st = Fire(d, EliteShots.Kind.Shell, new Vector2(1f, 3f), new Vector2(0f, -3f));
        Advance(.3f);
        Vector3 pf = f.transform.position, ps = st.transform.position;
        float af = f.Age;
        for (int i = 0; i < 120; i++) EliteSystem.Step(0f);
        Check("a frozen world (dt 0) leaves a leaf and a slug where they were", f.transform.position == pf && st.transform.position == ps && f.Age == af);
        string src = System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Elites/EliteShots.cs");
        bool clean = true;
        foreach (string bad in new[] { "Time.deltaTime", "Time.unscaledDeltaTime", "Time.time" }) clean &= !src.Contains(bad);
        Check("the shot code reads no Time.deltaTime or Time.time (it advances only through Step(dt))", clean);
        EliteSystem.Clear();
        Check("EliteSystem.Clear ends every shot, chip and ghost (" + Active() + " live)", Active() == 0);
    }

    // ---- the pink cue on the procedural bodies ---------------------------------------------------------------------------

    static Color32[] Pixels(Sprite sp)
    {
        var tex = sp.texture;
        var r = sp.rect;
        var all = tex.GetPixels32();
        var res = new Color32[(int)(r.width * r.height)];
        for (int y = 0; y < r.height; y++)
            for (int x = 0; x < r.width; x++)
                res[y * (int)r.width + x] = all[((int)r.y + y) * tex.width + (int)r.x + x];
        return res;
    }

    static bool PinkOrWhite(Color32 c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        float deg = h * 360f;
        return (s < .3f && v > .75f) || (deg >= 300f && deg <= 335f && s > .2f);
    }

    static void DrawnPixels()
    {
        Fresh();
        int n = 0, lowPink = 0, red = 0, hot = 0, heavy = 0, softEdge = 0, flat = 0;
        float minPink = 9f, minEdge = 9f, maxMat = 0f;
        string worstPink = "", worstEdge = "", hotWhere = "";
        foreach (ShotMotionArt.Piece piece in System.Enum.GetValues(typeof(ShotMotionArt.Piece)))
            for (int world = 0; world < ShotSkins.Worlds; world++)
                for (int f = 0; f < 2; f++)
                {
                    var sp = ShotMotionArt.Sprite_(piece, world, f);
                    var px = Pixels(sp);
                    int w = (int)sp.rect.width, h = (int)sp.rect.height;
                    var au = ShotSkinTest.AuditPixels(px);
                    n++;
                    string tag = piece + " w" + world + " f" + f;
                    if (au.PinkShare < minPink) { minPink = au.PinkShare; worstPink = tag; }
                    if (au.PinkShare < ShotSkinTest.MinPinkShare) lowPink++;
                    if (au.red > 0) red++;
                    if (au.PickupShare >= ShotSkinTest.MaxPickupShare) { hot++; hotWhere += tag + "; "; }
                    float mat = au.opaque > 0 ? 1f - au.PinkShare : 0f;
                    if (mat > maxMat) maxMat = mat;
                    if (mat > .5f) heavy++;
                    int edge = 0, edgePink = 0;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = px[y * w + x];
                            if (c.a <= 24) continue;
                            bool e = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                            if (!e)
                                for (int k = 0; k < 4 && !e; k++)
                                {
                                    int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                                    e = px[ny * w + nx].a <= 24;
                                }
                            if (e) { edge++; if (PinkOrWhite(c)) edgePink++; }
                        }
                    float ep = edge > 0 ? edgePink / (float)edge : 1f;
                    if (ep < minEdge) { minEdge = ep; worstEdge = tag; }
                    if (ep < .8f) softEdge++;
                    // PC4: never a smooth disc -- the pod and the others are not round (the box fill of the opaque area is well under a circle's)
                    float fill = au.opaque / (float)(w * h);
                    if (w == h && fill > .78f) flat++;
                }
        Check(n + " procedural bodies (slug, leaf, crescent, log, pod x 5 worlds x 2 frames): at least " + (ShotSkinTest.MinPinkShare * 100f) + "% pink-family or white in every one, the least " + (minPink * 100f).ToString("F0") + "% (" + worstPink + ")", lowPink == 0 && n == 50);
        Check("... no player-red pixel, no saturated pickup hue (" + red + " red, " + hot + " hot: " + hotWhere + "), material at most half (most " + (maxMat * 100f).ToString("F0") + "%)", red == 0 && hot == 0 && heavy == 0);
        Check("... the silhouette edge is the pink-white stroke (least " + (minEdge * 100f).ToString("F0") + "%, " + worstEdge + "; " + softEdge + " under 80%), and no body is a smooth filled square or disc (" + flat + ")", softEdge == 0 && flat == 0);
        var a = Pixels(ShotMotionArt.Sprite_(ShotMotionArt.Piece.Leaf, 2, 0)); var b = Pixels(ShotMotionArt.Sprite_(ShotMotionArt.Piece.Leaf, 2, 1));
        int diff = 0;
        for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) diff++;
        var cres = ShotMotionArt.Sprite_(ShotMotionArt.Piece.Crescent, 2, 0);
        Check("the two frames differ in the core (" + diff + " pixels: stepped flicker, PC5), and the crescent is a long thin blade (" + cres.rect.width + " x " + cres.rect.height + ")", diff >= 4 && cres.rect.width > cres.rect.height * 3f);
    }
}
