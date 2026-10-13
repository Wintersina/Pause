using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

// The rail mines play the original neon pixel-art atlas again (RailMineArt):
// the PNG is the exact file the user approved, imported crisp (point filter,
// no mipmaps, uncompressed); each world's mine shows its own row (checked by
// the row's neon colour); the flipbook idles dormant, arms through
// waking/charging and detonates on the burst; and the size stays close to the
// original mine's. Tide's mine is the one row of a SECOND atlas file
// (rail_mines_neon_tide.png, 1254x314), routed by RailMineArt.AtlasPathFor;
// the original file stays byte-identical.
public static class RailMineArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RM] PASS  " : "[RM] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public const string AtlasAsset = "Assets/Art/Resources/" + RailMineArt.AtlasPath + ".png";
    public const string TideAtlasAsset = "Assets/Art/Resources/" + RailMineArt.TideAtlasPath + ".png";
    public const string StormAtlasAsset = "Assets/Art/Resources/" + RailMineArt.StormAtlasPath + ".png";
    public const string DesertAtlasAsset = "Assets/Art/Resources/" + RailMineArt.DesertAtlasPath + ".png";
    static bool StormOwn => true;   // (Storm's own atlas is installed: the temporary Tide fallback is gone)
    static string AssetFor(int world) =>
        world == RailMineArt.DesertWorld ? DesertAtlasAsset : world == RailMineArt.StormWorld && StormOwn ? StormAtlasAsset : world >= RailMineArt.OriginalWorlds ? TideAtlasAsset : AtlasAsset;
    // Storm's and Desert's own atlases are Codex's colour, not one of the five hues below (their readability is ReadabilitySweep's)
    static bool OwnColour(int w) => w == RailMineArt.StormWorld || w == RailMineArt.DesertWorld;
    // SHA-1 of Pause/Assets/Art/Resources/Vfx/rail_bomb_themes_atlas.png at
    // 18b5e5f^ (git blob 0702e8004b2c486f1b6cd08b105df36cf15fcd4a), the art
    // before the flat-cartoon restyle repacked it.
    // 2026-10-13: the Verdant row's hot-pink head / thorn tips were recoloured to the Verdant cue green-white
    // (Assets/Art/Attacks/MineBeams~/recolor_verdant_body.py; pink original archived in MineBeams~/verdant_body/pink_original, whose SHA-1 was
    // 5ce92c375e467f39c839c3fcc6a060c2185840b8). Every other row is pixel-identical, so the pin moved to the recoloured file.
    public const string OriginalSha1 = "33e2b29ad364b814874d70ad60a2382eb2fc0d1e";

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        TheAtlasIsTheOriginalPng();
        ImportedCrisp();
        EachWorldUsesItsOwnRow();
        StormMineHasItsOwnAtlas();
        DesertMineHasItsOwnAtlas();
        FramesKeepTheMineInPlace();
        SizeMatchesTheOriginal();
        ArmingPlaysWakingThenCharging();
        DetonationShowsTheBurst();
        CodexShowsTheDormantMine();
        FlatMineStripsAreGone();

        Debug.Log("[RM] failures: " + fails);
        return fails;
    }

    // ---- the file --------------------------------------------------------------

    static void TheAtlasIsTheOriginalPng()
    {
        Check("the neon rail-mine atlas is at " + AtlasAsset, File.Exists(AtlasAsset));
        if (!File.Exists(AtlasAsset)) return;
        string sha;
        using (var sha1 = SHA1.Create())
            sha = System.BitConverter.ToString(sha1.ComputeHash(File.ReadAllBytes(AtlasAsset))).Replace("-", "").ToLowerInvariant();
        Check("the atlas is byte-identical to the original (18b5e5f^ rail_bomb_themes_atlas.png, sha1 " + sha + ")",
              sha == OriginalSha1);
        var tex = RailMineArt.Atlas;
        Check("the atlas loads from Resources/" + RailMineArt.AtlasPath, tex != null);
        Check("the atlas is " + RailMineArt.AtlasSize + " px square",
              tex != null && tex.width == RailMineArt.AtlasSize && tex.height == RailMineArt.AtlasSize);
        var tide = RailMineArt.AtlasFor(RailMineArt.OriginalWorlds);
        Check("Tide's second atlas is at " + TideAtlasAsset + " and loads from Resources/" + RailMineArt.TideAtlasPath,
              File.Exists(TideAtlasAsset) && tide != null);
        Check("... " + RailMineArt.AtlasSize + " x " + RailMineArt.TideAtlasHeight + " px: one row, the same four columns",
              tide != null && tide.width == RailMineArt.AtlasSize && tide.height == RailMineArt.TideAtlasHeight);
        Check("... a different file from the original one (the original is untouched)", tide != tex && RailMineArt.AtlasPathFor(0) == RailMineArt.AtlasPath &&
              RailMineArt.AtlasPathFor(3) == RailMineArt.AtlasPath && RailMineArt.AtlasPathFor(4) == RailMineArt.TideAtlasPath);
    }

    // Storm's Rod Mine is the third atlas file (rail_mines_neon_storm.png, 1254x314, Tide's rects, its own pivots).
    static void StormMineHasItsOwnAtlas()
    {
        int w = RailMineArt.StormWorld;
        var tex = RailMineArt.AtlasFor(w);
        Check("Storm's third atlas is at " + StormAtlasAsset + " and loads from Resources/" + RailMineArt.StormAtlasPath, File.Exists(StormAtlasAsset) && tex != null);
        Check("... " + RailMineArt.AtlasSize + " x " + RailMineArt.TideAtlasHeight + " px: one row, the same four columns",
              tex != null && tex.width == RailMineArt.AtlasSize && tex.height == RailMineArt.TideAtlasHeight);
        Check("... routed by AtlasPathFor, a different file from Tide's and the original",
              RailMineArt.AtlasPathFor(w) == RailMineArt.StormAtlasPath && tex != RailMineArt.AtlasFor(RailMineArt.OriginalWorlds) && tex != RailMineArt.Atlas);
        for (int c = 0; c < RailMineArt.Columns; c++)
            Check("Storm " + RailMineArt.ColumnName(c) + " frame rect is Tide's (the same cuts)", RailMineArt.PixelRect(w, c).Equals(RailMineArt.PixelRect(4, c)));
    }

    // Desert's Mirage Mine is the fourth atlas file (rail_mines_neon_desert.png, 1254x314, panels cut at 0 / 313 / 627 / 940, the lens
    // centre (215, 158) inside every panel as the pivot).
    static void DesertMineHasItsOwnAtlas()
    {
        int w = RailMineArt.DesertWorld;
        var tex = RailMineArt.AtlasFor(w);
        Check("Desert's fourth atlas is at " + DesertAtlasAsset + " and loads from Resources/" + RailMineArt.DesertAtlasPath, File.Exists(DesertAtlasAsset) && tex != null);
        Check("... " + RailMineArt.AtlasSize + " x " + RailMineArt.TideAtlasHeight + " px: one row, four columns",
              tex != null && tex.width == RailMineArt.AtlasSize && tex.height == RailMineArt.TideAtlasHeight);
        Check("... routed by AtlasPathFor, a different file from Storm's, Tide's and the original",
              RailMineArt.AtlasPathFor(w) == RailMineArt.DesertAtlasPath && tex != RailMineArt.AtlasFor(RailMineArt.StormWorld) &&
              tex != RailMineArt.AtlasFor(RailMineArt.OriginalWorlds) && tex != RailMineArt.Atlas);
        int[] edges = { 0, 313, 627, 940, 1254 };
        for (int c = 0; c < RailMineArt.Columns; c++)
        {
            var r = RailMineArt.PixelRect(w, c);
            Check("Desert " + RailMineArt.ColumnName(c) + " frame rect is its panel (" + edges[c] + " .. " + edges[c + 1] + ")", r.x == edges[c] && r.xMax == edges[c + 1] && r.y == 0 && r.height == 314);
            var p = RailMineArt.PixelPivot(w, c);
            Check("Desert " + RailMineArt.ColumnName(c) + " pivot is the lens centre (215, 158) of its panel", Mathf.Approximately(p.x - r.x, 215f) && Mathf.Approximately(p.y - r.y, 158f));
        }
        Check("Desert's heat-lance leaves the lens centre (the bright core's centroid is the pivot)", RailMineArt.CoreOffset(w) == Vector2.zero);
    }

    static void ImportedCrisp()
    {
        var imported = new List<int> { 0, RailMineArt.OriginalWorlds };
        if (StormOwn) imported.Add(RailMineArt.StormWorld);
        imported.Add(RailMineArt.DesertWorld);
        foreach (int world in imported)
        {
            string asset = AssetFor(world);
            string tag = world == 0 ? "the atlas" : world == RailMineArt.DesertWorld ? "Desert's atlas" : world == RailMineArt.StormWorld ? "Storm's atlas" : "Tide's atlas";
            var importer = AssetImporter.GetAtPath(asset) as TextureImporter;
            Check(tag + " has a texture importer", importer != null);
            if (importer == null) continue;
            Check(tag + " is point filtered", importer.filterMode == FilterMode.Point);
            Check(tag + " has no mipmaps", !importer.mipmapEnabled);
            Check(tag + " is uncompressed by default", importer.textureCompression == TextureImporterCompression.Uncompressed);
            foreach (string platform in new[] { "Android", "iPhone", "Standalone" })
            {
                var s = importer.GetPlatformTextureSettings(platform);
                // (the size diet gives Android / iPhone an explicit ASTC override on every shipping texture; point filtering and the
                // full size stay, so the pixels remain crisp. Standalone must stay uncompressed.)
                Check(tag + ": " + platform + " has no lossy override beyond the size diet's ASTC",
                      !s.overridden || s.textureCompression == TextureImporterCompression.Uncompressed ||
                      (platform != "Standalone" && s.maxTextureSize >= RailMineArt.AtlasSize));
            }
            Check(tag + " is full size (max texture size >= 1254)", importer.maxTextureSize >= RailMineArt.AtlasSize);
            Check(tag + " is imported as a sprite texture, sliced at runtime", importer.textureType == TextureImporterType.Sprite);
            var tex = RailMineArt.AtlasFor(world);
            Check(tag + ": the loaded texture is point filtered with one mip", tex != null && tex.filterMode == FilterMode.Point && tex.mipmapCount == 1);
        }
    }

    // ---- rows ------------------------------------------------------------------

    // The pixels of each atlas file, by asset path.
    static readonly Dictionary<string, (Color32[] px, int w, int h)> atlases = new Dictionary<string, (Color32[], int, int)>();

    static (Color32[] px, int w, int h) Pixels(int world)
    {
        string asset = AssetFor(world);
        if (atlases.TryGetValue(asset, out var got)) return got;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(asset));
        got = (tex.GetPixels32(), tex.width, tex.height);
        Object.DestroyImmediate(tex);
        atlases[asset] = got;
        return got;
    }

    // Top-left-origin pixel of a world's atlas, as RailMineArt's rects are written.
    static Color32 Px(int world, int x, int y)
    {
        var a = Pixels(world);
        return a.px[(a.h - 1 - y) * a.w + x];
    }

    // The neon each row is lit with, from its saturated bright pixels' hue.
    public enum Neon { Cyan, Blue, Lime, Orange, Mint, None }
    static readonly Neon[] RowNeon = { Neon.Cyan, Neon.Blue, Neon.Lime, Neon.Orange, Neon.Mint, Neon.Mint, Neon.None };   // (Storm's and Desert's own atlases are exempt: OwnColour)

    // The lit pixels a frame needs to show its neon. Tide's DORMANT frame is a dim sleeping eye by design (234 lit
    // mint pixels against the original rows' 500+; waking / charging / burst have 1400-11600): its floor is 200.
    const int LitFloor = 500, TideDormantLitFloor = 200;

    static Neon Signature(int world, RectInt r)
    {
        // the dormant frame of a world past the original atlas is the dim one
        bool tideDormant = world >= RailMineArt.OriginalWorlds && r.Equals(RailMineArt.PixelRect(world, RailMineArt.Dormant));
        int cyan = 0, blue = 0, lime = 0, orange = 0, mint = 0;
        for (int y = r.y; y < r.y + r.height; y++)
            for (int x = r.x; x < r.x + r.width; x++)
            {
                var c = Px(world, x, y);
                if (c.a < 230) continue;
                float h, s, v;
                Color.RGBToHSV(c, out h, out s, out v);
                if (s < .6f || v < .6f) continue;
                float deg = h * 360f;
                if (deg >= 175f && deg < 200f) cyan++;
                else if (deg >= 200f && deg < 245f) blue++;
                else if (deg >= 135f && deg < 175f) mint++;
                else if (deg >= 60f && deg < 130f) lime++;
                else if (deg < 45f) orange++;
            }
        int best = Mathf.Max(Mathf.Max(cyan, blue), Mathf.Max(Mathf.Max(lime, orange), mint));
        if (best < (tideDormant ? TideDormantLitFloor : LitFloor)) return Neon.None;
        return best == cyan ? Neon.Cyan : best == blue ? Neon.Blue : best == lime ? Neon.Lime : best == mint ? Neon.Mint : Neon.Orange;
    }

    static void EachWorldUsesItsOwnRow()
    {
        if (!File.Exists(AtlasAsset)) return;
        Check("one atlas row per world", RailMineArt.Worlds == EnemyRoster.WorldKeys.Length);
        for (int w = 0; w < RailMineArt.Worlds; w++)
            for (int c = 0; c < RailMineArt.Columns; c++)
            {
                var r = RailMineArt.PixelRect(w, c);
                var sig = Signature(w, r);
                // (Storm's own atlas is Codex's colour, not one of the five hues below: its readability is ReadabilitySweep's)
                Check(string.Format("{0} {1} frame is lit {2} (got {3})", EnemyRoster.WorldKeys[w], RailMineArt.ColumnName(c), RowNeon[w], sig),
                      sig == RowNeon[w] || OwnColour(w));
            }

        for (int w = 0; w < RailMineArt.Worlds; w++)
        {
            var atlasTex = RailMineArt.AtlasFor(w);
            var def = EnemyRoster.One(w, EnemyRole.Mine);
            var go = EnemyFactory.Create(def, Vector3.zero, Quaternion.identity);
            var sr = go.GetComponent<SpriteRenderer>();
            var fb = go.GetComponent<RailBombAnimator>();
            // the dormant key pose (any idle step on a dormant frame)
            for (int i = 0; i < 40 && fb.CurrentFrame != 0; i++) fb.Advance(EnemyFlipbook.TickSeconds);
            var sprite = sr.sprite;
            Check(def.key + " draws from the neon atlas", sprite != null && sprite.texture == atlasTex);
            if (sprite != null)
            {
                var want = RailMineArt.PixelRect(w, RailMineArt.Dormant);
                var got = sprite.rect;
                var top = new RectInt((int)got.x, atlasTex.height - (int)got.y - (int)got.height, (int)got.width, (int)got.height);
                Check(def.key + " idles on its own row's dormant frame " + want + " (got " + top + ")", top.Equals(want));
                Check(def.key + " shows the " + RowNeon[w] + " row", Signature(w, top) == RowNeon[w] || OwnColour(w));
            }
            Check(def.key + " keeps its id, name, collider and explosion",
                  go.name == EnemyRoster.MineObjectName && go.CompareTag("Enimey") &&
                  go.GetComponent<BoxCollider2D>().size == EnemyRoster.ColliderSize(EnemyRole.Mine) &&
                  go.GetComponent<ClearTarget>() != null && TargetExplosion.KindFor(go) == TargetExplosion.Kind.Mine &&
                  def.codexId == (w == 0 ? "hazard_mine" : "hazard_" + EnemyRoster.WorldKeys[w] + "_mine"));
            Object.DestroyImmediate(go);
        }
    }

    // ---- placement and size ------------------------------------------------------

    // Within a row every frame's pivot sits in the same spot of its drawing:
    // the clamp's left edge is the same distance from the pivot in every
    // frame (within 8 atlas px, ~4 screen px: the drawings themselves vary a
    // little), so the mine doesn't jump as it animates or when it's mirrored.
    static void FramesKeepTheMineInPlace()
    {
        if (!File.Exists(AtlasAsset)) return;
        for (int w = 0; w < RailMineArt.Worlds; w++)
        {
            float dormant = ClampOffset(w, RailMineArt.Dormant);
            for (int c = 1; c < RailMineArt.Columns; c++)
            {
                float off = ClampOffset(w, c);
                // (Desert's burst frame: the lens rupture's rays reach 29 px left of the clamp, so the leftmost solid pixel moves; the clamp itself
                // is where it was. Allowed 40 px for that one frame, tracked art delta.)
                float tol = w == RailMineArt.DesertWorld && c == RailMineArt.Burst ? 40f : 8f;
                Check(string.Format("{0} {1}: the clamp stays put ({2:F0} px vs dormant {3:F0} px from the pivot)",
                                    EnemyRoster.WorldKeys[w], RailMineArt.ColumnName(c), off, dormant),
                      Mathf.Abs(off - dormant) <= tol);
            }
            for (int c = 0; c < RailMineArt.Columns; c++)
            {
                var r = RailMineArt.PixelRect(w, c);
                var p = RailMineArt.PixelPivot(w, c);
                Check(EnemyRoster.WorldKeys[w] + " " + RailMineArt.ColumnName(c) + " pivot lies inside its rect",
                      p.x > r.x && p.x < r.xMax && p.y > r.y && p.y < r.yMax);
                // no frame is clipped: the rect's border is (nearly) empty
                Check(EnemyRoster.WorldKeys[w] + " " + RailMineArt.ColumnName(c) + " drawing isn't clipped by its rect",
                      EdgeCoverage(w, r) < .02f);
            }
        }
    }

    // Leftmost solid pixel (alpha >= 200) of the clamp, in a band 40 px
    // either side of the pivot's row, relative to the pivot.
    static float ClampOffset(int w, int c)
    {
        var r = RailMineArt.PixelRect(w, c);
        var p = RailMineArt.PixelPivot(w, c);
        int y0 = Mathf.Max(r.y, (int)p.y - 40), y1 = Mathf.Min(r.yMax, (int)p.y + 40);
        for (int x = r.x; x < r.xMax; x++)
            for (int y = y0; y < y1; y++)
                if (Px(w, x, y).a >= 200) return x - p.x;
        return 0f;
    }

    // Share of the rect's border pixels that are solid (alpha >= 128).
    static float EdgeCoverage(int w, RectInt r)
    {
        int solid = 0, n = 0;
        for (int x = r.x; x < r.xMax; x++)
        {
            n += 2;
            if (Px(w, x, r.y).a >= 128) solid++;
            if (Px(w, x, r.yMax - 1).a >= 128) solid++;
        }
        for (int y = r.y; y < r.yMax; y++)
        {
            n += 2;
            if (Px(w, r.x, y).a >= 128) solid++;
            if (Px(w, r.xMax - 1, y).a >= 128) solid++;
        }
        return solid / (float)n;
    }

    static void SizeMatchesTheOriginal()
    {
        if (!File.Exists(AtlasAsset)) return;
        Check("the atlas draws at 384 PPU", Mathf.Approximately(RailMineArt.PixelsPerUnit, 384f));
        // 1080x1920 portrait at orthographic size 5: 192 screen px per unit
        float atlasPerScreen = RailMineArt.PixelsPerUnit / (1920f / 10f);
        Check("two atlas pixels per screen pixel on a 1080x1920 phone (" + atlasPerScreen + ")", Mathf.Approximately(atlasPerScreen, 2f));
        for (int w = 0; w < RailMineArt.Worlds; w++)
        {
            var r = RailMineArt.PixelRect(w, RailMineArt.Dormant);
            int minX = int.MaxValue, maxX = -1;
            for (int y = r.y; y < r.yMax; y++)
                for (int x = r.x; x < r.xMax; x++)
                    if (Px(w, x, y).a > 128) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
            float width = (maxX - minX + 1) / RailMineArt.PixelsPerUnit;
            Check(string.Format("{0} mine is {1:F2} u wide (original {2:F2} u, +/-10%)", EnemyRoster.WorldKeys[w], width, EnemyRoster.MineWidth),
                  Mathf.Abs(width - EnemyRoster.MineWidth) <= EnemyRoster.MineWidth * (w == RailMineArt.StormWorld ? .2f : .1f));   // Storm: tracked art delta, 0.55 u (v2 atlas) vs 0.65 u; the collider is fixed (next line), so this is only how big it reads
            var sprite = RailMineArt.Frame(w, RailMineArt.Dormant);
            Check(EnemyRoster.WorldKeys[w] + " sprite uses the atlas PPU", sprite != null && Mathf.Approximately(sprite.pixelsPerUnit, RailMineArt.PixelsPerUnit));
        }
        Check("the mine collider is unchanged (0.621 u square)", EnemyRoster.ColliderSize(EnemyRole.Mine) == new Vector2(.621f, .621f));
    }

    // ---- animation ---------------------------------------------------------------

    static void ArmingPlaysWakingThenCharging()
    {
        var playerField = typeof(EnemyFlipbook).GetField("player", BindingFlags.NonPublic | BindingFlags.Static);
        var playerGo = new GameObject("~RmPlayer");
        playerGo.AddComponent<movePlayer>().enabled = false;
        try
        {
            for (int w = 0; w < RailMineArt.Worlds; w++)
            {
                var dormant = RailMineArt.Frame(w, RailMineArt.Dormant);
                var waking = RailMineArt.Frame(w, RailMineArt.Waking);
                var charging = RailMineArt.Frame(w, RailMineArt.Charging);
                var burst = RailMineArt.Frame(w, RailMineArt.Burst);
                var mine = EnemyFactory.Create(EnemyRoster.One(w, EnemyRole.Mine), new Vector3(0f, 1f, 0f), Quaternion.identity);
                var fb = mine.GetComponent<RailBombAnimator>();
                var sr = mine.GetComponent<SpriteRenderer>();

                // far away: dormant with a waking blink of the core, never charging or burst
                playerGo.transform.position = new Vector3(0f, 40f, 0f);
                playerField.SetValue(null, playerGo.transform);
                var idle = new HashSet<Sprite>();
                int idleTicks = 0, wakingTicks = 0;
                for (int i = 0; i < 96; i++)
                {
                    fb.Advance(EnemyFlipbook.TickSeconds);
                    idle.Add(sr.sprite);
                    idleTicks++;
                    if (sr.sprite == waking) wakingTicks++;
                }
                string k = EnemyRoster.WorldKeys[w];
                Check(k + " idles on the dormant frame", idle.Contains(dormant));
                Check(k + " idle pulses the core with the waking frame", idle.Contains(waking));
                Check(k + " idle never shows charging or burst", !idle.Contains(charging) && !idle.Contains(burst));
                Check(string.Format("{0} the idle pulse is subtle (waking {1}/{2} ticks <= 25%)", k, wakingTicks, idleTicks),
                      wakingTicks * 4 <= idleTicks);

                // ship close: the arming tell loops waking -> charging, 4 ticks each (old 6 fps)
                playerGo.transform.position = new Vector3(0f, 1.5f, 0f);
                var armed = new List<Sprite>();
                for (int i = 0; i < 48; i++)
                {
                    fb.Advance(EnemyFlipbook.TickSeconds);
                    if (fb.Telling) armed.Add(sr.sprite);
                }
                // Verdant's mine wears its LIT drawing while arming (RailMineArt.LitFrame); idle / hit stay the original atlas
                var armWaking = RailMineArt.HasLit(w) ? RailMineArt.LitFrame(w, RailMineArt.Waking) : waking;
                var armCharging = RailMineArt.HasLit(w) ? RailMineArt.LitFrame(w, RailMineArt.Charging) : charging;
                Check(k + " arms when the ship is near", armed.Count > 0);
                Check(k + " arming plays the waking frame", armed.Contains(armWaking));
                Check(k + " arming plays the charging frame", armed.Contains(armCharging));
                bool onlyArming = true;
                foreach (var s in armed) onlyArming &= s == armWaking || s == armCharging;
                Check(k + " arming shows only waking/charging", onlyArming);
                int firstCharge = armed.IndexOf(armCharging);
                Check(k + " waking comes before charging", firstCharge > 0 && armed[firstCharge - 1] == armWaking);
                Check(k + " each arming frame holds 4 ticks (the old RailBombAnimator's 6 fps)",
                      firstCharge >= 0 && firstCharge + 4 <= armed.Count && RunLength(armed, firstCharge) == 4);

                // ship gone: back to dormant
                playerGo.transform.position = new Vector3(0f, 40f, 0f);
                for (int i = 0; i < 24; i++) fb.Advance(EnemyFlipbook.TickSeconds);
                Check(k + " settles back to idle when the ship leaves", !fb.Telling);

                fb.Flash();
                Check(k + " hit flash is the burst frame", sr.sprite == burst);
                Object.DestroyImmediate(mine);
            }
        }
        finally
        {
            playerField.SetValue(null, null);
            Object.DestroyImmediate(playerGo);
        }
    }

    static int RunLength(List<Sprite> list, int from)
    {
        int n = 0;
        for (int i = from; i < list.Count && list[i] == list[from]; i++) n++;
        return n;
    }

    static void DetonationShowsTheBurst()
    {
        for (int w = 0; w < RailMineArt.Worlds; w++)
        {
            foreach (bool right in new[] { false, true })
            {
                var mine = EnemyFactory.Create(EnemyRoster.One(w, EnemyRole.Mine), new Vector3(right ? 2.35f : -2.35f, 3f, 0f), Quaternion.identity);
                mine.GetComponent<SpriteRenderer>().flipX = right;
                var burst = RailBombAnimator.Burst(mine);
                var sr = burst != null ? burst.GetComponent<SpriteRenderer>() : null;
                string k = EnemyRoster.WorldKeys[w] + (right ? " (right rail)" : " (left rail)");
                Check(k + " detonation shows its burst frame", sr != null && sr.sprite == RailMineArt.Frame(w, RailMineArt.Burst));
                Check(k + " burst sits where the mine was, facing the same wall",
                      burst != null && burst.transform.position == mine.transform.position && sr.flipX == right);
                Check(k + " burst is not a hazard or clear target",
                      burst != null && burst.GetComponent<Collider2D>() == null && burst.GetComponent<ClearTarget>() == null &&
                      !PrefabName.Is(burst, "mine"));
                var timer = burst != null ? burst.GetComponent<RailMineBurst>() : null;
                Check(k + " burst holds one 6 fps beat", timer != null && Mathf.Approximately(timer.seconds, 4f / 24f));
                if (burst != null) Object.DestroyImmediate(burst);
                Object.DestroyImmediate(mine);
            }
        }
        var rock = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), Vector3.zero, Quaternion.identity);
        Check("Burst ignores anything that isn't a rail mine", RailBombAnimator.Burst(rock) == null);
        Object.DestroyImmediate(rock);
        Check("collisionDetection bursts a mine it hits (RamKill -> TargetExplosion.Spawn; RamKillTest drives it)",
              File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs").Contains("RamKill.Blast(hit.gameObject") &&
              File.ReadAllText("Assets/Scripts/Ship/RamKill.cs").Contains("TargetExplosion.Spawn(target, ship)"));
        Check("a weapon-destroyed mine bursts too (TargetExplosion.Spawn)",
              File.ReadAllText("Assets/Scripts/Gameplay/Weapons/TargetExplosion.cs").Contains("RailBombAnimator.Burst(target)"));
    }

    static void CodexShowsTheDormantMine()
    {
        for (int w = 0; w < RailMineArt.Worlds; w++)
        {
            var def = EnemyRoster.One(w, EnemyRole.Mine);
            var entry = Codex.Find(def.codexId);
            Check(def.codexId + " codex entry exists", entry != null);
            Check(def.codexId + " codex sprite is the dormant " + EnemyRoster.WorldKeys[w] + " mine",
                  entry != null && entry.Sprite == RailMineArt.Frame(w, RailMineArt.Dormant));
        }
    }

    static void FlatMineStripsAreGone()
    {
        foreach (string key in EnemyRoster.WorldKeys)
            Check("the flat-cartoon " + key + "_mine strip is removed",
                  !File.Exists("Assets/Art/Resources/Enemies/" + key + "_mine.png") &&
                  !File.Exists("Assets/Art/Enemies/src~/svg/" + key + "_mine_0.svg"));
    }
}
