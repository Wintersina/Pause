using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// THE VOID ARCHON'S PAINTED POD LASER (Art/Resources/Attacks/Space/space_attack_laser*.png, AttackArt.SpaceLaser, BossBeam).
//
//   * the three slots exist with the right sizes, imported point-filtered, uncompressed, mip-free; the kit resolves to its
//     8 windup / 4 muzzle / 4 fade / 4 impact / 4 spark / 4 body / 2 sight / 2 lock cells; texture memory within budget;
//   * every loop animates (>= 3 % of its pixels change per frame, wrap included); the beam body tiles with no seam
//     (its bottom row continues into its top row as smoothly as any two rows do) and its opaque columns are exactly
//     AttackArt.SpaceLaserBodyOpaquePx wide;
//   * BossBeam, headless, on the pod lasers: the tell, hold and width are the old numbers, the hit shape is width x
//     BeamHitFraction, the painted body's opaque width equals the hit width, the windup plays at the pod across the tell,
//     the sight line and lock reticle show, the muzzle loops while live and the fade frames play on the way out, the
//     impact and sparks play where it meets a rail; stepping it allocates nothing.
public static class SpaceLaserArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SPACELASER] PASS  " : "[SPACELASER] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    const string Dir = "Assets/Art/Resources/Attacks/Space/";
    public const long MemoryBudget = 2560 * 1024;   // bytes: the three sheets are 2.25 MB uncompressed RGBA32

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Files();
                Animation();
                Beam();
            }
            finally { BossEncounter.ResetRun(); BossRails.Reset(); AttackArt.Clear(); }
        }
        Debug.Log("[SPACELASER] failures: " + fails);
        return fails;
    }

    // ---- the files ----

    static readonly string[] Names = { "space_attack_laser", "space_attack_laserbody", "space_attack_lasertell" };
    static readonly Vector2Int[] Sizes = { new Vector2Int(1024, 384), new Vector2Int(512, 256), new Vector2Int(512, 128) };

    static Texture2D Read(string name)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(Dir + name + ".png"));
        return t;
    }

    static void Files()
    {
        long bytes = 0;
        for (int i = 0; i < Names.Length; i++)
        {
            var imp = AssetImporter.GetAtPath(Dir + Names[i] + ".png") as TextureImporter;
            Check(Names[i] + ": imported", imp != null);
            if (imp != null)
                Check(Names[i] + ": point filter, no mipmaps, uncompressed, not readable, no rescale",
                      imp.filterMode == FilterMode.Point && !imp.mipmapEnabled && imp.textureCompression == TextureImporterCompression.Uncompressed &&
                      !imp.isReadable && imp.npotScale == TextureImporterNPOTScale.None);
            var tex = AttackArt.Atlas(0, Names[i].Substring("space_attack_".Length));
            Check(Names[i] + ": served by AttackArt as Attacks/Space/" + Names[i] + " at " + Sizes[i].x + " x " + Sizes[i].y,
                  tex != null && tex.width == Sizes[i].x && tex.height == Sizes[i].y);
            if (tex != null) bytes += tex.width * tex.height * (tex.format == TextureFormat.RGBA32 ? 4 : 99);   // the GPU copy (the editor's runtime-size figure also counts its CPU copy)
        }
        Debug.Log("[SPACELASER] texture memory " + (bytes / 1024) + " KB (budget " + (MemoryBudget / 1024) + " KB)");
        Check("texture memory " + (bytes / 1024) + " KB <= " + (MemoryBudget / 1024) + " KB", bytes > 0 && bytes <= MemoryBudget);

        var a = AttackArt.SpaceLaser();
        Check("the kit resolves", a != null);
        if (a == null) return;
        Check("8 windup, 4 muzzle, 4 fade, 4 impact, 4 spark, 4 body, 2 sight, 2 lock cells",
              a.windup.Length == 8 && a.muzzle.Length == 4 && a.fade.Length == 4 && a.impact.Length == 4 && a.spark.Length == 4 &&
              a.body.Length == 4 && a.sight.Length == 2 && a.lockOn.Length == 2 && !System.Array.Exists(a.body, s => s == null));
        Check("body cells are 1 u x 2 u, the others 1 u square",
              Mathf.Approximately(a.body[0].bounds.size.x, 1f) && Mathf.Approximately(a.body[0].bounds.size.y, 2f) &&
              Mathf.Approximately(a.windup[0].bounds.size.x, 1f) && Mathf.Approximately(a.sight[0].bounds.size.y, 1f));
    }

    // ---- the animation ----

    static Color32[] Slice(Texture2D t, int x, int yTop, int w, int h)
    {
        var all = t.GetPixels32();
        var o = new Color32[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++) o[j * w + i] = all[(t.height - 1 - (yTop + j)) * t.width + x + i];   // row 0 = top
        return o;
    }

    // The share of pixels (of those opaque in either) that differ.
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

    static void Loop(string what, Texture2D t, int[] xs, int[] ys, int w, int h, bool wrap)
    {
        float min = 1f;
        for (int i = 0; i < xs.Length; i++)
        {
            int n = i + 1;
            if (n == xs.Length) { if (!wrap) break; n = 0; }
            min = Mathf.Min(min, Change(Slice(t, xs[i] * w, ys[i] * h, w, h), Slice(t, xs[n] * w, ys[n] * h, w, h)));
        }
        Check(what + " advances >= 3 % of its pixels every frame (min " + (min * 100f).ToString("F1") + " %)", min >= .03f);
    }

    static void Animation()
    {
        var laser = Read(Names[0]); var body = Read(Names[1]); var tell = Read(Names[2]);
        Loop("windup 1-8", laser, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[] { 0, 0, 0, 0, 0, 0, 0, 0 }, 128, 128, false);
        Loop("muzzle a-d (loops)", laser, new[] { 0, 1, 2, 3 }, new[] { 1, 1, 1, 1 }, 128, 128, true);
        Loop("fade 1-4", laser, new[] { 4, 5, 6, 7 }, new[] { 1, 1, 1, 1 }, 128, 128, false);
        Loop("impact 1-4", laser, new[] { 0, 1, 2, 3 }, new[] { 2, 2, 2, 2 }, 128, 128, false);
        Loop("spark a-d (loops)", laser, new[] { 4, 5, 6, 7 }, new[] { 2, 2, 2, 2 }, 128, 128, true);
        Loop("body 1-4 (loops)", body, new[] { 0, 1, 2, 3 }, new[] { 0, 0, 0, 0 }, 128, 256, true);
        Loop("sight a,b (loops)", tell, new[] { 0, 1 }, new[] { 0, 0 }, 128, 128, true);
        Loop("lock a,b (loops)", tell, new[] { 2, 3 }, new[] { 0, 0 }, 128, 128, true);

        // the body: opaque columns are the claimed width, and tile with no seam
        int lo = 999, hi = -1;
        float worstSeam = 0f;
        for (int f = 0; f < 4; f++)
        {
            var px = Slice(body, f * 128, 0, 128, 256);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 128; x++)
                    if (px[y * 128 + x].a > 0) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
            float seam = RowDiff(px, 255, 0), adjacent = 0f;
            for (int y = 0; y < 255; y++) adjacent += RowDiff(px, y, y + 1);
            adjacent /= 255f;
            worstSeam = Mathf.Max(worstSeam, seam - adjacent);
        }
        Check("beam body: opaque columns " + lo + ".." + hi + " are " + (hi - lo + 1) + " px = SpaceLaserBodyOpaquePx (" + AttackArt.SpaceLaserBodyOpaquePx + ")",
              hi - lo + 1 == AttackArt.SpaceLaserBodyOpaquePx);
        Check("beam body tiles vertically: the bottom row meets the top row about as smoothly as any two rows (excess " + worstSeam.ToString("F1") + ")", worstSeam < 24f);
        Object.DestroyImmediate(laser); Object.DestroyImmediate(body); Object.DestroyImmediate(tell);
    }

    // mean per-pixel channel difference between two rows
    static float RowDiff(Color32[] px, int y0, int y1)
    {
        float d = 0f;
        for (int x = 0; x < 128; x++)
        {
            var a = px[y0 * 128 + x]; var b = px[y1 * 128 + x];
            d += Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
        }
        return d / 128f;
    }

    // ---- BossBeam ----

    static SpriteRenderer Piece(BossBeam b, string name) => b.transform.Find(name).GetComponent<SpriteRenderer>();

    static void Beam()
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

        var boss = BossCatalog.ForWorld(0);
        var atk = System.Array.Find(boss.DefaultAttacks, x => x.name == "pod lasers");
        Check("pod lasers keep their numbers: tell .95 s, hold .9 s, width .32, hit fraction .7, 2 parts",
              atk != null && Mathf.Approximately(atk.tellSeconds, .95f) && Mathf.Approximately(atk.hold, .9f) &&
              Mathf.Approximately(atk.beamWidth, .32f) && Mathf.Approximately(BossConfig.BeamHitFraction, .7f) && atk.parts.Length == 2);
        if (atk == null || AttackArt.SpaceLaser() == null) return;
        var art = AttackArt.SpaceLaser();

        var root = new GameObject("~Beams").transform;
        var b = BossBeam.Create(root);
        b.Begin(boss, null, -1, new Vector3(1.2f, 3f, 0f), 0f, 0f, atk.tellSeconds, atk.hold, atk.beamWidth);   // heading +x: ends on the right rail
        var sight = Piece(b, "Sight"); var beam = Piece(b, "Beam"); var flash = Piece(b, "Flash");
        var impact = Piece(b, "Impact"); var spark = Piece(b, "ImpactSpark"); var lockOn = Piece(b, "Lock");

        // the tell: windup spread across it at the pod, sight line + reticle
        bool first = false, last = false, sightSeen = false, lockSeen = false, hit = false, monotonic = true;
        int prevIdx = -1;
        float t = 0f;
        while (!b.Live && t < 3f)
        {
            b.Step(Dt); t += Dt;
            int idx = System.Array.IndexOf(art.windup, flash.sprite);
            if (flash.enabled && idx >= 0) { monotonic &= idx >= prevIdx; prevIdx = idx; first |= idx == 0; last |= idx == 7; }
            sightSeen |= sight.enabled && sight.drawMode == SpriteDrawMode.Tiled && System.Array.IndexOf(art.sight, sight.sprite) >= 0;
            lockSeen |= lockOn.enabled && System.Array.IndexOf(art.lockOn, lockOn.sprite) >= 0;
            hit |= !b.Live && b.Hitbox != null;
        }
        Check("the tell lasts " + atk.tellSeconds + " s (" + t.ToString("F2") + ")", Mathf.Abs(t - atk.tellSeconds) < .05f);
        Check("windup 1..8 plays at the pod across the tell, in order", first && last && monotonic && flash.transform.localPosition == Vector3.zero);
        Check("the sight line is the tell sheet's, tiled", sightSeen);
        Check("the lock reticle shows", lockSeen);
        Check("nothing hurts during the tell", !hit);

        // live: the body tiles from the root, its opaque width is the hit width, hit shape unchanged
        for (int i = 0; i < 6; i++) b.Step(Dt);
        Check("it ignites", b.Live);
        float hitW = atk.beamWidth * BossConfig.BeamHitFraction;
        var box = b.Hitbox.GetComponent<BoxCollider2D>();
        Check("hit shape: " + hitW.ToString("F3") + " u wide (" + box.size.x.ToString("F3") + ")", Mathf.Abs(box.size.x - hitW) < 1e-4f);
        Check("the body is tiled, beam sprites come from the body sheet", beam.drawMode == SpriteDrawMode.Tiled && System.Array.IndexOf(art.body, beam.sprite) >= 0);
        float opaqueW = beam.transform.lossyScale.x * AttackArt.SpaceLaserBodyOpaquePx / 128f;
        Check("its opaque part (" + opaqueW.ToString("F3") + " u) is within the hit shape (" + hitW.ToString("F3") + " u)", opaqueW <= hitW + 1e-4f);
        var seen = new System.Collections.Generic.HashSet<Sprite>();
        var muz = new System.Collections.Generic.HashSet<Sprite>();
        for (int i = 0; i < 40; i++) { b.Step(Dt); seen.Add(beam.sprite); if (flash.enabled) muz.Add(flash.sprite); }
        Check("the body animates through its 4 frames and the muzzle through its 4 (" + seen.Count + ", " + muz.Count + ")",
              seen.Count == 4 && muz.Count == 4 && muz.SetEquals(art.muzzle));
        bool onRail = b.EndsOnRail && b.Length >= b.Reach - 1e-3f;
        Check("it reaches the rail", onRail);
        Check("the impact and sparks play where it meets the rail",
              impact.enabled && System.Array.IndexOf(art.impact, impact.sprite) >= 0 && spark.enabled && System.Array.IndexOf(art.spark, spark.sprite) >= 0 &&
              Mathf.Abs(spark.transform.localPosition.y - b.Length) < 1e-3f);
        Check("the sight line is gone while live", !sight.enabled && !lockOn.enabled);

        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8; i++) b.Step(Dt * .1f);
        long used = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("steady steps allocate nothing (" + used + " bytes)", used == 0);

        // the fade
        bool fadeSeen = false;
        for (int i = 0; i < 120 && b.Active; i++)
        {
            b.Step(Dt);
            if (b.Active && !b.Live && System.Array.IndexOf(art.fade, flash.sprite) >= 0) fadeSeen = true;
        }
        Check("the fade frames play on the way out, then it recycles", fadeSeen && !b.Active);
    }

}
