using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: per-ship animated charge indicators in front of the hull, new
// per-ship shots / muzzle flashes / impacts, and cartoon target explosions,
// all in the flat cel style (Art/Weapons/src~/weapons.py).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod WeaponChargeTest.Run
public static class WeaponChargeTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WC] PASS  " : "[WC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        EveryRosterShipHasAWeapon();
        IndicatorProgressIsMonotonic();
        ReadyTellInTheFinalSecond();
        IndicatorFreezesAtTimeScaleZero();
        PickupsCatchUpSmoothly();
        FireReleasesTheIndicator();
        ShotPoolStaysBounded();
        ExplosionVariantsCoverEveryTarget();
        ExplosionPoolStaysBounded();
        ExplosionFreezesAtTimeScaleZero();

        Debug.Log("[WC] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        camGo.GetComponent<Camera>().orthographic = true;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
    }

    static ShipPowerController Ship(int index)
    {
        PlayerPrefs.SetInt("spawnShip", index);
        var shipGo = new GameObject("ship" + index, typeof(SpriteRenderer));
        shipGo.transform.position = new Vector3(0f, -4f, 0f);
        var c = shipGo.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        var gun = shipGo.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        return c;
    }

    static void SetTimer(ShipPowerController c, float seconds) =>
        typeof(ShipPowerController).GetField("timer", Inst).SetValue(c, seconds);

    static float Cooldown(ShipPowerController c) =>
        (float)typeof(ShipPowerController).GetField("cooldown", Inst).GetValue(c);

    static void Teardown(ShipPowerController c)
    {
        c.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
    }

    // ---- cases -----------------------------------------------------------

    static void EveryRosterShipHasAWeapon()
    {
        FreshScene();
        var charges = new HashSet<ChargeStyle>();
        var shots = new HashSet<ShotStyle>();
        int roster = shopingShips.Roster.Length;
        bool all = true, frames = true;
        for (int ship = 1; ship < roster; ship++)
        {
            if (!WeaponStyleTable.Has(ship) || WeaponStyleTable.For(ship).shipId != ship)
            {
                all = false;
                Debug.Log("[WC] no weapon entry for ship " + ship);
                continue;
            }
            var style = WeaponStyleTable.For(ship);
            charges.Add(style.charge);
            shots.Add(style.shot);
            bool ok = !string.IsNullOrEmpty(style.artKey) &&
                      AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Weapons/" + style.artKey + ".png") != null;
            for (int i = 0; i < WeaponArt.ChargeFrames; i++) ok &= WeaponArt.Charge(ship, i) != null;
            for (int i = 0; i < WeaponArt.ReadyFrames; i++) ok &= WeaponArt.Ready(ship, i) != null;
            for (int i = 0; i < WeaponArt.ShotFrames; i++) ok &= WeaponArt.Shot(ship, i) != null;
            for (int i = 0; i < WeaponArt.ImpactFrames; i++) ok &= WeaponArt.Impact(ship, i) != null;
            for (int i = 0; i < WeaponArt.MuzzleFrames; i++) ok &= WeaponArt.Muzzle(ship, i) != null;
            for (int i = 0; i < WeaponArt.ReleaseFrames; i++) ok &= WeaponArt.Release(ship, i) != null;
            for (int i = 0; i < WeaponArt.TrailFrames; i++) ok &= WeaponArt.Trail(ship, i) != null;
            if (!ok) Debug.Log("[WC] missing frames for ship " + ship + " (" + style.artKey + ")");
            frames &= ok;
        }
        Check("every roster ship has a weapon table entry", all);
        Check("every ship's indicator/shot/impact/muzzle frames resolve", frames);
        Check("every ship has its own charge indicator style", charges.Count == roster - 1);
        Check("every ship has its own shot style", shots.Count == roster - 1);
        Check("an unknown ship id falls back to the starter's weapon",
              WeaponStyleTable.For(999).shipId == shopingShips.StarterShip);
        Check("frames are one world unit at scale 1",
              Mathf.Abs(WeaponArt.Charge(1, 0).bounds.size.x - 1f) < .01f);
    }

    static void IndicatorProgressIsMonotonic()
    {
        int last = -1;
        bool monotonic = true;
        for (int i = 0; i <= 1000; i++)
        {
            int f = ChargeIndicator.ChargeFrameFor(i / 1000f);
            if (f < last) monotonic = false;
            last = f;
        }
        Check("charge drawing index never goes backwards as charge rises", monotonic);
        Check("empty charge shows the first drawing", ChargeIndicator.ChargeFrameFor(0f) == 0);
        Check("full charge shows the last drawing",
              ChargeIndicator.ChargeFrameFor(1f) == WeaponArt.ChargeFrames - 1);
        Check("half charge shows a middle drawing", ChargeIndicator.ChargeFrameFor(.5f) == WeaponArt.ChargeFrames / 2);

        // The live indicator over a whole cooldown, world running.
        FreshScene();
        var c = Ship(3);
        var ind = c.Indicator;
        float cooldown = Cooldown(c);
        float prevShown = -1f;
        int prevFrame = -1;
        bool live = true, bounded = true;
        for (float t = cooldown; t > 1.2f; t -= .25f)
        {
            SetTimer(c, t);
            ind.Step(.25f, .25f);
            if (ind.Shown < prevShown - 1e-5f || ind.ChargeFrame < prevFrame) live = false;
            if (ind.Shown > c.Charge01 + 1e-5f) bounded = false;
            prevShown = ind.Shown;
            prevFrame = ind.ChargeFrame;
        }
        Check("the live indicator's progress rises monotonically with the cooldown", live);
        Check("the indicator never shows more charge than the cooldown has", bounded);
        Check("the indicator tracks the cooldown closely", Mathf.Abs(ind.Shown - c.Charge01) < .05f);
        Check("the drawing follows the shown progress",
              ind.CurrentSprite == WeaponArt.Charge(3, ChargeIndicator.ChargeFrameFor(ind.Shown)));
        Teardown(c);
    }

    static void ReadyTellInTheFinalSecond()
    {
        FreshScene();
        var c = Ship(7);
        var ind = c.Indicator;
        SetTimer(c, 5f);
        ind.Step(.02f, .02f);
        Check("no ready tell with seconds to go", !ind.Ready && ind.ReadyTellCount == 0);
        SetTimer(c, 1.05f);
        ind.Step(.02f, .02f);
        Check("no ready tell just outside the final second", !ind.Ready);
        SetTimer(c, .95f);
        ind.Step(.02f, .02f);
        Check("the ready tell starts in the final second", ind.Ready && ind.ReadyTellCount == 1);
        Check("the ready tell plays its looping ready pose",
              ind.CurrentSprite == WeaponArt.Ready(7, 0) || ind.CurrentSprite == WeaponArt.Ready(7, 1) ||
              ind.CurrentSprite == WeaponArt.Ready(7, 2) || ind.CurrentSprite == WeaponArt.Ready(7, 3));
        Check("the first countdown tick sounds on entering it", ind.TicksPlayed == 1);
        SetTimer(c, .6f);
        ind.Step(.02f, .02f);
        SetTimer(c, .3f);
        ind.Step(.02f, .02f);
        Check("three rising ticks over the last second", ind.TicksPlayed == 3);
        Check("staying in the window doesn't re-trigger the tell", ind.ReadyTellCount == 1);
        SetTimer(c, .1f);
        ind.Step(.02f, .02f);
        Check("anticipation squash just before it fires", ind.View.localScale.x > ind.View.localScale.y);
        Teardown(c);
    }

    static void IndicatorFreezesAtTimeScaleZero()
    {
        FreshScene();
        var c = Ship(2);
        var ind = c.Indicator;
        SetTimer(c, Cooldown(c) * .6f);
        ind.Step(.1f, .1f);
        // open a gap so there is something it could catch up on
        c.ReduceTimer(c.secondsPerAtom);
        float shown = ind.Shown;
        var sprite = ind.CurrentSprite;
        Time.timeScale = 0f;
        for (int i = 0; i < 20; i++) ind.SendMessage("LateUpdate");
        Check("no indicator progress while timeScale = 0", Mathf.Approximately(ind.Shown, shown));
        Check("the indicator drawing holds while timeScale = 0", ind.CurrentSprite == sprite);
        ind.Step(0f, .5f);
        Check("unscaled time alone doesn't advance it", Mathf.Approximately(ind.Shown, shown));
        float timer = c.SecondsLeft;
        c.SendMessage("Update");
        Check("the cooldown itself doesn't count down at timeScale 0 either",
              Mathf.Approximately(c.SecondsLeft, timer));
        Time.timeScale = 1f;
        ind.Step(.1f, .1f);
        Check("it resumes once the world runs", ind.Shown > shown);
        Teardown(c);
    }

    static void PickupsCatchUpSmoothly()
    {
        FreshScene();
        var c = Ship(10);
        var ind = c.Indicator;
        SetTimer(c, Cooldown(c) * .8f);
        for (int i = 0; i < 30; i++) ind.Step(.05f, .05f);
        float before = ind.Shown;
        c.ReduceTimer(c.secondsPerAtom);
        float target = c.Charge01;
        ind.Step(1f / 60f, 1f / 60f);
        float oneFrame = ind.Shown;
        Check("a pickup doesn't snap the indicator forward in one frame",
              oneFrame > before && oneFrame < target - .02f);
        Check("it squashes with a little gulp", ind.View.localScale.x > ind.View.localScale.y * 1.05f);
        for (int i = 0; i < 30; i++) ind.Step(1f / 60f, 1f / 60f);
        Check("it catches up within about half a second", Mathf.Abs(ind.Shown - target) < .01f);
        Teardown(c);
    }

    static void FireReleasesTheIndicator()
    {
        FreshScene();
        var hazard = new GameObject("hazard");
        hazard.tag = "Enimey";
        hazard.transform.position = new Vector3(0f, 2f, 0f);
        var c = Ship(11);
        var ind = c.Indicator;
        SetTimer(c, .5f);
        ind.Step(.02f, .02f);
        typeof(ShipPowerController).GetMethod("Fire", Inst).Invoke(c, null);
        Check("Fire() releases the indicator", ind.Releasing && ind.ReleaseCount == 1);
        Check("the release plays the release frames", ind.CurrentSprite == WeaponArt.Release(11, 0));
        Check("the release empties the charge", ind.Shown == 0f && !ind.Ready);
        var gun = c.GetComponentInChildren<UltimateGun>();
        Check("the gun plays its muzzle flash flipbook", gun != null && gun.Flashing);
        Check("a homing shot launched from the pool", WeaponFx.ActiveShots >= 1);
        ind.Step(.02f, ChargeIndicator.ReleaseSeconds + .01f);
        Check("the release finishes and charging starts over", !ind.Releasing);
        Object.DestroyImmediate(hazard);
        Teardown(c);
    }

    static void ShotPoolStaysBounded()
    {
        FreshScene();
        var target = new GameObject("target").transform;
        target.position = new Vector3(0f, 3f, 0f);
        int hits = 0;
        int sizeAfterFirst = -1;
        bool landed = true;
        for (int round = 0; round < 8; round++)
        {
            var launched = new List<WeaponShot>();
            for (int i = 0; i < 6; i++)
                launched.Add(WeaponFx.Launch(1 + (i % 15), new Vector3(i * .2f - .5f, -3f, 0f), target, 2.4f, () => hits++));
            if (round == 0)
                Check("a shot body uses its ship's art (launch smear frame)",
                      launched[0] != null && launched[0].BodySprite == WeaponArt.Shot(1, WeaponArt.ShotLoopFrames));
            for (int step = 0; step < 200 && WeaponFx.ActiveShots > 0; step++)
                foreach (var s in launched) if (s != null) s.Tick(.02f);
            landed &= WeaponFx.ActiveShots == 0;
            if (round == 0) sizeAfterFirst = WeaponFx.ShotPoolSize;
        }
        Check("every shot homes in and lands", landed && hits == 8 * 6);
        Check("the shot pool doesn't grow over repeated fires",
              sizeAfterFirst == 6 && WeaponFx.ShotPoolSize == 6);

        int capHits = 0;
        for (int i = 0; i < WeaponFx.MaxShots + 10; i++)
            WeaponFx.Launch(1, Vector3.zero, target, 2.4f, () => capHits++);
        Check("the shot pool is capped", WeaponFx.ShotPoolSize == WeaponFx.MaxShots);
        Check("over-cap shots still hit their target", capHits == 10);

        int fizzled = 0;
        var doomed = new GameObject("doomed").transform;
        var shot = WeaponFx.Launch(4, Vector3.zero, doomed, 2.4f, () => fizzled++);
        Object.DestroyImmediate(doomed.gameObject);
        if (shot != null) shot.Tick(.02f);
        Check("a shot whose target is gone fizzles without a hit", shot != null && !shot.Active && fizzled == 0);
        Check("the impact / explosion pool stays capped", WeaponFx.FlipbookPoolSize <= WeaponFx.MaxFlipbooks);
    }

    static void ExplosionVariantsCoverEveryTarget()
    {
        Check("asteroids blow up as rock",
              TargetExplosion.KindFor("Astr", "kn_meteorBrown_big1(Clone)") == TargetExplosion.Kind.Rock &&
              TargetExplosion.KindFor("Astr", "aestroid_dark(Clone)") == TargetExplosion.Kind.Rock);
        Check("enemy craft blow up as metal",
              TargetExplosion.KindFor("Enimey", "kn_enemyRed4(Clone)") == TargetExplosion.Kind.Metal);
        Check("aliens blow up as metal", TargetExplosion.KindFor("Enimey", "alien1(Clone)") == TargetExplosion.Kind.Metal);
        Check("chasers blow up as metal",
              TargetExplosion.KindFor("Enimey", "kn_enemyRed5(Clone)") == TargetExplosion.Kind.Metal);
        Check("rail mines blow up as mines", TargetExplosion.KindFor("Enimey", "mine") == TargetExplosion.Kind.Mine);

        // Every hazard prefab the spawner can produce maps to a variant with frames.
        bool prefabs = true;
        int seen = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/prefabs" }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go == null || (!go.CompareTag("Enimey") && !go.CompareTag("Astr"))) continue;
            seen++;
            var kind = TargetExplosion.KindFor(go);
            bool expected = go.CompareTag("Astr") ? kind == TargetExplosion.Kind.Rock : kind != TargetExplosion.Kind.Rock;
            prefabs &= expected && WeaponArt.Explosion(kind, 0) != null;
        }
        Check("every hazard prefab maps to a matching explosion variant (" + seen + " prefabs)", prefabs && seen > 10);

        bool frames = true;
        foreach (TargetExplosion.Kind kind in System.Enum.GetValues(typeof(TargetExplosion.Kind)))
            for (int i = 0; i < WeaponArt.ExplosionFrames; i++) frames &= WeaponArt.Explosion(kind, i) != null;
        frames &= WeaponArt.ExplosionFlash(0) != null && WeaponArt.ExplosionFlash(1) != null && WeaponArt.ExplosionRing() != null;
        Check("every explosion variant's frames and overlays resolve", frames);
        Check("explosions come in three sizes",
              TargetExplosion.SizeFor(.3f) == TargetExplosion.Size.Small &&
              TargetExplosion.SizeFor(.7f) == TargetExplosion.Size.Medium &&
              TargetExplosion.SizeFor(1.4f) == TargetExplosion.Size.Large &&
              TargetExplosion.WorldSizeFor(TargetExplosion.Size.Large, TargetExplosion.Kind.Metal) >
              TargetExplosion.WorldSizeFor(TargetExplosion.Size.Small, TargetExplosion.Kind.Metal));
    }

    static void ExplosionPoolStaysBounded()
    {
        FreshScene();
        for (int round = 0; round < 6; round++)
        {
            for (int i = 0; i < 5; i++)
                TargetExplosion.Spawn(new Vector3(i, 0f, 0f), (TargetExplosion.Kind)(i % 3), TargetExplosion.Size.Medium, 1);
            for (int step = 0; step < 60; step++) TickFlipbooks(.05f);
        }
        int settled = WeaponFx.FlipbookPoolSize;
        for (int i = 0; i < 5; i++)
            TargetExplosion.Spawn(Vector3.zero, TargetExplosion.Kind.Rock, TargetExplosion.Size.Large, 2);
        Check("explosions are reused, not re-created, once finished", WeaponFx.FlipbookPoolSize == settled);
        for (int i = 0; i < 200; i++)
            TargetExplosion.Spawn(Vector3.zero, TargetExplosion.Kind.Metal, TargetExplosion.Size.Small, 3);
        Check("a burst of explosions stays within the pool cap", WeaponFx.FlipbookPoolSize <= WeaponFx.MaxFlipbooks);
    }

    static void TickFlipbooks(float dt)
    {
        foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            fx.Tick(dt);
    }

    static void ExplosionFreezesAtTimeScaleZero()
    {
        FreshScene();
        TargetExplosion.Spawn(Vector3.zero, TargetExplosion.Kind.Rock, TargetExplosion.Size.Medium, 1);
        FlipbookFx blast = null;
        foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (fx.Active && fx.CurrentMode == FlipbookFx.Mode.Explosion) blast = fx;
        Check("an explosion is playing", blast != null && blast.Frame == 0);
        if (blast == null) return;
        Vector3 at = blast.transform.position;
        Time.timeScale = 0f;
        Check("the gameplay clock stops at timeScale 0", TargetExplosion.Delta() == 0f);
        for (int i = 0; i < 30; i++) blast.SendMessage("Update");
        Check("the explosion advances no frames while paused", blast.Frame == 0 && blast.Active);
        Check("the explosion doesn't drift while paused", blast.transform.position == at);
        Time.timeScale = 1f;
        blast.Tick(.2f);
        Check("it plays on once the world runs", blast.Frame > 0);
        Check("the blast's key frames squash and stretch",
              blast.transform.localScale.x != blast.transform.localScale.y || blast.Frame > 4);
    }
}
