using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// PER-ATTACK BOSS PROJECTILE ART (docs/boss-projectile-art.md): PatternStream.art picks a row of
// Resources/Bosses/<Key>_attackshots.png (2 frames x N rows of 128 px) instead of the boss's one shared bolt / shard pair.
//
//   * the plumbing: a stream's / wave's / voice's art reaches the shot; art 0, an unpainted row or a missing file keeps the shared
//     pair and its rim; a painted variant is drawn at its STYLE's size with its STYLE's hit radius (the hitbox never depends on art);
//   * every art id the tables, waves and music voices use exists in the boss's file once the file does;
//   * the painted files: 256 px wide, whole 128 px rows, each variant animates, and no two attacks of a boss, nor of two bosses,
//     share a sprite; each reads on any backdrop (a dark edge, a bright core, no pale rim) and non-Space has no pink.
public static class BossAttackShotArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SHOTART] PASS  " : "[SHOTART] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Plumbing();
                TableIds();
                Files();
                PaleRims();
            }
            finally { BossArt.InjectAttackShots(null); BossEncounter.ResetRun(); BossRails.Reset(); }
        }
        Debug.Log("[SHOTART] failures: " + fails);
        return fails;
    }

    // ---- 1: the plumbing ----

    static Texture2D Fake(int rows)
    {
        var t = new Texture2D(256, 128 * rows, TextureFormat.RGBA32, false);
        var px = new Color32[t.width * t.height];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < 2; c++)
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                        px[(t.height - 1 - (r * 128 + y)) * t.width + c * 128 + x] = new Color32((byte)(40 + r * 60), (byte)(c * 90), 200, 255);
        t.SetPixels32(px); t.Apply(false);
        return t;
    }

    static void Plumbing()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;

        var boss = BossCatalog.ForWorld(5);   // Storm
        var empty = new Texture2D(2, 2, TextureFormat.RGBA32, false);   // a sheet of the wrong size: no variants (as if the file were missing)
        BossArt.InjectAttackShots(empty);
        var pool = new BossProjectilePool(8, 1);
        Check("without a file there are no variants and an art id falls back to the shared pair",
              BossArt.AttackShot(boss, 1, 0) == null && BossArt.AttackShotVariants(boss) == 0);
        var ps = new PShot { style = BossShotStyle.Bolt, art = 2, rail = BossRailMode.Pass };
        var plain = pool.FireShot(boss, new Vector3(0f, 3f, 0f), new Vector2(0f, -1f), ref ps, false);
        pool.Step(Dt);
        Check("art 2 with no file draws the shared bolt (with its rim)", plain != null && plain.Art == 0 &&
              plain.GetComponent<SpriteRenderer>().sprite == BossArt.Shot(boss, BossArt.Bolt0 + 0) && plain.Glow.enabled == (BossArt.ShotRim(boss, 0) != null));
        float boltR = plain.Radius;
        plain.Recycle();

        var tex = Fake(3);
        BossArt.InjectAttackShots(tex);
        Check("3 variants served from the injected sheet", BossArt.AttackShotVariants(boss) == 3 && BossArt.AttackShot(boss, 3, 1) != null &&
              BossArt.AttackShot(boss, 4, 0) == null && BossArt.AttackShot(boss, 0, 0) == null);
        var a = BossArt.AttackShot(boss, 2, 0);
        Check("a variant cell is 1 u square (128 px)", Mathf.Approximately(a.bounds.size.x, 1f) && Mathf.Approximately(a.bounds.size.y, 1f));

        foreach (var style in new[] { BossShotStyle.Bolt, BossShotStyle.Shard })
        {
            var q = new PShot { style = style, art = 2, rail = BossRailMode.Pass };
            var s = pool.FireShot(boss, new Vector3(0f, 3f, 0f), new Vector2(0f, -1f), ref q, false);
            var sr = s.GetComponent<SpriteRenderer>();
            float size = style == BossShotStyle.Bolt ? BossConfig.BoltWorldSize : BossConfig.ShardWorldSize;
            float radius = style == BossShotStyle.Bolt ? BossConfig.BoltHitRadius : BossConfig.ShardHitRadius;
            Check(style + " variant: drawn from the file's row 2, at the style's size " + size + ", hit radius " + radius + " unchanged, no extra rim",
                  s.Art == 2 && sr.sprite == a && Mathf.Abs(s.transform.localScale.x - size) < 1e-5f && Mathf.Abs(s.Radius - radius) < 1e-6f && !s.Glow.enabled);
            var seen = new HashSet<Sprite>();
            for (int i = 0; i < 30; i++) { pool.Step(Dt); seen.Add(sr.sprite); }
            Check(style + " variant: its two frames alternate", seen.Count == 2 && seen.Contains(BossArt.AttackShot(boss, 2, 0)) && seen.Contains(BossArt.AttackShot(boss, 2, 1)));
            s.Recycle();
        }
        var far = new PShot { style = BossShotStyle.Bolt, art = 9, rail = BossRailMode.Pass };
        var f = pool.FireShot(boss, new Vector3(0f, 3f, 0f), new Vector2(0f, -1f), ref far, false);
        Check("an art id past the file's rows falls back to the shared pair, radius " + boltR, f.Art == 0 && Mathf.Abs(f.Radius - boltR) < 1e-6f);
        f.Recycle();
        pool.Dispose();
        BossArt.InjectAttackShots(null);
        Object.DestroyImmediate(tex); Object.DestroyImmediate(empty);
    }

    // ---- 2: the ids the tables use ----

    static void TableIds()
    {
        for (int w = 0; w < Worlds.Length; w++)
        {
            var slots = BossPatternTables.For(w);
            int max = 0, painted = 0, streams = 0;
            var used = new HashSet<int>();
            if (slots != null)
                foreach (var sl in slots)
                    if (sl.pattern != null)
                        foreach (var st in sl.pattern.streams) { streams++; max = Mathf.Max(max, st.art); if (st.art > 0) { painted++; used.Add(st.art); } }
            foreach (var st in BossMusicVoices.For(w, BossCatalog.ForWorld(w))) { max = Mathf.Max(max, st.art); used.Add(st.art); }
            max = Mathf.Max(max, WaveMoves.For(w).art);
            used.Add(WaveMoves.For(w).art);
            Check(Worlds[w] + ": " + painted + " of " + streams + " pattern streams carry an art id, ids 0.." + max + ", " + used.Count + " distinct in use across patterns, waves, voices",
                  max <= 4 && used.Count >= 2);
            string file = "Assets/Art/Resources/Bosses/" + Worlds[w] + BossArt.AttackShotsSuffix + ".png";
            if (File.Exists(file))
            {
                var t = Read(file);
                int rows = t.height / 128;
                Check(Worlds[w] + ": every art id in use (max " + max + ") has a row in " + Path.GetFileName(file) + " (" + rows + " rows)", max <= rows);
                Object.DestroyImmediate(t);
            }
        }
    }

    // ---- 3: the painted files ----

    static Texture2D Read(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    static Color32[] Cell(Color32[] all, int w, int h, int col, int row)
    {
        var o = new Color32[128 * 128];
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++) o[y * 128 + x] = all[(h - 1 - (row * 128 + y)) * w + col * 128 + x];   // row 0 = top
        return o;
    }

    static long Hash(Color32[] px)
    {
        long hsh = 1469598103934665603L;
        foreach (var c in px) { hsh = (hsh ^ (c.a < 8 ? 0 : (c.r | (c.g << 8) | (c.b << 16) | (c.a << 24)))) * 1099511628211L; }
        return hsh;
    }

    static float Luma(Color32 c) => (.299f * c.r + .587f * c.g + .114f * c.b) / 255f;

    // the share of pixels (opaque in either) that differ
    static float Change(Color32[] a, Color32[] b)
    {
        int union = 0, diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].a < 128 && b[i].a < 128) continue;
            union++;
            if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || (a[i].a >= 128) != (b[i].a >= 128)) diff++;
        }
        return union == 0 ? 0f : diff / (float)union;
    }

    // Edge readability of one 128 px cell: the dark share of the silhouette's edge pixels, the pale share, and the brightest core pixel.
    public static void Edge(Color32[] px, out float darkShare, out float paleShare, out float core, out int opaque)
    {
        int edge = 0, dark = 0, pale = 0; core = 0f; opaque = 0;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                var c = px[y * 128 + x];
                if (c.a < 200) continue;
                opaque++;
                core = Mathf.Max(core, Luma(c));
                bool border = x == 0 || y == 0 || x == 127 || y == 127 ||
                              px[y * 128 + x - 1].a < 40 || px[y * 128 + x + 1].a < 40 || px[(y - 1) * 128 + x].a < 40 || px[(y + 1) * 128 + x].a < 40;
                if (!border) continue;
                edge++;
                float l = Luma(c);
                if (l < .32f) dark++;
                if (Mathf.Min(c.r, Mathf.Min(c.g, c.b)) > 205) pale++;
            }
        darkShare = edge == 0 ? 0f : dark / (float)edge;
        paleShare = edge == 0 ? 0f : pale / (float)edge;
    }

    static void Files()
    {
        var seen = new Dictionary<long, string>();
        int checkedFiles = 0;
        for (int w = 0; w < Worlds.Length; w++)
        {
            string file = "Assets/Art/Resources/Bosses/" + Worlds[w] + BossArt.AttackShotsSuffix + ".png";
            if (!File.Exists(file)) { Debug.Log("[SHOTART] (no " + Path.GetFileName(file) + " yet: " + Worlds[w] + " keeps its shared bolt / shard)"); continue; }
            checkedFiles++;
            var t = Read(file);
            var all = t.GetPixels32();
            int rows = t.height / 128;
            Check(Worlds[w] + ": " + Path.GetFileName(file) + " is 256 px wide and whole 128 px rows (" + t.width + " x " + t.height + ")", t.width == 256 && t.height % 128 == 0 && rows >= 2);
            int dups = 0, still = 0, unreadable = 0; string why = "";
            for (int r = 0; r < rows; r++)
            {
                var a = Cell(all, t.width, t.height, 0, r); var b = Cell(all, t.width, t.height, 1, r);
                if (Change(a, b) < .02f) still++;
                string key = Worlds[w] + " row " + (r + 1);
                long h = Hash(a);
                if (seen.ContainsKey(h)) { dups++; why += key + " = " + seen[h] + "; "; } else seen[h] = key;
                Edge(a, out float dark, out float pale, out float core, out int opaque);
                // a dark keyline round the solid body (sparks and trails are bright by design, hence 35 %), no pale rim, a bright core, and it fills part of its cell
                bool ok = dark >= .35f && pale <= .12f && core >= .7f && opaque >= 400 && opaque <= 128 * 128 * .8f;
                if (!ok) { unreadable++; why += key + " (dark " + dark.ToString("P0") + ", pale " + pale.ToString("P0") + ", core " + core.ToString("F2") + ", opaque " + opaque + "); "; }
            }
            Check(Worlds[w] + ": every attack variant animates between its two frames (" + still + " still)", still == 0);
            Check(Worlds[w] + ": no two attacks of any boss share a sprite (" + dups + " repeats " + why + ")", dups == 0);
            Check(Worlds[w] + ": every variant reads on any backdrop: dark edge, no pale rim, bright core (" + unreadable + " fail " + (unreadable > 0 ? why : "") + ")", unreadable == 0);
            Object.DestroyImmediate(t);
        }
        Debug.Log("[SHOTART] " + checkedFiles + " per-attack shot files checked");
    }

    // ---- 4: no pale rim round any boss attack art (the white outline the user rejected) ----

    // the share of a rectangle's silhouette-edge pixels (opaque beside a clear one, or on the rectangle's side edges) that are pale
    static float PaleEdge(Color32[] all, int tw, int th, int x0, int yTop, int w, int h, out int edge)
    {
        int pale = 0; edge = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = all[(th - 1 - (yTop + y)) * tw + x0 + x];
                if (c.a < 200) continue;
                bool border = x == 0 || x == w - 1 ||
                              all[(th - 1 - (yTop + y)) * tw + x0 + x - 1].a < 40 || all[(th - 1 - (yTop + y)) * tw + x0 + x + 1].a < 40 ||
                              (y > 0 && all[(th - 1 - (yTop + y - 1)) * tw + x0 + x].a < 40) || (y < h - 1 && all[(th - 1 - (yTop + y + 1)) * tw + x0 + x].a < 40);
                if (!border) continue;
                edge++;
                if (Mathf.Min(c.r, Mathf.Min(c.g, c.b)) > 205) pale++;
            }
        return edge == 0 ? 0f : pale / (float)edge;
    }

    // the share of a beam body frame's silhouette-edge pixels (any alpha > 20 beside a clear one, or on the side columns) that are dark: a keyline
    static float DarkEdge(Color32[] all, int tw, int th, int x0, int yTop, int w, int h, out int edge)
    {
        int dark = 0; edge = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = all[(th - 1 - (yTop + y)) * tw + x0 + x];
                if (c.a <= 20) continue;
                bool border = x == 0 || x == w - 1 ||
                              all[(th - 1 - (yTop + y)) * tw + x0 + x - 1].a <= 20 || all[(th - 1 - (yTop + y)) * tw + x0 + x + 1].a <= 20;
                if (!border) continue;
                edge++;
                if ((.299f * c.r + .587f * c.g + .114f * c.b) / 255f < .34f) dark++;
            }
        return edge == 0 ? 0f : dark / (float)edge;
    }

    public const float MaxDarkBeamEdge = .15f;
    public const float MaxPaleRim = .2f;

    static void PaleRims()
    {
        int bad = 0, files = 0; string why = "";
        for (int w = 0; w < Worlds.Length; w++)
        {
            string dir = "Assets/Art/Resources/Attacks/" + Worlds[w] + "/" + Worlds[w].ToLowerInvariant() + "_attack_";
            // the beam bodies (4 frames of 128 x 256), the vine lash and the trunk (cells), per world where they exist
            var jobs = new List<(string file, int cw, int ch)> { (dir + "laserbody.png", 128, 256), (dir + "lash.png", 128, 128), (dir + "log.png", 256, 128) };
            foreach (var (file, cw, ch) in jobs)
            {
                if (!File.Exists(file)) continue;
                var t = Read(file);
                var px = t.GetPixels32();
                files++;
                for (int cy = 0; cy + ch <= t.height; cy += ch)
                    for (int cx = 0; cx + cw <= t.width; cx += cw)
                    {
                        float share = PaleEdge(px, t.width, t.height, cx, cy, cw, ch, out int edge);
                        if (edge > 40 && share > MaxPaleRim) { bad++; why += Path.GetFileName(file) + "[" + cx / cw + "," + cy / ch + "] " + share.ToString("P0") + "; "; }
                        // a beam is just its own glowing body with a soft falloff: no dark keyline either (the vine lash and trunk keep their natural dark edge)
                        if (file.EndsWith("laserbody.png"))
                        {
                            float dk = DarkEdge(px, t.width, t.height, cx, cy, cw, ch, out int dedge);
                            if (dedge > 40 && dk > MaxDarkBeamEdge) { bad++; why += Path.GetFileName(file) + "[" + cx / cw + "] dark edge " + dk.ToString("P0") + "; "; }
                        }
                    }
                Object.DestroyImmediate(t);
            }
        }
        Check(files + " boss attack sheets (beam bodies, vine lash, trunk): no cell wears a pale rim (> " + (MaxPaleRim * 100f) + " % of its edge pixels near-white) and no beam body a dark keyline (> " + (MaxDarkBeamEdge * 100f) + " %) (" + bad + " bad " + why + ")", bad == 0 && files >= 7);
    }
}
