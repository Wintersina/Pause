using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using static DodgeBot;

// The fixtures the dodge bot rolls (AttackBudgetTest): one scenario per roster
// attacker, rail mine, elite and boss attack, each driven by the REAL game
// code (EnemyBrain + EnemyVolley, RailMineLaser, EliteShip, BossEncounter),
// stepped Dt at a time, the bot flying a bare transform as the ship.
//
// Ids: "roster:<key>", "elite:<defKey>", "boss:<artKey>:<attack name>".
public static class AttackBudgetScenarios
{
    public static IEnumerable<string> AllIds()
    {
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b == null || !b.Attacks || def.role == EnemyRole.Chaser) continue;
            yield return "roster:" + def.key;
        }
        foreach (var d in EliteCatalog.All) yield return "elite:" + d.key;
        for (int w = 0; w < BossCatalog.All.Length; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            foreach (var a in boss.DefaultAttacks) yield return "boss:" + boss.artKey + ":" + a.name;   // (today's table, whether or not the boss fights with its themed one)
        }
    }

    public static IScenario Make(string id)
    {
        if (id.StartsWith("themed:boss_")) return new Boss(id);   // a NEW boss attack of the themed table (BossThemed), fought with BossDef.themedAttacks on
        if (id.StartsWith("roster:") || id.StartsWith("themed:")) return new Roster(id);
        if (id.StartsWith("elite:")) return new Elite(id);
        if (id.StartsWith("boss:")) return new Boss(id);
        throw new ArgumentException("unknown attack id " + id);
    }

    // ---- TEST-ONLY FIXTURES for the themed area hazards (plan phases 1c / 1d) -----------------------------------
    //
    // No world's table uses EnemyAttack.Blast / Strike yet (the per-world phases switch them on), so the dodge
    // bot rolls them on a fixture: the behaviour of the attack they will replace with the attack swapped.
    // "themed:<name>" -> (the roster key whose body it is, the behaviour). AttackBudgetTest.Themed maps each to the
    // pinned attack it must stay within 1.15x (+2 points) of.
    public static class ThemedFixtures
    {
        public static readonly string[] Ids =
        {
            "themed:frost_cold_blast", "themed:ember_eruption", "themed:frost_icicle_drop",
            "themed:ember_flame_jet", "themed:frost_ray", "themed:tide_pressure_jet", "themed:tide_surf_wave", "themed:space_scan_line",
            "themed:verdant_vine_lash", "themed:space_rail_slug", "themed:frost_splinter_pair", "themed:verdant_leaf_volley",   // phases 1e / 1f
        };


        // the fixtures whose attack is an area hazard (AttackHazard): the fairness suite reads their tells from the real brain
        public static readonly string[] HazardIds = { "themed:frost_cold_blast", "themed:ember_eruption", "themed:frost_icicle_drop", "themed:ember_flame_jet", "themed:frost_ray", "themed:tide_pressure_jet", "themed:tide_surf_wave", "themed:space_scan_line", "themed:verdant_vine_lash" };

        public static string BaseKey(string id)
        {
            switch (id)
            {
                case "themed:frost_cold_blast": return "frost_big";
                case "themed:ember_eruption": return "ember_fighter_3";
                case "themed:frost_icicle_drop": return "frost_fighter_2";
                case "themed:verdant_vine_lash": return "verdant_fighter_4";
                case "themed:space_rail_slug": return "space_fighter_4";
                case "themed:frost_splinter_pair": return "frost_fighter_3";
                case "themed:verdant_leaf_volley": return "verdant_alien";
                case "themed:ember_flame_jet": return "ember_fighter_3";
                case "themed:frost_ray": return "frost_fighter_3";
                case "themed:tide_pressure_jet": return "frost_fighter_2";
                case "themed:tide_surf_wave": return "ember_fighter_4";
                case "themed:space_scan_line": return "space_fighter_3";
                default: return null;
            }
        }

        static BlastSpec BlastForGolem()
        {
            var s = BlastSpec.Wide(1);   // (the standard 3.4 u ring never reaches a ship 5-6 u below a hovering Golem: 0% for the bot and for a ghost)
            s.gapOffsetDeg = 40f;
            return s;
        }

        // The skin a phase-1f fixture shoots with (ShotMotion flags are read from the shot's skin): installed for the roll, removed by Reset / Cleanup.
        public static void InstallSkin(string id)
        {
            switch (id)
            {
                case "themed:space_rail_slug": ShotSkins.Override(0, EliteShots.Kind.Shell, ShotMotionArt.Skin(0, EliteShots.Kind.Shell, ShotMotion.Streak)); break;
                case "themed:frost_splinter_pair": ShotSkins.Override(1, EliteShots.Kind.Shard, ShotMotionArt.Skin(1, EliteShots.Kind.Shard, ShotMotion.Shatter)); break;
                case "themed:verdant_leaf_volley": ShotSkins.Override(2, EliteShots.Kind.Shard, ShotMotionArt.Skin(2, EliteShots.Kind.Shard, ShotMotion.Flutter)); break;
            }
        }

        // Fresh each call (a brain mutates nothing of it, but a fixture must not leak between rolls).
        public static EnemyBehaviour Behaviour(string id)
        {
            switch (id)
            {
                case "themed:frost_cold_blast":   // the Glacier Golem: the 3-shard fan becomes the cold blast; volleys 3 -> 2
                    // (the crack is turned 40 deg off the pilot, toward the lane's middle: he has to step into it, standing still is not safe)
                    return new EnemyBehaviour { key = "frost_big" }.Sway(.3f, 4.5f).Blast(BlastForGolem()).Muzzle(.5f).Timing(.9f, 3.2f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 1.6f, 9f, PilotExit.Climb).Slow().Volleys(2);
                case "themed:ember_eruption":     // Brand: the aimed bolt becomes an eruption of three columns
                    return new EnemyBehaviour { key = "ember_fighter_3" }.Drift(.75f, 1.5f).Strike(StrikeSpec.Standard(3), 3).Timing(.8f, 1.8f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 2.4f, 7f, PilotExit.Run).Volleys(2);
                case "themed:frost_icicle_drop":  // Icicle: the lance bolt becomes an icicle drop on three lanes
                    return new EnemyBehaviour { key = "frost_fighter_2" }.Track(.55f, .9f).Strike(StrikeSpec.Standard(1), 3).Timing(.8f, 2.4f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 2.4f, 5.5f, PilotExit.Peel).Volleys(2);
                case "themed:verdant_vine_lash":  // Hornet Queen: the fan of three stingers becomes a vine lash (volleys 4 -> 2)
                    return new EnemyBehaviour { key = "verdant_fighter_4" }.Track(.5f, .6f).Brake(1.8f, .65f).Lash(LashSpec.Standard(2)).Muzzle(.4f).Timing(.9f, 2.8f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 1.6f, 9f, PilotExit.Climb).Volleys(2);
                case "themed:space_rail_slug":    // Warden: the heavy shell becomes a rail-gun slug (Streak: 6 u/s, a longer charge 1.1 s, volleys 4 -> 3)
                    return new EnemyBehaviour { key = "space_fighter_4" }.Track(.5f, .5f).Brake(1.8f, .65f).Shot(EliteShots.Kind.Shell, 1, 0f, 3.4f, .3f, 28f).Muzzle(.4f).Timing(1.1f, 2.4f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 1.6f, 9f, PilotExit.Climb).Volleys(3);
                case "themed:frost_splinter_pair": // Frost Kite: the splayed pair of shards splits once in flight (Shatter; volleys 3 -> 2)
                    return new EnemyBehaviour { key = "frost_fighter_3" }.Orbit(.5f, 1.5f).Shot(EliteShots.Kind.Shard, 2, 34f, 3f, .18f).Timing(.55f, 2.2f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 2.6f, 7f, PilotExit.Run).Volleys(2);
                case "themed:verdant_leaf_volley": // Snap Sprout: a pair of fluttering leaves, in the place of a Twin Claw's pair of bolts (compared with roster:space_fighter_3)
                    return new EnemyBehaviour { key = "verdant_alien" }.Sway(.5f, 2.2f).Shot(EliteShots.Kind.Shard, 2, 20f, 1.8f, .2f).Timing(.6f, 2.4f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 2f, 7f, PilotExit.Peel).Volleys(2);
                // ---- phases 1a / 1b: the jet and the wave. A jet reaches its locked target, so the fixtures hold their shooters
                // lower on the screen (station 4.8 u below the top of the view = 3.6-4.6 u above the bot's ship) than the table's, as the Golem's wide ring did.
                case "themed:ember_flame_jet":    // Brand: the aimed bolt becomes a flamethrower cone swept 10 deg (design: cooldown 1.8 -> 2.6, volleys 3 -> 2)
                    return new EnemyBehaviour { key = "ember_fighter_3" }.Drift(.75f, 1.5f).Jet(JetSpec.Flame(3)).Timing(.8f, 2.6f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 4.8f, 7f, PilotExit.Run).Volleys(2);
                case "themed:frost_ray":          // Kite: the splayed shard pair becomes a thin frost ray
                    return new EnemyBehaviour { key = "frost_fighter_3" }.Orbit(.5f, 1.5f).Jet(JetSpec.Ray(1)).Timing(.8f, 2.6f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 4.8f, 7f, PilotExit.Run).Volleys(2);
                case "themed:tide_pressure_jet":  // Icicle's body: the lance bolt becomes a pressure jet (a straight column of water)
                    return new EnemyBehaviour { key = "frost_fighter_2" }.Track(.55f, .9f).Jet(JetSpec.Pressure(4)).Timing(.8f, 2.4f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 4.8f, 5.5f, PilotExit.Peel).Volleys(2);
                case "themed:tide_surf_wave":     // Pyre's body (Hammerhead's stand-in): the ring of eight becomes a surf wave with a 1.6 u gap turned .8 u off the pilot; volleys 3 -> 2
                    return new EnemyBehaviour { key = "ember_fighter_4" }.Track(.5f, .4f).Brake(1.8f, .65f).Wave(SurfWithOffset()).Muzzle(0f).Timing(1.1f, 3.8f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 1.5f, 9f, PilotExit.Climb).Volleys(2);
                case "themed:space_scan_line":    // Twin Claw's body: the Archon's scan line (a thin neon wave, 3 u/s) -- a NEW boss attack with no roster predecessor, held to the Space fighters' middle tier
                    return new EnemyBehaviour { key = "space_fighter_3" }.Sway(.7f, 2.6f).Wave(ScanWithOffset()).Timing(1.0f, 3.0f, 2, .15f)
                        .Pilot(PilotEntry.Drop, 2f, 7f, PilotExit.Peel).Volleys(2);
                default: return null;
            }
        }

        static WaveSpec SurfWithOffset() { var s = WaveSpec.Surf(4); s.gapOffset = .8f; return s; }   // (a gap on the pilot would make standing still safe)
        static WaveSpec ScanWithOffset() { var s = WaveSpec.Scan(0); s.gapOffset = 1f; return s; }
    }

    // The shape of a roster attack as the table says it (volleys, cooldown, count): pinned beside the hit rate.
    public static string Shape(string id)
    {
        if (id.StartsWith("themed:")) { var f = ThemedFixtures.Behaviour(id); return f == null ? "" : f.attack + " x" + f.ThreatCount + " volleys " + f.maxVolleys + " cooldown " + f.cooldown.ToString("0.0#") + " tell " + f.tell.ToString("0.0#"); }
        if (!id.StartsWith("roster:")) return "";
        var b = EnemyBehaviours.For(id.Substring(7));
        return b == null ? "" : b.attack + " x" + b.shotCount + " volleys " + b.maxVolleys + " cooldown " + b.cooldown.ToString("0.0#") + " tell " + b.tell.ToString("0.0#");
    }

    // ---- world plumbing ------------------------------------------------------

    public static Transform Ship;

    public static Transform EnsureShip(Vector2 at)
    {
        if (Ship == null) Ship = new GameObject("~DodgeBotShip").transform;
        Ship.position = new Vector3(at.x, at.y, 0f);
        EliteSystem.PlayerOverride = Ship;
        return Ship;
    }

    public static void Reset()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        EnemyThreat.Reset();
        PilotAirspace.Clear();
        PilotAirspace.ResetStats();
        EnemyBrain.PilotsEnabled = true;
        EnemyBehaviours.ClearOverrides();
        AttackPools.Forget();
        AttackHazard.ForgetAll();
        AttackHazardArt.Forget();
        ShotSkins.ResetForTests();
        lashRoots.Clear();
        ShotOutline.Bold = false;
        EnemyThreat.ForceShooting = true;
        BossEncounter.ResetRun();
        BossRails.Reset();
        ScreenInfo.ClearOverride();
        FriendlyFire.ResetCounters();
        FriendlyFire.OnSceneLoaded("gameS1");
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        RailMineLaser.AngleOverride = null;
        Ship = null;
        EnsureShip(new Vector2(0f, -3f));
        SpawnSpace.ClockOverride = 100f;
    }

    public static void Cleanup()
    {
        EliteSystem.Clear();
        ShotSkins.ResetForTests();
        EnemyBehaviours.ClearOverrides();
        ShotOutline.Bold = null;
        EliteSystem.PlayerOverride = null;
        EnemyThreat.ForceShooting = false;
        EnemyThreat.Reset();
        SpawnSpace.ClockOverride = null;
        BossEncounter.ResetRun();
        BossRails.Reset();
        RailMineLaser.AngleOverride = null;
        buttonClicks.playerDied = false;
        moveBackGround.speed = 0f;
        RunScore.EndRun(RunScore.RunId);
    }

    // shots in flight, lobs in the air (their landing spot), landed pools
    static void CollectShots(List<Hz> into)
    {
        var pool = EliteSystem.ShotsIfAny;
        if (pool == null) return;
        var all = pool.All;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null || !s.Active) continue;
            if (s.Airborne)
            {
                // a lobbed trunk (Roll): harmful where it comes down for an instant, then it rolls on (its own circle, below); the dotted lane it rolls along is known
                if ((s.Motion & ShotMotion.Roll) != 0)
                {
                    into.Add(Hz.Circle(100 + i, s.LobTarget, s.Radius + .05f, s.LobRemaining, .3f));
                    Vector2 rd = LogRollAttack.RollDir(s.LobTarget.x);
                    var lane = Hz.Segment(2400 + i, s.LobTarget, s.LobTarget + rd * LogRollAttack.LaneLength, s.Radius + LogRollAttack.PathHalf, s.LobRemaining, 4f);
                    lane.planOnly = true; lane.hasV = true; lane.va = lane.vb = new Vector2(0f, -EliteSystem.Scroll);
                    into.Add(lane);
                    continue;
                }
                into.Add(Hz.Circle(100 + i, s.LobTarget, s.PoolRadius, s.LobRemaining, s.PoolSeconds)); continue;
            }
            // a crescent (Slash) is hit along its whole length
            if (s.SlashCapsule)
            {
                Vector2 ax = s.SlashAxis * (ShotMotions.SlashLength * .5f), c0 = s.transform.position;
                into.Add(Hz.Segment(100 + i, c0 - ax, c0 + ax, s.HitRadiusOfCollider));
                continue;
            }
            // a spore pod (Burst): the ring its spores will cover is drawn, so the bot knows it
            if ((s.Motion & ShotMotion.Burst) != 0 && !s.IsChip && s.Age < ShotMotions.BurstSeconds)
            {
                float left = ShotMotions.BurstSeconds - s.Age;
                Vector2 at = (Vector2)s.transform.position + s.Velocity * left;
                var ring = Hz.Circle(2500 + i, at, SporeBurstAttack.Reach + .2f, left, ShotMotions.SporeSeconds);
                ring.planOnly = true;
                into.Add(ring);
            }
            // a Flutter leaf is read by its course: the bot follows the straight line and keeps the weave's width clear
            if ((s.Motion & ShotMotion.Flutter) != 0) { into.Add(Hz.Circle(100 + i, s.CoursePosition, s.Radius + ShotMotions.FlutterAmp)); continue; }
            float splitIn = s.SplitIn;
            if (splitIn >= 0f)
            {
                // a Shatter spear splits at a known moment: the bot sees the spear until then and the three chips after
                Vector2 va = s.Velocity + new Vector2(0f, -EliteSystem.Scroll * s.Ride);
                var spear = Hz.Circle(100 + i, s.transform.position, s.Radius, 0f, splitIn);
                spear.hasV = true; spear.va = spear.vb = va;
                into.Add(spear);
                Vector2 at = (Vector2)s.transform.position + va * splitIn;
                for (int c = 0; c < ShotMotions.ShatterChips; c++)
                {
                    Vector2 cv = EliteShot.ChipVelocity(s.Velocity, c, ShotMotions.ShatterChips, s.Ride);
                    var chip = Hz.Circle(2000 + i * 3 + c, at - cv * splitIn, s.Radius * ShotMotions.ChipScale, splitIn, ShotMotions.ChipSeconds);
                    chip.hasV = true; chip.va = chip.vb = cv;
                    into.Add(chip);
                }
                continue;
            }
            into.Add(Hz.Circle(100 + i, s.transform.position, s.Radius));
        }
    }

    static int ActiveRosterShots()
    {
        var pool = EliteSystem.ShotsIfAny;
        if (pool == null) return 0;
        int n = 0;
        foreach (var s in pool.All) if (s != null && s.Active) n++;
        return n;
    }

    // The themed area hazards as the bot sees them: a ring's bars (segments, the expansion known during the tell: the
    // preview shows the first ring and the crack), a strike's column (a segment, live from the end of its tell).
    static readonly Vector2[] lashJoints = new Vector2[AttackLash.MaxLinks + 1];
    static readonly Dictionary<AttackLash, Vector2> lashRoots = new Dictionary<AttackLash, Vector2>();

    static void CollectHazards(List<Hz> into)
    {
        var all = AttackHazard.All;
        for (int h = 0; h < all.Count; h++)
        {
            var hz = all[h];
            if (hz == null || !hz.Active || hz.State == AttackHazard.Phase.After) continue;
            int baseId = 1000 + h * 48;
            var blast = hz as AttackBlast;
            var strike = hz as AttackStrike;
            var lash = hz as AttackLash;
            if (lash != null)
            {
                // the sweep is drawn from the first frame of the tell: the bot knows where every link of the chain will be, at any moment of it
                // (Hz.segAt: the chain is evaluated for the moment the bot asks about; the root end of it lies on the start line while the tip runs ahead)
                int lb = 4000 + h * 100;
                bool tell = lash.State == AttackHazard.Phase.Tell;
                float sweep = lash.SweepSeconds, done = tell ? 0f : lash.Age, left = tell ? lash.TellLeft : 0f;
                int n = lash.Links;
                var lspec = lash.Spec;
                float length = lash.Length, startRad = lash.StartRad;
                int dir = lash.Dir;
                Vector2 root = lash.Root, vRoot = Vector2.zero;
                // (the root rides its shooter: the bot reads the shooter's drift off two frames, as it does a shot's speed)
                if (lashRoots.TryGetValue(lash, out Vector2 was)) vRoot = (root - was) / DodgeBot.Dt;
                lashRoots[lash] = root;
                for (int li = 0; li < n; li++)
                {
                    int link = li;
                    var hzd = Hz.Segment(lb + li, root, root, lash.HitHalf + .04f, left, sweep - done);
                    hzd.planOnly = true;
                    hzd.segAt = since =>
                    {
                        var j = new Vector2[AttackLash.MaxLinks + 1];
                        AttackLash.Chain(in lspec, n, length, root + vRoot * since, startRad, dir, Mathf.Clamp(since - left + done, 0f, sweep), j);
                        return new Vector4(j[link].x, j[link].y, j[link + 1].x, j[link + 1].y);
                    };
                    into.Add(hzd);
                }
                if (!tell)
                {
                    // what actually burns this frame: the links of the chain now, scored; they are gone from the bot's plan a moment later
                    AttackLash.Chain(lash.Spec, n, lash.Length, lash.Root, lash.StartRad, lash.Dir, done, lashJoints);
                    for (int li = 0; li < n; li++)
                    {
                        var now = Hz.Segment(lb + 50 + li, lashJoints[li], lashJoints[li + 1], lash.HitHalf, 0f, .001f);
                        now.hasV = true;
                        into.Add(now);
                    }
                }
                continue;
            }
            var jet = hz as AttackJet;
            var wave = hz as AttackWave;
            if (jet != null)
            {
                // a cone / column as eight capsules along its axis, each as wide as the jet is at its middle. That is where the jet IS (the current
                // direction). The preview shows the whole swept area (start footprint, end footprint, the arc), so the bot also KNOWS the sector from
                // the first frame of the tell (copies at the start, the middle and the end of the sweep, flagged planOnly: avoided, never scored):
                // a human steers clear of the swept sector, not of where the tip is now.
                bool tellJ = jet.State == AttackHazard.Phase.Tell;
                float jIn = tellJ ? jet.TellLeft : 0f;
                float jFor = tellJ ? jet.LiveSeconds : Mathf.Max(0f, jet.LiveSeconds - jet.Age);
                float dirNow = tellJ ? jet.StartRad : jet.Direction, dirEnd = jet.StartRad + jet.SweepRadians;
                bool sweeps = Mathf.Abs(jet.SweepRadians) > .02f;
                int copies = sweeps ? 4 : 1;   // 0 = where it is; 1..3 = the sector (start, middle, end)
                for (int c = 0; c < copies; c++)
                {
                    float dir = c == 0 ? dirNow : Mathf.Lerp(jet.StartRad, dirEnd, (c - 1) / 2f);
                    Vector2 u = AttackJet.Dir(dir);
                    for (int k = 0; k < 8; k++)
                    {
                        float s0 = jet.Length * k / 8f, s1 = jet.Length * (k + 1) / 8f;
                        float half = Mathf.Lerp(jet.BaseHalf, jet.TipHalf, (k + .5f) / 8f);
                        var hz1 = Hz.Segment(baseId + c * 8 + k, jet.Origin + u * s0, jet.Origin + u * s1, half, jIn, jFor);
                        hz1.hasV = true; hz1.va = hz1.vb = new Vector2(0f, -EliteSystem.Scroll * jet.Spec.ride);   // (it is where the outline is drawn: the bot extrapolates nothing)
                        hz1.planOnly = c > 0;
                        into.Add(hz1);
                    }
                }
                continue;
            }
            if (wave != null)
            {
                // two bars either side of the gap, falling at the world speed (known from the tell: back-projected to now)
                bool tellW = wave.State == AttackHazard.Phase.Tell;
                float v = wave.FallSpeed + EliteSystem.Scroll * wave.Spec.ride;
                float jIn = tellW ? wave.TellLeft : 0f;
                float y0 = tellW ? wave.StartHeight + v * jIn : wave.Y;
                float jFor = Mathf.Max(0f, (tellW ? wave.StartHeight : wave.Y) - wave.EndHeight) / v;
                AttackWave.Extents(wave.GapX, wave.GapWidth, wave.HalfWidth, out float leftTo, out float rightFrom);
                if (wave.HasLeft)
                {
                    var l = Hz.Segment(baseId, new Vector2(-wave.HalfWidth, y0), new Vector2(leftTo, y0), wave.HitHalf, jIn, jFor);
                    l.hasV = true; l.va = l.vb = new Vector2(0f, -v);
                    into.Add(l);
                }
                if (wave.HasRight)
                {
                    var rb = Hz.Segment(baseId + 1, new Vector2(rightFrom, y0), new Vector2(wave.HalfWidth, y0), wave.HitHalf, jIn, jFor);
                    rb.hasV = true; rb.va = rb.vb = new Vector2(0f, -v);
                    into.Add(rb);
                }
                continue;
            }
            if (strike != null)
            {
                AttackStrike.Column(strike.Spec, strike.LaneX, strike.ImpactY, out Vector2 foot, out Vector2 top);
                bool tell = strike.State == AttackHazard.Phase.Tell;
                into.Add(Hz.Segment(baseId, foot, top, strike.HitHalf, tell ? strike.TellLeft : 0f, tell ? strike.LiveSeconds : Mathf.Max(0f, strike.LiveSeconds - strike.Age)));
                continue;
            }
            if (blast == null) continue;
            var spec = blast.Spec;
            bool telling = blast.State == AttackHazard.Phase.Tell;
            float r = telling ? spec.startRadius : blast.Radius;
            float speed = blast.RadialSpeed;
            float liveIn = telling ? blast.TellLeft : 0f;
            float liveFor = Mathf.Max(0f, (spec.reach - r) / speed);
            const float d = .05f;
            for (int k = 0; k < blast.Bars; k++)
            {
                AttackBlast.BarAt(in spec, blast.Origin, blast.GapRad, r, k, out Vector2 a, out Vector2 b, out float ang);
                AttackBlast.BarAt(in spec, blast.Origin, blast.GapRad, r + d, k, out Vector2 a2, out Vector2 b2, out float ang2);
                Vector2 va = (a2 - a) / (d / speed), vb = (b2 - b) / (d / speed);
                if (telling)
                {
                    var hzd = Hz.Segment(baseId + k, a - va * liveIn, b - vb * liveIn, spec.barHalf, liveIn, liveFor);
                    hzd.hasV = true; hzd.va = va; hzd.vb = vb;
                    into.Add(hzd);
                }
                else
                {
                    var hzd = Hz.Segment(baseId + k, a, b, spec.barHalf, 0f, liveFor);
                    hzd.hasV = true; hzd.va = va; hzd.vb = vb;
                    into.Add(hzd);
                }
            }
        }
    }

    // ---- roster enemies and mines ------------------------------------------

    sealed class Roster : IScenario
    {
        readonly EnemyDef def;
        readonly EnemyBehaviour b;
        EnemyBrain brain;
        GameObject rail;
        float clock;
        float t;
        bool attacked, sawLaser;
        Transform ship;

        public Roster(string id)
        {
            bool themed = id.StartsWith("themed:");
            def = EnemyRoster.Find(themed ? ThemedFixtures.BaseKey(id) : id.Substring(7));
            b = themed ? ThemedFixtures.Behaviour(id) : EnemyBehaviours.For(def.key);
            Name = id;
            if (themed) { fixture = b; ThemedFixtures.InstallSkin(id); }
        }
        readonly EnemyBehaviour fixture;
        public string Name { get; }
        public float MaxSeconds => b.attack == EnemyAttack.Laser ? 14f : 16f;
        public bool Attacked => attacked;
        public bool Telling => brain != null && brain.State == EnemyBrain.Phase.Windup && b.attack != EnemyAttack.Laser && b.attack != EnemyAttack.Lunge;
        public bool Done => (brain == null || brain.Stage == EnemyBrain.PilotStage.Gone || (brain.RideFinished && !sawLaserLive())) && ActiveRosterShots() == 0 && AttackHazard.ActiveCount == 0 && t > 1f && attacked
                            || t > MaxSeconds - .1f;

        bool sawLaserLive()
        {
            for (int i = 0; i < RailMineLasers.All.Count; i++) if (RailMineLasers.All[i] != null && RailMineLasers.All[i].Active) return true;
            return false;
        }

        public Transform Begin(System.Random rng, Vector2 start)
        {
            ship = EnsureShip(start);
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = ship;
            EnemyThreat.Reset();
            PilotAirspace.Clear();
            if (fixture != null) EnemyBehaviours.TestOverride(def.key, fixture);   // (a themed fixture: the body of one attacker, the attack of the new one)
            clock = 100f;
            SpawnSpace.ClockOverride = clock;
            t = 0f;
            attacked = sawLaser = false;
            RailMineLaser.Seed((uint)rng.Next(1, int.MaxValue));
            float x = Mathf.Lerp(-1.5f, 1.5f, (float)rng.NextDouble());
            if (b.attack == EnemyAttack.Laser)
            {
                bool right = rng.NextDouble() < .5;
                float rx = enmiesOnBoard.WorldRailX(!right);
                rail = new GameObject("RailMineLane");
                rail.transform.position = new Vector3(rx, start.y, 0f);
                rail.AddComponent<RailLaneScroller>();
                var go = EnemyFactory.Create(def, new Vector3(rx, start.y, 0f), Quaternion.identity);
                brain = go.GetComponent<EnemyBrain>();
                var mount = go.AddComponent<RailMineMount>();
                mount.MountTo(rail.transform);
                mount.brain = brain;
                brain.TargetOverride = ship;
                return ship;
            }
            for (int k = 0; k < 200; k++)
            {
                var go = EnemyFactory.Create(def, new Vector3(x, 1.8f, 0f), Quaternion.identity);
                brain = go.GetComponent<EnemyBrain>();
                if (brain == null) return ship;
                brain.TargetOverride = ship;
                if (!b.Attacks || brain.Armed) return ship;
                UnityEngine.Object.DestroyImmediate(go);
                brain = null;
            }
            return ship;
        }

        public void Step(float dt)
        {
            if (brain == null) return;
            t += dt;
            clock += dt;
            SpawnSpace.ClockOverride = clock;
            brain.Step(dt);
            var mount = brain.GetComponent<RailMineMount>();
            if (mount != null) mount.SendMessage("LateUpdate");
            var fb = brain.GetComponent<EnemyFlipbook>();
            if (fb != null) fb.Advance(dt);
            EliteSystem.Step(dt);
            var l = brain.Laser;
            if (l != null) l.Place();
            if (brain.Windups > 0 || (brain.IsPilot && brain.Stage == EnemyBrain.PilotStage.Exiting && brain.LeftBy == PilotExit.Run)) attacked = true;
        }

        public void Collect(List<Hz> into)
        {
            CollectShots(into);
            CollectHazards(into);
            if (brain == null) return;
            var lasers = RailMineLasers.All;
            float tellTotal = Mathf.Max(EnemyBrain.TellFloorSeconds, b.tell);
            for (int i = 0; i < lasers.Count; i++)
            {
                var l = lasers[i];
                if (l == null || !l.Active) continue;
                if (l.State == RailMineLaser.Phase.Beam)
                    into.Add(Hz.Segment(200 + i, l.From, l.To, RailMineLaser.HitThickness * .5f, 0f, Mathf.Max(0f, RailMineLaser.BeamSeconds - l.PhaseTime)));
                else if (l.State == RailMineLaser.Phase.Aim && l.SightShown)
                    into.Add(Hz.Segment(200 + i, l.From, l.To, RailMineLaser.HitThickness * .5f, Mathf.Max(0f, tellTotal - l.PhaseTime), RailMineLaser.BeamSeconds));
            }
            if (!brain.isActiveAndEnabled) return;
            // the body: harmful only while it dashes (a lunge) or runs out the bottom
            var col = brain.GetComponentInChildren<Collider2D>();
            float r = col != null ? Mathf.Max(.2f, (col.bounds.extents.x + col.bounds.extents.y) * .5f * .9f) : .4f;
            bool dash = (brain.State == EnemyBrain.Phase.Release && b.attack == EnemyAttack.Lunge) ||
                        (brain.IsPilot && brain.Stage == EnemyBrain.PilotStage.Exiting && brain.LeftBy == PilotExit.Run);
            Vector2 p = brain.transform.position;
            into.Add(Hz.Circle(300, p, r, dash ? 0f : float.PositiveInfinity));
            if (brain.LungeAhead(out Vector2 reach, out float inS))
                into.Add(Hz.Segment(301, p, p + reach, r, inS, b.lungeSeconds + .2f));
        }

        public void End()
        {
            if (brain != null) UnityEngine.Object.DestroyImmediate(brain.gameObject);
            brain = null;
            if (rail != null) UnityEngine.Object.DestroyImmediate(rail);
            rail = null;
            EliteSystem.Clear();
        }
    }

    // ---- elites --------------------------------------------------------------

    sealed class Elite : IScenario
    {
        readonly EliteDef def;
        EliteShip ship;
        float t;
        Transform pilot;

        public Elite(string id) { def = EliteCatalog.Find(id.Substring(6)); Name = id; }
        public string Name { get; }
        public float MaxSeconds => 10f;
        public bool Attacked => ship != null && (ship.Attacks > 0 || EliteSystem.ShotsIfAny != null && EliteSystem.ShotsIfAny.Launched > 0);
        public bool Telling => ship != null && ship.Telling && !Rams;
        // one attack per roll: once the first is over, no second is begun; done when its shots are gone too
        public bool Done => ship != null && ship.Attacks >= 1 && ship.State != EliteState.Attack && ActiveRosterShots() == 0 && t > 1f;
        bool Rams => def.attack == "lance_dash" || def.attack == "claw_dive" || def.attack == "ice_ram" || def.attack == "leaf_dive";
        float tellLeft;

        public Transform Begin(System.Random rng, Vector2 start)
        {
            pilot = EnsureShip(start);
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = pilot;
            t = 0f;
            UnityEngine.Random.InitState(rng.Next());
            PlayerInvuln.Reset();
            float x = Mathf.Lerp(-1.6f, 1.6f, (float)rng.NextDouble());
            ship = EliteShip.CreateInPlay(def, new Vector2(x, 1.4f));
            ship.AttackCooldown = .3f;
            ship.EscapeLeft = 0f;
            return pilot;
        }

        public void Step(float dt)
        {
            t += dt;
            if (ship != null && ship.Attacks >= 1 && ship.State != EliteState.Attack) ship.AttackCooldown = 99f;
            EliteSystem.Step(dt);
            tellLeft = ship != null && ship.Telling ? Mathf.Max(0f, ship.Attack.TellSeconds - ship.AttackClock) : 0f;
        }

        // What the wind-up draws (the dotted outline of the attack): known to the bot from the first frame, never scored
        void CollectKnown(List<Hz> into)
        {
            if (ship == null || ship.State == EliteState.Dead) return;
            // (the drawing stays on screen until the crescent is away: a dive's lane and row are still known while it dives)
            var diving = ship.Attack as LeafDiveAttack;
            if (!ship.Telling && !(ship.Acting && diving != null)) return;
            var board = new Vector2(0f, -EliteSystem.Scroll);
            var lr = ship.Attack as LogRollAttack;
            if (lr != null)
            {
                for (int i = 0; i < Mathf.Max(1, def.shotCount); i++)
                {
                    Vector2 sp = lr.Spot(i);
                    float land = tellLeft + def.lobSeconds;
                    var ring = Hz.Circle(2600 + i, sp, def.shotSize * .4f + .05f, land, .3f);
                    ring.planOnly = ring.hasV = true; ring.va = ring.vb = board;
                    into.Add(ring);
                    var lane = Hz.Segment(2610 + i, sp, sp + LogRollAttack.RollDir(sp.x) * LogRollAttack.LaneLength, def.shotSize * .4f + LogRollAttack.PathHalf, land, 4f);
                    lane.planOnly = lane.hasV = true; lane.va = lane.vb = board;
                    into.Add(lane);
                }
            }
            var sb = ship.Attack as SporeBurstAttack;
            if (sb != null)
            {
                for (int i = 0; i < Mathf.Max(1, def.shotCount); i++)
                {
                    var ring = Hz.Circle(2620 + i, sb.Burst(i), SporeBurstAttack.Reach + .2f, tellLeft + def.shotInterval * i + ShotMotions.BurstSeconds, ShotMotions.SporeSeconds);
                    ring.planOnly = true;
                    into.Add(ring);
                }
            }
            var ld = ship.Attack as LeafDiveAttack;
            if (ld != null)
            {
                Vector2 dd = (ld.Aim - ld.DiveFrom).sqrMagnitude > 1e-4f ? (ld.Aim - ld.DiveFrom).normalized : Vector2.down;
                var dive = Hz.Segment(2640, ld.DiveFrom, ld.Aim + dd * .75f, def.hullRadius + LeafDiveAttack.DiveHalfPad, tellLeft, def.actionSeconds);
                dive.planOnly = true;
                into.Add(dive);
                float edge = EliteSystem.RailEdge;
                var row = Hz.Segment(2641, new Vector2(ld.Aim.x, ld.Aim.y), new Vector2(ld.SlashDeg > 90f ? -edge : edge, ld.Aim.y), LeafDiveAttack.SlashHalf, tellLeft + .1f, 3f);
                row.planOnly = true;
                into.Add(row);
            }
        }

        public void Collect(List<Hz> into)
        {
            CollectShots(into);
            CollectHazards(into);
            CollectKnown(into);
            // the hull is a hazard only while it rams (a dash / dive / ram attack in its action phase)
            if (ship != null && ship.State != EliteState.Dead)
                into.Add(Hz.Circle(300, ship.transform.position, def.hullRadius, ship.Acting && Rams ? 0f : float.PositiveInfinity));
        }

        public void End() { EliteSystem.Clear(); ship = null; }
    }

    // ---- bosses ---------------------------------------------------------------

    // "themed:boss_<artkey>_<attack name with underscores>" for each NEW attack of BossThemed's tables (plan phase 1g).
    public static IEnumerable<string> ThemedBossIds()
    {
        for (int w = 0; w < BossCatalog.All.Length; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            if (boss.themed == null) continue;
            foreach (var a in boss.themed)
                if (System.Array.IndexOf(boss.DefaultAttacks, a) < 0) yield return ThemedBossId(boss, a);
        }
    }

    public static string ThemedBossId(BossDef boss, BossAttack a) => "themed:boss_" + boss.artKey.ToLower() + "_" + a.name.Replace(' ', '_');

    sealed class Boss : IScenario
    {
        readonly int world, attackIndex;
        readonly bool themedBoss;
        BossDef themedDef;
        bool wasThemed;
        BossEncounter e;
        float t;
        static readonly FieldInfo PlayerField = typeof(BossEncounter).GetField("player", BindingFlags.NonPublic | BindingFlags.Instance);

        public Boss(string id)
        {
            Name = id;
            string[] parts = id.Split(new[] { ':' }, 3);
            if (id.StartsWith("themed:boss_"))
            {
                themedBoss = true;
                for (int w = 0; w < BossCatalog.All.Length; w++)
                {
                    var boss = BossCatalog.ForWorld(w);
                    if (boss.themed == null) continue;
                    for (int i = 0; i < boss.themed.Length; i++)
                        if (ThemedBossId(boss, boss.themed[i]) == id) { world = w; attackIndex = i; themedDef = boss; }
                }
                return;
            }
            for (int w = 0; w < BossCatalog.All.Length; w++)
            {
                var boss = BossCatalog.ForWorld(w);
                if (boss.artKey != parts[1]) continue;
                for (int i = 0; i < boss.DefaultAttacks.Length; i++)
                    if (boss.DefaultAttacks[i].name == parts[2]) { world = w; attackIndex = i; themedDef = boss; }
            }
        }

        public string Name { get; }
        public float MaxSeconds => themedBoss ? 26f : 12f;
        public bool Attacked => e != null && e.Actor != null && e.Actor.AttacksStarted > 0;
        public bool Telling => e != null && e.Actor != null && e.Actor.Telegraphing && e.Actor.CurrentAttack != null && e.Actor.CurrentAttack.kind != BossAttackKind.Beam;
        public bool Done => e != null && e.Actor != null && e.Actor.AttacksStarted >= 3 && e.Pool.ActiveShots == 0 && e.Pool.ActiveBeams == 0 && AttackHazard.ActiveCount == 0 && !e.Actor.HazardPhase;

        public Transform Begin(System.Random rng, Vector2 start)
        {
            var ship = EnsureShip(start);
            BossEncounter.ResetRun();
            BossRails.Reset();
            // (a pinned "boss:" id is today's attack in today's table even for a boss that now fights themed; a "themed:boss_" id is the new table)
            if (themedDef != null) { wasThemed = themedDef.themedAttacks; themedDef.themedAttacks = themedBoss; }
            if (Camera.main == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera));
                camGo.tag = "MainCamera";
                var cam = camGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 5f;
                camGo.transform.position = new Vector3(0f, 0f, -10f);
            }
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            moveBackGround.speed = .37f;
            Time.timeScale = 1f;
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
            t = 0f;
            BossEncounter.Begin(world, null);
            e = BossEncounter.Instance;
            PlayerField.SetValue(e, ship);
            e.Step(.1f, 1f);
            for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
            e.Actor.ForcedAttack = attackIndex;
            return ship;
        }

        public void Step(float dt) { t += dt; if (e != null) e.Step(dt, 1f); }

        public void Collect(List<Hz> into)
        {
            if (e == null || e.Pool == null) return;
            CollectHazards(into);
            var shots = e.Pool.Shots;
            for (int i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                if (s == null || !s.Active || s.Hitbox == null) continue;
                into.Add(Hz.Circle(400 + i, s.transform.position, s.Radius));
            }
            var beams = e.Pool.Beams;
            for (int i = 0; i < beams.Count; i++)
            {
                var bm = beams[i];
                if (bm == null || !bm.Active) { if (i < hadOrigin.Length) hadOrigin[i] = false; continue; }
                Vector2 o = bm.Origin, d = bm.Direction;
                float half = bm.Width * BossConfig.BeamHitFraction * .5f;
                // (live: the drawn length is the hitbox; the telegraph runs the whole way to the rail)
                if (bm.Live) into.Add(Hz.Segment(600 + i, o, o + d * bm.Length, half, 0f, Mathf.Max(.05f, bm.HoldLeft)));
                else if (bm.Telegraphing)
                {
                    // (the sight line scans the arc, so it is the ARC that is known: ignite at StartDeg, swing at SweepDeg / hold)
                    Vector2 d0 = BossBeam.Heading(bm.StartDeg);
                    var seg = Hz.Segment(600 + i, o, o + d0 * 9f, half, bm.TellLeft, Mathf.Max(.2f, bm.HoldTotal));
                    float omega = bm.SweepDeg / Mathf.Max(.05f, bm.HoldTotal) * Mathf.Deg2Rad;
                    // (the part the beam grows from sways with the boss: its measured velocity carries the whole line)
                    Vector2 ov = i < prevOrigin.Length && hadOrigin[i] ? (o - prevOrigin[i]) / DodgeBot.Dt : Vector2.zero;
                    seg.va = seg.vb = ov;
                    seg.vbExtra = new Vector2(-d0.y, d0.x) * (omega * 9f);
                    seg.hasV = true;
                    seg.moveFrom = bm.TellLeft;
                    into.Add(seg);
                }
                if (i < prevOrigin.Length) { prevOrigin[i] = o; hadOrigin[i] = true; }
            }
        }

        readonly Vector2[] prevOrigin = new Vector2[16];
        readonly bool[] hadOrigin = new bool[16];

        public void End()
        {
            BossEncounter.ResetRun();
            BossRails.Reset();
            if (themedDef != null) themedDef.themedAttacks = wasThemed;
            for (int i = 0; i < hadOrigin.Length; i++) hadOrigin[i] = false;
        }
    }
}
