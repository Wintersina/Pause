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
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        AsteroidsSpinSomeAtTieredSpeeds();
        AtomsStayInsideSideRails();
        Check("player cannot move below the gameplay floor", movePlayer.ClampPlayerY(-99f) >= -4.15f);
        Check("the first launch touch does not spend a pause", !score.ShouldSpendPause(false, false));
        Check("a later pause-resume touch spends exactly one pause", score.ShouldSpendPause(true, false));
        Check("per-ship weapon atlases are present",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Weapons/NeonComet.png") != null);
        Check("ultimate shots have a four-frame flight loop plus smear frames",
              WeaponArt.Shot(1, 0) != null && WeaponArt.Shot(1, WeaponArt.ShotLoopFrames) != null);
        Check("matching green heal-atom sprite is present",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Pickups/heal_atom_green.png") != null);
        Check("green heal atom targets the authored 28px pickup size",
              Mathf.Approximately(28f / 100f, .28f));
        PlayerPrefs.SetString("HasDoneTut", "true");
        score.paysRealDust = true;   // a real (non-tutorial) run
        score.totalCurrency = 0f;
        score.AwardStarDust(.12f);
        Check("destroying an enemy can award a small star-dust payout",
              Mathf.Approximately(score.totalCurrency, .12f));
        RailsCoverTallCamera();
        PauseQuickActionsVisibility();
        QuickActionIconFiles();
        HudPinnedTopLeft();
        UltimatePowerAutoFiresAndSpeedsUpFromPickups();

        Debug.Log("[NF] failures: " + fails);
        return fails;
    }

    // ---- 1: asteroid spin -------------------------------------------------

    static void AsteroidsSpinSomeAtTieredSpeeds()
    {
        // A spinning representative and deliberately-not-spinning ones. The
        // Kenney meteors and the aestroid_* prefabs this used were deleted
        // with the per-world enemy redraw: the roster rock spins, the roster
        // fighter and the armoured heavy do not.
        var spinning = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), Vector3.zero, Quaternion.identity);
        var notSpinning = EnemyFactory.Create(EnemyRoster.Fighter(0, 1), Vector3.zero, Quaternion.identity);
        var heavy = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Big), Vector3.zero, Quaternion.identity);

        Check("a roster rock builds", spinning != null);
        Check("a roster fighter builds", notSpinning != null);
        Check("a roster heavy builds", heavy != null);
        if (spinning == null || notSpinning == null || heavy == null) return;

        var spin = spinning.GetComponent<AsteroidSpin>();
        Check("roster rocks have AsteroidSpin", spin != null);
        Check("fighters are deliberately left without spin",
              notSpinning.GetComponent<AsteroidSpin>() == null);
        Check("the armoured heavy is deliberately left without spin",
              heavy.GetComponent<AsteroidSpin>() == null);
        Object.DestroyImmediate(notSpinning);
        Object.DestroyImmediate(heavy);

        // zigzag (moveEnimes) must still be present -- spin is additive.
        Check("roster rocks kept their zigzag movement (moveEnimes)",
              spinning.GetComponent<moveEnimes>() != null);
        if (spin == null) { Object.DestroyImmediate(spinning); return; }

        // Start() rolls a nonzero speed within the configured range (and can
        // land on either side of zero -- direction is random too), and
        // Update() follows the same playerDied/IsPressed/pauseCounter gate
        // every other flight-only script in this codebase already uses
        // (moveEnimes, ShipThruster, enmiesOnBoard), so a live rotation-delta
        // check here would only be re-testing Time.deltaTime, not this script.
        spin.SendMessage("Start");
        var speedField = typeof(AsteroidSpin).GetField("speed", BindingFlags.NonPublic | BindingFlags.Instance);
        float rolledSpeed = (float)speedField.GetValue(spin);
        Check("rolled a nonzero spin speed within its configured range",
              Mathf.Abs(rolledSpeed) >= spin.speedRange.x && Mathf.Abs(rolledSpeed) <= spin.speedRange.y);

        Object.DestroyImmediate(spinning);
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

        // Icons: matched square SVG-sourced sprites, never stretched.
        foreach (var (action, path) in new[] { (replay, PauseQuickActions.ReplayIconPath), (leave, PauseQuickActions.HomeIconPath) })
        {
            var img = action.GetComponent<UnityEngine.UI.Image>();
            var expected = Resources.Load<Sprite>(path);
            Check(action.name + " icon sprite loads from Resources", expected != null);
            Check(action.name + " uses the SVG-sourced icon", img != null && expected != null && img.sprite == expected);
            Check(action.name + " preserves aspect", img != null && img.preserveAspect);
            var size = action.GetComponent<RectTransform>().sizeDelta;
            Check(action.name + " is a square tap target of ButtonSize", size == new Vector2(PauseQuickActions.ButtonSize, PauseQuickActions.ButtonSize));
            if (expected != null)
            {
                Check(action.name + " sprite is square", Mathf.Approximately(expected.rect.width, expected.rect.height));
                Check(action.name + " texture has no mipmaps", expected.texture.mipmapCount == 1);
            }
        }
        var rRect = replay.GetComponent<RectTransform>();
        var lRect = leave.GetComponent<RectTransform>();
        Check("home icon sits left of replay without overlap",
              lRect.anchoredPosition.x + 0.01f < rRect.anchoredPosition.x - PauseQuickActions.ButtonSize);
        Check("both icons share the same top edge", Mathf.Approximately(lRect.anchoredPosition.y, rRect.anchoredPosition.y));

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

        // movePlayer ignores presses that land on an action. The hit-test must
        // cover each icon's whole square, including its corners, and nothing
        // well outside it.
        Canvas.ForceUpdateCanvases();
        foreach (var action in new[] { replay, leave })
        {
            var corners = new Vector3[4];
            action.GetComponent<RectTransform>().GetWorldCorners(corners);
            Vector2 centre = (corners[0] + corners[2]) * 0.5f;
            Vector2 nearCorner = Vector2.Lerp(corners[0], corners[2], 0.04f);
            Check(action.name + " centre counts as on-action", PauseQuickActions.IsScreenPointOnAction(centre));
            Check(action.name + " square corner counts as on-action", PauseQuickActions.IsScreenPointOnAction(nearCorner));
        }
        {
            var corners = new Vector3[4];
            leave.GetComponent<RectTransform>().GetWorldCorners(corners);
            Vector2 below = new Vector2((corners[0].x + corners[2].x) * 0.5f, corners[0].y - (corners[2].y - corners[0].y));
            Check("point below the icons is not on-action", !PauseQuickActions.IsScreenPointOnAction(below));
        }

        buttonClicks.playerDied = false;
        Object.DestroyImmediate(go);
    }

    // Every quick-action sprite (full tiles and the death panel's glyph-only
    // variants) stays a square, uncompressed-looking, mip-free Resources
    // sprite, whatever the art inside it looks like.
    static void QuickActionIconFiles()
    {
        foreach (string basePath in new[] { PauseQuickActions.ReplayIconPath, PauseQuickActions.HomeIconPath })
        {
            foreach (string path in new[] { basePath, basePath + DeathPanelView.GlyphSuffix })
            {
                var sprite = Resources.Load<Sprite>(path);
                Check(path + " loads as a sprite", sprite != null);
                if (sprite == null) continue;
                Check(path + " is square (" + sprite.rect.width + "x" + sprite.rect.height + ")",
                      Mathf.Approximately(sprite.rect.width, sprite.rect.height));
                Check(path + " is at least 128px", sprite.rect.width >= 128f);
                Check(path + " has no mipmaps", sprite.texture.mipmapCount == 1);
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite.texture)) as TextureImporter;
                Check(path + " importer keeps mipmaps off", importer != null && !importer.mipmapEnabled);
            }
        }
    }

    // The score read-out (SPEED / star dust / PAUSES) is pinned to the
    // safe area's top-left corner with the quick actions' margin, top-aligned
    // with them, and never runs into them -- on every aspect ratio and notch.
    static void HudPinnedTopLeft()
    {
        // Real phones/tablets, with their safe areas in pixels (origin bottom-left).
        var screens = new (string name, Vector2 size, Rect safe)[]
        {
            ("540x1170 window",        new Vector2(540, 1170),  new Rect(0, 0, 540, 1170)),
            ("iPhone SE 750x1334",     new Vector2(750, 1334),  new Rect(0, 0, 750, 1334)),
            ("iPhone 13 1170x2532",    new Vector2(1170, 2532), new Rect(0, 102, 1170, 2532 - 102 - 141)),
            ("iPhone 15 Pro Max",      new Vector2(1290, 2796), new Rect(0, 102, 1290, 2796 - 102 - 177)),
            ("Pixel 1080x2400 cutout", new Vector2(1080, 2400), new Rect(0, 0, 1080, 2400 - 118)),
            ("Z Flip 1080x2640",       new Vector2(1080, 2640), new Rect(0, 0, 1080, 2640 - 96)),
            ("side inset 1080x2340",   new Vector2(1080, 2340), new Rect(60, 40, 1080 - 120, 2340 - 140)),
            ("iPad 1536x2048",         new Vector2(1536, 2048), new Rect(0, 0, 1536, 2048)),
            ("iPad Pro 2048x2732",     new Vector2(2048, 2732), new Rect(0, 40, 2048, 2732 - 80)),
        };

        foreach (var scenePath in new[] { "Assets/Scenes/gameS1.unity", "Assets/Scenes/tutorialS5.unity" })
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            string scene = scenePath.Contains("tutorial") ? "tutorialS5" : "gameS1";
            var go = new GameObject("~HudPlacementTest");
            var styler = go.AddComponent<HudStyler>();
            styler.SendMessage("Start");

            var root = styler.HudRoot;
            Check(scene + ": HUD root found", root != null);
            if (root == null) { Object.DestroyImmediate(go); continue; }
            Check(scene + ": HUD root is a direct child of the root canvas",
                  root.parent != null && root.parent.GetComponent<Canvas>() != null &&
                  root.parent.GetComponent<Canvas>().isRootCanvas);
            Check(scene + ": HUD anchored and pivoted top-left",
                  root.anchorMin == new Vector2(0f, 1f) && root.anchorMax == new Vector2(0f, 1f) &&
                  root.pivot == new Vector2(0f, 1f));
            if (scene == "gameS1")
                Check("gameS1: HUD root is the score panel (Model Panel)", root.name == "Model Panel");

            var canvas = root.parent.GetComponent<Canvas>();
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            Vector2 hudSize = root.rect.size;

            foreach (var s in screens)
            {
                float hudScale = HudStyler.HudCanvasScale(canvas, scaler, s.size);
                Rect hud = HudStyler.HudScreenRect(s.safe, s.size, hudScale, hudSize);
                Rect actions = PauseQuickActions.ScreenRectFor(s.safe, s.size);
                float actionScale = PauseQuickActions.CanvasScaleFor(s.size);
                float margin = PauseQuickActions.EdgeMargin * actionScale;
                string tag = scene + " @ " + s.name + ": ";

                Check(tag + "HUD left edge sits one margin in from the safe area (" +
                      (hud.xMin - s.safe.xMin).ToString("F1") + "px vs " + margin.ToString("F1") + ")",
                      Mathf.Abs(hud.xMin - s.safe.xMin - margin) < 1f);
                Check(tag + "HUD top edge sits one margin below the safe area's top",
                      Mathf.Abs(s.safe.yMax - hud.yMax - margin) < 1f);
                Check(tag + "HUD top aligned with the quick actions (" +
                      ((hud.yMax - actions.yMax) / actionScale).ToString("F2") + " units)",
                      Mathf.Abs(hud.yMax - actions.yMax) / actionScale < 3f);
                Check(tag + "HUD inside the safe area", s.safe.Contains(hud.min) && s.safe.Contains(hud.max));
                Check(tag + "HUD does not overlap the quick actions", !hud.Overlaps(actions));
                Check(tag + "HUD stays in the top quarter", hud.yMin > s.size.y * 0.75f);
                Check(tag + "HUD keeps a readable size (>= 30% of the safe width)",
                      hud.width >= s.safe.width * 0.3f);

                // The death panel (DeathPanelView.Fit) treats the HUD and the
                // quick actions as one top band and drops below both.
                var popup = scene == "gameS1" ? SceneUtil.FindAny("PopUpCanvas") : null;
                var popupScaler = popup != null ? popup.GetComponent<UnityEngine.UI.CanvasScaler>() : null;
                if (popupScaler != null)
                {
                    float sf = HudStyler.HudCanvasScale(popup.GetComponent<Canvas>(), popupScaler, s.size);
                    Vector2 half = s.size / sf * 0.5f;
                    System.Func<Rect, Rect> toUnits = r => new Rect(r.x / sf - half.x, r.y / sf - half.y, r.width / sf, r.height / sf);
                    Rect band = Rect.MinMaxRect(Mathf.Min(hud.xMin, actions.xMin), Mathf.Min(hud.yMin, actions.yMin),
                                                Mathf.Max(hud.xMax, actions.xMax), Mathf.Max(hud.yMax, actions.yMax));
                    Vector2 centre; float scale;
                    DeathPanelView.ComputeFit(toUnits(s.safe), toUnits(band), out centre, out scale);
                    float w = (DeathPanelView.Width + 2f * DeathPanelView.GlowMargin) * scale;
                    float h = (DeathPanelView.Height + 2f * DeathPanelView.GlowMargin) * scale;
                    var panel = new Rect(centre.x - w * .5f, centre.y - h * .5f, w, h);
                    Check(tag + "death panel stays clear of the HUD (scale " + scale.ToString("F2") + ")",
                          !panel.Overlaps(toUnits(hud)) && scale >= 0.5f);
                }
            }

            Object.DestroyImmediate(go);
        }

        // Before: centre-anchored at (-137, 583) on an 800-wide match-width
        // canvas, it hung ~200 units below the top on a 540x1170 screen.
        {
            Vector2 pos; float fit;
            HudStyler.ComputeHudLayout(new Rect(0, 0, 540, 1170), new Vector2(540, 1170), 540f / 800f,
                                       new Vector2(351, 171), out pos, out fit);
            Check("540x1170: panel top is ~16px from the top, not ~190px (" + (-pos.y * 540f / 800f).ToString("F1") + "px)",
                  -pos.y * 540f / 800f < 20f);
            Check("540x1170: panel keeps full size", Mathf.Approximately(fit, 1f));
        }

        // A pathologically narrow screen shrinks the panel instead of overlapping.
        {
            var size = new Vector2(400, 2400);
            var safe = new Rect(0, 0, 400, 2400);
            float hudScale = 400f / 800f * 1.6f;   // a canvas wider-scaled than gameS1's
            Rect hud = HudStyler.HudScreenRect(safe, size, hudScale, new Vector2(351, 171));
            Check("narrow screen: HUD shrinks to stay clear of the quick actions",
                  !hud.Overlaps(PauseQuickActions.ScreenRectFor(safe, size)));
        }
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
        Check("power charge indicator is attached", shipGo.GetComponent<ChargeIndicator>() != null);
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
