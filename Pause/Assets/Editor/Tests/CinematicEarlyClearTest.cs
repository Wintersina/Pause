using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "when the player slows down to shoot the missiles that are on a
// timer, once all objects are destroyed in the level, the paused state or
// slowed down can instantly end and the player can continue".
//
// The ultimate's cinematic clear holds the world at CinematicSlowScale until
// a fixed hold after the last homing shot launches. It now also lifts the
// moment every hazard that was on screen is gone, through the same exit
// (an ease back to 1x) the hold timer uses. Hazards are tracked by the
// ClearTarget registry rather than a per-frame scene scan.
public static class CinematicEarlyClearTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CE] PASS  " : "[CE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        RegistryCountStaysCorrect();
        EndsOnTheFrameTheLastTargetDies();
        NewOnScreenTargetsKeepItRunning();
        NoTargetsAtStartKeepsTheNormalHold();
        PlayerDeathEndsItImmediately();

        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        buttonClicks.playerDied = false;
        score.pauseCounter = 0; // the world runs without a touch in batch mode
    }

    static GameObject Hazard(Vector3 at, string tag = "Enimey")
    {
        var go = new GameObject("hazard");
        go.tag = tag;
        go.transform.position = at;
        ClearTarget.Ensure(go);
        return go;
    }

    static ShipPowerController Ship()
    {
        PlayerPrefs.SetInt("spawnShip", 1);
        var shipGo = new GameObject("ship", typeof(SpriteRenderer));
        shipGo.transform.position = new Vector3(0f, -4f, 0f);
        var controller = shipGo.AddComponent<ShipPowerController>();
        // Edit mode doesn't auto-invoke Awake/Start on AddComponent.
        controller.SendMessage("Awake");
        controller.SendMessage("Start");
        var gun = shipGo.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        return controller;
    }

    static void Fire(ShipPowerController c) =>
        typeof(ShipPowerController).GetMethod("Fire", Inst).Invoke(c, null);

    // What HitTarget (a homing shot's onHit) does, minus the FX and with
    // DestroyImmediate, since Destroy() refuses to run in edit mode. The
    // object is gone from the scene before the clear check runs, and no
    // Update tick happens in between.
    static void MissileHits(ShipPowerController c, GameObject target)
    {
        ClearTarget.Release(target);
        Object.DestroyImmediate(target);
        typeof(ShipPowerController).GetMethod("CheckCleared", Inst).Invoke(c, null);
    }

    static void Tick(ShipPowerController c) => c.SendMessage("Update");

    // Edit-mode unscaled time barely moves; push the ease's start into the
    // past so the next tick sees it as complete.
    static void ElapseEase() =>
        typeof(ShipPowerController).GetField("exitStartedAt", Stat).SetValue(null, Time.unscaledTime - 10f);

    static void Teardown(ShipPowerController c)
    {
        if (c != null) DestroyShip(c);
        buttonClicks.playerDied = false;
    }

    // Edit mode doesn't send OnDestroy to non-[ExecuteAlways] scripts; play
    // mode does, so send it by hand to mirror the ship being destroyed.
    static void DestroyShip(ShipPowerController c)
    {
        c.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
    }

    // ---- cases -----------------------------------------------------------

    static void RegistryCountStaysCorrect()
    {
        FreshScene();
        int baseline = ClearTarget.Count;

        var a = Hazard(Vector3.zero);
        Check("registering a hazard bumps the count", ClearTarget.Count == baseline + 1);
        ClearTarget.Ensure(a);
        Check("Ensure() twice keeps a single registration",
              ClearTarget.Count == baseline + 1 && a.GetComponents<ClearTarget>().Length == 1);

        var t = a.GetComponent<ClearTarget>();
        t.enabled = false;
        Check("disabling the component unregisters it", ClearTarget.Count == baseline);
        t.enabled = true;
        Check("re-enabling re-registers it", ClearTarget.Count == baseline + 1);

        a.SetActive(false);
        Check("deactivating the object unregisters it", ClearTarget.Count == baseline);
        a.SetActive(true);
        Check("reactivating the object re-registers it", ClearTarget.Count == baseline + 1);

        var b = Hazard(new Vector3(1f, 1f, 0f), "Astr");
        Check("asteroids register too", ClearTarget.Count == baseline + 2);
        Check("both count as on-screen hazards", ClearTarget.CountOnScreen(Camera.main) == 2);

        var pickup = new GameObject("star");
        ClearTarget.Ensure(pickup); // untagged: movers sit on pickups as well
        Check("untagged objects register but are not hazards",
              ClearTarget.Count == baseline + 3 && ClearTarget.CountOnScreen(Camera.main) == 2);
        Object.DestroyImmediate(pickup);

        ClearTarget.Release(b);
        Check("Release() unregisters before the deferred Destroy lands", ClearTarget.Count == baseline + 1);
        Object.DestroyImmediate(b);

        var above = Hazard(new Vector3(0f, 12f, 0f));
        Check("a hazard above the top edge is registered but not on screen",
              ClearTarget.Count == baseline + 2 && ClearTarget.CountOnScreen(Camera.main) == 1);
        Object.DestroyImmediate(above);

        Object.DestroyImmediate(a);
        Check("destroying unregisters it", ClearTarget.Count == baseline);
    }

    static void EndsOnTheFrameTheLastTargetDies()
    {
        FreshScene();
        var a = Hazard(new Vector3(-1f, 1f, 0f));
        var b = Hazard(new Vector3(1f, 2f, 0f), "Astr");
        var ship = Ship();

        Fire(ship);
        Check("firing starts the cinematic slow motion",
              ShipPowerController.CinematicClearActive &&
              ShipPowerController.CinematicTimeScale <= ShipPowerController.CinematicSlowScale + 1e-4f);

        // moveBackGround applies the cinematic scale while it's active.
        var bgGo = new GameObject("bg", typeof(MeshRenderer));
        var bg = bgGo.AddComponent<moveBackGround>();
        bg.SendMessage("Update");
        Check("moveBackGround holds the world at the cinematic scale",
              Mathf.Approximately(Time.timeScale, ShipPowerController.CinematicSlowScale));

        MissileHits(ship, a);
        Tick(ship);
        Check("does not end while one target remains",
              ShipPowerController.CinematicClearActive && !ShipPowerController.CinematicExiting);

        MissileHits(ship, b);
        Check("ends on the same frame the last target is hit (no extra tick)",
              ShipPowerController.CinematicExiting && ShipPowerController.LastCinematicEndedEarly);
        Check("the exit eases from slow motion rather than snapping",
              ShipPowerController.CinematicTimeScale < 1f);

        ElapseEase();
        Tick(ship);
        Check("after the ease the cinematic has fully ended",
              !ShipPowerController.CinematicClearActive && Mathf.Approximately(ShipPowerController.CinematicTimeScale, 1f));

        // The standard path: moveBackGround is back in charge and runs at 1x.
        // (Its normal branch scrolls the material, which edit mode warns about.)
        bgGo.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        try { bg.SendMessage("Update"); } catch (System.Exception) { }
        Check("timeScale returns to normal through moveBackGround", Mathf.Approximately(Time.timeScale, 1f));

        Teardown(ship);
    }

    static void NewOnScreenTargetsKeepItRunning()
    {
        FreshScene();
        var a = Hazard(new Vector3(0f, 1f, 0f));
        var ship = Ship();
        Fire(ship);

        // Spawned mid-cinematic: one on screen, one still above the top edge.
        var late = Hazard(new Vector3(1.5f, 3f, 0f));
        var above = Hazard(new Vector3(0f, 14f, 0f));

        MissileHits(ship, a);
        Tick(ship);
        Check("a target spawned on screen during the state keeps it running",
              ShipPowerController.CinematicClearActive && !ShipPowerController.CinematicExiting);

        // Scrolling or despawning off screen counts as gone.
        late.transform.position = new Vector3(1.5f, -20f, 0f);
        Tick(ship);
        Check("a target that leaves the screen counts as gone; one above the screen doesn't block",
              ShipPowerController.CinematicExiting && ShipPowerController.LastCinematicEndedEarly);

        Object.DestroyImmediate(late);
        Object.DestroyImmediate(above);
        Teardown(ship);
        Check("destroying the ship clears the static state", !ShipPowerController.CinematicClearActive);
    }

    static void NoTargetsAtStartKeepsTheNormalHold()
    {
        FreshScene();
        var above = Hazard(new Vector3(0f, 14f, 0f));
        var ship = Ship();
        Fire(ship);
        Tick(ship);
        Check("nothing on screen at start: no instant early exit",
              ShipPowerController.CinematicClearActive && !ShipPowerController.CinematicExiting);

        // Hold timer running out -- the original ending -- uses the same exit.
        typeof(ShipPowerController).GetField("launchesDone", Inst).SetValue(ship, true);
        typeof(ShipPowerController).GetField("holdUntil", Inst).SetValue(ship, Time.unscaledTime - 1f);
        Tick(ship);
        Check("the hold timer expiring takes the same eased exit",
              ShipPowerController.CinematicExiting && !ShipPowerController.LastCinematicEndedEarly);
        ElapseEase();
        Tick(ship);
        Check("and finishes back at 1x", !ShipPowerController.CinematicClearActive);

        Object.DestroyImmediate(above);
        Teardown(ship);
    }

    static void PlayerDeathEndsItImmediately()
    {
        FreshScene();
        var a = Hazard(new Vector3(0f, 1f, 0f));
        var b = Hazard(new Vector3(1f, 1f, 0f));
        var ship = Ship();
        Fire(ship);

        buttonClicks.playerDied = true;
        Tick(ship);
        Check("player death ends the cinematic at once (no ease)",
              !ShipPowerController.CinematicClearActive && !ShipPowerController.CinematicExiting);
        Check("and is not reported as a screen clear", !ShipPowerController.LastCinematicEndedEarly);

        // Death destroys the ship outright, too.
        buttonClicks.playerDied = false;
        Fire(ship);
        Check("(re-fired for the destroy case)", ShipPowerController.CinematicClearActive);
        DestroyShip(ship);
        Check("the ship being destroyed mid-cinematic ends it",
              !ShipPowerController.CinematicClearActive &&
              Mathf.Approximately(ShipPowerController.CinematicTimeScale, 1f));

        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        buttonClicks.playerDied = false;
    }
}
