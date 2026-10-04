using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "when enemies are destroyed using the teleporting pause feature
// they should activate a new explosion sprite and should also provide score",
// and "when the ultimate destroys the enemies it should also give score too".
//
// The pause-teleport (movePlayer -> TeleportFx.Play -> Strike) erases every
// hazard the ship materialises on. Each is now a real kill through
// collisionDetection.AwardDestroyedTarget -- kill points plus
// ScoreRules.TeleportKillBonus, with the chain and speed multipliers, the
// "+N" popup, the kill achievements -- shown with its own pooled
// "erased by the pause" blast (TeleportKillFx), while every weapon / ultimate
// kill keeps the cel explosion (FlipbookFx.Mode.Explosion). The boss body is
// left alone by a blink. Ultimate kills (ShipPowerController.HitTarget ->
// ShipAttackHits.Hit) pay the same points, popup, chain and achievements.
public static class TeleportKillTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TPK] PASS  " : "[TPK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            ArtIsDistinctAndNeverRed();
            TeleportKillPaysAndPlaysTheNewBlast();
            TeleportKillsChainAndScaleWithSpeed();
            TeleportKillPopsUpOnTheHud();
            BossBodySurvivesABlink();
            NormalKillsKeepTheCelExplosion();
            UltimateKillsScore();
            BlastIsPooledAndFreezesWithTheWorld();
        }
        finally
        {
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[TPK] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static void FreshRun()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = 0f;   // below HUD 20: no speed multiplier
        RunScore.BeginRun(true, true);
    }

    static GameObject Spawn(EnemyRole role, Vector3 at)
    {
        var def = EnemyRoster.One(0, role);
        var go = EnemyFactory.Create(def, at, Quaternion.identity);
        ClearTarget.Ensure(go);
        return go;
    }

    static int Base(EnemyRole role)
    {
        var def = EnemyRoster.One(0, role);
        return ScoreRules.KillPoints(role, def.tier);
    }

    static void BreakChain() => RunScore.Tick(ScoreRules.ComboWindowSeconds + .1f, 0f);

    // ---- cases --------------------------------------------------------------

    static void ArtIsDistinctAndNeverRed()
    {
        Check("the teleport-kill atlas loads and slices into " + TeleportKillArt.Frames + " frames",
              TeleportKillArt.Frame(0) != null && TeleportKillArt.Frame(TeleportKillArt.Frames - 1) != null);
        Check("tick table covers every frame", TeleportKillArt.Ticks.Length == TeleportKillArt.Frames);
        var s = TeleportKillArt.Frame(0);
        Check("a frame is 1 world unit at scale 1", s != null && Mathf.Abs(s.bounds.size.x - 1f) < .01f);
        Check("it is not the weapons' explosion atlas",
              s != null && s.texture != WeaponArt.Explosion(TargetExplosion.Kind.Metal, 0).texture);

        // Enemy FX never use the player's red.
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Vfx/teleport_kill_atlas.png"));
        var px = tex.GetPixels32();
        int red = 0, lit = 0;
        foreach (var c in px)
        {
            if (c.a < 40) continue;
            lit++;
            if (c.r > 180 && c.g < 90 && c.b < 90) red++;
        }
        Object.DestroyImmediate(tex);
        Check("the blast has art (" + lit + " px) and no player red (" + red + " px)", lit > 1000 && red == 0);
    }

    static void TeleportKillPaysAndPlaysTheNewBlast()
    {
        FreshRun();
        var at = new Vector3(.5f, 1f, 0f);
        var alien = Spawn(EnemyRole.Alien, at);
        var far = Spawn(EnemyRole.Rock, new Vector3(-2f, -3f, 0f));
        int played = TeleportKillFx.Played;
        int flipbooks = WeaponFx.ActiveFlipbooks;
        int aliensBefore = AchievementTiers.Count(AchievementCategory.Aliens);
        var parts = RunScore.Parts;
        int scored = 0; Vector3 scoredAt = Vector3.zero; RunScore.Source src = RunScore.Source.Distance;
        System.Action<int, Vector3, RunScore.Source> spy = (p, w, s) => { scored = p; scoredAt = w; src = s; };
        RunScore.Scored += spy;
        int kills;
        try { kills = TeleportFx.Strike(at + new Vector3(.2f, -.1f, 0f)); }
        finally { RunScore.Scored -= spy; }

        int expect = Base(EnemyRole.Alien) + ScoreRules.TeleportKillBonus;
        Check("landing on an enemy erases it (kills " + kills + ")", kills == 1 && alien == null);
        Check("... and nothing outside the blast radius", far != null);
        Check("a teleport kill pays kill points + TeleportKillBonus (" + expect + ", got " + (RunScore.Total - parts.Total) + ")",
              RunScore.Total - parts.Total == expect);
        Check("... counted as a kill", RunScore.Parts.killCount == parts.killCount + 1 && RunScore.Parts.kills == parts.kills + expect);
        Check("... and raised for a popup at the enemy", scored == expect && src == RunScore.Source.Kill && (scoredAt - at).sqrMagnitude < 1e-4f);
        Check("it plays the new teleport-kill blast", TeleportKillFx.Played == played + 1 && TeleportKillFx.Last != null &&
              TeleportKillFx.Last.Active && TeleportKillFx.Last.CurrentSprite == TeleportKillArt.Frame(0));
        Check("... at the enemy, sized from it",
              TeleportKillFx.Last != null && ((Vector2)(TeleportKillFx.Last.transform.position - at)).sqrMagnitude < 1e-4f &&
              TeleportKillFx.Last.transform.localScale.x > .5f);
        Check("... not the weapons' cel explosion", WeaponFx.ActiveFlipbooks == flipbooks);
        Check("an alien teleport kill counts toward the alien achievements",
              AchievementTiers.Count(AchievementCategory.Aliens) == aliensBefore + 1);

        // A rock pays rock points + bonus and counts toward the asteroid tiers.
        BreakChain();
        int rocksBefore = AchievementTiers.Count(AchievementCategory.Asteroids);
        long before = RunScore.Total;
        TeleportFx.Strike(far.transform.position);
        Check("a rock teleport kill pays rock + bonus", RunScore.Total - before == ScoreRules.Rock + ScoreRules.TeleportKillBonus);
        Check("... and counts toward the asteroid achievements", AchievementTiers.Count(AchievementCategory.Asteroids) == rocksBefore + 1);
        Check("TeleportFx counts its kills", TeleportFx.Kills >= 2);
    }

    static void TeleportKillsChainAndScaleWithSpeed()
    {
        FreshRun();
        int each = Base(EnemyRole.Rock) + ScoreRules.TeleportKillBonus;
        long[] paid = new long[3];
        for (int i = 0; i < 3; i++)
        {
            var at = new Vector3(0f, i * 2.5f - 2f, 0f);
            Spawn(EnemyRole.Rock, at);
            long before = RunScore.Total;
            TeleportFx.Strike(at);
            paid[i] = RunScore.Total - before;
        }
        Check("teleport kills build the kill chain (" + RunScore.Chain + ")", RunScore.Chain == 3);
        Check("the third pays x2 (" + paid[2] + " = " + each * 2 + ")", paid[0] == each && paid[1] == each && paid[2] == each * 2);

        // Mixed with a weapon kill: one chain across both.
        Spawn(EnemyRole.Rock, new Vector3(2f, 3f, 0f));
        ShipAttackHits.Hit(Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)[0].gameObject, 1);
        Check("a weapon kill continues the same chain", RunScore.Chain == 4);

        FreshRun();
        moveBackGround.speed = .5f;   // HUD 50: x2 speed tier
        Spawn(EnemyRole.Rock, Vector3.zero);
        long b = RunScore.Total;
        TeleportFx.Strike(Vector3.zero);
        float m = ScoreRules.SpeedMultiplierFor(.5f);
        Check("the speed multiplier applies (x" + m + ")", m > 1f && RunScore.Total - b == Mathf.RoundToInt(each * m));
        moveBackGround.speed = 0f;

        // No run scoring (menus / tutorial): still erased, nothing paid.
        RunScore.EndRun(RunScore.RunId);
        var t = Spawn(EnemyRole.Rock, Vector3.zero);
        long frozen = RunScore.Total;
        TeleportFx.Strike(Vector3.zero);
        Check("outside a scoring run it erases but pays nothing", t == null && RunScore.Total == frozen);
    }

    static void TeleportKillPopsUpOnTheHud()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        moveBackGround.speed = 0f;
        RunScore.BeginRun(true, true);
        var go = new GameObject("~HudTeleportKill");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        var hud = go.GetComponent<ScoreHud>();
        if (hud == null) { Check("gameS1 HUD has a ScoreHud", false); return; }
        hud.SendMessage("OnEnable");   // edit mode: subscribe by hand
        try
        {
            int live = hud.LivePopups;
            var at = new Vector3(0f, 1.5f, 0f);
            Spawn(EnemyRole.Fighter, at);
            TeleportFx.Strike(at);
            Check("a teleport kill pops a +N on the HUD", hud.LivePopups == live + 1);
            string want = "+" + (Base(EnemyRole.Fighter) + ScoreRules.TeleportKillBonus);
            bool found = false;
            foreach (var t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "ScorePopup" && t.gameObject.activeSelf && t.text == want) found = true;
            Check("... reading " + want, found);
        }
        finally
        {
            hud.SendMessage("OnDisable");
            Object.DestroyImmediate(go);
        }
    }

    static void BossBodySurvivesABlink()
    {
        FreshRun();
        var body = new GameObject("BossBody");
        body.tag = BossHitbox.Tag;
        body.AddComponent<BoxCollider2D>().isTrigger = true;
        body.AddComponent<BossTarget>();
        ClearTarget.Ensure(body);
        var shot = new GameObject("BossShotHit");
        shot.tag = BossHitbox.Tag;
        shot.AddComponent<CircleCollider2D>().isTrigger = true;
        shot.transform.position = new Vector3(.3f, 0f, 0f);
        long before = RunScore.Total;
        int hits = ShipAttackHits.AttackTargetHits;
        int kills = TeleportFx.Strike(Vector3.zero);
        Check("a blink leaves the boss body hitbox alone", body != null && body.GetComponent<ClearTarget>().enabled);
        Check("... and costs the boss no hit", ShipAttackHits.AttackTargetHits == hits);
        Check("a boss shot under the landing is erased (" + kills + ")", shot == null && kills == 1);
        Check("... paying BossShot, unchained", RunScore.Total - before == ScoreRules.BossShot && RunScore.Chain == 0);
        Object.DestroyImmediate(body);
    }

    static void NormalKillsKeepTheCelExplosion()
    {
        FreshRun();
        var rock = Spawn(EnemyRole.Rock, new Vector3(1f, 1f, 0f));
        int played = TeleportFx.Kills;
        int tkPlayed = TeleportKillFx.Played;
        long before = RunScore.Total;
        ShipAttackHits.Hit(rock, 1);
        var book = LatestExplosion();
        Check("a weapon kill still plays the cel explosion", book != null &&
              book.CurrentSprite == WeaponArt.Explosion(TargetExplosion.Kind.Rock, 0));
        Check("... not the teleport-kill blast", TeleportKillFx.Played == tkPlayed && TeleportFx.Kills == played);
        Check("... and pays plain kill points (no teleport bonus)", RunScore.Total - before == ScoreRules.Rock);
    }

    static FlipbookFx LatestExplosion()
    {
        FlipbookFx best = null;
        foreach (var f in Object.FindObjectsByType<FlipbookFx>(FindObjectsSortMode.None))
            if (f.Active && f.CurrentMode == FlipbookFx.Mode.Explosion && (best == null || f.StartedAt >= best.StartedAt))
                best = f;
        return best;
    }

    // ---- the ultimate --------------------------------------------------------

    const int TopTierShip = 7;

    static ShipPowerController UltimateShip()
    {
        PlayerPrefs.SetInt("spawnShip", TopTierShip);
        var shipGo = new GameObject("ship" + TopTierShip, typeof(SpriteRenderer));
        shipGo.transform.position = new Vector3(0f, -4f, 0f);
        var c = shipGo.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        return c;
    }

    // What a landed homing shot of the cinematic volley calls.
    static void UltimateHits(ShipPowerController c, GameObject target) =>
        typeof(ShipPowerController).GetMethod("HitTarget", Inst).Invoke(c, new object[] { target, Color.white });

    static void UltimateKillsScore()
    {
        FreshRun();
        var cam = new GameObject("Main Camera", typeof(Camera));
        cam.tag = "MainCamera";
        cam.GetComponent<Camera>().orthographic = true;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var ship = UltimateShip();
        try
        {
            Check("the top-tier ship's ultimate is the cinematic volley", ship.Loadout.IsTopTier);
            var targets = new GameObject[4];
            targets[0] = Spawn(EnemyRole.Alien, new Vector3(-1f, 2f, 0f));
            for (int i = 1; i < targets.Length; i++) targets[i] = Spawn(EnemyRole.Rock, new Vector3(i - 2f, 3f, 0f));

            int aliens = AchievementTiers.Count(AchievementCategory.Aliens);
            int rocks = AchievementTiers.Count(AchievementCategory.Asteroids);
            int tk = TeleportKillFx.Played;
            int raised = 0;
            System.Action<int, Vector3, RunScore.Source> spy = (p, w, s) => { if (s == RunScore.Source.Kill) raised++; };
            RunScore.Scored += spy;
            long[] paid = new long[targets.Length];
            try
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    long before = RunScore.Total;
                    UltimateHits(ship, targets[i]);
                    paid[i] = RunScore.Total - before;
                }
            }
            finally { RunScore.Scored -= spy; }

            bool gone = true;
            foreach (var t in targets) gone &= t == null;
            Check("the ultimate destroys what it hits", gone);
            Check("ultimate kills pay kill points (" + string.Join(",", paid) + ")",
                  paid[0] == Base(EnemyRole.Alien) && paid[1] == ScoreRules.Rock);
            Check("... build the chain: the third and fourth pay x2",
                  paid[2] == ScoreRules.Rock * 2 && paid[3] == ScoreRules.Rock * 2 && RunScore.Chain == 4);
            Check("... raise a popup per kill", raised == targets.Length);
            Check("... count toward the kill achievements",
                  AchievementTiers.Count(AchievementCategory.Aliens) == aliens + 1 &&
                  AchievementTiers.Count(AchievementCategory.Asteroids) == rocks + 3);
            var book = LatestExplosion();
            Check("... and keep the cel explosion", book != null && TeleportKillFx.Played == tk);

            // Speed multiplier on an ultimate kill.
            BreakChain();
            moveBackGround.speed = .5f;
            var fast = Spawn(EnemyRole.Rock, new Vector3(0f, 2f, 0f));
            long b = RunScore.Total;
            UltimateHits(ship, fast);
            Check("an ultimate kill takes the speed multiplier",
                  RunScore.Total - b == Mathf.RoundToInt(ScoreRules.Rock * ScoreRules.SpeedMultiplierFor(.5f)));
            moveBackGround.speed = 0f;

            // The boss: one homing shot is one full hit, never a kill, no points.
            var body = new GameObject("BossBody");
            body.tag = BossHitbox.Tag;
            body.AddComponent<BossTarget>();
            int hits = ShipAttackHits.AttackTargetHits;
            long bb = RunScore.Total;
            UltimateHits(ship, body);
            Check("an ultimate shot on the boss is one hit, not a kill, and pays nothing",
                  body != null && ShipAttackHits.AttackTargetHits == hits + 1 && RunScore.Total == bb);
            Object.DestroyImmediate(body);
        }
        finally
        {
            ship.SendMessage("OnDestroy");
            Object.DestroyImmediate(ship.gameObject);
        }
    }

    // ---- pooling and time ------------------------------------------------------

    static void BlastIsPooledAndFreezesWithTheWorld()
    {
        FreshRun();
        var a = TeleportKillFx.Spawn(Vector3.zero, 1f);
        a.Tick(0f);
        Check("a paused world (dt 0) holds the blast on its first frame", a.Active && a.Frame == 0);
        float total = 0f;
        foreach (int t in TeleportKillArt.Ticks) total += t * WeaponArt.Tick;
        a.Tick(TeleportKillArt.Ticks[0] * WeaponArt.Tick + .001f);
        Check("it advances on gameplay time", a.Frame == 1);
        a.Tick(total);
        Check("... and returns to the pool when done", !a.Active && !a.gameObject.activeSelf);

        int size = TeleportKillFx.PoolSize;
        var b = TeleportKillFx.Spawn(Vector3.one, 1f);
        Check("a finished blast is reused, not rebuilt", b == a && TeleportKillFx.PoolSize == size);
        for (int i = 0; i < TeleportKillFx.MaxInFlight + 5; i++) TeleportKillFx.Spawn(Vector3.zero, 1f);
        Check("the pool is capped at " + TeleportKillFx.MaxInFlight + " (" + TeleportKillFx.PoolSize + ")",
              TeleportKillFx.PoolSize == TeleportKillFx.MaxInFlight);
    }
}
