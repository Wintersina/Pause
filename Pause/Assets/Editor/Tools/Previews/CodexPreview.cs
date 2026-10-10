using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Codex contact sheets without a build, for reviewing the whole catalogue:
//
//   Unity -batchmode -projectPath <abs>/Pause -executeMethod CodexPreview.Run
//
// Writes into $PAUSE_CODEX_PREVIEW_DIR (default /private/tmp):
//   codex-<tab>-<state>.png         the tab's whole scrolling list in one
//                                   tall image (the panel laid out tall
//                                   enough to hold every card)
//   codex-detail-<tab>-<state>.png  every entry's detail view of that tab,
//                                   as a grid of small renders
// for <state> locked (a fresh profile: only the Pilot's Log and the starter
// ship, bosses unlisted) and unlocked (developer mode: everything, bosses
// included). PlayerPrefs are restored afterwards (TestHarness.Sandbox).
public static class CodexPreview
{
    const int Width = 800;
    const int DetailHeight = 1280;
    const int TileColumns = 6;
    const int TileScale = 2;   // detail tiles at 1/2 size
    static readonly Color Backdrop = new Color(.03f, .03f, .06f, 1f);

    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("PAUSE_CODEX_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "/private/tmp";
        Directory.CreateDirectory(dir);
        int code = 0;
        try
        {
            using (new TestHarness.Sandbox())
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (bool unlocked in new[] { false, true })
                {
                    DeveloperUnlocks.SetEnabled(unlocked);
                    if (!unlocked)
                    {
                        PlayerPrefs.DeleteKey(Codex.PrefsKey);
                        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
                        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(i));
                    }
                    Codex.Reload();
                    var panel = CodexPanel.Open(null);
                    panel.Refresh();
                    panel.SkipAnimations();
                    string state = unlocked ? "unlocked" : "locked";
                    foreach (var tab in CodexPanel.Tabs)
                    {
                        string label = CodexPanel.CategoryLabel(tab).ToLowerInvariant();
                        TabSheet(panel, tab, Path.Combine(dir, "codex-" + label + "-" + state + ".png"));
                        DetailSheet(panel, tab, Path.Combine(dir, "codex-detail-" + label + "-" + state + ".png"));
                    }
                    panel.Close();
                    panel.SkipAnimations();
                }
                DeveloperUnlocks.SetEnabled(false);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    // The ACHIEVEMENTS tab on a 1170 x 2532 phone (800 x 1732 canvas units), in the
    // states a player sees, into $PAUSE_CODEX_PREVIEW_DIR (default /private/tmp):
    //   achievements-<n>-<what>.png
    //   Unity -batchmode -projectPath <abs>/Pause -executeMethod CodexPreview.RunAchievements
    public const int PhoneW = 1170, PhoneH = 2532;

    public static void RunAchievements()
    {
        string dir = Environment.GetEnvironmentVariable("PAUSE_CODEX_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "/private/tmp";
        Directory.CreateDirectory(dir);
        int code = 0;
        try
        {
            using (new TestHarness.Sandbox())
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                DeveloperUnlocks.SetEnabled(false);
                PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
                for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(i));
                PlayerPrefs.SetString("boughtship1", "True");
                PlayerPrefs.SetString(Codex.PrefsKey, "");
                Codex.Reload();
                WorldManager.TideEnabled = false;
                float sf = PhoneW / 800f;

                // 1. a fresh profile: everything locked
                AchievementStore.ResetAll();
                PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
                PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 240f);
                var panel = CodexPanel.Open(null);
                panel.SkipAnimations();
                Shot(panel, Path.Combine(dir, "achievements-1-fresh.png"), sf, () => { });

                // 2. a mid-game profile: some collected, some waiting, counters part-way
                string[] claimed = { "meta_first_flight", "world_frost_reached", "world_verdant_reached", "kills_100", "stars_150", "ship_first", "pause_first", "elite_first" };
                string[] waiting = { "meta_logged_on", "boss_space", "rocks_500", "codex_10", "score_10k", "skin_first" };
                foreach (string id in claimed.Concat(waiting)) AchievementStore.Unlock(AchievementCatalog.Find(id));
                foreach (string id in claimed) AchievementStore.Claim(AchievementCatalog.Find(id));
                AchievementStore.SetCounter("kills", 640);
                AchievementStore.SetCounter("stars", 780);
                AchievementStore.SetCounter("elites", 4);
                AchievementStore.SetCounter("deaths", 6);
                AchievementStore.SetCounter("blinks", 37);
                AchievementStore.SetCounter("spent", 3200);
                AchievementStore.SetCounter("ships", 5);
                AchievementStore.SetCounter("codex", 31);
                AchievementStore.SetCounter("elite_w0", 2);
                AchievementStore.SetCounter("best_score", 3100);
                AchievementStore.SetCounter("best_chain", 7);
                PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 640f);
                panel.Refresh();
                var v = panel.Achievements;
                string[] names = { "top", "bosses", "combat", "collection" };
                int[] sections = { 0, 1, 3, 6 };
                for (int k = 0; k < sections.Length; k++)
                {
                    int sec = sections[k];
                    Shot(panel, Path.Combine(dir, "achievements-2" + (char)('a' + k) + "-grid-" + names[k] + ".png"), sf, () => v.SetScrollY(v.JumpTargetY(sec)));
                }
                // the whole list in one tall image
                panel.ApplyLayout(new Rect(-400f, -640f, 800f, 1280f));
                panel.ShowAchievements();
                v.Show(false);
                int tallH = Mathf.Clamp(Mathf.CeilToInt(1280f - v.ListRect.height + v.ContentHeight + 8f), 1280, 8000);
                Shot(panel, Path.Combine(dir, "achievements-2e-grid-full.png"), 1f, () => { }, 800, tallH);

                // 3. details: claimable, claimed, locked with progress, hidden
                string[] details = { "boss_space", "kills_100", "kills_1000", "deaths_100" };
                string[] what = { "claimable", "claimed", "locked-progress", "hidden" };
                for (int k = 0; k < details.Length; k++)
                {
                    string id = details[k];
                    Shot(panel, Path.Combine(dir, "achievements-3" + (char)('a' + k) + "-detail-" + what[k] + ".png"), sf, () => v.ShowDetail(AchievementCatalog.Find(id)));
                    v.CloseDetail();
                }
                panel.Close();
                panel.SkipAnimations();
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    // The Enemies grid with a few NEW dots (and dots on the HAZARDS / ATOMS / ACHIEVEMENTS tabs)
    // on a 1080 x 2340 phone: codex_new_dots.png in dir.
    public static void NewDotsShot(string dir)
    {
        using (new TestHarness.Sandbox())
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DeveloperUnlocks.SetEnabled(false);
            PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
            PlayerPrefs.SetString(Codex.PrefsKey, "");
            PlayerPrefs.DeleteKey(Codex.NewKey); PlayerPrefs.DeleteKey(Codex.AckKey);
            AchievementStore.ResetAll();
            PlayerPrefs.SetInt(AchievementStore.SchemaKey, AchievementMigration.Schema);
            Codex.Reload();
            WorldManager.TideEnabled = false;
            var open = Codex.Entries.Where(e => e.category != CodexCategory.Log && !e.secret && !Codex.InFutureWorld(e)).ToList();
            foreach (var e in open.Where(e => e.category == CodexCategory.Enemies).Take(5)) Codex.Discover(e.id);
            // the first one has been looked at already: three NEW + one seen
            var seenOne = open.First(e => e.category == CodexCategory.Enemies);
            Codex.MarkSeen(seenOne.id);
            foreach (var e in open.Where(e => e.category == CodexCategory.Hazards).Take(1)) Codex.Discover(e.id);
            foreach (var e in open.Where(e => e.category == CodexCategory.Atoms).Take(1)) Codex.Discover(e.id);
            AchievementStore.Unlock(AchievementCatalog.All.First(d => AchievementCatalog.IsActive(d)));
            var panel = CodexPanel.Open(null);
            panel.SkipAnimations();
            float sf = PhoneW / 800f;
            var tex = Capture(panel, 1080, 2340, () =>
            {
                panel.ApplyLayout(new Rect(-400f, -866f, 800f, 1733f));
                panel.ShowCategory(CodexCategory.Enemies);
                panel.SkipAnimations();
            }, 1, 1080f / 800f);
            File.WriteAllBytes(Path.Combine(dir, "codex_new_dots.png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            panel.Close();
            panel.SkipAnimations();
        }
    }

    static void Shot(CodexPanel panel, string path, float sf, Action setup, int w = PhoneW, int h = PhoneH)
    {
        var tex = Capture(panel, w, h, () =>
        {
            panel.ShowAchievements();
            panel.Achievements.Show(false);
            setup();
            panel.SkipAnimations();
        }, 1, sf);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        Debug.Log("[CODEX-PREVIEW] " + path);
    }

    // The triple-tap death of some detail entries as a strip of frames (one
    // row per entry, one column per moment):
    //   PAUSE_CODEX_DEATH_IDS=id1,id2  PAUSE_CODEX_PREVIEW_DIR=<dir>
    //   Unity -batchmode -projectPath <abs>/Pause -executeMethod CodexPreview.RunDeath
    static readonly float[] DeathMoments = { 0f, .05f, .1f, .2f, .35f, .55f, .8f, 1.1f, 1.45f, 1.7f, 2.0f };

    public static void RunDeath()
    {
        string dir = Environment.GetEnvironmentVariable("PAUSE_CODEX_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "/private/tmp";
        string ids = Environment.GetEnvironmentVariable("PAUSE_CODEX_DEATH_IDS");
        Directory.CreateDirectory(dir);
        int code = 0;
        try
        {
            using (new TestHarness.Sandbox())
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                DeveloperUnlocks.SetEnabled(true);
                Codex.Reload();
                var panel = CodexPanel.Open(null);
                panel.Refresh();
                panel.SkipAnimations();
                var list = new System.Collections.Generic.List<CodexEntry>();
                foreach (string id in (ids ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var e = Codex.Find(id.Trim());
                    if (e != null) list.Add(e); else Debug.LogWarning("[CODEX-PREVIEW] no entry " + id);
                }
                int tw = Width / 2, th = DetailHeight / 2;
                // the art frame sits in the top half of the detail view
                int cropY = th * 11 / 100, cropH = th * 46 / 100;
                var sheet = new Texture2D(DeathMoments.Length * tw, list.Count * cropH, TextureFormat.RGBA32, false);
                for (int r = 0; r < list.Count; r++)
                {
                    var e = list[r];
                    for (int c = 0; c < DeathMoments.Length; c++)
                    {
                        float moment = DeathMoments[c];
                        var tile = Capture(panel, Width, DetailHeight, () =>
                        {
                            panel.ShowGrid(); panel.SkipAnimations();
                            panel.ShowDetail(e); panel.SkipAnimations();
                            for (int k = 0; k < 6; k++) panel.TickAnimations(1f / 30f);
                            float now = 1000f + r * 100f + c * 10f;
                            panel.TapDetailArt(now); panel.TapDetailArt(now + .1f);
                            Debug.Log("[CODEX-PREVIEW] " + e.id + " died: " + panel.TapDetailArt(now + .2f) + " kind=" + panel.LastDeathKind);
                            float left = moment;
                            while (left > 0f) { float dt = Mathf.Min(1f / 60f, left); panel.TickAnimations(dt); left -= dt; }
                        }, 2);
                        var px = tile.GetPixels(0, th - cropY - cropH, tw, cropH);
                        sheet.SetPixels(c * tw, (list.Count - 1 - r) * cropH, tw, cropH, px);
                        UnityEngine.Object.DestroyImmediate(tile);
                    }
                }
                sheet.Apply();
                string path = Path.Combine(dir, "codex_death_strip.png");
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                Debug.Log("[CODEX-PREVIEW] " + path);
                panel.Close();
                panel.SkipAnimations();
                DeveloperUnlocks.SetEnabled(false);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    // The whole list of a tab in one image: the panel laid out just tall
    // enough for every card row to sit inside the list unscrolled.
    static void TabSheet(CodexPanel panel, CodexCategory tab, string path)
    {
        panel.ShowGrid();
        panel.ApplyLayout(new Rect(-Width * .5f, -DetailHeight * .5f, Width, DetailHeight));
        panel.ShowCategory(tab);
        panel.SkipAnimations();
        var l = panel.CurrentLayout;
        float list = panel.Sectioned ? l.list.height : l.body.height;
        int height = Mathf.Clamp(Mathf.CeilToInt(DetailHeight - list + panel.Content.sizeDelta.y + 8f), DetailHeight, 8000);
        var tex = Capture(panel, Width, height, () =>
        {
            panel.ShowCategory(tab);
            panel.SetScrollY(0f);
            panel.SkipAnimations();
        });
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        Debug.Log("[CODEX-PREVIEW] " + path + " (" + panel.VisibleCards + " cards)");
    }

    // Every entry's detail view in a grid of half-size tiles.
    static void DetailSheet(CodexPanel panel, CodexCategory tab, string path)
    {
        panel.ShowGrid();
        panel.ShowCategory(tab);
        panel.SkipAnimations();
        int n = panel.VisibleCards;
        if (n == 0) return;
        var entries = new CodexEntry[n];
        for (int i = 0; i < n; i++) entries[i] = panel.CardEntry(i);

        int tw = Width / TileScale, th = DetailHeight / TileScale;
        int cols = Mathf.Min(TileColumns, n), rows = (n + cols - 1) / cols;
        var sheet = new Texture2D(cols * tw, rows * th, TextureFormat.RGBA32, false);
        var fill = new Color[sheet.width * sheet.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = Backdrop;
        sheet.SetPixels(fill);

        for (int i = 0; i < n; i++)
        {
            var e = entries[i];
            var tile = Capture(panel, Width, DetailHeight, () =>
            {
                panel.ShowDetail(e);
                panel.SkipAnimations();
                // a beat into its loop, so the art isn't always on frame 0
                for (int k = 0; k < 12; k++) panel.TickAnimations(1f / 30f);
            }, TileScale);
            int col = i % cols, row = i / cols;
            sheet.SetPixels(col * tw, (rows - 1 - row) * th, tw, th, tile.GetPixels());
            UnityEngine.Object.DestroyImmediate(tile);
            panel.ShowGrid();
            panel.SkipAnimations();
        }
        sheet.Apply();
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
        Debug.Log("[CODEX-PREVIEW] " + path + " (" + n + " entries)");
    }

    // Renders the panel's canvas at w x h pixels (sf pixels per canvas unit, 1 by default),
    // laid out for that whole area, after `setup`; downscaled by `scale`.
    internal static Texture2D Capture(CodexPanel panel, int w, int h, Action setup, int scale = 1, float sf = 1f)
    {
        var canvas = panel.GetComponent<Canvas>();
        var scaler = panel.GetComponent<CanvasScaler>();
        bool scalerOn = scaler != null && scaler.enabled;
        if (scaler != null) scaler.enabled = false;
        var mode = canvas.renderMode;
        var oldCam = canvas.worldCamera;
        float plane = canvas.planeDistance;
        float factor = canvas.scaleFactor;

        var camGo = new GameObject("~CodexPreviewCam");
        camGo.transform.position = new Vector3(9000f, 9000f, -10f);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Backdrop;
        cam.enabled = false;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 1f;
        canvas.scaleFactor = sf;
        Canvas.ForceUpdateCanvases();
        panel.ApplyLayout(new Rect(-w / sf * .5f, -h / sf * .5f, w / sf, h / sf));
        setup();
        Canvas.ForceUpdateCanvases();
        cam.Render();

        int ow = w / scale, oh = h / scale;
        var src = rt;
        RenderTexture small = null;
        if (scale > 1)
        {
            small = new RenderTexture(ow, oh, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(rt, small);
            src = small;
        }
        var prev = RenderTexture.active;
        RenderTexture.active = src;
        var tex = new Texture2D(ow, oh, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, ow, oh), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        canvas.renderMode = mode;
        canvas.worldCamera = oldCam;
        canvas.planeDistance = plane;
        canvas.scaleFactor = factor;
        if (scaler != null) scaler.enabled = scalerOn;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        if (small != null) UnityEngine.Object.DestroyImmediate(small);
        UnityEngine.Object.DestroyImmediate(camGo);
        Canvas.ForceUpdateCanvases();
        return tex;
    }
}
