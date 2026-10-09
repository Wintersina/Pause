using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frost's four newer elites (Floe Harrower, Cryo Siren, Glacier Tender,
// Whiteout Sentinel; FrostElites.cs): the roster and their strips, muzzles
// and nozzles on the hull, launching from their own kinds of Frost ground
// site (and any free one when theirs is not in view), and each one's brain
// and attack in simulation -- the herder cutting off the pilot's lane and
// funnelling it through one gap in a row of slabs with a lance down it, the
// kiter keeping its range with stutter steps and its orb bursting into a
// ring (or a painted beam sweep), the tender fleeing, deploying tethered
// drones and shield-linked while two live, the ironclad plodding straight
// at the pilot without dodging, its plates shattering in order into
// telegraphed sprays, then charging -- tell timings, clean-up on death, no
// overlaps and zero per-frame allocation.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites FrostEliteTest
public static class FrostEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FEL] PASS  " : "[FEL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    // key -> brain, attack, the site kinds it launches from
    static readonly (string key, string brain, string attack, LandingKind[] from)[] Expected =
    {
        ("frost_elite_floe_harrower", "herder", "floe_cast", new[] { LandingKind.Hangar, LandingKind.CrawlerBay }),
        ("frost_elite_cryo_siren", "kiter", "frost_bloom", new[] { LandingKind.RigBay }),
        ("frost_elite_glacier_tender", "tender", "drone_deploy", new[] { LandingKind.CrawlerBay }),
        ("frost_elite_whiteout_sentinel", "ironclad", "armour_shatter", new[] { LandingKind.PadRing, LandingKind.Hatch }),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Roster();
            Sites();
            foreach (var x in Expected) Cycle(EliteCatalog.Find(x.key));
            Herder();
            Kiter();
            Tender();
            Ironclad();
            Allocations();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[FEL] failures: " + fails);
        return fails;
    }

    // ---- fixtures (as SpaceEliteTest's) ---------------------------------------------

    static void Fresh(float speed = .25f)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = speed;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        Random.InitState(2468);
    }

    static EliteDef Def(string brain)
    {
        foreach (var d in EliteCatalog.All) if (d.brain == brain) return d;
        return null;
    }

    static EliteShip InPlay(string brain, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(Def(brain), at);
        e.AttackCooldown = 99f;
        return e;
    }

    static void Step(float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            if (each != null) each();
            EliteSystem.Step(Dt);
        }
    }

    static bool Alive(EliteShip e) => e != null && e.State != EliteState.Dead;

    // Hits it with a player weapon `n` times, a grace apart.
    static void Shoot(EliteShip e, int n)
    {
        for (int i = 0; i < n && Alive(e); i++)
        {
            e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f);
            Step(EliteShip.GraceSeconds + .05f);
        }
    }

    // How long it tells (s), stepping until the action starts.
    static float Tell(EliteShip e, System.Action each = null)
    {
        float s = 0f;
        int guard = 0;
        while (e.Telling && guard++ < 200) { if (each != null) each(); EliteSystem.Step(Dt); s += Dt; }
        return s;
    }

    static LandingSite Pad(Vector2 at, LandingKind kind, int id)
    {
        var anchor = new GameObject("~Pad" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = id, kind = kind };
    }

    static int ActiveShots(EliteShip owner, EliteShots.Kind kind)
    {
        int n = 0;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Owner == owner && s.Kind == kind) n++;
        return n;
    }

    // ---- roster ------------------------------------------------------------------------

    static void Roster()
    {
        var frost = new List<EliteDef>();
        Check("Frost has five elites: the Rimebreaker and these four (" + EliteCatalog.ForWorld(1, frost) + ")", frost.Count == 5);
        var brains = new HashSet<string> { "breaker", "bastion", "reaver", "lancer", "tug" };
        var attacks = new HashSet<string> { "ice_ram", "ward_curtain", "crescent_volley", "rift_rail", "gravity_sling" };
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Frost with its own brain / attack (" + (d != null ? d.brain + " / " + d.attack : "missing") + ")",
                  d != null && d.WorldIndex == 1 && d.brain == x.brain && d.attack == x.attack && brains.Add(d.brain) && attacks.Add(d.attack));
            if (d == null) continue;
            bool kinds = true;
            foreach (var k in x.from) kinds &= LandingSite.Accepts(d.launchFrom, k);
            Check(x.key + ": launches from " + d.launchFrom + ", never a Space body", kinds && !LandingSite.Accepts(d.launchFrom, LandingKind.Station) && !LandingSite.Accepts(d.launchFrom, LandingKind.Planet));
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            Check(x.key + ": Codex's 7-cell flight strip (1344 x 192) is installed with the flight cell map",
                  frames != null && frames.Length == 7 && Mathf.Approximately(frames[0].rect.width, 192f) && Mathf.Approximately(frames[0].rect.height, 192f) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.damaged == 6 && c.tell < 0 && c.action < 0 && c.hit < 0);
            // every muzzle and nozzle on solid hull pixels of the hover cell, well inside the cell
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Frost/" + d.key + ".png"));
            bool solid = true;
            string bad = "";
            foreach (var p in d.muzzles) if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; }
            foreach (var p in d.nozzles) if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; }
            Object.DestroyImmediate(tex);
            Check(x.key + ": its " + d.muzzles.Length + " muzzles and " + d.nozzles.Length + " nozzles sit on the hull " + bad, solid && d.muzzles.Length > 0 && d.nozzles.Length > 0);
            Check(x.key + ": two hearts, orbiting tighter than the player's (" + (d.hullRadius * d.heartOrbit + d.heartSize * 1.05f).ToString("0.00") + " u)",
                  d.hearts == 2 && d.hullRadius * d.heartOrbit + d.heartSize * 1.05f < .52f);
            Check(x.key + ": comparable to the other elites (speed " + d.speed + ", gap " + d.attackGap + ", tell " + d.tellSeconds + ")",
                  d.speed >= 1.2f && d.speed <= 3.4f && d.attackGap >= 2.5f && d.attackGap <= 5.5f && d.tellSeconds >= .5f);
            Check(x.key + ": a codex entry", global::Codex.Find(d.codexId) != null);
        }
        Check("Frost is open to elites after the first 20 s", EliteDirector.Blocked(1, 60f) == null && EliteDirector.Blocked(1, 10f) == "too early");
    }

    // Solid (alpha >= .5) within 2 px of the point, on cell 3, and inside the hull's reach.
    static bool OnHull(Texture2D tex, EliteDef d, ElitePoint p)
    {
        int cell = d.cells.Flight0, n = d.cellPixels;
        bool hit = false;
        for (int dy = -2; dy <= 2 && !hit; dy++)
            for (int dx = -2; dx <= 2 && !hit; dx++)
            {
                int x = cell * n + Mathf.RoundToInt(p.x) + dx, y = n - 1 - (Mathf.RoundToInt(p.y) + dy);
                hit = tex.GetPixel(x, y).a >= .5f;
            }
        return hit && d.PixelToLocal(p.x, p.y).magnitude < d.cellWorldSize * .42f;
    }

    // ---- the director: each from its own kind of Frost ground site ---------------------

    static void Sites()
    {
        Fresh(.05f);
        var pads = new List<LandingSite>
        {
            Pad(new Vector2(-1.6f, 3f), LandingKind.Hangar, 1), Pad(new Vector2(-.8f, 3.3f), LandingKind.RigBay, 2),
            Pad(new Vector2(0f, 3f), LandingKind.PadRing, 3), Pad(new Vector2(.8f, 3.3f), LandingKind.CrawlerBay, 4),
            Pad(new Vector2(1.6f, 3f), LandingKind.Hatch, 5),
        };
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int spawned = 0, mine = 0;
        bool match = true;
        var seen = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(1, 1);
            foreach (var e in EliteShip.Live)
            {
                if (e.Def.brain == "breaker") continue;
                mine++;
                seen.Add(e.Def.key);
                match &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
            }
        }
        Check("each Frost elite launches from its own kind of ground site (" + mine + " of " + spawned + " launches, " + seen.Count + " kinds of elite)",
              spawned == 40 && mine > 10 && match && seen.Count == 4);
        // none of theirs in view: any free site
        pads.Clear();
        pads.Add(Pad(new Vector2(0f, 3f), LandingKind.Ground, 9));
        bool fallback = true;
        for (int i = 0; i < 12; i++) { EliteSystem.Clear(); fallback &= dir.SpawnGroup(1, 1) == 1; }
        Check("... and any free site when none of theirs is in view", fallback);
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
    }

    // ---- the life cycle off a ground pad -------------------------------------------------

    static void Cycle(EliteDef def)
    {
        if (def == null) { Check("frost elite defined", false); return; }
        string k = def.key + ": ";
        Fresh(.05f);
        pilot.position = new Vector3(0f, -3f, 0f);
        var site = Pad(new Vector2(.8f, 3f), LandingSite.KindOf(def.launchFrom) ?? LandingKind.Hangar, 1);
        var e = EliteShip.Create(def, site, 2.2f, new Vector2(-1.2f, 1.4f));
        Check(k + "parked on its pad: no collider, not a target", e.State == EliteState.Parked && !e.Collider.enabled && e.GetComponent<ClearTarget>() == null);
        int guard = 0;
        while (e.State != EliteState.Follow && guard++ < 400) EliteSystem.Step(Dt);
        Check(k + "lifts off, joins (collider on, a hazard) and follows", e.State == EliteState.Follow && e.Collider.enabled && e.CompareTag("Enimey"));
        int launched = EliteSystem.Shots.Launched, attacks = e.Attacks;
        bool told = false, acted = false, back = false;
        e.AttackCooldown = 0f;
        for (int i = 0; i < 30 * 25 && !back; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .02f) * .8f, -2.8f, 0f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            told |= e.Telling;
            acted |= e.Acting;
            if (acted && e.State == EliteState.Follow) back = true;
        }
        var dd = Alive(e) ? e.Attack as DroneDeployAttack : null;
        bool out_ = dd != null ? dd.Released > 0 : EliteSystem.Shots.Launched > launched;
        Check(k + "attacks (" + e.Attack.Id + "): a wind-up, then " + (dd != null ? "drones" : "shots") + " out, back to following", told && acted && back && out_ && e.Attacks > attacks);
        // shot down for good: (shield / plates aside) it dies, is paid, and leaves nothing of its own behind
        float total = RunScore.Total;
        for (int i = 0; i < 12 && Alive(e); i++) { e.TakeHit(EliteDamage.Teleport, e.Position, 1); Step(.4f); }
        bool clean = !EliteShip.Live.Contains(e);
        foreach (var s in EliteSystem.Shots.All) clean &= !(s.Active && s.Owner == e && (s.Kind == EliteShots.Kind.Slab || s.Kind == EliteShots.Kind.Orb));
        if (dd != null) clean &= dd.Live == 0;
        Check(k + "brought down: paid, gone, its slabs / orb / drones with it", !Alive(e) && clean && RunScore.Total - total >= ScoreRules.EliteDown);
        EliteSystem.Clear();
    }

    // ---- Floe Harrower: herder + floe_cast ------------------------------------------------

    static void Herder()
    {
        Fresh(.05f);
        pilot.position = new Vector3(-1.2f, -2.5f, 0f);
        var e = InPlay("herder", new Vector2(-1f, .3f));
        var def = e.Def;
        var brain = (HerderBrain)e.Brain;
        Step(2.5f);
        // the pilot heads right: it gets there first
        float aheadR = -9f, hy = 0f;
        int n = 0;
        Step(3f, () =>
        {
            pilot.position += Vector3.right * .55f * Dt;
            aheadR = e.Position.x - pilot.position.x;
            hy += e.Position.y - pilot.position.y; n++;
        });
        // then left
        float aheadL = 9f;
        Step(2.5f, () => { pilot.position += Vector3.left * .55f * Dt; aheadL = e.Position.x - pilot.position.x; });
        Check("herder cuts off the lane the pilot heads for (ahead by " + aheadR.ToString("0.00") + " going right, " + aheadL.ToString("0.00") + " going left)",
              aheadR > .25f && aheadL < -.25f);
        Check("... holding a steady height ahead of it (" + (hy / Mathf.Max(1, n)).ToString("0.0") + " u above)", Mathf.Abs(hy / Mathf.Max(1, n) - def.followDistance) < .6f);
        Check("... patiently: its lane drifts slower than it can fly", HerderBrain.LaneShare < 1f && def.speed <= 2.2f);

        // floe_cast
        Fresh(.05f);
        pilot.position = new Vector3(.6f, -2.5f, 0f);
        e = InPlay("herder", new Vector2(.4f, .3f));
        Step(2f);
        e.ForceAttack();
        var cast = (FloeCastAttack)e.Attack;
        bool glow = false;
        float tellSpeed = 0f;
        bool sightTell = false;
        float tell = Tell(e, () => { glow |= e.ChargeGlowOn; sightTell |= e.SightShown; tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude); });
        Check("floe_cast tell: planted while its chutes glow (" + tell.ToString("0.00") + " s, " + tellSpeed.ToString("0.0") + " u/s)",
              glow && Mathf.Abs(tell - def.tellSeconds) < .1f && tellSpeed < 1.2f);
        bool sightBefore = false, sightAtLance = false;
        int lanceShots = 0;
        float lanceT = -1f;
        float t0 = 0f;
        EliteShot lance = null;
        int guard = 0;
        while (e.State == EliteState.Attack && guard++ < 200)
        {
            int was = EliteSystem.Shots.Launched;
            EliteSystem.Step(Dt);
            t0 += Dt;
            if (!cast.Lanced) sightBefore |= e.SightShown;
            if (cast.Lanced && lanceT < 0f)
            {
                lanceT = t0;
                foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Kind == EliteShots.Kind.Bolt && s.Owner == e) { lance = s; lanceShots++; }
            }
        }
        int slabs = ActiveShots(e, EliteShots.Kind.Slab);
        Check("floe_cast: " + cast.Count + " ice slabs out of its chutes (" + cast.Cast + " cast, " + slabs + " afloat)", cast.Cast == cast.Count && slabs == cast.Count);
        // the row: staggered across the board, one gap beside the pilot (toward the middle)
        float minGapSlab = 9f, minPair = 9f;
        Vector2 gap = cast.GapNow;
        var at = new List<Vector2>();
        bool staggered = false;
        for (int i = 0; i < cast.Count; i++)
        {
            var s = cast.Slab(i);
            if (s == null || !s.Active) continue;
            Vector2 p = s.transform.position;
            at.Add(p);
            minGapSlab = Mathf.Min(minGapSlab, Mathf.Abs(p.x - gap.x));
        }
        for (int i = 0; i < at.Count; i++)
            for (int j = i + 1; j < at.Count; j++) { minPair = Mathf.Min(minPair, Vector2.Distance(at[i], at[j])); staggered |= Mathf.Abs(at[i].y - at[j].y) > .2f; }
        float radius = cast.Slab(0) != null ? cast.Slab(0).Radius : 0f;
        Check("... in a staggered row across the board with one open gap (" + cast.SlotWidth.ToString("0.00") + " u slots, nearest slab " + minGapSlab.ToString("0.00") + " u from the gap)",
              staggered && minGapSlab > cast.SlotWidth * .7f && cast.SlotWidth * 2f - radius * 2f > .7f);
        Check("... the gap one slot beside the pilot, toward the middle (gap " + gap.x.ToString("0.00") + ", pilot " + pilot.position.x.ToString("0.00") + ")",
              gap.x < pilot.position.x && pilot.position.x - gap.x < cast.SlotWidth * 1.6f);
        Check("... slabs never overlap each other (closest pair " + minPair.ToString("0.00") + " u, radius " + radius.ToString("0.00") + ")", minPair > radius * 1.6f);
        Check("... a sight down the gap's lane from the wind-up, then one lance right after the last slab (" + lanceT.ToString("0.00") + " s, cast by " + ((cast.Count - 1) * FloeCastAttack.SlabGap).ToString("0.00") + ")",
              sightTell && sightBefore && lanceShots == 1 && lanceT >= (cast.Count - 1) * FloeCastAttack.SlabGap && lanceT < .6f);
        bool through = false;
        if (lance != null)
        {
            // straight down its line: it reaches the pilot's height in the gap's lane
            Vector2 a = lance.LaunchedAt, v = lance.Velocity;
            float k = v.y < -.01f ? (pilot.position.y - a.y) / v.y : -1f;
            through = k > 0f && Mathf.Abs(a.x + v.x * k - cast.GapNow.x) < .3f;
        }
        Check("... the lance goes down the gap's lane to the pilot's height", through);
        // the slabs drift and soak shots
        var slab = cast.Slab(0);
        Vector2 s0 = slab != null ? (Vector2)slab.transform.position : Vector2.zero;
        Step(.6f);
        Vector2 s1 = slab != null && slab.Active ? (Vector2)slab.transform.position : s0;
        Check("... the slabs ride the board drifting sideways (" + (s1.x - s0.x).ToString("0.00") + " u)", Mathf.Abs(s1.x - s0.x) > .05f && s1.y < s0.y);
        var hb = slab != null ? slab.Hitbox.GetComponent<EliteShotHitbox>() : null;
        int armour = def.hazardArmour;
        for (int i = 0; i < armour - 1 && hb != null; i++) hb.TakeShipAttack(0, 1f, slab.transform.position);
        bool soaked = slab != null && slab.Active;
        if (hb != null) hb.TakeShipAttack(0, 1f, slab.transform.position);
        Check("... a slab soaks " + (armour - 1) + " shots and breaks on the " + armour + "th", soaked && slab != null && !slab.Active);
        // its slabs go with it
        e.TakeHit(EliteDamage.Teleport, e.Position, 2);
        Check("... and break up when the Harrower goes down (" + ActiveShots(e, EliteShots.Kind.Slab) + " left)", !Alive(e) && ActiveShots(e, EliteShots.Kind.Slab) == 0);
    }

    // ---- Cryo Siren: kiter + frost_bloom --------------------------------------------------

    static void Kiter()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("kiter", new Vector2(.5f, .8f));
        var def = e.Def;
        var brain = (KiterBrain)e.Brain;
        Step(4f);
        float d0 = Vector2.Distance(e.Position, pilot.position);
        Check("kiter keeps its range above the pilot (" + d0.ToString("0.00") + " u vs " + def.keepDistance + ")",
              Mathf.Abs(d0 - def.keepDistance) < .5f && e.Position.y > pilot.position.y + 1f);
        // the pilot closes in: it backs off in stutter steps
        // the reach no longer caps the elite low, so it holds its full range: the pilot
        // jumps in to crowd it (inside Crowded x range) and then keeps closing
        pilot.position = (Vector3)(e.Position + Vector2.down * 2.1f);
        float closest = 9f, fast = 0f, slow = 9f;
        Step(2f, () =>
        {
                        pilot.position = Vector3.MoveTowards(pilot.position, e.Position, .6f * Dt);
            closest = Mathf.Min(closest, Vector2.Distance(e.Position, pilot.position));
            if (brain.Backing) { fast = Mathf.Max(fast, e.Velocity.magnitude); slow = Mathf.Min(slow, e.Velocity.magnitude); }
        });
        Check("... crowded, it backpedals (" + brain.Backpedals + "x, never closer than " + closest.ToString("0.00") + " u)", brain.Backpedals > 0 && closest > 1.2f);
        Check("... in stutter steps (" + slow.ToString("0.0") + " .. " + fast.ToString("0.0") + " u/s)", fast > slow * 1.6f);

        // frost_bloom: the orb
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("kiter", new Vector2(0f, .6f));
        Step(1.5f);
        e.ForceAttack();
        var bloom = (FrostBloomAttack)e.Attack;
        bool glow = false;
        float tell = Tell(e, () => glow |= e.ChargeGlowOn);
        Check("frost_bloom tell: the dish charges (" + tell.ToString("0.00") + " s)", glow && !bloom.Beam && Mathf.Abs(tell - def.tellSeconds) < .1f);
        EliteSystem.Step(Dt);
        var orb = bloom.Orb;
        Vector2 dish = e.MuzzleWorld(0);
        Check("... one slow cryo orb out of the dish, its fuse ring blinking (" + (orb != null ? orb.Velocity.magnitude.ToString("0.0") : "-") + " u/s)",
              orb != null && orb.Kind == EliteShots.Kind.Orb && Vector2.Distance(orb.LaunchedAt, dish) < .25f && orb.MarkShown && orb.Velocity.magnitude < 3f);
        int launched = EliteSystem.Shots.Launched;
        float fuse = 0f;
        Vector2 burstAt = Vector2.zero;
        int guard = 0;
        while (orb.Active && orb.Kind == EliteShots.Kind.Orb && guard++ < 100) { burstAt = orb.transform.position; EliteSystem.Step(Dt); fuse += Dt; }
        int ring = 0;
        bool radial = true;
        foreach (var s in EliteSystem.Shots.All)
        {
            if (!s.Active || s.Kind != EliteShots.Kind.Shard || s.Age > Dt * 1.5f) continue;
            ring++;
            radial &= Vector2.Distance(s.LaunchedAt, burstAt) < .3f && Mathf.Abs(s.Velocity.magnitude - def.hazardSpeed) < .2f;
        }
        Check("... bursts after its fuse (" + fuse.ToString("0.00") + " s) into a ring of " + def.hazardCount + " shards (" + ring + ")",
              Mathf.Abs(fuse - def.hazardSeconds) < .12f && ring == def.hazardCount && EliteSystem.Shots.Launched - launched == def.hazardCount && radial);
        Check("... gaps between them wide enough to slip through at a metre (" + (2f * Mathf.PI / def.hazardCount).ToString("0.00") + " u)", 2f * Mathf.PI / def.hazardCount - def.shotSize > .5f);

        // the beam sweep (every third)
        Step(1f);
        bloom.NextIsBeam();
        e.ForceAttack();
        bool sight = false;
        float a0 = bloom.BeamFrom, a1 = bloom.BeamTo;
        tell = Tell(e, () => sight |= e.SightShown);
        Check("frost_bloom beam: a longer tell painting the arc with a sight line (" + tell.ToString("0.00") + " s)",
              bloom.Beam && sight && Mathf.Abs(tell - def.tellSeconds * FrostBloomAttack.BeamTell) < .1f);
        float lo = 999f, hi = -999f;
        int before = EliteSystem.Shots.Launched;
        guard = 0;
        while (e.State == EliteState.Attack && guard++ < 100)
        {
            EliteSystem.Step(Dt);
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active || s.Kind != EliteShots.Kind.Bolt || s.Age > Dt * 1.5f) continue;
                float a = Mathf.Atan2(s.Velocity.y, s.Velocity.x) * Mathf.Rad2Deg;
                lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a);
            }
        }
        Check("... then sweeps a stream of bolts across that arc (" + (EliteSystem.Shots.Launched - before) + " bolts, " + lo.ToString("0") + ".." + hi.ToString("0") + " deg)",
              EliteSystem.Shots.Launched - before >= 10 && hi - lo > FrostBloomAttack.BeamArc * 1.6f && hi - lo < FrostBloomAttack.BeamArc * 2.3f && !e.SightShown);

        // an orb on its fuse dies with the Siren
        Step(1f);
        e.ForceAttack();
        Tell(e);
        EliteSystem.Step(Dt);
        orb = bloom.Orb;
        launched = EliteSystem.Shots.Launched;
        e.TakeHit(EliteDamage.Teleport, e.Position, 2);
        Step(1f);
        Check("an orb still on its fuse pops when the Siren goes down (no ring)", orb != null && !(orb.Active && orb.Kind == EliteShots.Kind.Orb) && EliteSystem.Shots.Launched == launched);
    }

    // ---- Glacier Tender: tender + drone_deploy ---------------------------------------------

    static void Tender()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("tender", new Vector2(.5f, 1.5f));
        var def = e.Def;
        var brain = (TenderBrain)e.Brain;
        Step(4f);
        Check("tender hovers high, hanging back (" + e.Position + ", height " + brain.Height.ToString("0.0") + ")",
              Mathf.Abs(e.Position.y - brain.Height) < .4f && e.Position.y > pilot.position.y + 2.5f);
        e.ForceAttack();
        var drones = (DroneDeployAttack)e.Attack;
        bool glow = false;
        float tell = Tell(e, () => glow |= e.ChargeGlowOn);
        Check("drone_deploy tell: the pods charge (" + tell.ToString("0.00") + " s)", glow && Mathf.Abs(tell - def.tellSeconds) < .1f);
        EliteSystem.Step(Dt);
        int live = drones.Live;
        bool atPods = live > 0;
        for (int i = 0; i < 4; i++)
        {
            var d = drones.Drone(i);
            if (d == null) continue;
            float best = 9f;
            for (int m = 0; m < def.muzzles.Length; m++) best = Mathf.Min(best, Vector2.Distance(d.transform.position, e.MuzzleWorld(m)));
            atPods &= best < .3f;
        }
        Check("... releases 2-3 drones (" + live + ") out of its pods, each a real Frost fighter, smaller", live >= 2 && live <= 3 && atPods &&
              drones.Drone(0) != null && EnemyIdentity.Of(drones.Drone(0)) != null && drones.Drone(0).CompareTag("Enimey"));
        int hearts = e.Hearts;
        Step(2f);
        Check("... its own drones never crash into it", e.Hearts == hearts && drones.Live == live);
        bool tethered = true, spread = true, floor = true;
        for (int i = 0; i < 4; i++)
        {
            var d = drones.Drone(i);
            if (d == null) continue;
            tethered &= drones.TetherShown(i) && Vector2.Distance(d.transform.position, drones.SlotSpot(i)) < .5f;
            floor &= d.transform.position.y > pilot.position.y + DroneDeployAttack.DroneFloor - .1f;
            for (int j = i + 1; j < 4; j++)
            {
                var o = drones.Drone(j);
                if (o != null) spread &= Vector2.Distance(d.transform.position, o.transform.position) > .5f;
            }
        }
        Check("... they fan out below it on glowing tethers, apart from each other, above the pilot", tethered && spread && floor);
        // shield-linked while two live
        e.TakeShipAttack(0, 1f, e.Position);
        Check("... shield-linked while two or more live: a shot costs it nothing (blocked " + drones.Blocked + ", ring " + drones.ShieldShown + ")",
              drones.Linked && drones.ShieldShown && e.Hearts == hearts && drones.Blocked == 1);
        // the pilot kills drones down to one: the link drops
        for (int i = 0; i < 4 && drones.Live > 1; i++) { var d = drones.Drone(i); if (d != null) Object.DestroyImmediate(d); }
        Step(.1f);
        e.TakeShipAttack(0, 1f, e.Position);
        Check("... one drone left: no link, the next shot costs a heart", !drones.Linked && !drones.ShieldShown && e.Hearts == hearts - 1);
        // never more than its cap
        Step(1f);
        e.ForceAttack();
        Step(def.tellSeconds + def.actionSeconds + .2f);
        e.ForceAttack();
        Step(def.tellSeconds + def.actionSeconds + .2f);
        Check("... never more than " + drones.Cap + " drones at once (" + drones.Live + ")", drones.Live <= drones.Cap && drones.Live >= 2);
        // the pilot comes close: it flees
        Vector2 from = e.Position;
        Vector3 reach = new Vector3(e.Position.x - .4f, e.Position.y - 1.2f, 0f);
        Step(2.8f, () => pilot.position = Vector3.MoveTowards(pilot.position, reach, 3f * Dt));
        Check("tender flees when the pilot reaches it (" + brain.Flights + "x, moved " + Vector2.Distance(from, e.Position).ToString("0.0") + " u)",
              brain.Flights > 0 && Vector2.Distance(from, e.Position) > .8f && Vector2.Distance(e.Position, pilot.position) > Vector2.Distance(from, pilot.position));
        // dies: its drones go with it
        var any = drones.Drone(0) ?? drones.Drone(1) ?? drones.Drone(2);
        e.TakeHit(EliteDamage.Teleport, e.Position, 2);
        Check("killing the Tender scuttles its drones (" + drones.Live + " left)", !Alive(e) && drones.Live == 0 && any == null);
    }

    // ---- Whiteout Sentinel: ironclad + armour_shatter ---------------------------------------

    static void Ironclad()
    {
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("ironclad", new Vector2(.6f, 2.2f));
        var def = e.Def;
        var brain = (IroncladBrain)e.Brain;
        // a rock right in its path: it does not swerve
        var rock = EnemyFactory.Create(EnemyRoster.One(1, EnemyRole.Rock), new Vector3(.4f, 1.2f, 0f), Quaternion.identity);
        ClearTarget.Ensure(rock);
        int legs = brain.Legs;
        float startY = e.Position.y, maxTurn = 0f;
        Vector2 lastV = Vector2.zero;
        int lastLeg = brain.Legs;
        float legAge = 0f;
        Step(3.2f, () =>
        {
            if (brain.Legs != lastLeg) { lastLeg = brain.Legs; legAge = 0f; }
            legAge += Dt;
            Vector2 v = e.Velocity;
            if (legAge > .6f && v.magnitude > .3f && lastV.magnitude > .3f) maxTurn = Mathf.Max(maxTurn, Vector2.Angle(v, lastV));
            lastV = v;
        });
        Check("ironclad plods at the pilot (" + startY.ToString("0.0") + " -> " + e.Position.y.ToString("0.0") + ") in straight legs (" + (brain.Legs - legs) + ", turn " + maxTurn.ToString("0.0") + " deg/step)",
              e.Position.y < startY - .3f && brain.Legs - legs >= 2 && maxTurn < 6f && e.Velocity.magnitude <= def.speed * IroncladBrain.Pace + .3f);
        Check("... never dodges: no evasions, ploughs the rock (armoured, no heart lost)", e.Evasions == 0 && rock == null && e.Hearts == def.hearts);
        float face = Mathf.Atan2(pilot.position.y - e.Position.y, pilot.position.x - e.Position.x) * Mathf.Rad2Deg;
        Check("... its plated prow turned on the pilot (" + Mathf.DeltaAngle(e.Facing, face).ToString("0") + " deg off)", Mathf.Abs(Mathf.DeltaAngle(e.Facing, face)) < 30f);

        // armour_shatter: plates go in order, each a telegraphed spray
        Fresh(.02f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        e = InPlay("ironclad", new Vector2(0f, 1f));
        var armour = (ArmourShatterAttack)e.Attack;
        Step(2.5f);
        int plates = armour.Plates;
        bool shown = armour.PlateShown(0) && armour.PlateShown(1) && armour.PlateShown(2);
        e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f);
        bool order = armour.Plates == plates - 1 && !armour.PlateShown(2) && armour.PlateShown(1);
        Check("armour_shatter: " + plates + " ice plates on its prow; a shot breaks the outermost instead of a heart",
              plates == def.hazardCount && shown && order && e.Hearts == def.hearts);
        e.TakeShipAttack(0, 1f, e.Position);
        Check("... a second shot inside the plate grace breaks nothing more", armour.Plates == plates - 1);
        bool cone = false;
        int launched = EliteSystem.Shots.Launched;
        float sprayIn = 0f;
        int guard = 0;
        while (armour.SprayTelegraphed && guard++ < 60) { EliteSystem.Step(Dt); sprayIn += Dt; cone |= e.transform.Find("ConeL") != null && e.transform.Find("ConeL").GetComponent<SpriteRenderer>().enabled; }
        int sprayed = EliteSystem.Shots.Launched - launched;
        bool forward = true;
        Vector2 toPilot = ((Vector2)pilot.position - e.Position).normalized;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Age < Dt * 1.5f) forward &= Vector2.Dot(s.Velocity.normalized, toPilot) > .7f;
        Check("... the broken plate sprays shards forward after a blinking cone (" + sprayIn.ToString("0.00") + " s, " + sprayed + " shards)",
              cone && Mathf.Abs(sprayIn - ArmourShatterAttack.SprayDelay) < .1f && sprayed == def.shotCount && forward && armour.Sprays == 1);
        Shoot(e, 2);
        Check("... plates break in order to none, every heart kept, and it shows the stripped hull (" + armour.Plates + " plates, frame " + e.CurrentFrame + ")",
              armour.Stripped && armour.Shattered == plates && e.Hearts == def.hearts && e.CurrentFrame == def.cells.damaged && !armour.PlateShown(0));
        // stripped: an enraged charge soon, telegraphed, then it limps
        guard = 0;
        while (!e.Telling && guard++ < 90) EliteSystem.Step(Dt);
        if (!e.Telling) e.ForceAttack();
        bool sight = false;
        float tell = Tell(e, () => sight |= e.SightShown);
        float charge = 0f;
        guard = 0;
        while (e.Acting && guard++ < 60) { EliteSystem.Step(Dt); charge = Mathf.Max(charge, e.Velocity.magnitude); }
        Check("... stripped, it charges: a long sighted tell (" + tell.ToString("0.00") + " s) then a dash (" + charge.ToString("0.0") + " u/s)",
              sight && Mathf.Abs(tell - def.tellSeconds * ArmourShatterAttack.ChargeTell) < .1f && charge > def.dashSpeed * .8f && armour.Charges == 1);
        Check("... then recovers, limping", armour.Recovering && brain != null);
        Shoot(e, 1);
        Check("... and now a shot costs a heart", e.Hearts == def.hearts - 1 || !Alive(e));
    }

    // ---- allocations -------------------------------------------------------------------

    static void Allocations()
    {
        Fresh(.1f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var a = InPlay("herder", new Vector2(-1.2f, .5f));
        var b = InPlay("kiter", new Vector2(1.2f, .6f));
        var c = InPlay("tender", new Vector2(0f, 2f));
        var d = InPlay("ironclad", new Vector2(.5f, 2.5f));
        a.AttackCooldown = b.AttackCooldown = c.AttackCooldown = d.AttackCooldown = 0f;
        int f = 0;
        Step(6f, () => pilot.position = new Vector3(Mathf.Sin(f++ * .03f), -2.5f, 0f));   // warm up: every attack once (drones out)
        if (Alive(c)) c.AttackCooldown = 99f;   // (a release builds its drones: a spawn, not a frame)
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long allocated = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 90; i++)
            {
                pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
                EliteSystem.Step(Dt);
            }
        });
        Check("zero per-frame allocations stepping the four Frost elites, their slabs, orbs, drones and plates (" + allocated + " bytes over 90 frames; meter control " + control + ")",
              meter && allocated == 0);
    }
}
