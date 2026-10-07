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
// original mine's.
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
    // SHA-1 of Pause/Assets/Art/Resources/Vfx/rail_bomb_themes_atlas.png at
    // 18b5e5f^ (git blob 0702e8004b2c486f1b6cd08b105df36cf15fcd4a), the art
    // before the flat-cartoon restyle repacked it.
    public const string OriginalSha1 = "5ce92c375e467f39c839c3fcc6a060c2185840b8";

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        TheAtlasIsTheOriginalPng();
        ImportedCrisp();
        EachWorldUsesItsOwnRow();
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
    }

    static void ImportedCrisp()
    {
        var importer = AssetImporter.GetAtPath(AtlasAsset) as TextureImporter;
        Check("the atlas has a texture importer", importer != null);
        if (importer == null) return;
        Check("point filtered", importer.filterMode == FilterMode.Point);
        Check("no mipmaps", !importer.mipmapEnabled);
        Check("uncompressed by default", importer.textureCompression == TextureImporterCompression.Uncompressed);
        foreach (string platform in new[] { "Android", "iPhone", "Standalone" })
        {
            var s = importer.GetPlatformTextureSettings(platform);
            Check(platform + " keeps it uncompressed (no compressed override)",
                  !s.overridden || s.textureCompression == TextureImporterCompression.Uncompressed);
        }
        Check("full size (max texture size >= 1254)", importer.maxTextureSize >= RailMineArt.AtlasSize);
        Check("imported as a sprite texture, sliced at runtime", importer.textureType == TextureImporterType.Sprite);
        var tex = RailMineArt.Atlas;
        Check("the loaded texture is point filtered with one mip", tex != null && tex.filterMode == FilterMode.Point && tex.mipmapCount == 1);
    }

    // ---- rows ------------------------------------------------------------------

    static Color32[] pixels;
    static int pixelsW, pixelsH;

    static void LoadPixels()
    {
        if (pixels != null) return;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(AtlasAsset));
        pixels = tex.GetPixels32();
        pixelsW = tex.width;
        pixelsH = tex.height;
        Object.DestroyImmediate(tex);
    }

    // Top-left-origin pixel, as RailMineArt's rects are written.
    static Color32 Px(int x, int y) => pixels[(pixelsH - 1 - y) * pixelsW + x];

    // The neon each row is lit with, from its saturated bright pixels' hue.
    public enum Neon { Cyan, Blue, Lime, Orange, None }
    static readonly Neon[] RowNeon = { Neon.Cyan, Neon.Blue, Neon.Lime, Neon.Orange };

    static Neon Signature(RectInt r)
    {
        LoadPixels();
        int cyan = 0, blue = 0, lime = 0, orange = 0;
        for (int y = r.y; y < r.y + r.height; y++)
            for (int x = r.x; x < r.x + r.width; x++)
            {
                var c = Px(x, y);
                if (c.a < 230) continue;
                float h, s, v;
                Color.RGBToHSV(c, out h, out s, out v);
                if (s < .6f || v < .6f) continue;
                float deg = h * 360f;
                if (deg >= 175f && deg < 200f) cyan++;
                else if (deg >= 200f && deg < 245f) blue++;
                else if (deg >= 60f && deg < 130f) lime++;
                else if (deg < 45f) orange++;
            }
        int best = Mathf.Max(Mathf.Max(cyan, blue), Mathf.Max(lime, orange));
        if (best < 500) return Neon.None;
        return best == cyan ? Neon.Cyan : best == blue ? Neon.Blue : best == lime ? Neon.Lime : Neon.Orange;
    }

    static void EachWorldUsesItsOwnRow()
    {
        if (!File.Exists(AtlasAsset)) return;
        Check("one atlas row per world", RailMineArt.Worlds == EnemyRoster.WorldKeys.Length);
        for (int w = 0; w < RailMineArt.Worlds; w++)
            for (int c = 0; c < RailMineArt.Columns; c++)
            {
                var r = RailMineArt.PixelRect(w, c);
                var sig = Signature(r);
                Check(string.Format("{0} {1} frame is lit {2} (got {3})", EnemyRoster.WorldKeys[w], RailMineArt.ColumnName(c), RowNeon[w], sig),
                      sig == RowNeon[w]);
            }

        var atlasTex = RailMineArt.Atlas;
        for (int w = 0; w < RailMineArt.Worlds; w++)
        {
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
                var top = new RectInt((int)got.x, RailMineArt.AtlasSize - (int)got.y - (int)got.height, (int)got.width, (int)got.height);
                Check(def.key + " idles on its own row's dormant frame " + want + " (got " + top + ")", top.Equals(want));
                Check(def.key + " shows the " + RowNeon[w] + " row", Signature(top) == RowNeon[w]);
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
        LoadPixels();
        for (int w = 0; w < RailMineArt.Worlds; w++)
        {
            float dormant = ClampOffset(w, RailMineArt.Dormant);
            for (int c = 1; c < RailMineArt.Columns; c++)
            {
                float off = ClampOffset(w, c);
                Check(string.Format("{0} {1}: the clamp stays put ({2:F0} px vs dormant {3:F0} px from the pivot)",
                                    EnemyRoster.WorldKeys[w], RailMineArt.ColumnName(c), off, dormant),
                      Mathf.Abs(off - dormant) <= 8f);
            }
            for (int c = 0; c < RailMineArt.Columns; c++)
            {
                var r = RailMineArt.PixelRect(w, c);
                var p = RailMineArt.PixelPivot(w, c);
                Check(EnemyRoster.WorldKeys[w] + " " + RailMineArt.ColumnName(c) + " pivot lies inside its rect",
                      p.x > r.x && p.x < r.xMax && p.y > r.y && p.y < r.yMax);
                // no frame is clipped: the rect's border is (nearly) empty
                Check(EnemyRoster.WorldKeys[w] + " " + RailMineArt.ColumnName(c) + " drawing isn't clipped by its rect",
                      EdgeCoverage(r) < .02f);
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
                if (Px(x, y).a >= 200) return x - p.x;
        return 0f;
    }

    // Share of the rect's border pixels that are solid (alpha >= 128).
    static float EdgeCoverage(RectInt r)
    {
        int solid = 0, n = 0;
        for (int x = r.x; x < r.xMax; x++)
        {
            n += 2;
            if (Px(x, r.y).a >= 128) solid++;
            if (Px(x, r.yMax - 1).a >= 128) solid++;
        }
        for (int y = r.y; y < r.yMax; y++)
        {
            n += 2;
            if (Px(r.x, y).a >= 128) solid++;
            if (Px(r.xMax - 1, y).a >= 128) solid++;
        }
        return solid / (float)n;
    }

    static void SizeMatchesTheOriginal()
    {
        if (!File.Exists(AtlasAsset)) return;
        LoadPixels();
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
                    if (Px(x, y).a > 128) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
            float width = (maxX - minX + 1) / RailMineArt.PixelsPerUnit;
            Check(string.Format("{0} mine is {1:F2} u wide (original {2:F2} u, +/-10%)", EnemyRoster.WorldKeys[w], width, EnemyRoster.MineWidth),
                  Mathf.Abs(width - EnemyRoster.MineWidth) <= EnemyRoster.MineWidth * .1f);
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
                Check(k + " arms when the ship is near", armed.Count > 0);
                Check(k + " arming plays the waking frame", armed.Contains(waking));
                Check(k + " arming plays the charging frame", armed.Contains(charging));
                bool onlyArming = true;
                foreach (var s in armed) onlyArming &= s == waking || s == charging;
                Check(k + " arming shows only waking/charging", onlyArming);
                int firstCharge = armed.IndexOf(charging);
                Check(k + " waking comes before charging", firstCharge > 0 && armed[firstCharge - 1] == waking);
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
