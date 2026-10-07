using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The death crash's domino (DeathCrashDomino): the board drops into a slow
// motion instead of freezing; a flying piece that hits an enemy destroys it
// and its own pieces fly on and hit more (a chain, generation by generation);
// each chain kill's multiplier rises; the DEATH COMBO lands on the run once,
// as its own death panel row; the panel waits for the chain and follows it
// as soon as it is over (a short chain ends early, a long one runs past 3 s,
// nothing past the ceiling, a death with no chain keeps its old timing); the
// caps hold; a tap resolves the rest of the chain's score; the boss body is
// never destroyed; a forced MEGA DOMINO takes every other target on screen,
// one after another; and a chain frame allocates nothing.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod DeathDominoTest.Run
public static class DeathDominoTest
{
    static int fails;
    const float Dt = 1f / 60f;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DDT] PASS  " : "[DDT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    struct KillEvent { public int mult, points; public float at; }
    static readonly List<KillEvent> kills = new List<KillEvent>();
    static int megaBanners, comboBanners, lastComboBanner;

    static void OnKill(Vector3 at, int mult, int points)
    {
        var crash = DeathCrash.Instance;
        kills.Add(new KillEvent { mult = mult, points = points, at = crash != null ? crash.Elapsed : 0f });
    }
    static void OnMega(Vector3 at) { megaBanners++; }
    static void OnCombo(int points, int n, bool mega) { comboBanners++; lastComboBanner = points; }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        EliteCatalog.Reload();
        DeathCrash.DominoKill += OnKill;
        DeathCrash.MegaDominoStarted += OnMega;
        DeathCrash.DeathCombo += OnCombo;
        try
        {
            DeathCrash.ForceMega = false;
            // Every touch breaks, every chain kill's pieces live: these
            // scenarios stage the chain by hand (DeathComboTest covers the
            // 40% rolls).
            DeathCombo.ForceDeathHit = 1f;
            DeathCombo.ForceDeathChain = 1f;
            SlowMotion();
            NoChain();
            ShortChain();
            ShotsCleared();
            Chain();
            Caps(false);
            Caps(true);
            Mega();
            SkipResolves();
            TallScreens();
            Allocations();
        }
        finally
        {
            DeathCrash.DominoKill -= OnKill;
            DeathCrash.MegaDominoStarted -= OnMega;
            DeathCrash.DeathCombo -= OnCombo;
            DeathCrash.ForceMega = null;
            DeathCombo.ForceDeathHit = DeathCombo.ForceDeathChain = -1f;
            RunScore.EndRun(RunScore.RunId);
            moveBackGround.speed = 0f;
            buttonClicks.playerDied = false;
        }
        Debug.Log("[DDT] failures: " + fails);
        return fails;
    }

    // ---------------------------------------------------------------- fixtures

    static readonly List<GameObject> spawned = new List<GameObject>();

    static void Fresh(int seed)
    {
        foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
        spawned.Clear();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, false);
        moveBackGround.speed = .3f;
        kills.Clear();
        megaBanners = comboBanners = lastComboBanner = 0;
        Random.InitState(seed);
    }

    static DeathCrashTest.Rig Ship(int id, Vector3 at)
    {
        var rig = new DeathCrashTest.Rig(id);
        rig.ship.transform.position = at;
        return rig;
    }

    // A real roster rock (art, collider, identity), its movers off unless `moving`.
    static GameObject Rock(Vector3 at, bool moving = false, int world = 0)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(world, EnemyRole.Rock), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        if (!moving)
            foreach (var b in go.GetComponents<MonoBehaviour>())
                if (!(b is EnemyIdentity) && !(b is ClearTarget)) b.enabled = false;
        spawned.Add(go);
        return go;
    }

    static List<GameObject> Field(int n, float margin, bool moving = false)
    {
        var list = new List<GameObject>();
        float top = CameraFit.ViewTop - margin, bottom = CameraFit.ViewBottom + margin;
        BossRails.Measure();
        float half = BossRails.InnerEdge - .35f;
        int cols = 4;
        int rows = Mathf.CeilToInt(n / (float)cols);
        for (int i = 0; i < n; i++)
        {
            int c = i % cols, r = i / cols;
            float x = Mathf.Lerp(-half, half, (c + .5f) / cols) + Random.Range(-.15f, .15f);
            float y = Mathf.Lerp(bottom, top, (r + .5f) / Mathf.Max(1, rows)) + Random.Range(-.1f, .1f);
            list.Add(Rock(new Vector3(x, y, 0f), moving));
        }
        return list;
    }

    static GameObject Kill(DeathCrashTest.Rig rig)
    {
        var k = DeathCrashTest.Killer("aestroid_1", "Astr", rig.ship.transform.position + new Vector3(.15f, .4f, 0f));
        rig.Hit(k);
        Object.DestroyImmediate(k);
        return k;
    }

    // Steps to the panel (or `limit` s), checking the panel never shows while
    // anything is still in the air or a cascade kill is pending.
    static float RunToEnd(DeathCrash crash, ref bool early, float limit = 14f, System.Action each = null)
    {
        float t = 0f;
        while (DeathCrash.Running && t < limit)
        {
            if (each != null) each();
            crash.Step(Dt);
            t += Dt;
            if (DeathCrash.PanelReady && (crash.LastLanding <= 0f)) early = true;
        }
        return t;
    }

    static int Alive(List<GameObject> list)
    {
        int n = 0;
        foreach (var go in list) if (go != null) n++;
        return n;
    }

    // ---------------------------------------------------------------- suites

    // The world moves on, slowly: a scrolling rock covers SlowMo of what it
    // would at full speed, while the gameplay systems stay stopped.
    static void SlowMotion()
    {
        Fresh(11);
        var rig = Ship(1, new Vector3(0f, -2.4f, 1f));
        var go = new GameObject("~SlowRock", typeof(SpriteRenderer), typeof(CircleCollider2D));
        go.tag = "Astr";
        go.transform.position = new Vector3(1.2f, CameraFit.ViewTop - .6f, 0f);
        var mover = go.AddComponent<moveItemEnmInStrightLine>();
        ClearTarget.Ensure(go);
        spawned.Add(go);

        // The same mover at full speed, for the yardstick.
        float y0 = go.transform.position.y;
        mover.Step(.1f);
        float full = y0 - go.transform.position.y;
        go.transform.position = new Vector3(1.2f, y0, 0f);

        Kill(rig);
        var crash = DeathCrash.Instance;
        Check("the rock is in the slow motion", crash.ActorCount >= 1);
        for (int i = 0; i < 6; i++) crash.Step(Dt);   // .1 s, inside the hit-stop (no piece flies yet)
        float slow = y0 - go.transform.position.y;
        float ratio = full > 0f ? slow / full : 0f;
        Check("the world keeps moving in slow motion (moved " + slow.ToString("F4") + " vs " + full.ToString("F4") + " at full speed, x" + ratio.ToString("F2") + ")",
              slow > 0f && Mathf.Abs(ratio - DeathCrash.SlowMo) < .03f);
        Check("the slow-motion clock runs at x" + DeathCrash.SlowMo, Mathf.Abs(crash.WorldClock - .1f * DeathCrash.SlowMo) < .002f);
        Check("gameplay stays stopped (playerDied, no scroll)", buttonClicks.playerDied && !TargetExplosion.WorldScrolling);
        bool early = false;
        RunToEnd(crash, ref early);
        rig.Dispose();
    }

    // No targets: no chain, and the old timing.
    static void NoChain()
    {
        Fresh(12);
        var rig = Ship(4, new Vector3(-.5f, -2f, 1f));
        Kill(rig);
        var crash = DeathCrash.Instance;
        bool early = false;
        float end = RunToEnd(crash, ref early);
        Check("no chain: nothing to hit (" + crash.Targets + " targets)", crash.Targets == 0 && crash.DominoKills == 0);
        Check("no chain: the panel at the old " + DeathCrash.MinTotal + "-" + DeathCrash.MaxTotal + "s (" + end.ToString("F2") + ")",
              end >= DeathCrash.MinTotal && end <= DeathCrash.MaxTotal + Dt);
        Check("no chain: settle " + DeathCrash.Settle + "s after the last landing",
              Mathf.Abs(crash.FinishedAt - crash.LastLanding - DeathCrash.Settle) < Dt + .001f);
        Check("no chain: no DEATH COMBO", RunScore.Parts.deathCombo == 0 && comboBanners == 0);
        rig.Dispose();
    }

    // One rock on the main chunk's path: one kill, and the panel follows the
    // end of the chain, not a fixed budget.
    // An elite shot in the wreckage's way is cleared, and scores nothing.
    static void ShotsCleared()
    {
        Fresh(21);
        var rig = Ship(1, new Vector3(0f, -2.2f, 1f));
        var defs = EliteCatalog.All;
        if (defs == null || defs.Length == 0) { Check("shots: an elite to fire with", false); rig.Dispose(); return; }
        var owner = EliteShip.CreateInPlay(defs[0], new Vector2(0f, CameraFit.ViewTop + 1.2f));
        spawned.Add(owner.gameObject);
        var shot = EliteSystem.Shots.Fire(owner, defs[0], EliteShots.Kind.Bolt, new Vector2(0f, CameraFit.ViewTop - .5f), Vector2.zero);
        Kill(rig);
        var crash = DeathCrash.Instance;
        bool placed = false, early = false;
        RunToEnd(crash, ref early, 14f, () =>
        {
            if (placed || shot == null) return;
            for (int i = 0; i < crash.PieceCount; i++)
                if (crash.IsMain(i) && crash.Flying(i))
                {
                    shot.transform.position = crash.PathAt(i, crash.ProgressOf(i) + .1f);
                    placed = true;
                }
        });
        Check("shots: the elite shot on the main chunk's path is cleared (" + crash.ShotsCleared + ")",
              shot != null && !shot.Active && crash.ShotsCleared >= 1);
        Check("shots: no chain kill, no DEATH COMBO for a shot", crash.DominoKills == 0 && RunScore.Parts.deathCombo == 0);
        rig.Dispose();
        EliteSystem.Clear();
    }

    static void ShortChain()
    {
        Fresh(13);
        var rig = Ship(1, new Vector3(0f, -2.2f, 1f));
        var rock = Rock(new Vector3(0f, CameraFit.ViewTop - .5f, 0f));
        Kill(rig);
        var crash = DeathCrash.Instance;
        bool placed = false, early = false;
        float end = RunToEnd(crash, ref early, 14f, () =>
        {
            if (placed || rock == null) return;
            for (int i = 0; i < crash.PieceCount; i++)
                if (crash.IsMain(i) && crash.Flying(i))
                {
                    rock.transform.position = crash.PathAt(i, crash.ProgressOf(i) + .15f) + Vector3.back;
                    placed = true;
                }
        });
        Check("short chain: the rock on the main chunk's path dies (" + crash.DominoKills + " kills)", rock == null && crash.DominoKills == 1);
        Check("short chain: it ends early (" + end.ToString("F2") + "s)", end < 3f);
        Check("short chain: the panel " + (DeathCrash.Settle + DeathCrash.ComboHold) + "s after the last landing",
              Mathf.Abs(crash.FinishedAt - crash.LastLanding - DeathCrash.Settle - DeathCrash.ComboHold) < Dt + .001f);
        Check("short chain: DEATH COMBO +" + crash.ComboTotal + " (" + ScoreRules.Rock + " x1)",
              crash.ComboTotal == ScoreRules.Rock && RunScore.Parts.deathCombo == ScoreRules.Rock && comboBanners == 1);
        rig.Dispose();
    }

    // A chain three deep: a hull piece hits rock A, A's pieces hit B, B's hit C.
    static void Chain()
    {
        Fresh(14);
        var rig = Ship(1, new Vector3(0f, -2.4f, 1f));
        var parked = new List<GameObject>();
        // parked low, out of the arcs' way, until staged on a piece's path
        float low = CameraFit.ViewBottom + .3f;
        for (int i = 0; i < 6; i++) parked.Add(Rock(new Vector3(-1.5f + i * .6f, low, 0f)));
        Kill(rig);
        var crash = DeathCrash.Instance;
        Check("chain: the parked rocks are targets (" + crash.Targets + ")", crash.Targets == parked.Count);
        GameObject staged = null;
        int stagedKills = 0, stagedFrames = 0;
        int debrisOfFirst = -1, partsOfFirst = 0;
        bool early = false, panelWhileFlying = false;
        float end = RunToEnd(crash, ref early, 14f, () =>
        {
            if (DeathCrash.PanelReady && (crash.AnyFlying || crash.CascadePending)) panelWhileFlying = true;
            if (debrisOfFirst < 0 && crash.DominoKills >= 1)
            {
                debrisOfFirst = 0;
                for (int i = 0; i < crash.PieceCount; i++)
                    if (crash.KindOf(i) == DeathCrash.PieceKind.Debris && crash.GenerationOf(i) == 1)
                    {
                        debrisOfFirst++;
                        var s = crash.SpriteOf(i);
                        if (s != null && s.texture != null && DeathCrash.DrawnPixels(s.texture) > 0) partsOfFirst++;
                    }
            }
            // a staged rock its piece missed is staged again
            if (staged != null && crash.DominoKills == stagedKills && ++stagedFrames > 30) staged = null;
            if (staged != null || crash.DeepestGeneration >= 3) return;
            int gen = crash.DeepestGeneration;
            for (int i = 0; i < crash.PieceCount; i++)
            {
                if (!crash.Flying(i) || crash.GenerationOf(i) != gen || crash.ProgressOf(i) > .6f) continue;
                if (gen == 0 && crash.KindOf(i) != DeathCrash.PieceKind.Hull) continue;
                // just ahead of it, clear of any older piece (the one that
                // made it ricochets away from the same spot)
                Vector3 spot = crash.PathAt(i, crash.ProgressOf(i) + .06f);
                bool clear = true;
                for (int j = 0; j < crash.PieceCount; j++)
                    if (j != i && crash.PieceVisible(j) && !crash.Landed(j) && crash.GenerationOf(j) != gen &&
                        ((Vector2)(crash.PositionOf(j) - spot)).sqrMagnitude < .45f * .45f) clear = false;
                if (!clear) continue;
                foreach (var r in parked)
                    if (r != null)
                    {
                        r.transform.position = spot + Vector3.back;
                        staged = r;
                        stagedKills = crash.DominoKills;
                        stagedFrames = 0;
                        break;
                    }
                if (staged != null) break;
            }
        });
        // `staged` is reset as each one dies (Unity null)
        Check("chain: the first rock broke into its own pieces (" + partsOfFirst + "/" + debrisOfFirst + " drawn)",
              debrisOfFirst >= 2 && partsOfFirst == debrisOfFirst);
        Check("chain: three generations deep (" + crash.DeepestGeneration + "), " + crash.DominoKills + " kills",
              crash.DeepestGeneration >= 3 && crash.DominoKills >= 3);
        bool rising = kills.Count == crash.DominoKills;
        long sum = 0;
        for (int i = 0; i < kills.Count; i++)
        {
            rising &= kills[i].mult == ScoreRules.DominoMultiplier(i + 1) && kills[i].points == ScoreRules.Rock * kills[i].mult;
            sum += kills[i].points;
        }
        Check("chain: the multiplier rises x1, x2, x3 ... and each kill pays rock x it", rising && kills.Count >= 3 && kills[2].mult == 3);
        Check("chain: DEATH COMBO is their sum (" + sum + "), added to the run once",
              crash.ComboTotal == sum && RunScore.Parts.deathCombo == sum && crash.ComboAwarded == sum && comboBanners == 1 && lastComboBanner == sum);
        Check("chain: the death panel lists it (DEATH COMBO row)",
              DeathPanelView.BreakdownLabels[7] == "DEATH COMBO" && DeathPanelView.BreakdownPoints(RunScore.Parts)[7] == sum &&
              DeathPanelView.BreakdownCounts(RunScore.Parts)[7] == crash.DominoKills);
        Check("chain: the run total includes it", RunScore.Total >= sum);
        Check("chain: the panel never shows while a piece is in the air", !panelWhileFlying && !early);
        Check("chain: the panel follows the end of the chain (" + crash.FinishedAt.ToString("F2") + "s)",
              Mathf.Abs(crash.FinishedAt - crash.LastLanding - DeathCrash.Settle - DeathCrash.ComboHold) < Dt + .001f);
        for (int i = 0; i < 30; i++) crash.Step(Dt);
        Check("chain: stepping on adds nothing more", RunScore.Parts.deathCombo == sum);
        Debug.Log("[DDT] chain: " + crash.DominoKills + " kills, " + crash.DeepestGeneration + " deep, " + crash.Ricochets +
                  " ricochets, +" + crash.ComboTotal + ", panel at " + end.ToString("F2") + "s");
        rig.Dispose();
    }

    // A screen full of rocks: the chain stays within its caps and ends.
    static void Caps(bool mega)
    {
        string who = mega ? "mega field" : "field";
        Fresh(mega ? 16 : 15);
        DeathCrash.ForceMega = mega;
        var rig = Ship(2, new Vector3(0f, -.5f, 1f));
        var field = Field(56, .7f);
        Kill(rig);
        var crash = DeathCrash.Instance;
        bool early = false;
        float end = RunToEnd(crash, ref early, 20f);
        DeathCrash.ForceMega = false;
        Check(who + ": the chain ends (" + end.ToString("F2") + "s, " + crash.DominoKills + " kills, " + crash.DeepestGeneration + " deep)",
              !DeathCrash.Running && crash.Finished);
        Check(who + ": nothing past the " + DeathCrash.ExtraCeiling + "s ceiling (" + crash.FinishedAt.ToString("F2") + " <= " + DeathCrash.CeilingEnd + ")",
              crash.FinishedAt <= DeathCrash.CeilingEnd + Dt && crash.LastKillTime <= DeathCrash.LastKillAt + Dt);
        Check(who + ": generations capped at " + DeathCrash.MaxGeneration, crash.DeepestGeneration <= DeathCrash.MaxGeneration);
        if (!mega) Check(who + ": kills capped at " + DeathCrash.MaxChainKills, crash.DominoKills <= DeathCrash.MaxChainKills);
        else
        {
            Check(who + ": the whole screen goes (" + Alive(field) + " left), compressed to fit", Alive(field) == 0 && crash.DominoKills == field.Count);
            Check(who + ": a long chain runs past 3 s (" + end.ToString("F2") + "s)", end > 3f);
        }
        Check(who + ": the multiplier caps at x" + ScoreRules.DominoMaxMultiplier, crash.BestDominoMultiplier <= ScoreRules.DominoMaxMultiplier);
        Check(who + ": DEATH COMBO added once", RunScore.Parts.deathCombo == crash.ComboTotal && comboBanners == (crash.DominoKills > 0 ? 1 : 0));
        Check(who + ": every piece ended on a rail", AllOnRails(crash));
        rig.Dispose();
    }

    static bool AllOnRails(DeathCrash crash)
    {
        bool ok = true;
        for (int i = 0; i < crash.PieceCount; i++)
        {
            if (!crash.Landed(i)) { ok = false; continue; }
            var p = crash.PositionOf(i);
            if (!crash.PieceVisible(i)) continue;   // faded already
            ok &= Mathf.Abs(Mathf.Abs(p.x) - crash.RailEdge) <= .17f && p.y > crash.ViewBottomAtStart + .5f && p.y < crash.ViewTopAtStart - .5f;
        }
        return ok;
    }

    // MEGA DOMINO: every target on screen goes, one after another; the boss
    // body stays; an elite dies through its own API (and pays its reward).
    static void Mega()
    {
        Fresh(17);
        DeathCrash.ForceMega = true;
        var rig = Ship(1, new Vector3(0f, -2.4f, 1f));
        var field = Field(14, 1f);
        var boss = new GameObject("BossBody", typeof(SpriteRenderer), typeof(CircleCollider2D)) { tag = "Enimey" };
        boss.transform.position = new Vector3(0f, CameraFit.ViewTop - 1.2f, 0f);
        ClearTarget.Ensure(boss).SetRadius(.9f);
        spawned.Add(boss);
        EliteShip elite = null;
        var defs = EliteCatalog.All;
        if (defs != null && defs.Length > 0)
        {
            elite = EliteShip.CreateInPlay(defs[0], new Vector2(1.1f, .6f));
            if (elite != null) spawned.Add(elite.gameObject);
        }
        int paidBefore = EliteRewards.Paid;
        bool hadElite = elite != null;
        Kill(rig);
        var crash = DeathCrash.Instance;
        int targets = crash.Targets;
        Check("mega: it is a MEGA DOMINO, with its banner", crash.Mega && megaBanners == 1);
        Check("mega: the boss body is not a target (" + targets + " targets)", targets == field.Count + (elite != null ? 1 : 0));
        bool early = false;
        float end = RunToEnd(crash, ref early);
        Check("mega: every rock on screen dies (" + Alive(field) + " left)", Alive(field) == 0);
        if (hadElite)
            Check("mega: the elite goes through its own death (reward paid)",
                  (elite == null || elite.State == EliteState.Dead) && EliteRewards.Paid == paidBefore + 1);
        else Check("mega: an elite to test with", false);
        Check("mega: the boss body is never destroyed", boss != null);
        Check("mega: " + crash.DominoKills + " kills, one per target", crash.DominoKills == targets);
        // one after another, not all at once
        int distinct = 0;
        float lastAt = -1f, minGap = 99f;
        foreach (var k in kills)
        {
            if (k.at > lastAt + 1e-4f) { distinct++; if (lastAt >= 0f) minGap = Mathf.Min(minGap, k.at - lastAt); lastAt = k.at; }
        }
        Check("mega: a cascade, kill after kill (" + distinct + " moments for " + kills.Count + " kills)", distinct >= kills.Count * .75f);
        Check("mega: under the ceiling (" + end.ToString("F2") + "s)", crash.FinishedAt <= DeathCrash.CeilingEnd + Dt);
        long expect = crash.DominoPoints + ScoreRules.MegaDominoBonus;
        Check("mega: DEATH COMBO = the kills + the MEGA bonus (" + crash.ComboTotal + "), added once",
              crash.ComboTotal == expect && RunScore.Parts.deathCombo == expect && RunScore.Parts.megaDominos == 1 && comboBanners == 1);
        Check("mega: the multiplier rose to x" + crash.BestDominoMultiplier, crash.BestDominoMultiplier == Mathf.Min(targets, ScoreRules.DominoMaxMultiplier));
        DeathCrash.ForceMega = false;
        Debug.Log("[DDT] mega: " + targets + " targets, panel at " + end.ToString("F2") + "s, +" + crash.ComboTotal);
        rig.Dispose();
    }

    // A tap mid-chain: the panel at once, and the rest of the chain scored.
    static void SkipResolves()
    {
        Fresh(18);
        DeathCrash.ForceMega = true;
        var rig = Ship(3, new Vector3(0f, -2.4f, 1f));
        var field = Field(16, 1f);
        Kill(rig);
        var crash = DeathCrash.Instance;
        for (int i = 0; i < 40; i++) crash.Step(Dt);
        int before = crash.DominoKills;
        Check("skip: the tap skips (" + before + " kills so far)", crash.Skip() && DeathCrash.PanelReady && crash.Skipped);
        DeathCrash.ForceMega = false;
        Check("skip: the rest of the chain is resolved (" + crash.DominoKills + " kills, " + Alive(field) + " left)",
              crash.DominoKills > before && Alive(field) == 0);
        long expect = crash.DominoPoints + ScoreRules.MegaDominoBonus;
        Check("skip: its score is on the run, once", crash.ComboTotal == expect && RunScore.Parts.deathCombo == expect && comboBanners == 1);
        for (int i = 0; i < 60; i++) crash.Step(Dt);
        Check("skip: nothing more after", RunScore.Parts.deathCombo == expect);
        Check("skip: every piece on a rail", AllOnRails(crash));
        rig.Dispose();
    }

    static void TallScreens()
    {
        var cam = Camera.main;
        if (cam == null) { Check("gameS1 has a main camera", false); return; }
        float baseSize = cam.orthographicSize, baseAspect = cam.aspect;
        foreach (var s in TallScreenTest.Screens)
        {
            cam.orthographicSize = CameraFit.ComputeSize(baseSize, 2.85f, s.w, s.h);
            cam.aspect = (float)s.w / s.h;
            Fresh(19);
            DeathCrash.ForceMega = true;
            var rig = Ship(11, new Vector3(0f, -1.6f, 1f));
            var field = Field(10, .9f);
            Kill(rig);
            var crash = DeathCrash.Instance;
            bool early = false;
            RunToEnd(crash, ref early);
            DeathCrash.ForceMega = false;
            Check(s.name + ": the mega chain clears the screen and every piece ends on a rail, on screen",
                  Alive(field) == 0 && AllOnRails(crash) && crash.FinishedAt <= DeathCrash.CeilingEnd + Dt);
            rig.Dispose();
        }
        cam.orthographicSize = baseSize;
        cam.aspect = baseAspect;
    }

    // Once the drawings are cut (the first pass), a chain step allocates
    // nothing -- with the movers stepping in slow motion.
    static void Allocations()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            Fresh(20);
            DeathCrash.ForceMega = true;
            var rig = Ship(1, new Vector3(0f, -2.4f, 1f));
            Field(12, 1f, moving: true);
            Kill(rig);
            var crash = DeathCrash.Instance;
            int frames = 0, allocFrames = 0;
            for (float t = 0f; DeathCrash.Running && t < 14f; t += Dt)
            {
                int k = crash.DominoKills, im = crash.Impacts, r = crash.Ricochets;
                long a = System.GC.GetAllocatedBytesForCurrentThread();
                crash.Step(Dt);
                long b = System.GC.GetAllocatedBytesForCurrentThread();
                if (crash.DominoKills == k && crash.Impacts == im && crash.Ricochets == r && DeathCrash.Running)
                {
                    frames++;
                    if (b != a) allocFrames++;
                }
            }
            DeathCrash.ForceMega = false;
            if (pass == 1)
                Check("a chain frame allocates nothing (" + allocFrames + "/" + frames + " frames allocated)", allocFrames == 0 && frames > 60);
            rig.Dispose();
        }
        foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
        spawned.Clear();
    }
}
