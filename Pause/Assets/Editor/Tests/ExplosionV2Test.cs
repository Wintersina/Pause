using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explosions v2: the installed atlas (Art/Resources/Weapons/Explosions.png)
// and the loudness tuning around it (TargetExplosion.Intensity).
//
//   * the atlas keeps the 16 x 6 grid of 128 px cells and its importer
//     settings; no burst frame is dominated by the player's red (#FF3E4E,
//     hue within 28 degrees); every burst stays compact (opaque bounds <= 70%
//     of its cell); the flash / ring overlay cells are white so tinting works
//   * Intensity 1: sizes .61 / .90 / 1.30 u, .65-.80 s, kick <= .03, overlay
//     alpha .7; Intensity .5 and 2 scale them; the body fades out cleanly
//   * at most MaxConcurrentBursts bursts play at full strength: the rest are
//     fainter and flashless
//   * each world's cast bursts in its material (Space metal / rock, Frost
//     ice, Verdant spore, Ember magma, mines as mines)
//   * spawning and ticking explosions allocates nothing per frame
public static class ExplosionV2Test
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EX] PASS  " : "[EX] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string AtlasPath = "Assets/Art/Resources/Weapons/Explosions.png";

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            TargetExplosion.Intensity = 1f;
            AtlasGridAndImporter();
            BurstsAreCompactAndNotRed();
            LoudnessAtIntensityOne();
            IntensityScales();
            BodyFadesOutCleanly();
            CrowdCap();
            MaterialsPerWorld();
            NoPerFrameAllocations();
        }
        finally { TargetExplosion.Intensity = 1f; }
        Debug.Log("[EX] failures: " + fails);
        return fails;
    }

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        camGo.GetComponent<Camera>().orthographic = true;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = 0f;
        Time.timeScale = 1f;
    }

    static void TickAll(float dt)
    {
        foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            fx.Tick(dt);
    }

    static Texture2D atlas;
    static Texture2D Atlas()
    {
        if (atlas != null) return atlas;
        atlas = new Texture2D(2, 2);
        atlas.LoadImage(File.ReadAllBytes(AtlasPath));
        return atlas;
    }

    static void AtlasGridAndImporter()
    {
        var t = Atlas();
        Check("the atlas is 2048 x 768 (16 x 6 cells of 128 px): " + t.width + " x " + t.height,
              t.width == 2048 && t.height == 768);
        var live = Resources.Load<Texture2D>("Weapons/Explosions");
        Check("the live texture imports at full size (no NPOT rescale)", live != null && live.width == 2048 && live.height == 768);
        var s = WeaponArt.Explosion(TargetExplosion.Kind.Magma, 9);
        Check("runtime slicing reads 128 px cells (Magma frame 9 rect " + (s != null ? s.rect.ToString() : "null") + ")",
              s != null && Mathf.Approximately(s.rect.width, 128f) && Mathf.Approximately(s.rect.x, 9 * 128f) &&
              Mathf.Approximately(s.rect.y, 0f) && Mathf.Approximately(s.pixelsPerUnit, 128f));
        var flash = WeaponArt.ExplosionFlash(0);
        var ring = WeaponArt.ExplosionRing();
        Check("flash / ring overlays read row 0 columns 10 and 12",
              flash != null && ring != null && Mathf.Approximately(flash.rect.x, 1280f) && Mathf.Approximately(ring.rect.x, 1536f) &&
              Mathf.Approximately(flash.rect.y, 640f));
        var imp = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        Check("importer: no mipmaps, no NPOT scale, bilinear, clamp, 2048, alpha is transparency",
              imp != null && !imp.mipmapEnabled && imp.npotScale == TextureImporterNPOTScale.None &&
              imp.filterMode == FilterMode.Bilinear && imp.wrapMode == TextureWrapMode.Clamp &&
              imp.maxTextureSize == 2048 && imp.alphaIsTransparency);
        Check("the v1 atlas is kept for rollback outside Resources",
              File.Exists("Assets/Art/Weapons/ExplosionsV1~/Explosions.png"));
    }

    static void BurstsAreCompactAndNotRed()
    {
        var t = Atlas();
        Color.RGBToHSV(new Color(1f, .243f, .306f), out float redH, out _, out _);
        float worstRed = 0f, worstBounds = 0f;
        string worstRedAt = "", worstBoundsAt = "";
        for (int row = 0; row < 6; row++)
            for (int col = 0; col < 10; col++)
            {
                var px = t.GetPixels(col * 128, (5 - row) * 128, 128, 128);
                int lit = 0, red = 0, x0 = 128, x1 = -1, y0 = 128, y1 = -1;
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    if (c.a > .1f)
                    {
                        int x = i % 128, y = i / 128;
                        if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                    }
                    if (c.a < .25f) continue;
                    Color.RGBToHSV(c, out float h, out float sat, out float v);
                    if (sat < .35f || v < .25f) continue;
                    lit++;
                    float d = Mathf.Abs(h - redH) * 360f;
                    if (Mathf.Min(d, 360f - d) <= 28f) red++;
                }
                float fr = lit > 0 ? red / (float)lit : 0f;
                float b = x1 < 0 ? 0f : Mathf.Max(x1 - x0 + 1, y1 - y0 + 1) / 128f;
                string at = (TargetExplosion.Kind)row + " f" + col;
                if (fr > worstRed) { worstRed = fr; worstRedAt = at; }
                if (b > worstBounds) { worstBounds = b; worstBoundsAt = at; }
            }
        Check("no burst frame is dominated by the player's red hue (worst " + worstRedAt + " " + worstRed.ToString("0.00") + " <= .15)",
              worstRed <= .15f);
        Check("every burst stays compact (worst opaque bounds " + worstBoundsAt + " " + worstBounds.ToString("0.00") + " of the cell <= .70)",
              worstBounds <= .70f);

        // the flash / ring cells are white (or grey): the weapon tint colours them
        float maxSat = 0f;
        for (int col = 10; col <= 12; col++)
        {
            var px = t.GetPixels(col * 128, 5 * 128, 128, 128);
            int lit = 0;
            float maxA = 0f;
            foreach (var c in px)
            {
                maxA = Mathf.Max(maxA, c.a);
                if (c.a < .05f) continue;
                lit++;
                Color.RGBToHSV(c, out _, out float sat, out _);
                maxSat = Mathf.Max(maxSat, sat);
            }
            // v2 overlays are soft glows (flash core ~.8 alpha falling to ~.05,
            // the ring ~.23): present, but never a hard opaque star
            Check("overlay cell " + col + " has soft art (" + lit + " px, max alpha " + maxA.ToString("0.00") + " in .15-.9)",
                  lit > 20 && maxA >= .15f && maxA <= .9f);
        }
        Check("overlay cells are untinted white (max saturation " + maxSat.ToString("0.00") + ")", maxSat <= .1f);
    }

    static bool Near(float a, float b, float tol) => Mathf.Abs(a - b) <= tol;

    static void LoudnessAtIntensityOne()
    {
        TargetExplosion.Intensity = 1f;
        var M = TargetExplosion.Kind.Metal;
        float s = TargetExplosion.WorldSizeFor(TargetExplosion.Size.Small, M);
        float m = TargetExplosion.WorldSizeFor(TargetExplosion.Size.Medium, M);
        float l = TargetExplosion.WorldSizeFor(TargetExplosion.Size.Large, M);
        Check("Intensity 1 sizes ~ .62 / .90 / 1.30 u (" + s.ToString("0.00") + " / " + m.ToString("0.00") + " / " + l.ToString("0.00") + ")",
              Near(s, .62f, .02f) && Near(m, .9f, .02f) && Near(l, 1.3f, .02f));
        Check("big targets still burst bigger (mines 15% up)", s < m && m < l &&
              Near(TargetExplosion.WorldSizeFor(TargetExplosion.Size.Medium, TargetExplosion.Kind.Mine), m * 1.15f, .001f));
        float hs = TargetExplosion.SecondsFor(TargetExplosion.Size.Small), hl = TargetExplosion.SecondsFor(TargetExplosion.Size.Large);
        Check("a burst lasts .65 - .80 s (" + hs.ToString("0.00") + " - " + hl.ToString("0.00") + ")",
              hs >= .62f && hl <= .82f && hs < hl);
        float ks = TargetExplosion.KickFor(TargetExplosion.Size.Small), km = TargetExplosion.KickFor(TargetExplosion.Size.Medium),
              kl = TargetExplosion.KickFor(TargetExplosion.Size.Large);
        Check("camera kick is half the old one and never above .03 (" + ks + " / " + km + " / " + kl + ")",
              ks == 0f && Near(km, .014f, .001f) && Near(kl, .0275f, .001f) && kl <= .03f);
        Check("flash / ring alpha .7", Near(TargetExplosion.OverlayAlphaNow, .7f, .001f));
        Check("crowd cap is 8 bursts", TargetExplosion.MaxConcurrentBursts == 8);
    }

    static void IntensityScales()
    {
        var L = TargetExplosion.Size.Large;
        var M = TargetExplosion.Kind.Metal;
        TargetExplosion.Intensity = 1f;
        float size1 = TargetExplosion.WorldSizeFor(L, M), hold1 = TargetExplosion.SecondsFor(L),
              kick1 = TargetExplosion.KickFor(L), alpha1 = TargetExplosion.OverlayAlphaNow;
        TargetExplosion.Intensity = .5f;
        bool half = Near(TargetExplosion.WorldSizeFor(L, M), size1 * .5f, .001f) &&
                    Near(TargetExplosion.KickFor(L), kick1 * .5f, .0005f) &&
                    Near(TargetExplosion.OverlayAlphaNow, alpha1 * .5f, .001f) &&
                    Near(TargetExplosion.SecondsFor(L), hold1 * Mathf.Sqrt(.5f), .005f);
        Check("Intensity .5 halves size / kick / overlay alpha, hold x sqrt(.5)", half);
        TargetExplosion.Intensity = 2f;
        bool dbl = Near(TargetExplosion.WorldSizeFor(L, M), size1 * 2f, .001f) &&
                   Near(TargetExplosion.KickFor(L), kick1 * 2f, .0005f) &&
                   Near(TargetExplosion.OverlayAlphaNow, 1f, .001f) &&
                   Near(TargetExplosion.SecondsFor(L), hold1 * Mathf.Sqrt(2f), .005f);
        Check("Intensity 2 doubles size / kick, overlay alpha saturates at 1, hold x sqrt(2)", dbl);
        TargetExplosion.Intensity = 1f;
    }

    static void BodyFadesOutCleanly()
    {
        FreshScene();
        TargetExplosion.Spawn(Vector3.zero, TargetExplosion.Kind.Rock, TargetExplosion.Size.Large, 1);
        FlipbookFx body = null;
        foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (fx.Active && fx.CurrentMode == FlipbookFx.Mode.Explosion) body = fx;
        Check("a burst body is playing at full alpha", body != null && Near(body.Alpha, 1f, .001f));
        if (body == null) return;
        float total = TargetExplosion.SecondsFor(TargetExplosion.Size.Large), t = 0f, last = 1f;
        bool monotonic = true;
        float beforeEnd = 1f;
        const float dt = 1f / 60f;
        while (body.Active && t < 3f)
        {
            body.Tick(dt);
            t += dt;
            if (body.Active)
            {
                if (body.Alpha > last + 1e-4f && t > total * .5f) monotonic = false;
                last = body.Alpha;
                if (t >= total - 2f * dt) beforeEnd = Mathf.Min(beforeEnd, body.Alpha);
            }
        }
        Check("the body eases out to transparent before it ends (last alpha " + beforeEnd.ToString("0.00") + ")",
              monotonic && beforeEnd <= .1f);
        Check("it is gone on time (" + t.ToString("0.00") + " s for " + total.ToString("0.00") + " s)",
              !body.Active && t <= total + .05f);
    }

    static void CrowdCap()
    {
        FreshScene();
        for (int i = 0; i < 14; i++)
            TargetExplosion.Spawn(new Vector3(i * .3f, 0f, 0f), TargetExplosion.Kind.Metal, TargetExplosion.Size.Large, 1);
        int bodies = 0, full = 0, faint = 0, flashes = 0;
        foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!fx.Active) continue;
            if (fx.CurrentMode == FlipbookFx.Mode.Flash) flashes++;
            if (fx.CurrentMode != FlipbookFx.Mode.Explosion) continue;
            bodies++;
            if (Near(fx.Alpha, 1f, .001f)) full++;
            else if (Near(fx.Alpha, TargetExplosion.CrowdAlpha, .001f)) faint++;
        }
        Check("14 simultaneous bursts: " + full + " full + " + faint + " faint bodies, " + flashes + " flashes (cap 8)",
              bodies == 14 && full == TargetExplosion.MaxConcurrentBursts && faint == 14 - full &&
              flashes == TargetExplosion.MaxConcurrentBursts);
        Check("ActiveBursts counts the bodies", TargetExplosion.ActiveBursts == 14 && TargetExplosion.Crowded);
        for (int i = 0; i < 120; i++) TickAll(1f / 60f);
        Check("once they finish the cap frees up", TargetExplosion.ActiveBursts == 0 && !TargetExplosion.Crowded);
    }

    static void MaterialsPerWorld()
    {
        Check("world keys map to materials",
              TargetExplosion.KindForWorld("space") == TargetExplosion.Kind.Metal &&
              TargetExplosion.KindForWorld("Frost") == TargetExplosion.Kind.Ice &&
              TargetExplosion.KindForWorld("verdant") == TargetExplosion.Kind.Spore &&
              TargetExplosion.KindForWorld("Ember") == TargetExplosion.Kind.Magma &&
              TargetExplosion.KindForWorld(null) == TargetExplosion.Kind.Metal);
        string wrong = "";
        foreach (var d in EnemyRoster.All)
        {
            TargetExplosion.Kind want;
            if (d.role == EnemyRole.Mine) want = TargetExplosion.Kind.Mine;
            else if (d.world == 0) want = d.role == EnemyRole.Rock ? TargetExplosion.Kind.Rock : TargetExplosion.Kind.Metal;
            else want = EnemyPalette.ThemeFor(d.world).explosion;
            if (d.explosion != want) wrong += " " + d.key + "=" + d.explosion;
        }
        Check("every roster enemy bursts in its world's material" + (wrong.Length > 0 ? " (wrong:" + wrong + ")" : ""), wrong.Length == 0);
        string elites = "";
        foreach (var e in EliteCatalog.All)
            if (string.IsNullOrEmpty(e.world)) elites += " " + e.key;
        Check("every elite names its world (its burst material)" + elites, elites.Length == 0);
    }

    static void NoPerFrameAllocations()
    {
        FreshScene();
        // warm the pool and the sprite sheet
        for (int i = 0; i < 6; i++)
            TargetExplosion.Spawn(new Vector3(i * .4f, 0f, 0f), (TargetExplosion.Kind)i, TargetExplosion.Size.Medium, 1);
        for (int i = 0; i < 120; i++) TickAll(1f / 60f);
        var books = Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool meter = TestHarness.AllocMeterWorks(out long control);
        Check("the allocation meter passes its positive control (" + control + " bytes)", meter);
        long bytes = TestHarness.AllocatedBytes(() =>
        {
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < 6; i++)
                    TargetExplosion.Spawn(new Vector3(i * .4f, 0f, 0f), (TargetExplosion.Kind)i, TargetExplosion.Size.Medium, 1);
                for (int f = 0; f < 60; f++)
                    for (int b = 0; b < books.Length; b++) books[b].Tick(1f / 60f);
            }
        });
        Check("spawning and ticking pooled explosions allocates nothing (" + bytes + " bytes)", bytes == 0);
    }
}
