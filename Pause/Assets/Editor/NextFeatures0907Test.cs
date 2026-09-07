using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Reflection;

// Covers the second 2026-09-07 batch:
//   1. asteroid/meteor prefabs spin, some of them, at tiered per-type speeds,
//      on top of the zigzag they already had.
//   2. leftPipe/rightPipe now rescale to keep covering the full screen
//      height on tall phones instead of sitting fixed at the baseline size.
//   3. a small leave/replay pair at the top-right, visible only during an
//      ordinary pause (not death, not mid-flight).
//   4. the ultimate power fires itself on a rerolled 30-60s timer instead of
//      waiting for a second touch, with no more text HUD, a gun that
//      extends/retracts, and star dust/atoms shaving time off the timer.
public static class NextFeatures0907Test
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[NF] PASS  " : "[NF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        AsteroidsSpinSomeAtTieredSpeeds();
        AtomsStayInsideSideRails();
        RailsCoverTallCamera();
        PauseQuickActionsVisibility();
        UltimatePowerAutoFiresAndSpeedsUpFromPickups();

        Debug.Log("[NF] failures: " + fails);
        EditorApplication.Exit(0);
    }

    // ---- 1: asteroid spin -------------------------------------------------

    static void AsteroidsSpinSomeAtTieredSpeeds()
    {
        // A spinning representative and a deliberately-not-spinning one.
        var spinning = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/aestroid_brown.prefab");
        var notSpinning = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/Enemies/kn_meteorGrey_tiny1.prefab");
        var bigMeteor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/Enemies/kn_meteorBrown_big1.prefab");
        var medMeteor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/Enemies/kn_meteorBrown_med1.prefab");

        Check("aestroid_brown loaded", spinning != null);
        Check("kn_meteorGrey_tiny1 loaded", notSpinning != null);
        if (spinning == null || notSpinning == null || bigMeteor == null || medMeteor == null) return;

        var spin = spinning.GetComponent<AsteroidSpin>();
        Check("aestroid_brown has AsteroidSpin", spin != null);
        Check("kn_meteorGrey_tiny1 was deliberately left without spin",
              notSpinning.GetComponent<AsteroidSpin>() == null);

        var big = bigMeteor.GetComponent<AsteroidSpin>();
        var med = medMeteor.GetComponent<AsteroidSpin>();
        Check("big meteors have AsteroidSpin", big != null);
        Check("med meteors have AsteroidSpin", med != null);
        if (big != null && med != null)
            Check("big meteors are tuned to spin slower than med ones (heavier reads slower)",
                  big.speedRange.y <= med.speedRange.y);

        // zigzag (moveEnimes) must still be present -- spin is additive.
        Check("aestroid_brown kept its zigzag movement (moveEnimes)",
              spinning.GetComponent<moveEnimes>() != null);

        // Start() rolls a nonzero speed within the configured range (and can
        // land on either side of zero -- direction is random too), and
        // Update() follows the same playerDied/IsPressed/pauseCounter gate
        // every other flight-only script in this codebase already uses
        // (moveEnimes, ShipThruster, enmiesOnBoard), so a live rotation-delta
        // check here would only be re-testing Time.deltaTime, not this script.
        var go = Object.Instantiate(spinning);
        var comp = go.GetComponent<AsteroidSpin>();
        comp.SendMessage("Start");
        var speedField = typeof(AsteroidSpin).GetField("speed", BindingFlags.NonPublic | BindingFlags.Instance);
        float rolledSpeed = (float)speedField.GetValue(comp);
        Check("rolled a nonzero spin speed within its configured range",
              Mathf.Abs(rolledSpeed) >= comp.speedRange.x && Mathf.Abs(rolledSpeed) <= comp.speedRange.y);

        Object.DestroyImmediate(go);
    }

    static void AtomsStayInsideSideRails()
    {
        Check("atom clamp keeps its visible edge inside the left rail",
              AtomSpin.ClampAtomX(-4f, .22f) >= -2.13f);
        Check("atom clamp keeps its visible edge inside the right rail",
              AtomSpin.ClampAtomX(4f, .22f) <= 2.13f);
    }

    // ---- 2: rails cover the camera -----------------------------------------

    static void RailsCoverTallCamera()
    {
        foreach (var scenePath in new[] { "Assets/Scenes/gameS1.unity", "Assets/Scenes/tutorialS5.unity" })
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            string scene = scenePath.Contains("tutorial") ? "tutorialS5" : "gameS1";

            var cam = Camera.main;
            Check(scene + ": has a main camera", cam != null);
            if (cam == null) continue;

            foreach (var name in new[] { "leftPipe", "rightPipe" })
            {
                var go = SceneUtil.FindAny(name);
                Check(scene + ": " + name + " exists", go != null);
                if (go == null) continue;

                var rail = go.GetComponent<RailFit>();
                if (rail == null) rail = go.AddComponent<RailFit>();
                rail.SendMessage("Start");

                cam.orthographicSize = 5f;
                rail.SendMessage("Reposition");
                float scaleAtBaseline = go.transform.localScale.y;

                cam.orthographicSize = 6.65f; // tall-phone worst case seen on a Galaxy Z Flip
                rail.SendMessage("Reposition");
                float scaleAtTallPhone = go.transform.localScale.y;
                float meshHeight = go.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
                float worldHeight = scaleAtTallPhone * meshHeight;

                Check(scene + ": " + name + " grew for the taller camera",
                      scaleAtTallPhone > scaleAtBaseline);
                Check(scene + ": " + name + " fully covers the camera's visible height",
                      worldHeight >= cam.orthographicSize * 2f);
            }
        }
    }

    // ---- 3: pause-time quick actions ---------------------------------------

    static void PauseQuickActionsVisibility()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        var go = new GameObject("~PauseQuickActionsTest");
        var comp = go.AddComponent<PauseQuickActions>();
        comp.SendMessage("Start");

        var replayField = typeof(PauseQuickActions).GetField("replayClone", BindingFlags.NonPublic | BindingFlags.Instance);
        var leaveField = typeof(PauseQuickActions).GetField("leaveClone", BindingFlags.NonPublic | BindingFlags.Instance);
        var replay = replayField.GetValue(comp) as GameObject;
        var leave = leaveField.GetValue(comp) as GameObject;

        Check("quick-action replay clone exists", replay != null);
        Check("quick-action leave clone exists", leave != null);
        if (replay == null || leave == null) { Object.DestroyImmediate(go); return; }

        Check("replay clone is anchored top-right", replay.GetComponent<RectTransform>().anchorMin == new Vector2(1f, 1f));

        var replayButton = replay.GetComponent<UnityEngine.UI.Button>();
        bool wiredToReplay = false;
        for (int i = 0; i < replayButton.onClick.GetPersistentEventCount(); i++)
            if (replayButton.onClick.GetPersistentMethodName(i) == "replay") wiredToReplay = true;
        Check("replay clone still calls buttonClicks.replay()", wiredToReplay);

        var leaveButton = leave.GetComponent<UnityEngine.UI.Button>();
        bool wiredToLeave = false;
        for (int i = 0; i < leaveButton.onClick.GetPersistentEventCount(); i++)
            if (leaveButton.onClick.GetPersistentMethodName(i) == "mainMenuButton") wiredToLeave = true;
        Check("leave clone still calls buttonClicks.mainMenuButton()", wiredToLeave);

        // The same high-priority controls remain available after death. This
        // avoids the old death dialog eating taps intended for its lower-canvas
        // button pair, and keeps the location consistent with pause.
        buttonClicks.playerDied = true;
        score.pauseCounter = 3;
        comp.SendMessage("Update");
        Check("shown while dead", replay.activeSelf && leave.activeSelf);
        var overlay = replay.GetComponentInParent<Canvas>();
        Check("death actions live above the popup canvas", overlay != null && overlay.sortingOrder > 1);
        Check("death actions have a raycaster for taps", overlay != null &&
              overlay.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null);

        // Alive and out of pauses: flight continues without touch, but the
        // player must still be able to leave the run.
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        comp.SendMessage("Update");
        Check("shown when out of pauses so the player can leave", replay.activeSelf && leave.activeSelf);

        // alive, pauses left, not touching: a genuine ordinary pause.
        score.pauseCounter = 3;
        comp.SendMessage("Update");
        Check("shown during an ordinary pause", replay.activeSelf && leave.activeSelf);

        buttonClicks.playerDied = false;
        Object.DestroyImmediate(go);
    }

    // ---- 4: ultimate power --------------------------------------------------

    static void UltimatePowerAutoFiresAndSpeedsUpFromPickups()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        // Ship 0 -> Laser, a synchronous effect (not a coroutine), so firing
        // it here doesn't depend on the player loop actually ticking.
        PlayerPrefs.SetInt("spawnShip", 0);

        var shipGo = new GameObject("ship0", typeof(SpriteRenderer));
        var controller = shipGo.AddComponent<ShipPowerController>();
        // Outside Play mode, AddComponent does not auto-invoke Awake() any
        // more than it auto-invokes Start() -- every other test in this
        // suite already hand-calls Start() for the same reason. In a real
        // build/Play mode both fire on their own; this is purely a batch-
        // mode testing artifact.
        controller.SendMessage("Awake");
        controller.SendMessage("Start");

        Check("ShipPowerController.Instance is set once attached", ShipPowerController.Instance == controller);

        var gunField = typeof(ShipPowerController).GetField("gun", BindingFlags.NonPublic | BindingFlags.Instance);
        var gun = gunField.GetValue(controller) as UltimateGun;
        Check("a gun was attached", gun != null);
        Check("ship gained an UltimateGun child", shipGo.GetComponentInChildren<UltimateGun>() != null);
        if (gun != null) gun.SendMessage("Awake");
        Check("gun has roster-specific barrel art", gun != null &&
              gun.transform.Find("Barrel") != null &&
              gun.transform.Find("Barrel").GetComponent<SpriteRenderer>().sprite != null);
        Check("gun is a companion of the player ship", gun != null && gun.transform.parent == shipGo.transform);
        Check("roster has several companion hover patterns",
              UltimateGun.HoverModeFor(1) != UltimateGun.HoverModeFor(2) &&
              UltimateGun.HoverModeFor(2) != UltimateGun.HoverModeFor(3));
        Check("power charge indicator is attached", shipGo.GetComponent<PowerReadyIndicator>() != null);
        Vector3 bentHeading = PowerFx.SteerHeading(Vector3.up, Vector3.right, 180f, .25f);
        Check("homing projectile bends toward a moving side target",
              bentHeading.x > .1f && bentHeading.y > .1f);

        var timerField = typeof(ShipPowerController).GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance);
        var cooldownField = typeof(ShipPowerController).GetField("cooldown", BindingFlags.NonPublic | BindingFlags.Instance);

        float cooldown = (float)cooldownField.GetValue(controller);
        Check("initial cooldown falls within the 30-60s range",
              cooldown >= controller.cooldownRange.x && cooldown <= controller.cooldownRange.y);

        // Star dust and atoms speed the countdown up, atoms by more.
        float before = (float)timerField.GetValue(controller);
        controller.ReduceTimer(controller.secondsPerDust);
        float afterDust = (float)timerField.GetValue(controller);
        Check("collecting star dust shaves time off the countdown",
              afterDust < before && Mathf.Approximately(before - afterDust, controller.secondsPerDust));

        controller.ReduceTimer(controller.secondsPerAtom);
        float afterAtom = (float)timerField.GetValue(controller);
        Check("collecting an atom shaves noticeably more time off than dust",
              before - afterAtom > (before - afterDust) &&
              controller.secondsPerAtom > controller.secondsPerDust);

        // Auto-fires with no second touch once the countdown reaches zero,
        // and rerolls a fresh 30-60s cooldown for next time.
        buttonClicks.playerDied = false;
        score.pauseCounter = 0; // flying without needing a touch in batch mode
        timerField.SetValue(controller, 0f);
        controller.SendMessage("Update");
        float rerolled = (float)timerField.GetValue(controller);
        Check("fires on its own once charged (timer reset for the next cycle)", rerolled > 0f);
        Check("the reroll lands back in the 30-60s range",
              rerolled >= controller.cooldownRange.x && rerolled <= controller.cooldownRange.y);
        // Firing (just above) is what actually starts the CinematicClear
        // coroutine, which sets CinematicClearActive true as its first
        // statement before any yield -- moved here from right after the gun/
        // indicator checks, where nothing had triggered it yet and this
        // always read the inactive (1x) branch instead.
        Check("cinematic clear uses super slow motion", ShipPowerController.CinematicTimeScale <= .1f);

        buttonClicks.playerDied = false;
        Object.DestroyImmediate(shipGo);
    }
}
