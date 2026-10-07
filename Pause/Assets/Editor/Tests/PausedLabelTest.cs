using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The tiny logo-style "PAUSED" caption above the in-run pause icon
// (PausedLabel): it exists in both run scenes, reads PAUSED, sits centred
// just above the icon, shows and pops only with the icon, takes no touches,
// and stays clear of the HUD, the quick actions, the safe area's edges and
// the ship's hearts / UI slots at every test aspect. The home logo it is
// lettered after must stay byte-for-byte as on master.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod PausedLabelTest.Run
public static class PausedLabelTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PL] PASS  " : "[PL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    // git blob id of Assets/Art/UI/Title/pause_title_2.png on master.
    const string LogoBlob = "5aa3474211729bf6f1ffc48828b6ea7573f97285";
    const string LogoPath = "Assets/Art/UI/Title/pause_title_2.png";

    static readonly (string name, Vector2 size, Rect safe)[] Screens =
    {
        ("9:16 1080x1920",         new Vector2(1080, 1920), new Rect(0, 0, 1080, 1920)),
        ("Pixel 1080x2400 cutout", new Vector2(1080, 2400), new Rect(0, 0, 1080, 2400 - 118)),
        ("iPhone 15 Pro Max",      new Vector2(1290, 2796), new Rect(0, 102, 1290, 2796 - 102 - 177)),
        ("Z Flip 1080x2520",       new Vector2(1080, 2520), new Rect(0, 0, 1080, 2520 - 96)),
        ("9:22 1080x2640",         new Vector2(1080, 2640), new Rect(0, 48, 1080, 2640 - 48 - 96)),
        ("9:24 1080x2880",         new Vector2(1080, 2880), new Rect(0, 48, 1080, 2880 - 48 - 120)),
        ("Z Fold6 cover 968x2376", new Vector2(968, 2376),  new Rect(0, 0, 968, 2376 - 90)),
        ("Z Fold5 cover 904x2316", new Vector2(904, 2316),  new Rect(0, 0, 904, 2316 - 90)),
        ("side inset 1080x2340",   new Vector2(1080, 2340), new Rect(60, 40, 1080 - 120, 2340 - 140)),
        ("iPad 1536x2048",         new Vector2(1536, 2048), new Rect(0, 0, 1536, 2048)),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            LogoUntouched();
            Rect labelWorld = default;
            foreach (string scene in new[] { "gameS1", "tutorialS5" })
            {
                Rect r = SceneChecks(scene);
                if (scene == "gameS1") labelWorld = r;
            }
            if (labelWorld.width > 0f) ClearOfTheShip(labelWorld);
        }
        finally
        {
            buttonClicks.playerDied = false;
            ShipUiSlots.ScreenOverride = null;
            Time.timeScale = 1f;
        }
        Debug.Log("[PL] failures: " + fails);
        return fails;
    }

    static void LogoUntouched()
    {
        byte[] bytes = File.ReadAllBytes(LogoPath);
        string id;
        using (var sha = SHA1.Create())
        {
            byte[] head = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
            var all = new byte[head.Length + bytes.Length];
            head.CopyTo(all, 0);
            bytes.CopyTo(all, head.Length);
            var sb = new StringBuilder();
            foreach (byte b in sha.ComputeHash(all)) sb.Append(b.ToString("x2"));
            id = sb.ToString();
        }
        Check("pause_title_2.png is byte-identical to master (" + id + ")", id == LogoBlob);
    }

    // Highest opaque row of the two glow variants, as a fraction of the
    // sprite's height above its centre (read from the PNGs themselves).
    static float OpaqueTopFraction()
    {
        float best = 0f;
        foreach (string v in new[] { "a", "b" })
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/PauseGlow/pausedGlow_" + v + ".png"));
            var px = tex.GetPixels32();
            int top = -1;
            for (int y = tex.height - 1; y >= 0 && top < 0; y--)
                for (int x = 0; x < tex.width; x++)
                    if (px[y * tex.width + x].a > 40) { top = y; break; }
            best = Mathf.Max(best, (top + 1f) / tex.height - .5f);
            Object.DestroyImmediate(tex);
        }
        return best;
    }

    static Rect WorldRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    static Rect ToScreen(Rect world, Vector3 camPos, Vector2 screen)
    {
        float size = CameraFit.ComputeSize(5f, 2.85f, (int)screen.x, (int)screen.y);
        float halfW = size * screen.x / screen.y;
        float px = screen.y / (2f * size);
        float x0 = (world.xMin - camPos.x + halfW) * px, x1 = (world.xMax - camPos.x + halfW) * px;
        float y0 = (world.yMin - camPos.y + size) * px, y1 = (world.yMax - camPos.y + size) * px;
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    static Rect SceneChecks(string scene)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/" + scene + ".unity", OpenSceneMode.Single);
        buttonClicks.playerDied = false;
        var icon = SceneUtil.FindAny("paused");
        Check(scene + ": pause icon exists", icon != null);
        if (icon == null) return default;
        var iconSr = icon.GetComponent<SpriteRenderer>();

        // Through the real wiring: moveStarsBackground builds it on Start.
        var stars = Object.FindFirstObjectByType<moveStarsBackground>(FindObjectsInactive.Include);
        Check(scene + ": moveStarsBackground present", stars != null);
        PausedLabel label = null;
        if (stars != null)
        {
            var start = typeof(moveStarsBackground).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
            // Its star-material grab is irrelevant here; build the label the
            // same way Start does without leaking an edit-mode material.
            Check(scene + ": moveStarsBackground.Start attaches the label",
                  File.ReadAllText("Assets/Scripts/UI/moveStarsBackground.cs").Contains("PausedLabel.AttachTo(pauseText)") && start != null);
        }
        label = PausedLabel.AttachTo(icon);
        Check(scene + ": attaching twice reuses the one label", PausedLabel.AttachTo(icon) == label &&
              icon.GetComponentsInChildren<PausedLabel>(true).Length == 1);
        Check(scene + ": label exists", label != null);
        if (label == null) return default;
        Check(scene + ": label is a child of the pause icon", label.transform.parent == icon.transform);
        Check(scene + ": label reads PAUSED, in capitals like the logo", label.Text != null && label.Text.text == "PAUSED");
        Check(scene + ": label uses the game's Orbitron face",
              label.Text != null && label.Text.font != null && label.Text.font.name.StartsWith("Orbitron"));

        // Logo treatment.
        var go = label.Text.gameObject;
        var grad = go.GetComponent<PausedLabelGradient>();
        var outline = go.GetComponent<Outline>();
        Shadow drop = null;
        foreach (var s in go.GetComponents<Shadow>()) if (!(s is Outline)) drop = s;
        Check(scene + ": logo red fill, lighter at the top", grad != null && grad.top.r > grad.bottom.r &&
              grad.bottom.r > .75f && grad.bottom.g < .1f);
        Check(scene + ": white contour like the logo", outline != null && outline.effectColor == Color.white);
        Check(scene + ": muted red drop shadow knocked down-right", drop != null &&
              drop.effectDistance.x > 0f && drop.effectDistance.y < 0f && drop.effectColor.r > drop.effectColor.g);
        var effects = go.GetComponents<BaseMeshEffect>();
        Check(scene + ": fill tinted before contour and shadow", effects.Length == 3 &&
              effects[0] is PausedLabelGradient && effects[1] is Outline && !(effects[2] is Outline));

        // Placement relative to the icon.
        Canvas.ForceUpdateCanvases();
        label.Advance(1f);
        Rect lw = WorldRect(label.Box);
        float iconTop = iconSr.bounds.center.y + OpaqueTopFraction() * iconSr.bounds.size.y;
        float gap = lw.yMin - iconTop;
        Check(scene + ": label centred on the icon (dx " + (lw.center.x - iconSr.bounds.center.x).ToString("F4") + ")",
              Mathf.Abs(lw.center.x - iconSr.bounds.center.x) < .005f);
        Check(scene + ": label sits above the icon's drawing, small gap (" + gap.ToString("F3") + " world)",
              gap >= 0f && gap <= .08f);
        Check(scene + ": label is very small (" + lw.width.ToString("F2") + " x " + lw.height.ToString("F2") + " world)",
              lw.height <= .2f && lw.width <= .8f);
        Check(scene + ": label draws in the icon's sorting slot, under the ship",
              label.GetComponent<Canvas>().sortingOrder == iconSr.sortingOrder &&
              label.GetComponent<Canvas>().sortingLayerID == iconSr.sortingLayerID);

        // No input.
        bool raycastable = false;
        foreach (var g in label.GetComponentsInChildren<Graphic>(true)) raycastable |= g.raycastTarget;
        Check(scene + ": no raycast targets", !raycastable);
        Check(scene + ": no raycaster on or above it", label.GetComponentInParent<BaseRaycaster>(true) == null &&
              label.GetComponentsInChildren<BaseRaycaster>(true).Length == 0);
        Check(scene + ": canvas group lets touches through", !label.GetComponent<CanvasGroup>().blocksRaycasts);

        // Shown only with the icon, popping in on its ticks.
        if (stars != null)
        {
            var show = typeof(moveStarsBackground).GetMethod("showPaused", BindingFlags.NonPublic | BindingFlags.Instance);
            var f = typeof(moveStarsBackground).GetField("pauseText", BindingFlags.Public | BindingFlags.Instance);
            f.SetValue(stars, icon);
            show.Invoke(stars, new object[] { false });
            Check(scene + ": hidden while flying", !icon.activeSelf && !label.gameObject.activeInHierarchy);
            show.Invoke(stars, new object[] { true });
            label.SendMessage("OnEnable"); // edit mode doesn't fire it on SetActive
            Check(scene + ": shown while paused", icon.activeSelf && label.gameObject.activeInHierarchy);
            Check(scene + ": pops in squashed", label.PopStep == 0);
            label.Advance(PausedOverlayAnim.Tick * 1.01f);
            Check(scene + ": then stretched one icon tick later", label.PopStep == 1);
            label.Advance(PausedOverlayAnim.Tick * 1.01f);
            Check(scene + ": then rests, in step with the icon", label.PopStep == 2 &&
                  label.Text.transform.localScale == Vector3.one);
            show.Invoke(stars, new object[] { false });
            Check(scene + ": hides again on resume", !label.gameObject.activeInHierarchy);
            show.Invoke(stars, new object[] { true });
            label.SendMessage("OnEnable");
            Check(scene + ": re-pops on the next pause", label.PopStep == 0);
            label.Advance(1f);
        }
        icon.SetActive(true);

        // Clear of the HUD and the quick actions, inside the safe area.
        var hudGo = new GameObject("~PausedLabelHud");
        var styler = hudGo.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        var root = styler.HudRoot;
        Check(scene + ": HUD root found", root != null);
        var cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : new Vector3(0f, 0f, -10f);
        if (root != null)
        {
            var canvas = root.parent.GetComponent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            Vector2 hudSize = root.rect.size;
            foreach (var s in Screens)
            {
                float hudScale = HudStyler.HudCanvasScale(canvas, scaler, s.size);
                Rect hud = HudStyler.HudScreenRect(s.safe, s.size, hudScale, hudSize);
                Rect actions = PauseQuickActions.ScreenRectFor(s.safe, s.size);
                Rect ls = ToScreen(lw, camPos, s.size);
                string tag = scene + " @ " + s.name + ": ";
                Check(tag + "label clear of the HUD", !ls.Overlaps(hud));
                Check(tag + "label clear of the quick actions", !ls.Overlaps(actions));
                Check(tag + "label inside the safe area", s.safe.Contains(ls.min) && s.safe.Contains(ls.max));
                Check(tag + "label still legible (" + ls.height.ToString("F0") + "px tall)", ls.height >= 14f);
            }
        }
        Object.DestroyImmediate(hudGo);
        return lw;
    }

    // The ship sits at its spawn point when the run first pauses: its hearts
    // and its ultimate's slots (gun, charge indicator, secret meter) must not
    // run into the caption there, in any pose.
    static void ClearOfTheShip(Rect label)
    {
        ShipUiSlots.ScreenOverride = () => Rect.MinMaxRect(-2.85f, -5f, 2.85f, 5f);
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        var lb = new Bounds(new Vector3(label.center.x, label.center.y, 0f), new Vector3(label.width, label.height, 10f));
        foreach (int id in ShipId.All)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            camGo.GetComponent<Camera>().orthographic = true;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            var spawn = spawnShips.SpawnPoint;
            var rig = HeartsPlacementTest.Build(id, new Vector3(spawn.x, spawn.y, 0f));
            string hit = null;
            foreach (string pose in HeartsPlacementTest.Poses(rig))
            {
                var parts = new List<Bounds>(HeartsPlacementTest.HeartBounds(rig));
                parts.Add(rig.ship.GetComponent<SpriteRenderer>().bounds);
                if (rig.indicator != null && rig.indicator.View != null)
                    parts.Add(rig.indicator.View.GetComponent<SpriteRenderer>().bounds);
                if (rig.meter != null) parts.Add(rig.meter.GetComponent<SpriteRenderer>().bounds);
                foreach (var b in parts)
                {
                    var flat = new Bounds(new Vector3(b.center.x, b.center.y, 0f), new Vector3(b.size.x, b.size.y, 10f));
                    if (flat.size.x > 0f && flat.Intersects(lb)) { hit = pose; break; }
                }
                if (hit != null) break;
            }
            Check("ship " + ShipId.KeyOf(id) + " at spawn: hearts and ship UI clear of the label" +
                  (hit != null ? " -- hit while " + hit : ""), hit == null);
            HeartsPlacementTest.Teardown(rig);
        }
    }
}
