using System.IO;
using UnityEditor;
using UnityEngine;

// The per-world HUD skins: every world resolves a score board / button panel
// frame, FLIGHT COMPLETE panel and home / replay glyphs; the new textures are
// small (ASTC overrides, no mips, right 9-slice borders); and the home /
// replay panel mirrors the score board.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HudSkinTest.Run
public static class HudSkinTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        if (ok) Debug.Log("[HSK] PASS  " + what);
        else { Debug.Log("[HSK] FAIL  " + what); fails++; }
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        Check("seven worlds", HudSkin.WorldCount == 7);

        for (int w = 0; w < HudSkin.WorldCount; w++)
        {
            string name = WorldManager.Worlds[w].displayName;
            var paths = new[] { HudSkin.FramePath(w), HudSkin.ButtonPanelPath(w), HudSkin.DeathPanelPath(w), HudSkin.HomeGlyphPath(w), HudSkin.ReplayGlyphPath(w) };
            foreach (string path in paths)
            {
                var sprite = Resources.Load<Sprite>(path);
                Check(name + ": " + path + " loads", sprite != null);
                if (sprite == null) continue;
                bool glyph = path.EndsWith("glyph");
                bool own = w > 0 || !glyph;
                if (own)
                    Check(name + ": " + path + " is the world's own art", path.StartsWith("HudSkins/" + name + "/"));
                if (glyph) Check(name + ": " + path + " is square", Mathf.Approximately(sprite.rect.width, sprite.rect.height));
                CheckImport(name, path, sprite, !own);
            }
            Check(name + ": frame is distinct from the other worlds'", w == 0 || HudSkin.FramePath(w) != HudSkin.FramePath(0));
            Check(name + ": the HUD resolves its frame and glyphs", HudSkin.Frame(w) != null && HudSkin.ButtonPanel(w) != null && HudSkin.DeathPanel(w) != null &&
                  HudSkin.HomeGlyph(w) != null && HudSkin.ReplayGlyph(w) != null);
        }

        Layout();
        Debug.Log("[HSK] failures: " + fails);
        return fails;
    }

    static void CheckImport(string world, string path, Sprite sprite, bool original)
    {
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite.texture)) as TextureImporter;
        Check(world + ": " + path + " has an importer", importer != null);
        if (importer == null) return;
        Check(world + ": " + path + " has no mipmaps", !importer.mipmapEnabled);
        if (original) return;   // Space's glyphs are the original art, imported as they always were
        Check(world + ": " + path + " max size <= 1024", importer.maxTextureSize <= 1024);
        bool dp = path.EndsWith("dp_panel");
        var want = dp ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
        foreach (string platform in new[] { "Android", "iOS" })
        {
            var ps = importer.GetPlatformTextureSettings(platform);
            Check(world + ": " + path + " " + platform + " override is " + want, ps.overridden && ps.format == want);
        }
        if (path.EndsWith("hud_board") || path.EndsWith("hud_buttons"))
        {
            Check(world + ": " + path + " 9-slice border 50/60 at 200 ppu",
                  importer.spriteBorder == new Vector4(50, 60, 50, 60) && Mathf.Approximately(importer.spritePixelsPerUnit, 200f));
            Check(world + ": " + path + " is " + (path.EndsWith("hud_board") ? "702" : "340") + "x310",
                  Mathf.Approximately(sprite.rect.height, 310f) && Mathf.Approximately(sprite.rect.width, path.EndsWith("hud_board") ? 702f : 340f));
        }
        if (dp)
        {
            Check(world + ": " + path + " 9-slice border 100 at 83.33 ppu",
                  importer.spriteBorder == new Vector4(100, 100, 100, 100) && Mathf.Abs(importer.spritePixelsPerUnit - 83.33f) < .01f);
            Check(world + ": " + path + " is 600x900", Mathf.Approximately(sprite.rect.width, 600f) && Mathf.Approximately(sprite.rect.height, 900f));
        }
    }

    // The button panel mirrors the score board: same top edge and height,
    // symmetric about the screen's centre line, inside the band, not touching it.
    static void Layout()
    {
        var screens = new[] { new Vector2(1080, 2520), new Vector2(1080, 2340), new Vector2(1170, 2532), new Vector2(720, 1560) };
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
                PauseQuickActions.LiveReadoutHeightPx = 0f;
                string tag = size + " edge " + edge + ": ";
                Check(tag + "panel shares the score board's top edge", Mathf.Abs(panel.yMax - read.yMax) < .5f);
                Check(tag + "panel is the score board's height (or the buttons', if taller)", panel.height >= read.height - .5f && panel.height <= Mathf.Max(read.height, PauseQuickActions.ButtonSize * s) + .5f);
                Check(tag + "panel mirrors the score board about the centre line (outer edges)",
                      Mathf.Abs((size.x - read.xMin) - panel.xMax) < 1.5f);
                Check(tag + "panel does not overlap the score board", !panel.Overlaps(read));
                Check(tag + "panel stays inside the band", panel.xMin >= band.left - .5f && panel.xMax <= band.right + .5f);
                Rect home = PauseQuickActions.ButtonScreenRect(band, size, 1), replay = PauseQuickActions.ButtonScreenRect(band, size, 0);
                Check(tag + "home is left of replay, both inside the panel", home.xMax < replay.xMin && home.xMin >= panel.xMin && replay.xMax <= panel.xMax);
                Check(tag + "buttons keep the full tap size", home.width >= PauseQuickActions.ButtonSize * s - .01f && home.height >= PauseQuickActions.ButtonSize * s - .01f);
                float left = home.xMin - panel.xMin, right = panel.xMax - replay.xMax;
                Check(tag + "buttons evenly spaced in the panel", Mathf.Abs(left - right) < .5f);
            }
        }
    }
}
