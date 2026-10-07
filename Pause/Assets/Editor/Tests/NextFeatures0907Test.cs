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
        Check("player cannot move below the gameplay floor (the hull and flame stay in view)",
              movePlayer.ClampPlayerY(-99f) - ShipReach.HullBelow >= CameraFit.ViewBottom);   // (was >= -4.15)
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
        TopBandInsideRailsClearOfCutouts();
        TopBandWithUiScaleFloor();
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

    // The score read-out (SCORE / SPEED / PAUSES) is pinned to the left end of
    // the top band (TopBand: inside the left rail, inside the safe area),
    // top-aligned with the quick actions at its right end, and never runs
    // into them -- on every aspect ratio and notch.
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
            ("9:24 1080x2880",         new Vector2(1080, 2880), new Rect(0, 48, 1080, 2880 - 48 - 120)),
            ("Z Fold cover 968x2376",  new Vector2(968, 2376),  new Rect(0, 0, 968, 2376 - 90)),
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
                float topMargin = PauseQuickActions.TopMargin * actionScale;
                var frame = TopBand.FrameFor(s.safe, s.size);
                string tag = scene + " @ " + s.name + ": ";

                Check(tag + "HUD left edge sits at the band's left end, at least one margin in from the safe area (" +
                      (hud.xMin - s.safe.xMin).ToString("F1") + "px vs " + margin.ToString("F1") + ")",
                      Mathf.Abs(hud.xMin - frame.left) < 1f && hud.xMin - s.safe.xMin >= margin - 1f);
                Check(tag + "quick actions end at the band's right end, at least one margin in from the safe area",
                      Mathf.Abs(actions.xMax - frame.right) < 1f && s.safe.xMax - actions.xMax >= margin - 1f);
                Check(tag + "HUD top edge sits one top margin below the safe area's top",
                      Mathf.Abs(s.safe.yMax - hud.yMax - topMargin) < 1f);
                Check(tag + "quick actions sit one top margin below the safe area's top, inside it",
                      Mathf.Abs(s.safe.yMax - actions.yMax - topMargin) < 1f && actions.yMax < s.safe.yMax);
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
                                       new Vector2(351, 131), out pos, out fit);
            Check("540x1170: panel top is ~3px from the top, not ~190px (" + (-pos.y * 540f / 800f).ToString("F1") + "px)",
                  -pos.y * 540f / 800f < 20f);
            Check("540x1170: panel keeps a legible size beside the quick actions (" + fit.ToString("F2") + ")",
                  fit >= TopBand.ReadoutMinScale && fit <= 1f);
        }

        // A pathologically narrow screen shrinks the panel instead of overlapping.
        {
            var size = new Vector2(400, 2400);
            var safe = new Rect(0, 0, 400, 2400);
            float hudScale = 400f / 800f * 1.6f;   // a canvas wider-scaled than gameS1's
            Rect hud = HudStyler.HudScreenRect(safe, size, hudScale, new Vector2(351, 131));
            Check("narrow screen: HUD shrinks to stay clear of the quick actions",
                  !hud.Overlaps(PauseQuickActions.ScreenRectFor(safe, size)));
        }
    }

    // ---- 3b: the top band, the rails and display cutouts ----------------------

    // Screen shapes, with the points-per-pixel / density used to judge the
    // buttons as tap targets where the device is known (0: not judged).
    static readonly (string name, Vector2 size, float pxPerPoint, float minPoints)[] BandShapes =
    {
        ("Z Flip7 1080x2520",     new Vector2(1080, 2520), 2.625f, 48f),   // 420 dpi bucket, dp
        ("1080x2400",             new Vector2(1080, 2400), 2.625f, 48f),
        ("16:9 1080x1920",        new Vector2(1080, 1920), 0f, 0f),
        ("iPhone 15 1179x2556",   new Vector2(1179, 2556), 3f, 44f),
        ("iPhone 15 PM 1290x2796", new Vector2(1290, 2796), 3f, 44f),
        ("iPhone SE 750x1334",    new Vector2(750, 1334), 0f, 0f),
        ("Fold cover 904x2316",   new Vector2(904, 2316), 0f, 0f),
        ("Fold open 1812x2176",   new Vector2(1812, 2176), 0f, 0f),
        ("iPad 1536x2048",        new Vector2(1536, 2048), 0f, 0f),
        ("720x1280",              new Vector2(720, 1280), 0f, 0f),
    };

    // Synthetic display cutouts for a w x h portrait screen, each with the
    // safe area the platform reports for it (pixels, origin bottom-left).
    public static (string name, Rect safe, Rect[] cutouts)[] CutoutCases(Vector2 size)
    {
        float w = size.x, h = size.y;
        // iPhone notch: 54% wide, flush with the top; the safe area starts under it
        var notch = new Rect(w * .23f, h - h * .037f, w * .54f, h * .037f);
        // Dynamic Island: a floating pill; the safe area starts a little below it
        var island = new Rect(w * .34f, h - h * .057f, w * .32f, h * .044f);
        // punch-holes: a 6.5%-wide camera a little down from the top
        float d = w * .065f;
        var hole = new Rect(w * .5f - d * .5f, h - h * .012f - d, d, d);
        var corner = new Rect(w * .055f, h - h * .012f - d, d, d);
        // waterfall: both long edges curve away
        float fall = w * .03f;
        var fallL = new Rect(0f, 0f, fall, h);
        var fallR = new Rect(w - fall, 0f, fall, h);
        return new[]
        {
            ("no cutout", new Rect(0, 0, w, h), new Rect[0]),
            ("centre notch", new Rect(0, h * .04f, w, notch.yMin - h * .04f), new[] { notch }),
            ("centre island", new Rect(0, h * .04f, w, h * (1f - .069f) - h * .04f), new[] { island }),
            ("centre punch-hole", new Rect(0, 0, w, hole.yMin), new[] { hole }),
            ("corner punch-hole", new Rect(0, 0, w, corner.yMin), new[] { corner }),
            ("waterfall edges", new Rect(fall, 0, w - 2f * fall, h), new[] { fallL, fallR }),
            // a platform that reports the hole but leaves the safe area whole
            ("centre punch-hole, safe area not inset", new Rect(0, 0, w, h), new[] { hole }),
            ("corner punch-hole, safe area not inset", new Rect(0, 0, w, h), new[] { corner }),
        };
    }

    // ---- 3c: the top band on phones small in points / dp (UiScale's floor) ----

    // Shapes the floor raises (their real density, as the OS reports it) and
    // two it must leave alone (the user's 1080x2520 phone, iPhone 15).
    static readonly (string name, Vector2 size, float dpi, bool ios, float pxPerPt, bool raised)[] FloorShapes =
    {
        ("480x854 hdpi",        new Vector2(480, 854),   240f, false, 1.5f,   true),
        ("720x1280 xhdpi",      new Vector2(720, 1280),  320f, false, 2f,     true),
        ("1080x1920 420dpi",    new Vector2(1080, 1920), 420f, false, 2.625f, true),
        ("iPhone SE 750x1334",  new Vector2(750, 1334),  326f, true,  2f,     true),
        ("Z Flip7 1080x2520",   new Vector2(1080, 2520), 420f, false, 2.625f, false),
        ("iPhone 15 1179x2556", new Vector2(1179, 2556), 460f, true,  3f,     false),
    };

    // With the floor (dpi reported, and not), on every cutout case: the
    // quick actions are >= 44 pt / 48 dp; the read-out keeps at least
    // TopBand.ReadoutMinScale -- beside them, or stacked under them where the
    // lane is too narrow -- and every piece (read-out, buttons, BOSS
    // INCOMING's chip and banner, PORTAL DANGER's chip, the codex toast's
    // top) stays inside the rails' inner edges and the safe area, clear of
    // the cutouts and of each other. Where the floor does not raise the
    // actions, the band is exactly what it was without it.
    static void TopBandWithUiScaleFloor()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        UiScaleFloor.AttachScene();   // what the scene-load hook does on a device
        var go = new GameObject("~TopBandFloorTest");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        var root = styler.HudRoot;
        Check("floor: the read-out exists", root != null);
        if (root == null) { Object.DestroyImmediate(go); return; }
        var canvas = root.parent.GetComponent<Canvas>();
        var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        Check("floor: the HUD canvas carries UiScale's floor", scaler.GetComponent<UiScaleFloor>() != null);
        Vector2 hudSize = root.rect.size;

        float painted = float.MaxValue;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            WorldPainter.Apply(WorldManager.Worlds[w]);
            painted = Mathf.Min(painted, BossRails.InnerEdge);
        }

        try
        {
            foreach (var shape in FloorShapes)
            {
                Vector2 size = shape.size;
                float halfW = CameraFit.GameplayViewHalfWidth(size);
                float railL = size.x * .5f - painted / halfW * size.x * .5f, railR = size.x - railL;
                float minTap = shape.ios ? 44f : 48f;

                // the band without any floor (the editor's plain pixel scaling)
                var plainCase = CutoutCases(size)[0];
                Rect plainRead = HudStyler.HudScreenRect(TopBand.FrameFor(plainCase.safe, size, painted, plainCase.cutouts), size,
                                                         HudStyler.HudCanvasScale(canvas, scaler, size), hudSize);

                foreach (bool reported in new[] { true, false })
                {
                    string tag = "floor: " + shape.name + (reported ? "" : " (dpi unreported)") + ": ";
                    string why = "";
                    int stackedCases = 0;
                    float worstFit = 1f, worstTap = float.MaxValue;
                    foreach (var c in CutoutCases(size))
                    {
                        using (ScreenInfo.Override((int)size.x, (int)size.y, c.safe, c.cutouts, reported ? shape.dpi : 0f, shape.ios))
                        {
                            UiScaleFloor.ApplyAll();
                            float s = PauseQuickActions.CanvasScaleFor(size);
                            float hudScale = HudStyler.HudCanvasScale(canvas, scaler, size);
                            var band = TopBand.FrameFor(c.safe, size, painted, c.cutouts);
                            Vector2 pos; float fit; bool stacked;
                            HudStyler.ComputeHudLayout(band, size, hudScale, hudSize, out pos, out fit, out stacked);
                            Rect read = HudStyler.HudScreenRect(band, size, hudScale, hudSize);
                            Rect actions = PauseQuickActions.ScreenRectFor(band, size);
                            Rect home = PauseQuickActions.ButtonScreenRect(band, size, 1);
                            Rect replay = PauseQuickActions.ButtonScreenRect(band, size, 0);
                            HudStyler.StackedReadout = stacked ? read : default(Rect);
                            var l = BossWarningHud.ComputeLayout(c.safe, size, read, band);
                            float portalScale = Mathf.Min(size.x / 800f, size.y / 1200f);   // its overlay canvas: 800x1200, Expand
                            Rect portal = PortalPressureHud.ChipScreenRect(band, portalScale);
                            float toastTop = size.y - CodexToast.TopOffset(0f, size.y, 1f, default(Rect), default(Rect), portal);
                            if (stacked) stackedCases++;
                            worstFit = Mathf.Min(worstFit, fit);
                            worstTap = Mathf.Min(worstTap, Mathf.Min(home.width, home.height) / shape.pxPerPt);

                            var pieces = new[] { ("read-out", read), ("home", home), ("replay", replay), ("boss chip", l.chip) };
                            float gap = TopBand.CutoutClearance * s - .5f;
                            for (int i = 0; i < pieces.Length && why == ""; i++)
                            {
                                Rect r = pieces[i].Item2;
                                string at = " [" + c.name + ": " + pieces[i].Item1 + " " + r + "; rails " + railL.ToString("F0") + ".." + railR.ToString("F0") + "]";
                                if (r.xMin < railL - .01f || r.xMax > railR + .01f) why = "outside the rails" + at;
                                else if (!InsideRect(c.safe, r)) why = "outside the safe area" + at;
                                foreach (var cut in c.cutouts)
                                    if (why == "" && r.Overlaps(new Rect(cut.x - gap, cut.y - gap, cut.width + 2f * gap, cut.height + 2f * gap)))
                                        why = "on a cutout" + at;
                                for (int j = i + 1; j < pieces.Length && why == ""; j++)
                                    if (r.Overlaps(pieces[j].Item2)) why = "overlaps the " + pieces[j].Item1 + at;
                            }
                            if (why == "" && fit < TopBand.ReadoutMinScale - .001f)
                                why = "read-out at " + fit.ToString("F2") + " [" + c.name + "]";
                            if (why == "" && Mathf.Min(home.width, home.height) / shape.pxPerPt < minTap - .01f)
                                why = "a button is " + (home.width / shape.pxPerPt).ToString("F1") + " [" + c.name + "]";
                            if (why == "" && stacked && read.yMax > actions.yMin + .5f)
                                why = "stacked read-out is not under the quick actions [" + c.name + "]";
                            if (why == "" && !stacked && (Mathf.Abs(read.yMax - band.top) > .5f || Mathf.Abs(home.yMax - band.top) > .5f))
                                why = "read-out and icons do not share the band's top edge [" + c.name + "]";
                            if (why == "" && (!InsideRect(c.safe, l.banner) || l.banner.Overlaps(read) || l.banner.Overlaps(actions)))
                                why = "BOSS INCOMING's banner on the band [" + c.name + "]";
                            if (why == "" && (portal.Overlaps(read) || portal.Overlaps(actions) || portal.xMin < band.left - .5f || portal.xMax > band.right + .5f))
                                why = "PORTAL DANGER's chip on the band or outside it: " + portal + " [" + c.name + "]";
                            if (why == "" && (toastTop > read.yMin + .5f || toastTop > actions.yMin + .5f || toastTop > portal.yMin + .5f))
                                why = "the codex toast's top " + toastTop.ToString("F0") + " is over the band [" + c.name + "]";
                            // (with no dpi reported the fallback takes the screen as small
                            // as its shape can be, so it may raise these too: only checked
                            // with the density the device really reports)
                            if (why == "" && !shape.raised && reported && (stacked || c.name == "no cutout" && !SameRect(read, plainRead)))
                                why = "the band moved where the floor has nothing to raise: " + read + " vs " + plainRead + " [" + c.name + "]";
                        }
                    }
                    Check(tag + "buttons >= " + minTap + (shape.ios ? " pt" : " dp") + " (" + worstTap.ToString("F1") + "), read-out >= " +
                          TopBand.ReadoutMinScale + " (" + worstFit.ToString("F2") + (stackedCases > 0 ? ", stacked under the actions on " + stackedCases + " of 8 cutout cases" : ", beside the actions") +
                          "), every piece inside the rails and the safe area, clear of cutouts and each other" + (why == "" ? "" : ": " + why), why == "");
                    if (shape.raised && reported)
                        Debug.Log("[NF] floor: " + shape.name + ": quick-action buttons " + worstTap.ToString("F1") + (shape.ios ? " pt" : " dp") +
                                  ", read-out " + (stackedCases > 0 ? "stacked under them (" + stackedCases + "/8 cases)" : "beside them") + ", fit " + worstFit.ToString("F2"));
                }
            }
        }
        finally
        {
            HudStyler.StackedReadout = default(Rect);
            UiScaleFloor.ApplyAll();   // back to the editor's own (unfloored) scaling
            Object.DestroyImmediate(go);
        }
    }

    static bool SameRect(Rect a, Rect b)
    {
        return Mathf.Abs(a.xMin - b.xMin) < .5f && Mathf.Abs(a.yMin - b.yMin) < .5f && Mathf.Abs(a.xMax - b.xMax) < .5f && Mathf.Abs(a.yMax - b.yMax) < .5f;
    }

    static bool InsideRect(Rect outer, Rect inner)
    {
        return inner.xMin >= outer.xMin - .01f && inner.xMax <= outer.xMax + .01f &&
               inner.yMin >= outer.yMin - .01f && inner.yMax <= outer.yMax + .01f;
    }

    // The read-out, the two buttons and the boss chip stay inside the rails'
    // inner edges and the safe area, clear of every cutout and of each other,
    // on every shape x cutout, for the painted rails and the authored wall.
    static void TopBandInsideRailsClearOfCutouts()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var go = new GameObject("~TopBandTest");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        var root = styler.HudRoot;
        Check("top band: the read-out exists", root != null);
        if (root == null) { Object.DestroyImmediate(go); return; }
        var canvas = root.parent.GetComponent<Canvas>();
        var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        Vector2 hudSize = root.rect.size;

        // the edge gameplay uses, once a world's rails are painted
        float authored = BossRails.AuthoredInnerEdge;
        float painted = float.MaxValue;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            WorldPainter.Apply(WorldManager.Worlds[w]);
            painted = Mathf.Min(painted, BossRails.InnerEdge);
        }
        Check("top band: every world's painted rails start outside the authored wall edge (" + painted.ToString("F3") + " vs " +
              authored.ToString("F3") + ")", painted > authored && painted < CameraFit.GameplayHalfWidth);
        Check("top band: the live band follows the rails as painted",
              TopBand.FrameFor(new Rect(0, 0, 1080, 2520), new Vector2(1080, 2520)).Same(
                  TopBand.FrameFor(new Rect(0, 0, 1080, 2520), new Vector2(1080, 2520), BossRails.InnerEdge, null)));

        float worstFit = 1f;
        string worstAt = "";
        foreach (var shape in BandShapes)
        {
            Vector2 size = shape.size;
            float s = PauseQuickActions.CanvasScaleFor(size);
            float hudScale = HudStyler.HudCanvasScale(canvas, scaler, size);
            float halfW = CameraFit.GameplayViewHalfWidth(size);
            foreach (float edge in new[] { painted, authored })
            {
                float railL = size.x * .5f - edge / halfW * size.x * .5f, railR = size.x - railL;
                foreach (var c in CutoutCases(size))
                {
                    string tag = shape.name + ", " + c.name + (edge == painted ? "" : " (unpainted wall)") + ": ";
                    var band = TopBand.FrameFor(c.safe, size, edge, c.cutouts);
                    Rect read = HudStyler.HudScreenRect(band, size, hudScale, hudSize);
                    Rect home = PauseQuickActions.ButtonScreenRect(band, size, 1);
                    Rect replay = PauseQuickActions.ButtonScreenRect(band, size, 0);
                    var l = BossWarningHud.ComputeLayout(c.safe, size, read, band);
                    var pieces = new[] { ("read-out", read), ("home", home), ("replay", replay), ("boss chip", l.chip) };

                    bool inRails = true, inSafe = true, clear = true, apart = true;
                    string why = "";
                    float gap = TopBand.CutoutClearance * s - .5f;
                    for (int i = 0; i < pieces.Length; i++)
                    {
                        Rect r = pieces[i].Item2;
                        bool a = r.xMin >= railL - .01f && r.xMax <= railR + .01f;
                        bool b = InsideRect(c.safe, r);
                        bool k = true;
                        foreach (var cut in c.cutouts)
                            k &= !r.Overlaps(new Rect(cut.x - gap, cut.y - gap, cut.width + 2f * gap, cut.height + 2f * gap));
                        bool o = true;
                        for (int j = i + 1; j < pieces.Length; j++) o &= !r.Overlaps(pieces[j].Item2);
                        if (!(a && b && k && o) && why == "")
                            why = " [" + pieces[i].Item1 + " x " + r.xMin.ToString("F0") + ".." + r.xMax.ToString("F0") + " y " +
                                  r.yMin.ToString("F0") + ".." + r.yMax.ToString("F0") + "; rails " + railL.ToString("F0") + ".." + railR.ToString("F0") + "]";
                        inRails &= a; inSafe &= b; clear &= k; apart &= o;
                    }
                    Check(tag + "read-out, buttons and chip inside the rails' inner edges" + why, inRails);
                    Check(tag + "inside the safe area" + why, inSafe);
                    Check(tag + "clear of every cutout by the clearance" + why, clear);
                    Check(tag + "not overlapping each other" + why, apart);

                    bool banner = InsideRect(c.safe, l.banner) && !l.banner.Overlaps(read) && !l.banner.Overlaps(home) &&
                                  !l.banner.Overlaps(replay) && !l.banner.Overlaps(l.chip);
                    foreach (var cut in c.cutouts) banner &= !l.banner.Overlaps(cut);
                    Check(tag + "the boss banner is under the band, inside the safe area, clear of the cutouts", banner);

                    Check(tag + "the read-out, the icons and the band share one top edge, in the top quarter",
                          Mathf.Abs(read.yMax - band.top) < .5f && Mathf.Abs(home.yMax - band.top) < .5f &&
                          Mathf.Abs(replay.yMax - band.top) < .5f && read.yMin > size.y * .75f);

                    float fit = read.width / (hudSize.x * hudScale);
                    if (edge == painted && fit < worstFit) { worstFit = fit; worstAt = shape.name + ", " + c.name; }
                    // unpainted (a frame at most, before the world's rails load): never overlapping is enough
                    if (edge == painted)
                        Check(tag + "the read-out keeps a legible size (" + fit.ToString("F2") + " of full)", fit >= TopBand.ReadoutMinScale);

                    bool full = Mathf.Abs(home.width - PauseQuickActions.ButtonSize * s) < .01f &&
                                Mathf.Abs(replay.height - PauseQuickActions.ButtonSize * s) < .01f;
                    Check(tag + "the buttons are never scaled down (" + home.width.ToString("F0") + " px)", full);
                    if (shape.pxPerPoint > 0f)
                        Check(tag + "each button is a " + (home.width / shape.pxPerPoint).ToString("F1") + " pt/dp tap target (>= " + shape.minPoints + ")",
                              home.width / shape.pxPerPoint >= shape.minPoints && replay.width / shape.pxPerPoint >= shape.minPoints);
                }
            }

            // what each cutout does to the band
            var cases = CutoutCases(size);
            var none = TopBand.FrameFor(cases[0].safe, size, painted, cases[0].cutouts);
            var cornerOpen = TopBand.FrameFor(cases[7].safe, size, painted, cases[7].cutouts);
            var holeOpen = TopBand.FrameFor(cases[6].safe, size, painted, cases[6].cutouts);
            Check(shape.name + ": a corner punch-hole out over the rail leaves the band where it was", cornerOpen.Same(none));
            Check(shape.name + ": a centre punch-hole drops the whole band below it (" + (none.top - holeOpen.top).ToString("F0") + " px), not around it",
                  holeOpen.top < cases[6].cutouts[0].yMin && holeOpen.left == none.left && holeOpen.right == none.right);
            var fallBand = TopBand.FrameFor(cases[5].safe, size, painted, cases[5].cutouts);
            Check(shape.name + ": waterfall edges never push the band outside the rails", fallBand.left >= none.left && fallBand.right <= none.right);
            // rounded corners: the band's outer top corners are further in than any corner radius (<= 10% of the width)
            Check(shape.name + ": the band's ends are clear of rounded corners (" + (none.left / size.x).ToString("P0") + " in)",
                  none.left >= size.x * .1f && size.x - none.right >= size.x * .1f);
        }
        Debug.Log("[NF] top band: the read-out's smallest scale is " + worstFit.ToString("F3") + " at " + worstAt);

        // No rails (edge 0): the band spans the safe area, as it did.
        {
            var size = new Vector2(1080, 2520);
            var safe = new Rect(0, 0, 1080, 2520);
            float s = PauseQuickActions.CanvasScaleFor(size);
            var open = TopBand.FrameFor(safe, size, 0f, null);
            Check("no rails: the band spans the safe area less the edge margin",
                  Mathf.Approximately(open.left, PauseQuickActions.EdgeMargin * s) &&
                  Mathf.Approximately(open.right, 1080f - PauseQuickActions.EdgeMargin * s));
        }

        // The built buttons sit where the layout says.
        {
            var actionsGo = new GameObject("~PauseQuickActionsBandTest");
            var actions = actionsGo.AddComponent<PauseQuickActions>();
            actions.SendMessage("Start");
            var size = new Vector2(1080, 2520);
            var c = CutoutCases(size)[3];
            var band = TopBand.FrameFor(c.safe, size, painted, c.cutouts);
            actions.PlaceFor(c.safe, size, band);
            float s = PauseQuickActions.CanvasScaleFor(size);
            var replayGo = SceneUtil.FindAny("replayQuickAction");
            var leaveGo = SceneUtil.FindAny("leaveQuickAction");
            bool built = replayGo != null && leaveGo != null;
            if (built)
            {
                var r0 = replayGo.GetComponent<RectTransform>();
                var r1 = leaveGo.GetComponent<RectTransform>();
                // anchored to the safe area's top-right corner, in canvas units
                Rect want0 = PauseQuickActions.ButtonScreenRect(band, size, 0), want1 = PauseQuickActions.ButtonScreenRect(band, size, 1);
                built = Mathf.Abs(c.safe.xMax + r0.anchoredPosition.x * s - want0.xMax) < .5f &&
                        Mathf.Abs(c.safe.yMax + r0.anchoredPosition.y * s - want0.yMax) < .5f &&
                        Mathf.Abs(c.safe.xMax + r1.anchoredPosition.x * s - want1.xMax) < .5f &&
                        Mathf.Abs(c.safe.yMax + r1.anchoredPosition.y * s - want1.yMax) < .5f &&
                        r0.sizeDelta == new Vector2(PauseQuickActions.ButtonSize, PauseQuickActions.ButtonSize) &&
                        r0.pivot == Vector2.one && r1.pivot == Vector2.one;
            }
            Check("the built quick actions sit where the band says (tap rect = drawn rect)", built);
            Object.DestroyImmediate(actionsGo);
            var holder = GameObject.Find("RunActionCanvas");
            if (holder != null) Object.DestroyImmediate(holder);
        }

        BossRails.Reset();
        Object.DestroyImmediate(go);
    }

    // ---- 4: ultimate power --------------------------------------------------

    static void UltimatePowerAutoFiresAndSpeedsUpFromPickups()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        // Gold Warden: a top-tier hull, the only kind whose ultimate is the
        // cinematic volley (ShipLoadoutTable).
        PlayerPrefs.SetInt("spawnShip", 7);

        var shipGo = new GameObject("ship7", typeof(SpriteRenderer));
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
        // (a float tolerance: 0.4 s off a ~45 s timer is not exact in binary,
        // so Mathf.Approximately's ~1e-6 relative epsilon misses it)
        Check("collecting star dust shaves time off the countdown",
              afterDust < before && Mathf.Abs((before - afterDust) - controller.secondsPerDust) < 1e-4f);

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
