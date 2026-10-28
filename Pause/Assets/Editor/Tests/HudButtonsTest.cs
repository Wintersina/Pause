using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The in-run home / replay buttons: never transparent (alpha 1 in code), the approved hollow designs
// untouched, and a symmetrical, score-board-centred layout that stacks on narrow phones.
//
// "Narrow" is PauseQuickActions.Vertical: the pair side by side would shrink the score board below
// TopBand.ReadoutMinScale; then the pair stands in one column (home above restart) at the band's right end.
// Side by side, the pair mirrors the score board about the screen's centre line and is centred on the
// board's vertical middle.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HudButtonsTest.Run
public static class HudButtonsTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        if (ok) Debug.Log("[HBT] PASS  " + what);
        else { Debug.Log("[HBT] FAIL  " + what); fails++; }
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        Designs();
        Baseline();
        Neon();
        Layout();
        Debug.Log("[HBT] failures: " + fails);
        return fails;
    }

    // The hollow home / replay designs are untouched (md5 of the PNGs as on master).
    static void Designs()
    {
        var want = new System.Collections.Generic.Dictionary<string, string>
        {
            { "Assets/Art/Resources/HudSkins/hud_home.png", "3ce57069b8cba6bfbec6a857c664c23e" },
            { "Assets/Art/Resources/HudSkins/hud_replay.png", "0569bdc34cf5fa5b1ca8940481c3f919" },
        };
        foreach (var kv in want)
        {
            string got = "missing";
            if (File.Exists(kv.Key))
                using (var md5 = System.Security.Cryptography.MD5.Create())
                    got = System.BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(kv.Key))).Replace("-", "").ToLowerInvariant();
            Check(kv.Key + " is the approved hollow design", got == kv.Value);
        }
        Check("glyph alpha constant is 1", PauseQuickActions.GlyphAlpha >= .999f);
        for (int w = 0; w < HudSkin.WorldCount; w++)
            Check(WorldManager.Worlds[w].displayName + ": icon tint is fully opaque", HudSkin.IconTint(w).a >= .999f);
    }


    // ---- Frost and Verdant: neon glyphs that read against the (brighter, cloud-shadowed) backdrops ----

    static bool Same(Color a, Color32 b) { return ((Color32)a).Equals(b); }

    static float Luma(Color c)
    {
        return .2126f * Mathf.GammaToLinearSpace(c.r) + .7152f * Mathf.GammaToLinearSpace(c.g) + .0722f * Mathf.GammaToLinearSpace(c.b);
    }
    static float Contrast(float a, float b) { return (Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f); }

    // Mean / darkest 1% / lightest 1% luminance of a world's backdrop tiles (every variant's sky, mid and far), computed from the PNGs once.
    static readonly Dictionary<string, float[]> tileStats = new Dictionary<string, float[]>();
    static float[] BackdropTones(string world)
    {
        float[] cached;
        if (tileStats.TryGetValue(world, out cached)) return cached;
        var lums = new List<float>();
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        for (int v = 1; v <= 4; v++)
            foreach (var layer in new[] { "sky", "mid", "far" })
            {
                string path = "Assets/Art/Backgrounds/Resources/Worlds/" + world + "/Backdrop3/v" + v + "/" + layer + ".png";
                if (!File.Exists(path)) continue;
                tex.LoadImage(File.ReadAllBytes(path));
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i += 11)
                    if (px[i].a > 200) lums.Add(Luma(px[i]));
            }
        Object.DestroyImmediate(tex);
        lums.Sort();
        float sum = 0f; foreach (var l in lums) sum += l;
        int n = lums.Count;
        cached = n == 0 ? new float[] { .5f, .5f, .5f } : new[] { sum / n, lums[Mathf.Clamp((int)(n * .01f), 0, n - 1)], lums[Mathf.Clamp((int)(n * .99f), 0, n - 1)] };
        tileStats[world] = cached;
        return cached;
    }

    static void Neon()
    {
        // the new constants
        Check("Frost glyph is the neon lime constant", Same(HudSkin.IconTint(1), HudSkin.FrostNeon) && HudSkin.FrostNeon.Equals(new Color32(200, 255, 30, 255)));
        Check("Verdant glyph is the neon yellow constant", Same(HudSkin.IconTint(2), HudSkin.VerdantNeon) && HudSkin.VerdantNeon.Equals(new Color32(255, 236, 0, 255)));
        // every other world keeps its tint and halo exactly
        var tints = new Dictionary<int, Color32>
        {
            { 0, new Color32(190, 240, 255, 255) }, { 3, new Color32(255, 232, 196, 255) }, { 4, new Color32(255, 255, 255, 255) },
            { 5, new Color32(228, 232, 255, 255) }, { 6, new Color32(255, 238, 176, 255) },
        };
        foreach (var kv in tints)
        {
            Check(WorldManager.Worlds[kv.Key].displayName + ": tint unchanged", Same(HudSkin.IconTint(kv.Key), kv.Value));
            Color32 haloWas = kv.Key == 4 ? new Color32(6, 28, 30, 150) : new Color32(0, 0, 0, 0);
            Check(WorldManager.Worlds[kv.Key].displayName + ": halo unchanged", Same(HudSkin.IconHalo(kv.Key), haloWas));
            Check(WorldManager.Worlds[kv.Key].displayName + ": halo reach unchanged", Mathf.Approximately(HudSkin.IconHaloDistance(kv.Key), 1.5f));
        }
        // fully opaque, hollow glyph (alpha 1), a dark soft halo hugging the stroke, no backing graphic
        foreach (int w in new[] { 1, 2 })
        {
            string name = WorldManager.Worlds[w].displayName;
            Color tint = HudSkin.IconTint(w), halo = HudSkin.IconHalo(w);
            Check(name + ": neon glyph is fully opaque", tint.a >= .999f && PauseQuickActions.GlyphAlpha >= .999f);
            Check(name + ": neon is saturated and bright (not a pastel or a dark tint)", Luma(tint) > .45f && Mathf.Max(tint.r, tint.g, tint.b) >= .9f && Mathf.Min(tint.r, tint.g, tint.b) <= .2f);
            Check(name + ": halo is dark and nearly opaque (hugs the strokes)", halo.a >= .8f && Luma(halo) < .02f);
            Check(name + ": halo reach is a thin stroke hug (<= 2.5 canvas units)", HudSkin.IconHaloDistance(w) > 1.5f && HudSkin.IconHaloDistance(w) <= 2.5f);
            Check(name + ": no pink or magenta outside Space", !(tint.r > .6f && tint.b > .5f && tint.g < .5f * Mathf.Min(tint.r, tint.b)));

            // contrast of the glyph against the backdrop's tones (tile art, cached): mean and darkest patches >= 4.5:1; the lightest
            // patches are held apart by the halo hugging the stroke (glyph against halo >= 4.5:1), and the glyph vs lightest >= 2:1
            float[] tones = BackdropTones(name);
            float g = Luma(tint);
            Check(name + ": glyph vs backdrop mean >= 4.5:1 (" + Contrast(g, tones[0]).ToString("0.0") + ")", Contrast(g, tones[0]) >= 4.5f);
            Check(name + ": glyph vs darkest 1% >= 4.5:1 (" + Contrast(g, tones[1]).ToString("0.0") + ")", Contrast(g, tones[1]) >= 4.5f);
            Check(name + ": glyph vs lightest 1% of the tile art >= 2:1 (" + Contrast(g, tones[2]).ToString("0.0") + ")", Contrast(g, tones[2]) >= 2f);
            Check(name + ": glyph vs its own halo >= 4.5:1 (" + Contrast(g, Luma(halo)).ToString("0.0") + ")", Contrast(g, Luma(halo)) >= 4.5f);
            Check(name + ": halo vs the lightest patch is kept out of the glyph's colour (halo darker than the lightest tone)", Luma(halo) < tones[2]);
        }
        // no backing disc / ring / plate: the glyph is one Image child (+ at most an Outline), and the source adds nothing else
        string src = File.ReadAllText("Assets/Scripts/UI/PauseQuickActions.cs");
        Check("PauseQuickActions adds only the Outline halo to the glyph (no backing disc / ring)", CountOf(src, "AddComponent<") == 2 && src.Contains("AddComponent<Outline>()"));
        Check("glyph child is a single Image (typeof(RectTransform), typeof(Image))", src.Contains("new GameObject(GlyphName, typeof(RectTransform), typeof(Image))"));
    }

    static int CountOf(string s, string what)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(what, i, System.StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
        return n;
    }

    // Drawn art bottoms on the 256 px tiles, measured from the PNGs' alpha (the constants in PauseQuickActions must match).
    static int ArtBottomRow(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        var px = tex.GetPixels32(); int w = tex.width, h = tex.height, bottom = -1;
        for (int y = 0; y < h && bottom < 0; y++)   // LoadImage rows run bottom-up
            for (int x = 0; x < w; x++) if (px[y * w + x].a > 0) { bottom = h - 1 - y; break; }
        Object.DestroyImmediate(tex);
        return bottom + 1;   // exclusive edge, like Image.getbbox
    }

    // Side by side, the replay's DRAWN bottom lines up with the home glyph's drawn bottom (<= 1 px).
    static void Baseline()
    {
        Check("home art bottom row is measured from the PNG", ArtBottomRow("Assets/Art/Resources/HudSkins/hud_home.png") == PauseQuickActions.HomeArtBottomRow);
        Check("replay art bottom row is measured from the PNG", ArtBottomRow("Assets/Art/Resources/HudSkins/hud_replay.png") == PauseQuickActions.ReplayArtBottomRow);
        foreach (var size in new[] { new Vector2(480, 854), new Vector2(720, 1280), new Vector2(1080, 2340), new Vector2(1080, 2520), new Vector2(2520, 1080), new Vector2(1536, 2048) })
        {
            var safe = new Rect(0, 0, size.x, size.y);
            var band = TopBand.FrameFor(safe, size, 0f, null);
            float s = PauseQuickActions.CanvasScaleFor(size);
            PauseQuickActions.LiveReadoutHeightPx = 0f;
            Rect home = PauseQuickActions.ButtonScreenRect(band, size, 1), replay = PauseQuickActions.ButtonScreenRect(band, size, 0);
            float glyph = PauseQuickActions.ButtonSize * PauseQuickActions.GlyphScale * s;
            // glyph rect centred on the tap square (replay shifted down by its drop); art bottom = rect bottom + (tile - row)/tile * size
            float homeBottom = home.center.y - glyph * .5f + (PauseQuickActions.GlyphTile - PauseQuickActions.HomeArtBottomRow) / (float)PauseQuickActions.GlyphTile * glyph;
            float replayBottom = replay.center.y - PauseQuickActions.ReplayGlyphDrop * s - glyph * .5f + (PauseQuickActions.GlyphTile - PauseQuickActions.ReplayArtBottomRow) / (float)PauseQuickActions.GlyphTile * glyph;
            bool column = PauseQuickActions.Vertical(band, size);
            if (!column)
                Check(size + ": drawn bottoms of home and replay share one baseline (" + Mathf.Abs(homeBottom - replayBottom).ToString("0.00") + " px)", Mathf.Abs(homeBottom - replayBottom) <= 1f);
            Check(size + ": the dropped replay glyph stays inside its tap square", PauseQuickActions.ReplayGlyphDrop * s + glyph * .5f <= replay.height * .5f);
        }
    }

    static bool Overlap(Rect a, Rect b) { return a.xMin < b.xMax - .5f && b.xMin < a.xMax - .5f && a.yMin < b.yMax - .5f && b.yMin < a.yMax - .5f; }

    static void Layout()
    {
        // 9:16 phones, 9:19.5 and 9:21 tall phones, 21:9 (Z Flip) and a 3:4 tablet
        var screens = new[] { new Vector2(480, 854), new Vector2(720, 1280), new Vector2(1080, 2340), new Vector2(1080, 2520),
                              new Vector2(720, 1680), new Vector2(1170, 2532), new Vector2(1536, 2048), new Vector2(2520, 1080) };
        var hudSize = new Vector2(351f, 131f);
        foreach (var size in screens)
        {
            var safe = new Rect(0, 0, size.x, size.y);
            float s = PauseQuickActions.CanvasScaleFor(size);
            foreach (float edge in new[] { 0f, BossRails.InnerEdge })
            {
                var band = TopBand.FrameFor(safe, size, edge, null);
                Vector2 pos; float fit;
                PauseQuickActions.LiveReadoutHeightPx = 0f;
                float hs = HudStyler.ScaleWithScreenSize(size, new Vector2(800f, 600f), UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, 0f);
                HudStyler.ComputeHudLayout(band, size, hs, hudSize, out pos, out fit);
                Rect read = HudStyler.HudScreenRect(band, size, hs, hudSize);
                PauseQuickActions.LiveReadoutHeightPx = read.height;
                Rect panel = PauseQuickActions.ScreenRectFor(band, size);
                Rect home = PauseQuickActions.ButtonScreenRect(band, size, 1), replay = PauseQuickActions.ButtonScreenRect(band, size, 0);
                bool column = PauseQuickActions.Vertical(band, size);
                PauseQuickActions.LiveReadoutHeightPx = 0f;
                string tag = size + " edge " + edge + (column ? " stacked" : " side by side") + ": ";

                Check(tag + "home and replay are the same size", Mathf.Abs(home.width - replay.width) < .01f && Mathf.Abs(home.height - replay.height) < .01f);
                Check(tag + "buttons do not overlap each other or the score board", !Overlap(home, replay) && !Overlap(home, read) && !Overlap(replay, read));
                Check(tag + "buttons stay inside the screen", home.xMin >= 0 && replay.xMax <= size.x && home.yMax <= size.y && replay.yMin >= 0);
                Check(tag + "buttons stay inside the band", Mathf.Min(home.xMin, replay.xMin) >= band.left - .5f && Mathf.Max(home.xMax, replay.xMax) <= band.right + .5f);
                Check(tag + "tap size >= 48 dp / 44 pt", home.width >= UiScale.MinTapPointsFor(false) * UiScale.PxPerPoint - 1f || home.width >= PauseQuickActions.ButtonSize * s - .01f);
                if (column)
                {
                    Check(tag + "stacked: one shared centre line, home above restart", Mathf.Abs(home.center.x - replay.center.x) < .5f && home.yMin >= replay.yMax - .5f);
                }
                else
                {
                    // mirrored about the pair's own centre, which mirrors the score board about the screen's centre line
                    float mid = (home.center.x + replay.center.x) * .5f;
                    Check(tag + "side by side: home and replay are mirrored about the pair's centre (equal margins in the slot)",
                          Mathf.Abs((home.xMin - panel.xMin) - (panel.xMax - replay.xMax)) < .5f && Mathf.Abs(mid - panel.center.x) < .5f);
                    Check(tag + "side by side: one shared horizontal line", Mathf.Abs(home.center.y - replay.center.y) < .5f);
                    Check(tag + "side by side: the pair mirrors the score board about the screen's centre line", Mathf.Abs((size.x - read.xMin) - panel.xMax) < 1.5f);
                    Check(tag + "side by side: centred on the score board's vertical middle",
                          Mathf.Abs(home.center.y - read.center.y) <= Mathf.Max(.5f, (home.height - read.height) * .5f + 1f));
                }
            }
        }
    }
}
