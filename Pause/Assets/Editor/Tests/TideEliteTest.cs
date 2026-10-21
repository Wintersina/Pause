using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tide's five elites as mini-boss fights (Riptide, Bathyscaphe, Trawler, Thunderfin, Pearl Warden; TideElites.cs): the roster, their strips
// (7 flight cells, 3 death cells), muzzles on the hull, launching from Tide's own pads, the director's rotation, the codex entries, the
// fight rules (3 hearts, 5 armoured, the shrunk size band, no pink) and each one's own brain and attack in simulation. Every attack shows its footprint from the first frame of its tell for at least the FR1 lead
// and cleans up after itself; nothing allocates per frame.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites TideEliteTest
public static class TideEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TEL] PASS  " : "[TEL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    static readonly (string key, string brain, string attack, LandingKind from)[] Expected =
    {
        ("tide_elite_riptide", "breaker", "foam_wake", LandingKind.TideTrenchHatch),
        ("tide_elite_bathyscaphe", "deep", "mine_chain", LandingKind.TideRigDeck),
        ("tide_elite_trawler", "trawl", "drag_net", LandingKind.TideReefDock),
        ("tide_elite_thunderfin", "tempest", "thunder_sweep", LandingKind.TideVentStack),
        ("tide_elite_pearl_warden", "oyster", "pearl_spiral", LandingKind.TideWreckBay),
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
            Rotation();
            Codex();
            Rules();
            Foam();
            Mines();
            Nets();
            Thunder();
            Pearl();
            Choreography();
            EngineFlames();
            Allocations();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            AttackPools.ClearAll();
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[TEL] failures: " + fails);
        return fails;
    }

    // ---- fixtures ---------------------------------------------------------------------

    static void Fresh(float speed = .25f)
    {
        EliteSystem.Clear();
        AttackPools.ClearAll();
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
        AttackPreview.ResetCounters();
        Random.InitState(1357);
    }

    static EliteShip InPlay(string key, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(EliteCatalog.Find(key), at);
        e.AttackCooldown = 99f;
        e.EscapeLeft = 0f;
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

    // Starts the attack and steps one frame: the first frame of its tell.
    static void StartTell(EliteShip e)
    {
        e.SetSeen(pilot.position);
        e.ForceAttack();
        EliteSystem.Step(Dt);
    }

    static bool StepUntil(System.Func<bool> done, float seconds)
    {
        for (float t = 0f; t < seconds; t += Dt)
        {
            if (done()) return true;
            pilot.position = new Vector3(0f, -2.5f, 0f);   // (a pilot standing still: the attack's own geometry is under test)
            EliteSystem.Step(Dt);
        }
        return done();
    }

    // Hits it with a player weapon `n` times, a grace apart.
    static void Shoot(EliteShip e, int n)
    {
        for (int i = 0; i < n && e != null && e.State != EliteState.Dead; i++)
        {
            e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f);
            Step(EliteShip.GraceSeconds + .05f);
        }
    }

    static LandingSite Pad(Vector2 at, LandingKind kind, int id)
    {
        var anchor = new GameObject("~Pad" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = id, kind = kind };
    }

    static bool OnHull(Texture2D tex, EliteDef d, ElitePoint p)
    {
        int cell = d.cells.Flight0, n = d.cellPixels;
        bool hit = false;
        int r = 2 * n / 192;   // a 2 px reach at the 192 px grid, scaled to HD cells
        for (int dy = -r; dy <= r && !hit; dy++)
            for (int dx = -r; dx <= r && !hit; dx++)
            {
                int x = cell * n + Mathf.RoundToInt(p.x) + dx, y = n - 1 - (Mathf.RoundToInt(p.y) + dy);
                hit = tex.GetPixel(x, y).a >= .5f;
            }
        return hit && d.PixelToLocal(p.x, p.y).magnitude < d.cellWorldSize * .42f;
    }

    // ---- roster ------------------------------------------------------------------------

    static void Roster()
    {
        var tide = new List<EliteDef>();
        Check("Tide has five elites (" + EliteCatalog.ForWorld(4, tide) + ")", tide.Count == 5);
        var brains = new HashSet<string>();
        var attacks = new HashSet<string>();
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Tide with its own brain / attack (" + (d != null ? d.brain + " / " + d.attack : "missing") + ")",
                  d != null && d.WorldIndex == 4 && d.brain == x.brain && d.attack == x.attack && brains.Add(d.brain) && attacks.Add(d.attack));
            if (d == null) continue;
            Check(x.key + ": launches from its own Tide pad, never a Space body", LandingSite.Accepts(d.launchFrom, x.from) && !LandingSite.Accepts(d.launchFrom, LandingKind.Station));
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            float cell = d.cellPixels;   // 192, or 384 for the HD rebuilds (build_tide_hd.py)
            Check(x.key + ": the flight strip (7 x " + cell + " px cells) slices into 7 cells and the cell map is the flight layout",
                  EliteHdResolution.CellOk(d) &&
                  frames != null && frames.Length == 10 + 6 && Mathf.Approximately(frames[0].rect.width, cell) && Mathf.Approximately(frames[0].rect.height, cell) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.bankLeft == 4 && c.bankRight == 5 && c.damaged == 6 &&
                  c.HasDamagedLoop && c.damagedLoop.Length == 6 && c.damagedLoop[0] == 10 && c.damagedLoop[5] == 15 &&
                  c.tell < 0 && c.action < 0 && c.hit < 0);
            var death = EliteArt.ExtraFrames(d, EliteArt.Extra.Death);
            Check(x.key + ": the death strip slices into 3 cells of " + cell + " (" + (death != null ? death.Length.ToString() : "none") + ")",
                  death != null && death.Length == 3 && Mathf.Approximately(death[0].rect.width, cell));
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Tide/" + d.key + ".png"));
            bool solid = true;
            string bad = "";
            bool inCell = true;
            foreach (var p in d.muzzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > cell * .04f && p.x < cell * .96f && p.y > cell * .04f && p.y < cell * .96f; }
            foreach (var p in d.nozzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > cell * .04f && p.x < cell * .96f && p.y > cell * .04f && p.y < cell * .96f; }
            Object.DestroyImmediate(tex);
            Check(x.key + ": its " + d.muzzles.Length + " muzzles and " + d.nozzles.Length + " nozzles sit on the hull, well inside the cell " + bad, solid && inCell && d.muzzles.Length > 0 && d.nozzles.Length > 0);
            Check(x.key + ": " + d.hearts + " hearts orbiting tighter than the player's (" + (d.hullRadius * d.heartOrbit + d.heartSize * 1.05f).ToString("0.00") + " u)",
                  d.hearts >= 3 && d.hullRadius * d.heartOrbit + d.heartSize * 1.05f < .52f);
            Check(x.key + ": tuned like its siblings (speed " + d.speed + ", gap " + d.attackGap + ", tell " + d.tellSeconds + ")",
                  d.speed >= 1.2f && d.speed <= 3.4f && d.attackGap >= .2f && d.attackGap <= 5.5f && d.tellSeconds >= .55f && d.avoidance >= .4f && d.avoidance <= .8f);
            Check(x.key + ": lore is three sentences or fewer", d.lore.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries).Length <= 3);
        }
    }

    static void Sites()
    {
        Fresh(.05f);
        var pads = new List<LandingSite>
        {
            Pad(new Vector2(-1.6f, 3f), LandingKind.TideTrenchHatch, 1), Pad(new Vector2(-.8f, 3.3f), LandingKind.TideRigDeck, 2),
            Pad(new Vector2(0f, 3f), LandingKind.TideReefDock, 3), Pad(new Vector2(.8f, 3.3f), LandingKind.TideVentStack, 4),
            Pad(new Vector2(1.6f, 3f), LandingKind.TideWreckBay, 5),
        };
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int spawned = 0;
        bool match = true;
        var seen = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(4, 1);
            foreach (var e in EliteShip.Live) { match &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind); seen.Add(e.Def.key); }
        }
        Check("each Tide elite launches from its own pad (" + spawned + " launches)", spawned == 40 && match);
        Check("all five Tide elites turn up (" + seen.Count + ")", seen.Count == 5);
        pads.Clear();
        pads.Add(Pad(new Vector2(0f, 3f), LandingKind.Ground, 9));
        bool fallback = true;
        for (int i = 0; i < 12; i++) { EliteSystem.Clear(); fallback &= dir.SpawnGroup(4, 1) == 1; }
        Check("... and any free pad when none of theirs is in view", fallback);
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
    }

    static void Rotation()
    {
        Fresh(.05f);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        var seen = new HashSet<string>();
        bool perDeck = true;
        for (int deck = 0; deck < 4; deck++)
        {
            seen.Clear();
            for (int i = 0; i < 5; i++) seen.Add(dir.NextDef(4).key);
            perDeck &= seen.Count == 5;
        }
        Check("the director deals Tide's five elites as a shuffled deck", perDeck);
        Check("Tide's elites wait for the calm start", EliteDirector.Blocked(4, EliteDirector.FirstSeconds - .5f) == "too early");
        Object.DestroyImmediate(dir.gameObject);
    }

    static void Codex()
    {
        int n = 0;
        bool strips = true;
        foreach (var d in EliteCatalog.All)
        {
            if (d.WorldIndex != 4) continue;
            n++;
            var entry = global::Codex.Find(d.codexId);
            strips &= entry != null && entry.category == CodexCategory.Enemies && EliteArt.HasExtra(d, EliteArt.Extra.Death);
        }
        Check("the codex lists five Tide elites with their death strips (" + n + ")", n == 5 && strips);
    }

    // ---- shared helpers for the five fights ------------------------------------------------

    static bool Alive(EliteShip e) { return e != null && e.State != EliteState.Dead; }

    // Kills it where it stands (a pause jump's damage), the way a player's pause would.
    static void Kill(EliteShip e) { e.TakeHit(EliteDamage.Teleport, e.transform.position, 9); }

    // ---- Riptide: breaker / foam_wake ------------------------------------------------------

    // Is the ship inside the view (the rails, the top and the bottom)?
    static bool InView(EliteShip e)
    {
        Vector2 p = e.Position;
        return Mathf.Abs(p.x) <= EliteSystem.RailEdge + .05f && p.y <= EliteSystem.ViewTop + .05f && p.y >= EliteSystem.ViewBottom - .05f;
    }

    static void Settle(EliteShip e)
    {
        if (!e.Telling && !e.Acting) e.AttackCooldown = 99f;
    }

    static void Foam()
    {
        Fresh();
        var e = InPlay("tide_elite_riptide", new Vector2(0f, 3f));
        var bb = (BreakerBrain)e.Brain;
        var fa = (FoamWakeAttack)e.Attack;
        Check("Riptide: it does not roam (never leaves the view) and starts a ride along its wave line (" + bb.Phase + ")", !bb.Roams && bb.Phase == BreakerBrain.Stage.Ride);
        bool inView = true;
        float minX = 9f, maxX = -9f;
        var lanes = new bool[3];
        int flips = 0;
        float lastDir = bb.DirX;
        Step(26f, () =>
        {
            inView &= InView(e);
            minX = Mathf.Min(minX, e.Position.x);
            maxX = Mathf.Max(maxX, e.Position.x);
            lanes[Mathf.Clamp(bb.Lane, 0, 2)] = true;
            if (!Mathf.Approximately(bb.DirX, lastDir)) { flips++; lastDir = bb.DirX; }
        });
        int lanesSeen = (lanes[0] ? 1 : 0) + (lanes[1] ? 1 : 0) + (lanes[2] ? 1 : 0);
        Check("it TURNS ROUND at the rail instead of leaving (" + flips + " U-turns, x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ", always in view " + inView + ")",
              inView && flips >= 3 && maxX - minX > 1.5f);
        Check("each pass is a lane lower or higher than the last (" + lanesSeen + " lanes ridden)", lanesSeen >= 2);

        Fresh();
        e = InPlay("tide_elite_riptide", new Vector2(0f, 3f));
        bb = (BreakerBrain)e.Brain;
        fa = (FoamWakeAttack)e.Attack;
        bool window = StepUntil(() => bb.Phase == BreakerBrain.Stage.Ride && bb.WantsAttack(Vector2.zero), 14f);
        Check("the attack window opens on a pass, with the whole trail of rings behind it (" + bb.Phase + ")", window);
        StartTell(e);
        int want = Mathf.Clamp(e.Def.shotCount, 2, BreakerBrain.MaxWake);
        bool behind = true, near = true;
        for (int k = 0; k < want; k++)
        {
            behind &= (fa.Spot(k).x - e.Position.x) * bb.DirX <= .01f;
            near &= Mathf.Abs(fa.Spot(k).y - e.Position.y) < BreakerBrain.Amplitude * 2f + .6f;
        }
        Check("on the first frame of the tell " + want + " rings are drawn on the water BEHIND it along its line (" + fa.Marks.ShownCount + " shown)",
              e.Telling && fa.Count == want && fa.Marks.ShownCount == want && behind && near);
        Check("it keeps riding through the wind-up (not held still, " + e.Velocity.magnitude.ToString("0.0") + " u/s)", !fa.HoldsDuringTell && e.Velocity.magnitude > .8f);
        StepUntil(() => e.Acting, 3f);
        var best = new float[want];
        for (int k = 0; k < best.Length; k++) best[k] = 9f;
        int maxOrbs = 0;
        for (float t = 0f; t < 4.5f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            Settle(e);
            int o = 0;
            foreach (var sh in EliteSystem.Shots.All)
            {
                if (!sh.Active || sh.Kind != EliteShots.Kind.Orb) continue;
                o++;
                for (int k = 0; k < best.Length; k++) best[k] = Mathf.Min(best[k], Vector2.Distance((Vector2)sh.transform.position, fa.Spot(k)));
            }
            maxOrbs = Mathf.Max(maxOrbs, o);
        }
        bool landed = true;
        for (int k = 0; k < best.Length; k++) landed &= best[k] < .45f;
        Check("a foam bank is lobbed onto every ring (" + maxOrbs + " at once; closest " + best[0].ToString("0.00") + " u)", maxOrbs >= want && landed && fa.Laid == want);
        Check("the cast is counted (" + bb.Casts + ") and the FR1 lead was shown", bb.Casts == 1 && AttackPreview.TooShort == 0);

        Fresh();
        e = InPlay("tide_elite_riptide", new Vector2(0f, 3f));
        bb = (BreakerBrain)e.Brain;
        fa = (FoamWakeAttack)e.Attack;
        StepUntil(() => bb.Phase == BreakerBrain.Stage.Ride && bb.WantsAttack(Vector2.zero), 14f);
        StartTell(e);
        Kill(e);
        Check("a Riptide killed in its tell takes its rings and foam with it", (e == null || fa.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);
    }

    // ---- Bathyscaphe: deep / mine_chain ----------------------------------------------------

    static void Mines()
    {
        Fresh();
        var e = InPlay("tide_elite_bathyscaphe", new Vector2(0f, 2f));
        var db = (DeepBrain)e.Brain;
        var ma = (MineChainAttack)e.Attack;
        bool hold = StepUntil(() => db.Phase == DeepBrain.Stage.Hold, 8f);
        Check("Bathyscaphe: it holds a column at the top (" + db.Phase + ", column " + db.Column + ", y " + e.Position.y.ToString("0.00") + ")", hold && db.Column == 0 && e.Position.y > pilot.position.y + 1.5f);
        Step(DeepBrain.ReadyWait + .1f);
        StartTell(e);
        Check("on the first frame of the tell every mine of the chain is ringed (" + ma.Marks.ShownCount + " of " + ma.PlanCount + ")",
              e.Telling && ma.Count == ma.PlanCount && ma.Marks.ShownCount == ma.PlanCount);
        bool descends = true;
        for (int i = 1; i < ma.Count; i++) descends &= ma.Spot(i).y < ma.Spot(i - 1).y + 1e-3f;
        Check("the chain runs down from under the hull to the pilot's column (" + ma.Spot(0).y.ToString("0.0") + " .. " + ma.Spot(ma.Count - 1).y.ToString("0.0") + ", end x " + ma.Spot(ma.Count - 1).x.ToString("0.00") + ")",
              descends && ma.Spot(0).y < e.Position.y && Mathf.Abs(ma.Spot(ma.Count - 1).x - pilot.position.x) < .15f);
        StepUntil(() => e.Acting, 3f);
        int maxOrbs = 0, minesAlive = 0;
        float lowestMine = 99f;
        bool inView = true;
        for (float t = 0f; t < 6f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            Settle(e);
            inView &= InView(e);
            int o = 0;
            minesAlive = 0;
            foreach (var sh in EliteSystem.Shots.All)
            {
                if (!sh.Active) continue;
                if (sh.IsMine) { minesAlive++; lowestMine = Mathf.Min(lowestMine, sh.transform.position.y); }
                if (sh.Kind == EliteShots.Kind.Orb) o++;
            }
            maxOrbs = Mathf.Max(maxOrbs, o);
        }
        Check("the mines are laid one after another (" + ma.Laid + ", most at once " + maxOrbs + ") and burst, not fall away (" + minesAlive + " left, lowest y " + lowestMine.ToString("0.0") + " vs view bottom " + EliteSystem.ViewBottom.ToString("0.0") + ")",
              ma.Laid == ma.Count && maxOrbs >= 2 && minesAlive == 0 && lowestMine > EliteSystem.ViewBottom - .6f && AttackPreview.TooShort == 0);
        Check("the cast done is counted (" + db.Casts + ") and it stayed in the view", db.Casts == 1 && inView);

        // between columns it SINKS, slides low and rises again: never out of the view
        Fresh();
        e = InPlay("tide_elite_bathyscaphe", new Vector2(0f, 2f));
        db = (DeepBrain)e.Brain;
        StepUntil(() => db.Phase == DeepBrain.Stage.Hold, 8f);
        float holdY = e.Position.y;
        e.AttackCooldown = 99f;
        db.OnScriptMove("col:2");
        Check("the script names another column: it sinks (" + db.Phase + ")", db.Phase == DeepBrain.Stage.Sink && db.Target == 2);
        bool stayed = true, slid = false, rose = false;
        float lowY = 99f;
        bool back = StepUntil(() =>
        {
            stayed &= InView(e);
            lowY = Mathf.Min(lowY, e.Position.y);
            slid |= db.Phase == DeepBrain.Stage.Slide;
            rose |= db.Phase == DeepBrain.Stage.Rise;
            e.AttackCooldown = 99f;
            return db.Phase == DeepBrain.Stage.Hold && db.Column == 2;
        }, 14f);
        Check("... slides low along the zone and rises onto column 2, in the view all the way (dipped " + (holdY - lowY).ToString("0.00") + " u, dives " + db.Dives + ")",
              back && stayed && slid && rose && holdY - lowY > DeepBrain.Dip * .5f && db.Dives == 1);

        // the script's phase 2 lays a longer chain
        Fresh();
        e = InPlay("tide_elite_bathyscaphe", new Vector2(0f, 2f));
        ma = (MineChainAttack)e.Attack;
        int full = ma.PlanCount;
        e.TakeHit(EliteDamage.PlayerWeapon, e.transform.position + Vector3.left * .3f);
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        Check("phase 2 (3 hearts left) lays a longer chain (" + full + " -> " + ma.PlanCount + " at " + e.Hearts + " hearts)", ma.PlanCount >= full + 1 && ma.PlanCount < TideSpotAttack.MaxSpots);

        Fresh();
        e = InPlay("tide_elite_bathyscaphe", new Vector2(0f, 2f));
        db = (DeepBrain)e.Brain;
        ma = (MineChainAttack)e.Attack;
        StepUntil(() => db.Phase == DeepBrain.Stage.Hold, 8f);
        StartTell(e);
        Kill(e);
        Check("a Bathyscaphe killed in its tell takes its rings with it", (e == null || ma.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);
    }

    // ---- Trawler: trawl / drag_net ---------------------------------------------------------

    static void Nets()
    {
        Fresh();
        var e = InPlay("tide_elite_trawler", new Vector2(1.5f, 2.5f));
        var tb = (TrawlBrain)e.Brain;
        var dn = (DragNetAttack)e.Attack;
        bool flank = StepUntil(() => tb.Phase == TrawlBrain.Stage.Flank && Mathf.Abs(e.Position.x - tb.FlankX) < .25f, 12f);
        int side = tb.Side;
        Check("Trawler: it works the LEFT flank first, standing against the rail (side " + side + ", x " + e.Position.x.ToString("0.00") + ")",
              flank && side == -1 && Mathf.Sign(e.Position.x) == side && Mathf.Abs(e.Position.x) > EliteSystem.RailEdge - e.Def.hullRadius - .7f);
        Step(TrawlBrain.ReadyWait + .1f);
        StartTell(e);
        int n = dn.Count;
        float lastX = dn.Spot(n - 1).x;
        float clear = 2f * dn.Edge - (n - 1) * e.Def.lobSpacing - DragNetAttack.Ring;
        bool inward = true;
        for (int i = 1; i < n; i++) inward &= Mathf.Abs(dn.Spot(i).x) < Mathf.Abs(dn.Spot(i - 1).x) && Mathf.Sign(dn.Spot(i).x) == side;
        Check("on the first frame of the tell every net is ringed (" + dn.Marks.ShownCount + " of " + n + "), flank first, running in toward the middle",
              e.Telling && n == dn.NetsFor() && dn.Marks.ShownCount == n && n >= 1 && Mathf.Sign(dn.Spot(0).x) == side && inward);
        Check("the far end leaves a gap of at least " + DragNetAttack.Gap + " u to the far rail (" + clear.ToString("0.00") + ", last net x " + lastX.ToString("0.00") + ")", clear >= DragNetAttack.Gap - .01f);
        StepUntil(() => e.Acting, 3f);
        int pools = 0;
        for (float t = 0f; t < 4f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            Settle(e);
            int p = 0;
            foreach (var sh in EliteSystem.Shots.All) if (sh.Active && sh.Kind == EliteShots.Kind.Glob && !sh.Airborne) p++;
            pools = Mathf.Max(pools, p);
        }
        Check("the nets land as pools (" + pools + " at once of " + n + ") and the FR1 lead was shown", dn.Laid == n && pools >= n && AttackPreview.TooShort == 0);

        // the script sends it over the top to the other flank: one arc, never out of the view
        e.AttackCooldown = 99f;
        float topY = -99f, startY = e.Position.y;
        bool stayed = true;
        tb.OnScriptMove("R");
        Check("the script says 'R': it sets off over the top (" + tb.Phase + ")", tb.Phase == TrawlBrain.Stage.Cross);
        bool crossed = StepUntil(() =>
        {
            stayed &= InView(e);
            topY = Mathf.Max(topY, e.Position.y);
            e.AttackCooldown = 99f;
            return tb.Phase == TrawlBrain.Stage.Flank && tb.Side == 1 && Mathf.Abs(e.Position.x - tb.FlankX) < .3f;
        }, 16f);
        Check("... and lands on the opposite flank (side " + side + " -> " + tb.Side + ", crossings " + tb.Crossings + ", rose " + (topY - startY).ToString("0.00") + " u over the flank, in view " + stayed + ")",
              crossed && stayed && tb.Crossings == 1);

        Fresh();
        e = InPlay("tide_elite_trawler", new Vector2(1.5f, 2.5f));
        tb = (TrawlBrain)e.Brain;
        dn = (DragNetAttack)e.Attack;
        StepUntil(() => tb.Phase == TrawlBrain.Stage.Flank && Mathf.Abs(e.Position.x - tb.FlankX) < .25f, 12f);
        StartTell(e);
        Kill(e);
        Check("a Trawler killed in its tell takes its rings with it", (e == null || dn.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);

        // the 'both rails' row: nets from both rails, the gap in the middle
        Fresh();
        e = InPlay("tide_elite_trawler", new Vector2(0f, 2.5f));
        dn = (DragNetAttack)e.Attack;
        e.Def.shotCount = 4;
        e.Def.hazardCount = 2;
        e.Def.lobSpacing = .8f;
        StartTell(e);
        bool left = false, right = false, gap = true;
        for (int i = 0; i < dn.Count; i++)
        {
            left |= dn.Spot(i).x < -1f;
            right |= dn.Spot(i).x > 1f;
        }
        for (int i = 0; i < dn.Count; i++)
            for (int j = i + 1; j < dn.Count; j++)
                if (dn.Spot(i).x < 0f && dn.Spot(j).x > 0f) gap &= dn.Spot(j).x - dn.Spot(i).x >= DragNetAttack.Gap + 2f * DragNetAttack.Ring - .05f;
        Check("the 'both rails' row rings nets at BOTH rails and keeps the middle open (" + dn.Count + " nets)", dn.Both && dn.Count >= 2 && left && right && gap);
    }

    // ---- Thunderfin: tempest / thunder_sweep ------------------------------------------------

    // The frames (seconds from now) at which the n armed columns go live.
    static float[] LitTimes(EliteShip e, ThunderSweepAttack ts, int n)
    {
        var litAt = new float[n];
        for (int i = 0; i < n; i++) litAt[i] = -1f;
        float clock = 0f;
        for (float t = 0f; t < 6f; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            Settle(e);
            clock += Dt;
            for (int i = 0; i < n; i++)
            {
                var st = ts.Strike(i);
                if (litAt[i] < 0f && st != null && st.State == AttackHazard.Phase.Live) litAt[i] = clock;
            }
        }
        return litAt;
    }

    static void Thunder()
    {
        Fresh();
        var e = InPlay("tide_elite_thunderfin", new Vector2(0f, 3f));
        var tb = (TempestBrain)e.Brain;
        var ts = (ThunderSweepAttack)e.Attack;
        float minX = 9f, maxX = -9f;
        Step(5f, () => { minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x); });
        Check("Thunderfin: it loiters on an ellipse high over the board, never to the pilot (x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ", y " + e.Position.y.ToString("0.0") + ")",
              maxX - minX > 1f && e.Position.y > pilot.position.y + 1.5f);
        bool rightward = tb.SweepDir > 0;
        StartTell(e);
        bool allTell = ts.Armed >= 3 && ts.Armed <= ts.Want;
        for (int i = 0; i < ts.Armed; i++) allTell &= ts.Strike(i) != null && ts.Strike(i).State == AttackHazard.Phase.Tell;
        Check("a glyph is armed under EVERY column on the first frame of the tell (" + ts.Armed + ")", e.Telling && allTell);
        bool ordered = true, spaced = true, pilotLane = false;
        for (int i = 0; i < ts.Armed; i++)
        {
            pilotLane |= Mathf.Abs(ts.Lane(i) - pilot.position.x) < .2f;
            if (i == 0) continue;
            ordered &= rightward ? ts.Lane(i) > ts.Lane(i - 1) : ts.Lane(i) < ts.Lane(i - 1);
            spaced &= Mathf.Abs(ts.Lane(i) - ts.Lane(i - 1)) >= AttackStrike.MinLaneSpacing - .01f;
        }
        Check("the columns are ordered along the way it flies (" + (rightward ? "right" : "left") + "), at least a strike's corridor apart, the pilot's own lane among them", ordered && spaced && pilotLane);
        Check("... told at least " + AttackHazard.MinTellSeconds + " s (" + e.Attack.TellSeconds.ToString("0.00") + ")", e.Attack.TellSeconds >= AttackHazard.MinTellSeconds + .3f);
        int n = ts.Armed;
        var litAt = LitTimes(e, ts, n);
        bool sequence = true;
        for (int i = 0; i < n; i++) sequence &= litAt[i] >= 0f && (i == 0 || litAt[i] - litAt[i - 1] >= e.Def.shotInterval * .6f);
        Check("the columns strike ONE AFTER ANOTHER, " + e.Def.shotInterval + " s apart (first " + litAt[0].ToString("0.00") + " s, last " + litAt[n - 1].ToString("0.00") + " s)", sequence);
        e.AttackCooldown = 99f;
        Step(1.5f);
        Check("nothing is left on the board afterwards (" + AttackHazard.ActiveCount + " hazards)", AttackHazard.ActiveCount == 0);

        // the script's other orders: both ways from the outside in, and from the middle out
        e.Def.shotInterval = .3f;
        e.Def.hazardCount = 1;
        StartTell(e);
        n = ts.Armed;
        litAt = LitTimes(e, ts, n);
        int mid = n / 2;
        Check("'both ways' (order 1): the two outer columns strike together, the middle after (" + litAt[0].ToString("0.00") + " / " + litAt[mid].ToString("0.00") + " / " + litAt[n - 1].ToString("0.00") + ")",
              n >= 3 && Mathf.Abs(litAt[0] - litAt[n - 1]) < .12f && litAt[mid] > litAt[0] + .15f);
        e.AttackCooldown = 99f;
        Step(1.5f);
        e.Def.hazardCount = 2;
        StartTell(e);
        n = ts.Armed;
        litAt = LitTimes(e, ts, n);
        mid = n / 2;
        Check("'middle out' (order 2): the middle strikes first, the outer columns together after (" + litAt[0].ToString("0.00") + " / " + litAt[mid].ToString("0.00") + " / " + litAt[n - 1].ToString("0.00") + ")",
              n >= 3 && Mathf.Abs(litAt[0] - litAt[n - 1]) < .12f && litAt[mid] < litAt[0] - .15f);

        Fresh();
        e = InPlay("tide_elite_thunderfin", new Vector2(0f, 3f));
        StartTell(e);
        Kill(e);
        Check("a Thunderfin killed in its tell takes its glyphs with it", AttackHazard.ActiveCount == 0);
    }

    // ---- Pearl Warden: oyster / pearl_spiral ------------------------------------------------

    static void Pearl()
    {
        Fresh();
        var e = InPlay("tide_elite_pearl_warden", new Vector2(0f, 3f));
        var ob = (OysterBrain)e.Brain;
        var ps = (PearlSpiralAttack)e.Attack;
        bool settled = StepUntil(() => ob.Settled && e.Position.y > pilot.position.y + 1.5f, 10f);
        Check("Pearl Warden: it keeps the script's first station at the top (station " + ob.Station + ", y " + e.Position.y.ToString("0.00") + ")", settled && ob.Station == 0);
        Step(OysterBrain.ReadyWait + .1f);
        int station = ob.Station;
        StartTell(e);
        Check("the two edge lanes of the arc are drawn on the first frame of the tell (" + ps.Telegraph.Loops + " outlines)", e.Telling && ps.Telegraph.Showing && ps.Telegraph.Loops == 2);
        StepUntil(() => e.Acting, 3f);
        int sweep = ps.Sweep;
        float gap = ps.GapAngle;
        bool clearGap = true, monotone = true;
        for (int i = 0; i < ps.Total; i++)
        {
            clearGap &= Mathf.Abs(Mathf.DeltaAngle(ps.Angle(i), gap)) >= PearlSpiralAttack.GapDeg - .01f;
            if (i > 0) monotone &= (ps.Angle(i) - ps.Angle(i - 1)) * sweep > 0f;
        }
        Check("the spiral sweeps " + ps.Total + " of " + ps.PearlCount + " pearls across the arc in order, none within " + PearlSpiralAttack.GapDeg + " deg of where the pilot stood (gap " + gap.ToString("0") + " deg)",
              ps.Total >= 3 && ps.Total < ps.PearlCount && ps.Total >= ps.PearlCount - 4 && clearGap && monotone);
        int pearls = 0;
        for (float t = 0f; t < 2.5f; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            foreach (var sh in EliteSystem.Shots.All) if (sh.Active) pearls++;
        }
        Check("they are fired (" + ps.Flung + " of " + ps.Total + ", pearl frames " + pearls + ") and the script sends it to its next station (" + station + " -> " + ob.Station + ")",
              ps.Flung == ps.Total && pearls > 10 && ob.Hops == 1 && ob.Station == 1);
        Check("the next cast sweeps the other way", PearlSpiralAttack.AngleOf(0, 5, 1) < PearlSpiralAttack.AngleOf(4, 5, 1) && PearlSpiralAttack.AngleOf(0, 5, -1) > PearlSpiralAttack.AngleOf(4, 5, -1));

        // the last heart's DOUBLE spiral: both sweeps at once, wider
        e.AttackCooldown = 99f;
        StepUntil(() => ob.Settled && !e.Acting && !e.Telling, 8f);
        e.Def.hazardCount = 2;
        e.Def.shotCount = 6;
        e.Def.shotSpread = 14f;
        e.Def.shotInterval = .1f;
        StartTell(e);
        StepUntil(() => e.Acting, 3f);
        bool both = false;
        for (int i = 1; i < ps.Total; i++) both |= (ps.Angle(i) - ps.Angle(i - 1)) * ps.Sweep < 0f;
        Check("a double spiral throws both sweeps (" + ps.Total + " pearls, wider arc " + ps.Arc.ToString("0") + " deg)", ps.Double && ps.Total >= 10 && ps.Total <= PearlSpiralAttack.MaxPearls && both && ps.Arc > PearlSpiralAttack.ArcDeg + 10f);

        Fresh();
        e = InPlay("tide_elite_pearl_warden", new Vector2(0f, 3f));
        ob = (OysterBrain)e.Brain;
        ps = (PearlSpiralAttack)e.Attack;
        StepUntil(() => ob.Settled, 10f);
        StartTell(e);
        Kill(e);
        Check("a Pearl Warden killed in its tell takes its lanes with it", AttackPreview.ActiveCount == 0);
    }

    // ---- the scripts: fixed choreography, phases that differ, always on screen ----------------------------

    // What the loop of a phase attacks with, in order (the "-" movement beats left out).
    static string ExpectedAttacks(EliteDef d, int phase, int count)
    {
        var p = phase == 1 ? d.script.phase1 : phase == 2 ? d.script.phase2 : d.script.last;
        var parts = new List<string>();
        for (int i = 0; parts.Count < count; i++)
        {
            var s = p.loop[i % p.loop.Length];
            if (s.attack != "-") parts.Add(s.attack);
        }
        return string.Join(",", parts);
    }

    // Where the script has it (a column / station / flank / direction) at each attack of the loop, from the move tokens.
    static string ExpectedWhere(EliteDef d, int phase, int count)
    {
        var p = phase == 1 ? d.script.phase1 : phase == 2 ? d.script.phase2 : d.script.last;
        var parts = new List<string>();
        int cur = 0;
        for (int i = 0; parts.Count < count; i++)
        {
            var s = p.loop[i % p.loop.Length];
            int v;
            if (TideFlight.TokenInt(s.move, "col", out v) || TideFlight.TokenInt(s.move, "st", out v)) cur = v;
            else if (s.move == "L") cur = -1;
            else if (s.move == "R") cur = 1;
            if (s.attack != "-") parts.Add(cur.ToString());
        }
        return string.Join(",", parts);
    }

    static int WhereNow(EliteShip e)
    {
        if (e.Brain is DeepBrain) return ((DeepBrain)e.Brain).Column;
        if (e.Brain is OysterBrain) return ((OysterBrain)e.Brain).Station;
        if (e.Brain is TrawlBrain) return ((TrawlBrain)e.Brain).Side;
        if (e.Brain is TempestBrain) return ((TempestBrain)e.Brain).Dir;
        return 0;
    }

    static string lastDiag = "";

    // Flies the script from `hits` lost hearts for `seconds` and records what each attack was (variant name, and where it stood).
    static void Fly(string key, int hits, float seconds, int seed, out string attacks, out string places, out int phase, out bool onScreen)
    {
        Fresh();
        Random.InitState(seed);
        var e = EliteShip.CreateInPlay(EliteCatalog.Find(key), new Vector2(0f, 2f));
        e.EscapeLeft = 0f;
        e.EngageLeft = 9999f;
        if (hits > 0) e.AttackCooldown = 99f;   // (the first phase's loop starts at once; later phases are entered idle)
        for (int i = 0; i < hits; i++)
        {
            e.TakeHit(EliteDamage.Teleport, e.Position + Vector2.left, 1);
            if (i == hits - 1) break;
            e.AttackCooldown = 99f;
            Step(EliteShip.GraceSeconds + .1f);
        }
        phase = e.Script.Phase;
        var names = new List<string>();
        var spots = new List<string>();
        int seen = e.Attacks, frames = 0, on = 0;
        for (float t = 0f; t < seconds; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (e == null || e.State == EliteState.Dead) break;
            frames++;
            if (!e.Hidden && !e.Offstage && InView(e)) on++;
            if (e.Attacks > seen)
            {
                seen = e.Attacks;
                names.Add(e.Script.VariantName);
                spots.Add(WhereNow(e).ToString());
            }
        }
        onScreen = frames > 0 && on / (float)frames >= .85f;
        lastDiag = e == null ? "gone" : "state " + e.State + " pos " + e.Position.ToString("0.00") + " zoneTop " + e.ZoneTop.ToString("0.00") + " inZone " + e.InZone + " attacks " + e.Attacks;
        attacks = string.Join(",", names);
        places = string.Join(",", spots);
    }

    static string Prefix(string csv, int n)
    {
        var p = csv.Split(',');
        if (p.Length <= n) return csv;
        return string.Join(",", p, 0, n);
    }

    static void Choreography()
    {
        var kinds = new HashSet<string>();
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            kinds.Add(d.script.entrance.kind);
            Check(x.key + ": its own entrance (" + d.script.entrance.kind + ", " + d.script.entrance.seconds + " s)", d.script.entrance.seconds >= 1f && d.script.entrance.seconds <= 2f);
            bool exits = d.hurtHide && !d.armored;
            Check(x.key + ": leaves the view only for its one scripted last-heart exit (hurtHide " + d.hurtHide + ", lastExit '" + d.script.lastExit + "')",
                  exits ? d.script.lastExit == "left" || d.script.lastExit == "right" || d.script.lastExit == "top" : d.script.lastExit == "none" || !d.hurtHide || d.armored);
            Check(x.key + ": three loops that differ (" + d.script.phase1.loop.Length + " / " + d.script.phase2.loop.Length + " / " + d.script.last.loop.Length + " steps)",
                  d.script.phase1.loop.Length > 1 && d.script.phase2.loop.Length > 1 && d.script.last.loop.Length > 1 &&
                  ExpectedAttacks(d, 1, 4) != ExpectedAttacks(d, 2, 4) && ExpectedAttacks(d, 2, 4) != ExpectedAttacks(d, 3, 4));

            string a1, w1, a2, w2, aP, wP, aL, wL;
            int ph1, ph2, ph3, ph4;
            bool s1, s2, s3, s4;
            Fly(x.key, 0, 60f, 11, out a1, out w1, out ph1, out s1);
            Fly(x.key, 0, 60f, 987, out a2, out w2, out ph2, out s2);
            int n = Mathf.Min(a1.Split(',').Length, a2.Split(',').Length);
            Check(x.key + ": phase 1 flies the authored loop, the same under any random seed (" + a1 + " | where " + w1 + ")",
                  ph1 == 1 && ph2 == 1 && a1.Length > 0 && a1.Split(',').Length >= 3 && n >= 3 &&
                  Prefix(a1, n) == Prefix(a2, n) && Prefix(a1, n) == ExpectedAttacks(d, 1, n) &&
                  (x.brain == "breaker" || Prefix(w1, n) == ExpectedWhere(d, 1, n) && Prefix(w2, n) == ExpectedWhere(d, 1, n)));
            Check(x.key + ": phase 1 stays on screen", s1 && s2);

            int lost2 = d.armored ? 2 : 1, lostLast = d.hearts - 1;
            Fly(x.key, lost2, 60f, 5, out aP, out wP, out ph3, out s3);
            int n2 = aP.Split(',').Length;
            Check(x.key + ": phase 2 (after " + lost2 + " lost) flies its own loop (" + aP + " | where " + wP + " | " + lastDiag + ")",
                  ph3 == 2 && aP.Length > 0 && n2 >= 2 && Prefix(aP, n2) == ExpectedAttacks(d, 2, n2) &&
                  (x.brain == "breaker" || Prefix(wP, n2) == ExpectedWhere(d, 2, n2)));
            Fly(x.key, lostLast, 70f, 6, out aL, out wL, out ph4, out s4);
            int n3 = aL.Split(',').Length;
            Check(x.key + ": the last heart flies the desperation loop (" + aL + " | where " + wL + ") and stays on screen (" + s3 + "/" + s4 + ")",
                  ph4 == 3 && aL.Length > 0 && n3 >= 2 && Prefix(aL, n3) == ExpectedAttacks(d, 3, n3) &&
                  (x.brain == "breaker" || Prefix(wL, n3) == ExpectedWhere(d, 3, n3)) && s3 && s4);
        }
        Check("the five entrances are all different kinds (" + string.Join(",", kinds) + ")", kinds.Count == 5);
    }

    // ---- the fight rules -------------------------------------------------------------------

    static void Rules()
    {
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            if (d == null) continue;
            Check(x.key + ": hearts " + d.hearts + " (5 armoured, 3 not), hurtHide only for the non-armoured chasers",
                  d.hearts == (d.armored ? 5 : 3) && (!d.hurtHide || !d.armored));
            float drawn = ShipTierTest.DrawnLongSide(d);
            float target = d.Heavy || System.Array.IndexOf(EliteDef.TideHeavyBrains, d.brain) >= 0 ? 1.6f : 1.55f;
            Check(x.key + ": drawn long side " + drawn.ToString("0.00") + " u in the shrunk band (about " + target.ToString("0.00") + " u)", Mathf.Abs(drawn - target) < .06f);
            Check(x.key + ": no pink (shot " + ColorUtility.ToHtmlStringRGB(d.ShotColor) + ", core " + ColorUtility.ToHtmlStringRGB(d.ShotCore) + ")", !WorldCue.IsPink(d.ShotColor) && !WorldCue.IsPink(d.ShotCore));
            var atk = EliteAttacks.Create(d.attack);
            Check(x.key + ": an aimed attack winds up long enough for the lock (tell " + d.tellSeconds + " s)", !atk.Aimed || d.tellSeconds >= EliteDef.MinAimedTell);
        }
        Check("none of Tide's five roams: they keep to the board and turn round at the rail",
              !EliteBrains.Create("breaker").Roams && !EliteBrains.Create("deep").Roams && !EliteBrains.Create("trawl").Roams &&
              !EliteBrains.Create("tempest").Roams && !EliteBrains.Create("oyster").Roams);
        Check("the base ids Storm and Desert fly are still there",
              System.Array.IndexOf(EliteBrains.Ids, "interceptor") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "lance_dash") >= 0 &&
              System.Array.IndexOf(EliteBrains.Ids, "surfer") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "surf_dash") >= 0);
    }

    // ---- engine flames: baked into the strips, leaving each engine's mouth, in front of the hull ----------------
    // The burn cells (2..6) must show every engine's flame OVER the hull (a flame under the hull reads as coming from behind the engine):
    // the nozzle point is the exit (the dark bell mouth in cell 0; Bathyscaphe's lamp pods are lenses, so just solid hull), the flame covers it
    // in every burn cell, runs out past the hull's bottom edge in the hover cell, each engine on its own phase, no runtime plume doubles it.
    static Color32[] LoadCell(string key, int cell, int size)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Tide/" + key + ".png"))) return null;
        var px = tex.GetPixels32(); int w = tex.width;
        Object.DestroyImmediate(tex);
        var c = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) c[y * size + x] = px[y * w + cell * size + x];   // rows run bottom-up
        return c;
    }

    static void EngineFlames()
    {
        foreach (var e in Expected)
        {
            var def = EliteCatalog.Find(e.key);
            int S = def.cellPixels;
            var hull = LoadCell(e.key, 0, S);
            Check(e.key + ": strip loads for the flame check", hull != null);
            if (hull == null) continue;
            Check(e.key + ": no runtime plume doubles the baked flames (exhaustScale 0)", def.exhaustScale <= 0f);
            int hullBottom = 0;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) if (hull[(S - 1 - y) * S + x].a > 128) hullBottom = Mathf.Max(hullBottom, y);
            var lengths = new HashSet<int>();
            bool anyBurnsDamaged = false;   // the damaged cell (6) is a wreck: some engines are out, at least one still burns
            foreach (var nz in def.nozzles)
            {
                int nx = Mathf.RoundToInt(nz.x), ny = Mathf.RoundToInt(nz.y);
                var h0 = hull[(S - 1 - ny) * S + nx];
                bool lens = e.key.EndsWith("bathyscaphe");
                Check(e.key + " " + nz.name + ": nozzle point is on the hull at the exit (" + (lens ? "lamp-pod lens" : "dark bell mouth") + ")",
                      h0.a == 255 && (lens || (h0.r * .3f + h0.g * .59f + h0.b * .11f) < 70f));
                for (int cell = 2; cell <= 6; cell++)
                {
                    var c = LoadCell(e.key, cell, S);
                    bool covered = false;
                    for (int dy = -2; dy <= 2 && !covered; dy++)
                        for (int dx = -5; dx <= 5 && !covered; dx++)
                        {
                            var p = c[(S - 1 - (ny + dy)) * S + nx + dx]; var q = hull[(S - 1 - (ny + dy)) * S + nx + dx];
                            covered = p.a == 255 && (p.r != q.r || p.g != q.g || p.b != q.b) && Mathf.Max(p.r, Mathf.Max(p.g, p.b)) > 150;
                        }
                    if (cell == 6) anyBurnsDamaged |= covered;
                    else Check(e.key + " cell " + cell + " " + nz.name + ": flame covers the nozzle point (in front of the hull, not hidden by the bell)", covered);
                    if (cell == 3)
                    {
                        int tip = ny;
                        for (int y = ny; y < S; y++)
                            for (int dx = -3; dx <= 3; dx++)
                            {
                                var q = c[(S - 1 - y) * S + nx + dx]; var r0 = hull[(S - 1 - y) * S + nx + dx];
                                if (q.a > 0 && (q.r != r0.r || q.g != r0.g || q.b != r0.b || q.a != r0.a)) tip = Mathf.Max(tip, y);
                            }
                        Check(e.key + " " + nz.name + ": flame leaves the engine and runs past the hull's bottom edge (tip " + tip + " vs " + hullBottom + ")", tip >= hullBottom + 10);
                        lengths.Add(tip);
                    }
                }
            }
            Check(e.key + ": each engine keeps its own phase (flame lengths differ in the hover cell)", lengths.Count >= 2);
            Check(e.key + ": the damaged cell still has an engine burning (the others sputter out)", anyBurnsDamaged);
        }
    }

    // ---- allocations ------------------------------------------------------------------------

    static void Allocations()
    {
        string[][] groups =
        {
            new[] { "tide_elite_riptide", "tide_elite_bathyscaphe", "tide_elite_trawler" },
            new[] { "tide_elite_thunderfin", "tide_elite_pearl_warden" },
        };
        foreach (var g in groups)
        {
            Fresh(.1f);
            pilot.position = new Vector3(0f, -2.5f, 0f);
            var ships = new List<EliteShip>();
            for (int i = 0; i < g.Length; i++) ships.Add(InPlay(g[i], new Vector2(-1.4f + i * 1.4f, .5f + i * .4f)));
            foreach (var s in ships) s.AttackCooldown = 0f;
            Step(8f);   // warm up: every attack, preview, shot and pool once
            foreach (var s in ships) s.AttackCooldown = 0f;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 90; i++)
            {
                pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
                EliteSystem.Step(Dt);
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check("zero per-frame allocations stepping " + string.Join(", ", g) + " (" + allocated + " bytes over 90 frames)", allocated == 0);
        }
    }
}
