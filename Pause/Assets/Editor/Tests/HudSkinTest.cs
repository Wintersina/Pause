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
        Check("HUD rows share one font size", ScoreHud.FontSize == 19);

        for (int w = 0; w < HudSkin.WorldCount; w++)
        {
            string name = WorldManager.Worlds[w].displayName;
            var paths = new[] { HudSkin.FramePath(w), HudSkin.DeathPanelPath(w), HudSkin.HomeGlyphPath(w), HudSkin.ReplayGlyphPath(w) };
            foreach (string path in paths)
            {
                var sprite = Resources.Load<Sprite>(path);
                Check(name + ": " + path + " loads", sprite != null);
                if (sprite == null) continue;
                bool glyph = path.EndsWith("glyph");
                bool own = w > 0 || !path.EndsWith("glyph");
                if (own)
                    Check(name + ": " + path + " is the world's own art", path.StartsWith("HudSkins/" + name + "/"));
                if (glyph) Check(name + ": " + path + " is square", Mathf.Approximately(sprite.rect.width, sprite.rect.height));
                CheckImport(name, path, sprite, !own);
            }
            Check(name + ": frame is distinct from the other worlds'", w == 0 || HudSkin.FramePath(w) != HudSkin.FramePath(0));
            Check(name + ": the HUD resolves its frame and glyphs", HudSkin.Frame(w) != null && HudSkin.DeathPanel(w) != null &&
                  HudSkin.HomeGlyph(w) != null && HudSkin.ReplayGlyph(w) != null && HudSkin.HudHome(w) != null && HudSkin.HudReplay(w) != null);
            Check(name + ": the board's backing fill is see-through (centre alpha below 0.85)", HudSkin.Frame(w).texture != null && CentreAlpha(HudSkin.Frame(w)) < .85f);
        }

        SharedIcons();
        Plates();
        Layout();
        Debug.Log("[HSK] failures: " + fails);
        return fails;
    }

    // One shared white hud_home / hud_replay pair for every world, tinted per world in code;
    // no per-world copies are left behind.
    static void SharedIcons()
    {
        foreach (string path in new[] { HudSkin.SharedHomePath, HudSkin.SharedReplayPath })
        {
            var sprite = Resources.Load<Sprite>(path);
            Check(path + " loads", sprite != null);
            if (sprite == null) continue;
            Check(path + " is square", Mathf.Approximately(sprite.rect.width, sprite.rect.height));
            CheckImport("shared", path, sprite, false);
        }
        for (int w = 0; w < HudSkin.WorldCount; w++)
        {
            string name = WorldManager.Worlds[w].displayName;
            foreach (string leaf in new[] { "hud_home", "hud_replay" })
                Check(name + ": no per-world " + leaf + " copy", Resources.Load<Sprite>("HudSkins/" + name + "/" + leaf) == null);
            var tint = HudSkin.IconTint(w);
            Check(name + ": has an opaque icon tint", tint.a > .99f);
            float l = .2126f * Mathf.GammaToLinearSpace(tint.r) + .7152f * Mathf.GammaToLinearSpace(tint.g) + .0722f * Mathf.GammaToLinearSpace(tint.b);
            bool brightBackdrop = w == 1 || w == 2;   // Frost clouds, Verdant foliage
            Check(name + ": icon tint " + (brightBackdrop ? "dark" : "light") + " for its backdrop (luma " + l.ToString("0.00") + ")", brightBackdrop ? l < .1f : l > .6f);
        }
    }

    static float Lum(Color c)
    {
        return .2126f * Mathf.GammaToLinearSpace(c.r) + .7152f * Mathf.GammaToLinearSpace(c.g) + .0722f * Mathf.GammaToLinearSpace(c.b);
    }

    // The in-run glyphs must not read as see-through: opaque strokes, a mostly opaque backing plate and
    // rim behind them (still holo: world tint + rim), and a stroke-vs-plate contrast of at least 3:1 even
    // when the backdrop behind the plate is pure black or pure white (the plate is PlateAlpha opaque).
    static void Plates()
    {
        Check("glyph stroke alpha is fully opaque", PauseQuickActions.GlyphAlpha >= .99f);
        Check("backing plate is at least 75% opaque", HudSkin.PlateAlpha >= .75f && HudSkin.PlateAlpha <= 1f);
        Check("plate and rim sprites exist", HudSkin.PlateDisc != null && HudSkin.PlateRing != null);
        Check("plate and rim share one texture (one batch)", HudSkin.PlateDisc.texture == HudSkin.PlateRing.texture);
        foreach (string leaf in new[] { HudSkin.SharedHomePath, HudSkin.SharedReplayPath })
        {
            var tex = Resources.Load<Sprite>(leaf);
            if (tex == null) continue;
            float max = 0f;
            var rt = RenderTexture.GetTemporary(tex.texture.width, tex.texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex.texture, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var copy = new Texture2D(tex.texture.width, tex.texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, copy.width, copy.height), 0, 0); copy.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            foreach (var px in copy.GetPixels32()) max = Mathf.Max(max, px.a / 255f);
            Object.DestroyImmediate(copy);
            Check(leaf + " has fully opaque stroke pixels", max >= .99f);
        }
        for (int w = 0; w < HudSkin.WorldCount; w++)
        {
            string name = WorldManager.Worlds[w].displayName;
            var tint = HudSkin.IconTint(w); var fill = HudSkin.PlateFill(w);
            Check(name + ": holo kept: rim is the world tint", HudSkin.PlateRim(w) == tint);
            float lt = Lum(tint), lf = Lum(fill);
            foreach (float backdrop in new[] { 0f, 1f })
            {
                float eff = HudSkin.PlateAlpha * lf + (1f - HudSkin.PlateAlpha) * backdrop;
                float hi = Mathf.Max(lt, eff) + .05f, lo = Mathf.Min(lt, eff) + .05f;
                Check(name + ": stroke vs plate over a " + (backdrop > .5f ? "white" : "black") + " backdrop >= 3:1 (" + (hi / lo).ToString("0.0") + ")", hi / lo >= 3f);
            }
        }
    }

    // Alpha of the sprite's centre pixel (the board's backing fill); the texture is compressed, so decode a copy.
    static float CentreAlpha(Sprite sprite)
    {
        var tex = sprite.texture;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); copy.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        float a = copy.GetPixel((int)sprite.rect.center.x, (int)sprite.rect.center.y).a;
        Object.DestroyImmediate(copy);
        return a;
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
        if (path.EndsWith("hud_board"))
        {
            Check(world + ": " + path + " 9-slice border 50/60 at 200 ppu",
                  importer.spriteBorder == new Vector4(50, 60, 50, 60) && Mathf.Approximately(importer.spritePixelsPerUnit, 200f));
            Check(world + ": " + path + " is 702x310",
                  Mathf.Approximately(sprite.rect.height, 310f) && Mathf.Approximately(sprite.rect.width, 702f));
        }
        if (dp)
        {
            Check(world + ": " + path + " 9-slice border 100 at 83.33 ppu",
                  importer.spriteBorder == new Vector4(100, 100, 100, 100) && Mathf.Abs(importer.spritePixelsPerUnit - 83.33f) < .01f);
            Check(world + ": " + path + " is 600x900", Mathf.Approximately(sprite.rect.width, 600f) && Mathf.Approximately(sprite.rect.height, 900f));
        }
    }

    // The quick-action slot mirrors the score board: same top edge and height,
    // symmetric about the screen's centre line, inside the band, not touching it.
    static void Layout()
    {
        var screens = new[] { new Vector2(1080, 2520), new Vector2(1080, 2340), new Vector2(1170, 2532), new Vector2(720, 1560), new Vector2(480, 854) };
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
                Check(tag + "panel is the score board's height (or the buttons', if taller)", panel.height >= read.height - .5f && panel.height <= Mathf.Max(read.height, (2f * PauseQuickActions.ButtonSize + PauseQuickActions.VerticalGap) * s) + .5f);
                Check(tag + "panel mirrors the score board about the centre line (outer edges)",
                      Mathf.Abs((size.x - read.xMin) - panel.xMax) < 1.5f);
                Check(tag + "panel does not overlap the score board", !panel.Overlaps(read));
                Check(tag + "panel stays inside the band", panel.xMin >= band.left - .5f && panel.xMax <= band.right + .5f);
                Rect home = PauseQuickActions.ButtonScreenRect(band, size, 1), replay = PauseQuickActions.ButtonScreenRect(band, size, 0);
                bool column = PauseQuickActions.Vertical(band, size);
                if (column)
                {
                    Check(tag + "narrow lane: home stands above restart in one column, both inside the slot",
                          home.yMin >= replay.yMax && Mathf.Abs(home.center.x - replay.center.x) < .5f && home.xMin >= panel.xMin && replay.xMax <= panel.xMax);
                    Check(tag + "narrow lane: the tap squares are spaced apart", home.yMin - replay.yMax >= PauseQuickActions.VerticalGap * s - .5f);
                }
                else
                {
                    Check(tag + "home is left of replay, both inside the panel", home.xMax <= replay.xMin + .01f && home.xMin >= panel.xMin && replay.xMax <= panel.xMax);
                    float left = home.xMin - panel.xMin, right = panel.xMax - replay.xMax;
                    Check(tag + "buttons evenly spaced in the panel", Mathf.Abs(left - right) < .5f);
                }
                Check(tag + "buttons keep the full tap size", home.width >= PauseQuickActions.ButtonSize * s - .01f && home.height >= PauseQuickActions.ButtonSize * s - .01f);
            }
        }
    }
}
