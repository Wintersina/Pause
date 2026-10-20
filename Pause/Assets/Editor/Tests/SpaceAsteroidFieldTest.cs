using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

// SPACE ASTEROID FIELD (Scripts/Gameplay/Asteroids) and SPACE'S RANDOM OPENING SKY (Worlds/Backdrop/SpaceOpening).
//
//   HAZARD     a field rock is a real "Astr" hazard: trigger collider, ClearTarget (weapons and the ultimate see it)
//   HITS       it takes SizeHits worth of ship attacks, then bursts; large -> two medium, medium -> two small, small gone
//   MOTION     falls with the board, drifts sideways, leaves the view; a frozen dt moves nothing
//   RULES      Space only, not in the first 25 s, tutorial, death, boss, transition, elites; modest rate, MaxAlive
//   OPENING    the weighted pick (None stays possible, missing art drops out), a forced kind is what Space opens with,
//              no planet at all still lets the planet queue and the rest of the backdrop run
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod SpaceAsteroidFieldTest.Run
public static class SpaceAsteroidFieldTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FIELD] PASS  " : "[FIELD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Hazard();
            Hits();
            Motion();
            Patterns();
            Rules();
            Director();
            Picks();
            Openings();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            SpaceAsteroidField.Disabled = false;
            SpaceAsteroidField.SeedOverride = -1;
            SpaceAsteroidFieldDirector.WorldOverride = -1;
            SpaceAsteroidField.ClearAll();
            SpaceAsteroidField.Forget();
            SpaceDirector.OpeningOverride = -1;
            SpaceDirector.OpeningSeedOverride = -1;
        }
        Debug.Log("[FIELD] failures: " + fails);
        return fails;
    }

    static void Fresh()
    {
        SpaceAsteroidField.ClearAll();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6.6f;
        cam.aspect = 9f / 16f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.atomCheck = false;
        collisionDetection.lifeCounter = 0;
        BossEncounter.ResetRun();
        BossRails.Reset();
        SpaceAsteroidField.Disabled = false;
        SpaceAsteroidField.SeedOverride = 77;
        SpaceAsteroidFieldDirector.WorldOverride = -1;
        Random.InitState(4242);
    }

    // ---- hazard ----

    static void Hazard()
    {
        Fresh();
        Check("the Space rock art loads (three rocks)", SpaceAsteroidField.Art.Length == 3);
        for (int tier = 0; tier < 3; tier++)
        {
            var a = SpaceAsteroidField.Spawn(tier, tier, new Vector2(0f, 2f), 0f, 0f, 20f);
            var go = a.gameObject;
            var col = go.GetComponent<CircleCollider2D>();
            Check("tier " + tier + ": tagged Astr, a trigger collider, a ClearTarget",
                  go.CompareTag("Astr") && col != null && col.isTrigger && go.GetComponent<ClearTarget>() != null);
            Check("tier " + tier + ": a hazard weapons can lock (ShipTargets.IsHazard)", ShipTargets.IsHazard(go.GetComponent<ClearTarget>()));
            var sprite = go.GetComponent<SpriteRenderer>();
            float w = sprite.sprite.bounds.size.x * go.transform.localScale.x;
            Check("tier " + tier + ": drawn at its width (" + w.ToString("F2") + ")", Mathf.Abs(w - SpaceAsteroidField.Width[tier]) < .02f);
            Check("tier " + tier + ": collider inside the drawing", col.bounds.extents.x < w * .5f);
            Check("tier " + tier + ": no pink tint (white sprite colour)", go.GetComponent<SpriteRenderer>().color == Color.white);
        }
        float last = 0f; bool grows = true;
        foreach (float w in SpaceAsteroidField.Width) { grows &= w > last; last = w; }
        Check("sizes grow with the tier", grows);
        SpaceAsteroidField.ClearAll();
    }

    // ---- hits and splits ----

    static void Hits()
    {
        Fresh();
        var big = SpaceAsteroidField.Spawn(2, 0, new Vector2(0f, 2f), 0f, 0f, 10f);
        big.TakeShipAttack(0, 1f, big.transform.position);
        Check("a large rock survives one hit", !big.Broken && SpaceAsteroidField.Live.Count == 1);
        big.TakeShipAttack(0, 1f, big.transform.position);
        Check("... and two", !big.Broken);
        int splits = SpaceAsteroidField.Splits;
        big.TakeShipAttack(0, 1f, big.transform.position);
        Check("the third breaks it into two medium rocks", SpaceAsteroidField.Live.Count == 2 && SpaceAsteroidField.Splits == splits + 1);
        foreach (var a in SpaceAsteroidField.Live) Check("... each a medium (tier 1)", a != null && a.Tier == 1);
        Check("... and the pieces part sideways", SpaceAsteroidField.Live[0].Vx * SpaceAsteroidField.Live[1].Vx < 0f);

        // a gatling-style fraction chips it instead of killing it
        var chunk = SpaceAsteroidField.Live[0];
        chunk.TakeShipAttack(0, .25f, chunk.transform.position);
        Check("a quarter-weight hit only chips a medium rock", !chunk.Broken && Mathf.Abs(chunk.Health - 1.75f) < .001f);

        // small ones just burst
        SpaceAsteroidField.ClearAll();
        var small = SpaceAsteroidField.Spawn(0, 1, new Vector2(0f, 2f), 0f, 0f, 10f);
        small.TakeShipAttack(0, 1f, small.transform.position);
        Check("a small rock bursts in one hit, leaving nothing", SpaceAsteroidField.Live.Count == 0);

        // the cap holds when the field is crowded
        SpaceAsteroidField.ClearAll();
        for (int i = 0; i < SpaceAsteroidField.MaxTotal; i++)
            SpaceAsteroidField.Spawn(i == 0 ? 2 : 0, i, new Vector2(-1.5f + i * .6f, 3f), 0f, 0f, 10f);
        var first = SpaceAsteroidField.Live[0];
        first.TakeShipAttack(0, 99f, first.transform.position);
        Check("a crowded field does not split past MaxTotal", SpaceAsteroidField.Live.Count <= SpaceAsteroidField.MaxTotal);

        // the ultimate / weapons path: ShipAttackHits routes to TakeShipAttack, not a plain kill
        SpaceAsteroidField.ClearAll();
        var tough = SpaceAsteroidField.Spawn(1, 2, new Vector2(0f, 2f), 0f, 0f, 10f);
        ShipAttackHits.Hit(tough.gameObject, 0);
        Check("ShipAttackHits.Hit lands as a hit (a medium rock takes two)", tough != null && !tough.Broken && Mathf.Abs(tough.Health - 1f) < .001f);
        SpaceAsteroidField.ClearAll();
    }

    // ---- motion ----

    static void Motion()
    {
        Fresh();
        var a = SpaceAsteroidField.Spawn(1, 0, new Vector2(0f, 7f), .5f, 0f, 30f);
        float y0 = a.transform.position.y, x0 = a.transform.position.x;
        a.Step(0f, .2f, CameraFit.ViewBottom);
        Check("a zero dt moves nothing", a.transform.position.y == y0 && a.transform.position.x == x0);
        a.Step(1f, .2f, CameraFit.ViewBottom);
        Check("it falls with the board and drifts sideways",
              a.transform.position.y < y0 - .5f && a.transform.position.x > x0 + .4f);
        Check("it falls slower than the board's own scroll",
              y0 - a.transform.position.y < .2f * SpaceAsteroidField.ScrollK);
        bool gone = false;
        for (int i = 0; i < 40 * 30 && !gone; i++) gone = !a.Step(Dt, .2f, CameraFit.ViewBottom);
        Check("it leaves once past the bottom of the view", gone);
        Check("... and is no longer tracked", SpaceAsteroidField.Live.Count == 0);
    }

    // ---- the two descent patterns ----

    static void Patterns()
    {
        Fresh();
        // straight: no sideways movement at all
        var s = SpaceAsteroidField.Spawn(1, 0, new Vector2(0f, 7f), 0f, 0f, 30f);
        float x0 = s.transform.position.x, y0 = s.transform.position.y;
        for (int i = 0; i < 60; i++) s.Step(Dt, .2f, CameraFit.ViewBottom);
        Check("a straight rock falls without drifting sideways",
              s.Pattern == SpaceAsteroidField.Straight && Mathf.Abs(s.transform.position.x - x0) < 1e-4f &&
              s.transform.position.y < y0 - .5f);
        // zig-zag: sways both ways about its start, descends, stays inside the rails
        var z = SpaceAsteroidField.Spawn(1, 0, new Vector2(0f, 7f), 0f, 0f, 30f, .8f, 4f, 0f);
        float minX = 0f, maxX = 0f, zy0 = z.transform.position.y;
        bool inside = true;
        for (int i = 0; i < 12 * 30; i++)
        {
            z.Step(Dt, .02f, CameraFit.ViewBottom - 100f);
            float x = z.transform.position.x;
            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
            inside &= Mathf.Abs(x) <= SpaceAsteroidField.Edge + 1e-3f;
        }
        Check("a zig-zag rock weaves both ways across the lane (" + minX.ToString("F2") + ".." + maxX.ToString("F2") + ")",
              z.Pattern == SpaceAsteroidField.ZigZag && minX < -.3f && maxX > .3f && inside);
        Check("... while still descending", z.transform.position.y < zy0 - 1f);
        SpaceAsteroidField.ClearAll();

        // the director picks both patterns, keeps every rock in the lane, and never sets a heading
        var go = new GameObject("~dirPat");
        var dir = go.AddComponent<SpaceAsteroidFieldDirector>();
        dir.Begin();
        int straight = 0, zig = 0, off = 0;
        for (int i = 0; i < 60; i++)
        {
            var a = dir.SpawnOne();
            if (a == null) { SpaceAsteroidField.ClearAll(); continue; }
            if (a.Pattern == SpaceAsteroidField.ZigZag) zig++; else straight++;
            if (a.Vx != 0f || a.Vy != 0f || Mathf.Abs(a.transform.position.x) > SpaceAsteroidField.Edge) off++;
            SpaceAsteroidField.ClearAll();
        }
        Check("the director gives both straight (" + straight + ") and zig-zag (" + zig + ") rocks, all in the lane", straight > 5 && zig > 5 && off == 0);
        Object.DestroyImmediate(go);
        SpaceAsteroidField.ClearAll();
    }

    // ---- rules ----

    static void Rules()
    {
        Fresh();
        Check("Space, in the clear, late enough: allowed", SpaceAsteroidFieldDirector.Blocked(0, 120f) == null);
        Check("not in the first FirstSeconds", SpaceAsteroidFieldDirector.Blocked(0, SpaceAsteroidField.FirstSeconds - 1f) == "too early");
        Check("not in another world", SpaceAsteroidFieldDirector.Blocked(1, 120f) == "not space");
        Check("not when there is no world", SpaceAsteroidFieldDirector.Blocked(-1, 120f) == "not space");
        startMenu.youAreInTutorial = true;
        Check("not in the tutorial", SpaceAsteroidFieldDirector.Blocked(0, 120f) == "tutorial");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = true;
        Check("not after a death", SpaceAsteroidFieldDirector.Blocked(0, 120f) == "dead");
        buttonClicks.playerDied = false;
        SpaceAsteroidField.Disabled = true;
        Check("the switch turns it off", SpaceAsteroidFieldDirector.Blocked(0, 120f) == "disabled");
        SpaceAsteroidField.Disabled = false;
        for (int i = 0; i < SpaceAsteroidField.MaxAlive; i++) SpaceAsteroidField.Spawn(0, i, new Vector2(-2f + i, 3f), 0f, 0f, 5f);
        Check("not past MaxAlive", SpaceAsteroidFieldDirector.Blocked(0, 120f) == "alive");
        SpaceAsteroidField.ClearAll();
        Check("the rate is modest (a gap of at least 8 s)", SpaceAsteroidField.Gap.x >= 8f && SpaceAsteroidField.Gap.y > SpaceAsteroidField.Gap.x);
        Check("a boss's run-up lead is set", SpaceAsteroidField.BossLeadSeconds >= 10f);
    }

    static void Director()
    {
        Fresh();
        var go = new GameObject("~dir");
        var dir = go.AddComponent<SpaceAsteroidFieldDirector>();
        dir.Begin();
        SpaceAsteroidFieldDirector.WorldOverride = 0;
        float t = 0f; int firstAt = -1, arrivals = 0, prev = SpaceAsteroidField.Spawned;
        float maxSeen = 0;
        for (int i = 0; i < 300 * 30; i++)
        {
            dir.Tick(Dt);
            t += Dt;
            // the rocks fall on: keep the view clear so the rate is the director's own
            foreach (var a in SpaceAsteroidField.Live.ToArray()) if (a != null) a.Step(Dt, .2f, CameraFit.ViewBottom);
            int n = SpaceAsteroidField.Spawned;
            if (n > prev) { arrivals += n - prev; if (firstAt < 0) firstAt = Mathf.RoundToInt(t); }
            prev = n;
            maxSeen = Mathf.Max(maxSeen, SpaceAsteroidField.Live.Count);
        }
        Check("nothing arrives before FirstSeconds (first at " + firstAt + " s)", firstAt >= SpaceAsteroidField.FirstSeconds);
        Check("rocks do arrive in five minutes, but modestly (" + arrivals + ")", arrivals >= 8 && arrivals <= 40);
        Check("never more than MaxTotal at once (" + maxSeen + ")", maxSeen <= SpaceAsteroidField.MaxTotal);

        // a world change clears the field
        SpaceAsteroidField.Spawn(0, 0, new Vector2(0f, 3f), 0f, 0f, 5f);
        SpaceAsteroidFieldDirector.WorldOverride = 1;
        dir.Tick(Dt);
        Check("leaving Space clears the field", SpaceAsteroidField.Live.Count == 0);
        Object.DestroyImmediate(go);
    }

    // ---- opening sky ----

    static void Picks()
    {
        int[] counts = new int[SpaceDirector.OpeningWeights.Length];
        int n = 20000;
        for (int i = 0; i < n; i++)
            counts[(int)SpaceDirector.PickOpening((i + .5) / n, true, true, true, true)]++;
        float total = 0f; foreach (float w in SpaceDirector.OpeningWeights) total += w;
        bool close = true;
        for (int k = 0; k < counts.Length; k++) close &= Mathf.Abs(counts[k] / (float)n - SpaceDirector.OpeningWeights[k] / total) < .01f;
        Check("the weighted pick follows the weights", close);
        Check("no planet at all is a real outcome", counts[(int)SpaceDirector.OpeningKind.None] > n / 10);
        Check("the giant is the most common single planet", counts[1] > counts[2] && counts[1] > counts[3] && counts[1] > counts[4]);

        Check("with no art at all the pick is an empty sky",
              SpaceDirector.PickOpening(.9, false, false, false, false) == SpaceDirector.OpeningKind.None);
        bool dropped = true;
        for (int i = 0; i < 2000; i++)
            dropped &= SpaceDirector.PickOpening((i + .5) / 2000.0, true, false, false, true) != SpaceDirector.OpeningKind.Reference;
        Check("a kind without art drops out of the draw", dropped);
        Check("roll 0 and roll ~1 are both valid kinds",
              (int)SpaceDirector.PickOpening(0.0, true, true, true, true) >= 0 && (int)SpaceDirector.PickOpening(.99999, true, true, true, true) <= 4);
    }

    static SpaceDirector OpenSpace(GameObject host, int kind, int seed, out WorldBackdrop wb)
    {
        SpaceDirector.OpeningOverride = kind;
        SpaceDirector.OpeningSeedOverride = seed;
        wb = host.AddComponent<WorldBackdrop>();
        wb.Show("Space", false);
        return wb.Current.Director as SpaceDirector;
    }

    static int ActivePlanets(SpaceDirector sd)
    {
        int n = 0;
        foreach (var p in sd.Bodies[0].items) if (p.active && p.kind == SpaceDirector.Planet) n++;
        return n;
    }

    static void Openings()
    {
        Fresh();
        // the old opening is untouched in edit mode with no override
        SpaceDirector.OpeningOverride = -1;
        var legacyHost = new GameObject("~legacy");
        var legacyWb = legacyHost.AddComponent<WorldBackdrop>();
        legacyWb.Show("Space", false);
        var legacy = legacyWb.Current.Director as SpaceDirector;
        Check("edit-mode default keeps the legacy giant opening", legacy.OpeningWasLegacy && legacy.Opening == SpaceDirector.OpeningKind.Giant && ActivePlanets(legacy) == 1);
        Object.DestroyImmediate(legacyHost);

        foreach (var kind in new[] { SpaceDirector.OpeningKind.Giant, SpaceDirector.OpeningKind.Reference,
                                     SpaceDirector.OpeningKind.Neon, SpaceDirector.OpeningKind.Halo })
        {
            var host = new GameObject("~open");
            WorldBackdrop wb;
            var sd = OpenSpace(host, (int)kind, 5, out wb);
            bool has = ActivePlanets(sd) == 1;
            Check(kind + ": opens with exactly one planet", sd.Opening == kind && has);
            if (has)
            {
                string name = "";
                foreach (var p in sd.Bodies[0].items) if (p.active && p.kind == SpaceDirector.Planet) name = p.sr.sprite.name;
                string want = kind == SpaceDirector.OpeningKind.Giant ? "giant_" : kind == SpaceDirector.OpeningKind.Reference ? "reference"
                            : kind == SpaceDirector.OpeningKind.Neon ? "neon_planet_" : "halo_planet_";
                Check(kind + ": it is that planet's art (" + name + ")", name.StartsWith(want, System.StringComparison.Ordinal));
            }
            for (int i = 0; i < 20 * 30; i++) wb.Step(Dt);
            Check(kind + ": the backdrop keeps running for 20 s", wb.Current.Director != null);
            Object.DestroyImmediate(host);
        }

        // no planet at all: stars and nebula, and the planet queue still brings one in
        {
            var host = new GameObject("~none");
            WorldBackdrop wb;
            var sd = OpenSpace(host, (int)SpaceDirector.OpeningKind.None, 5, out wb);
            Check("None: the sky opens with no planet", sd.Opening == SpaceDirector.OpeningKind.None && ActivePlanets(sd) == 0);
            int stars = 0, wisps = 0;
            foreach (var s in sd.SetPieces) { }
            int seenPlanet = 0;
            moveBackGround.speed = .3f;
            for (int i = 0; i < 180 * 30; i++)
            {
                wb.Step(Dt);
                if (ActivePlanets(sd) > 0) { seenPlanet++; }
            }
            Check("None: the planet queue still delivers planets later (" + seenPlanet + " frames with one)", seenPlanet > 0);
            Check("None: the rock field still runs", sd.AsteroidDrift != null && sd.AsteroidDrift.ActiveCount > 0);
            Object.DestroyImmediate(host);
        }

        // variety: a seed changes the look, the same seed repeats it
        string a = Look(3), b = Look(3), c = Look(11);
        Check("a seeded opening repeats exactly", a == b && a.Length > 0);
        Check("a different seed looks different (size / side / height / tint)", a != c);
    }

    static string Look(int seed)
    {
        var host = new GameObject("~look");
        WorldBackdrop wb;
        var sd = OpenSpace(host, (int)SpaceDirector.OpeningKind.Giant, seed, out wb);
        string s = "";
        foreach (var p in sd.Bodies[0].items)
            if (p.active && p.kind == SpaceDirector.Planet)
                s = p.x.ToString("F3") + "/" + p.y.ToString("F3") + "/" + p.size.ToString("F3") + "/" + p.color;
        Object.DestroyImmediate(host);
        return s;
    }
}
