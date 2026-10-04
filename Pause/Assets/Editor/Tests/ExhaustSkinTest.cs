using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The engine exhaust follows the ship's skin "somewhat" (ExhaustColors.For,
// ExhaustRemap):
//   - the stock skin draws exactly the stock exhaust: stock palette, default
//     sprite material, no property block
//   - a non-stock skin moves the outer / mid bands measurably toward the
//     skin (part of the way, within the blend), keeps the hot core and any
//     friendly-red accent
//   - the remap of the real atlas texels lands on those colours
//   - equipping / previewing a skin re-colours plumes, spin drifts and the
//     home-screen traffic's exhaust, also while the world is frozen
public static class ExhaustSkinTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[XS] PASS  " : "[XS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        ResetSkins();

        StockIsUnchanged();
        SkinsShiftTheBands();
        AtlasTexelsRemap();
        PlumeFollowsTheSkin();
        SpinDriftFollowsTheSkin();
        TrafficFollowsTheSkin();

        ResetSkins();
        Debug.Log("[XS] failures: " + fails);
        return fails;
    }

    static void ResetSkins()
    {
        ShipSkins.ClearPreview();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        for (int id = 0; id <= shopingShips.shipTotal; id++)
        {
            PlayerPrefs.DeleteKey(ShipSkins.EquippedKey(id));
            PlayerPrefs.DeleteKey(ShipSkins.DeveloperEquippedKey(id));
            for (int n = 0; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
    }

    static void Wear(int id, int skin)
    {
        if (skin != ShipSkins.Stock) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, skin), 1);
        ShipSkins.Equip(id, skin);
    }

    static float Dist(Color a, Color b)
    {
        float r = a.r - b.r, g = a.g - b.g, bl = a.b - b.b;
        return Mathf.Sqrt(r * r + g * g + bl * bl);
    }

    static float Lab(Color a, Color b) { return (ExhaustColors.ToOklab(a) - ExhaustColors.ToOklab(b)).magnitude; }

    // Distance in colour only (OKLab a/b), ignoring lightness: flame bands
    // keep their own brightness, so "toward the skin" is about hue/chroma.
    static float Chroma(Color a, Color b)
    {
        Vector3 x = ExhaustColors.ToOklab(a), y = ExhaustColors.ToOklab(b);
        return new Vector2(x.y - y.y, x.z - y.z).magnitude;
    }

    static bool Exact(Color a, Color b) { return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a; }

    static bool SamePalette(ExhaustColors.Palette a, ExhaustColors.Palette b)
    {
        for (int i = 0; i < ExhaustColors.Palette.Bands; i++) if (!Exact(a[i], b[i])) return false;
        return true;
    }

    static float HueOf(Color c)
    {
        var lab = ExhaustColors.ToOklab(c);
        return Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg;
    }

    static float HueGap(Color a, Color b) { return Mathf.Abs(Mathf.DeltaAngle(HueOf(a), HueOf(b))); }

    // ------------------------------------------------------------ palettes

    static void StockIsUnchanged()
    {
        bool same = true, shown = true, texels = true;
        foreach (int id in ShipId.All)
        {
            var stock = ExhaustColors.Stock(id);
            var style = ShipExhaustStyle.For(id);
            same &= SamePalette(stock, ExhaustColors.For(id, ShipSkins.Stock));
            same &= Exact(stock.outer, style.outer) && Exact(stock.mid, style.mid) && Exact(stock.core, style.core)
                 && Exact(stock.dark, style.dark) && Exact(stock.accent, style.accent);
            shown &= SamePalette(stock, ExhaustColors.For(id));
            for (int i = 0; i < ExhaustColors.Palette.Bands; i++)
                texels &= Exact(ExhaustRemap.RemapColor(stock[i], stock, stock), stock[i]);
        }
        Check("stock skin: exhaust palette is the drawn palette, every ship", same);
        Check("stock skin equipped: ExhaustColors.For is the drawn palette", shown);
        Check("stock palette remaps every texel to itself", texels);
        Check("blend knob is about half", ExhaustColors.SkinBlend >= .35f && ExhaustColors.SkinBlend <= .65f);

        // A stock renderer: default sprite material, no remap block.
        var go = new GameObject("~xs-stock", typeof(SpriteRenderer));
        var sr = go.GetComponent<SpriteRenderer>();
        var before = sr.sharedMaterial;
        ExhaustRemap.Apply(sr, 3);
        Check("stock skin keeps the default sprite material", sr.sharedMaterial == before
              && sr.sharedMaterial != null && sr.sharedMaterial.shader.name == "Sprites/Default");
        Check("stock skin sets no remap property block", !sr.HasPropertyBlock() && !ExhaustRemap.IsRemapped(sr));
        Object.DestroyImmediate(go);
    }

    static void SkinsShiftTheBands()
    {
        float t = ExhaustColors.SkinBlend;
        int checkedBands = 0;
        foreach (int id in ShipId.All)
        {
            var stock = ExhaustColors.Stock(id);
            for (int skin = 1; skin < ShipSkins.CountFor(id); skin++)
            {
                var s = ShipSkins.Get(id, skin);
                var p = ExhaustColors.For(id, skin);
                string who = ShipId.NameOf(id) + " " + s.name;

                Check(who + ": core stays within .01 of the hot core", Dist(p.core, stock.core) < .01f);
                if (ExhaustColors.IsFriendlyRed(stock.accent))
                    Check(who + ": friendly-red accent unchanged", Exact(p.accent, stock.accent));

                // Bands toward the skin: measurably, but only part of the way.
                Band(who + " outer", stock.outer, p.outer, s.primary, t, ref checkedBands);
                Band(who + " mid", stock.mid, p.mid, s.highlight, t, ref checkedBands);
            }
        }
        Check("checked a meaningful number of shifted bands (" + checkedBands + ")", checkedBands > 80);

        // The brief's example: Night Solar Fang -- indigo / cyan bands, warm core.
        var night = ExhaustColors.For(3, 1);
        var stockFang = ExhaustColors.Stock(3);
        Check("Night Solar Fang: outer band turns blue-violet (" + ColorUtility.ToHtmlStringRGB(night.outer) + ")",
              night.outer.b > night.outer.r && night.outer.b > night.outer.g);
        Check("Night Solar Fang: mid band turns cyan-ward (" + ColorUtility.ToHtmlStringRGB(night.mid) + ")",
              night.mid.g > stockFang.mid.g - .05f && night.mid.b > stockFang.mid.b + .3f);
        Check("Night Solar Fang: warm-white core kept", Exact(night.core, stockFang.core));
        Check("Night Solar Fang: flame stays bright (outer not darkened)",
              ExhaustColors.ToOklab(night.outer).x >= ExhaustColors.ToOklab(stockFang.outer).x - .02f);

        // No two stock bands that shift differently share a colour (the
        // remap could not tell them apart).
        bool distinct = true;
        foreach (int id in ShipId.All)
        {
            var stock = ExhaustColors.Stock(id);
            for (int skin = 1; skin < ShipSkins.CountFor(id); skin++)
            {
                var p = ExhaustColors.For(id, skin);
                for (int a = 0; a < ExhaustColors.Palette.Bands; a++)
                    for (int b = a + 1; b < ExhaustColors.Palette.Bands; b++)
                        if (Dist(stock[a], stock[b]) < .01f && Dist(p[a], p[b]) > .01f) distinct = false;
            }
        }
        Check("no two exhaust bands share a colour but shift apart", distinct);
    }

    static void Band(string who, Color stock, Color shown, Color target, float t, ref int counted)
    {
        float before = Chroma(stock, target);
        if (before < .04f) return;             // already the skin's colour
        counted++;
        float after = Chroma(shown, target);
        float moved = Chroma(stock, shown);
        float hueBefore = HueGap(stock, target), hueAfter = HueGap(shown, target);
        bool grey = new Vector2(ExhaustColors.ToOklab(target).y, ExhaustColors.ToOklab(target).z).magnitude < .02f;
        bool toward = after < before - .015f || (!grey && hueAfter < hueBefore - 5f);
        Check(who + " moves toward the skin (" + before.ToString("F3") + " -> " + after.ToString("F3") + ")",
              toward && moved > .015f);
        // Part of the way: against the target as a flame band can reach it
        // (never darker than drawn), between a twelfth (hue leads) and four fifths
        // of the distance is left.
        Vector3 a = ExhaustColors.ToOklab(stock), b = ExhaustColors.ToOklab(target), c = ExhaustColors.ToOklab(shown);
        if (b.x < a.x) b.x = a.x;
        float total = (b - a).magnitude, left = (b - c).magnitude;
        Check(who + " moves only part of the way (" + left.ToString("F3") + " of " + total.ToString("F3") + " left)",
              total < .04f || (left > total * .08f && left < total * .85f));
    }

    // The real atlas texels: a texel drawn in a band colour shows that band's
    // remapped colour; the core texels stay put.
    static void AtlasTexelsRemap()
    {
        string path = Path.Combine(Application.dataPath, "Art/Resources/ShipArt/Exhaust/exhaust_atlas.png");
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        var pixels = tex.GetPixels32();
        foreach (int id in new[] { 1, 3, 6, 13 })
        {
            var stock = ExhaustColors.Stock(id);
            var p = ExhaustColors.For(id, 1);
            Color32 outer = stock.outer, core = stock.core;
            int outerHits = 0, coreHits = 0;
            float outerErr = 0f, coreErr = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                var px = pixels[i];
                if (px.a < 40) continue;
                if (px.r == outer.r && px.g == outer.g && px.b == outer.b)
                {
                    outerHits++;
                    outerErr = Mathf.Max(outerErr, Dist(ExhaustRemap.RemapColor(px, stock, p), p.outer));
                }
                else if (px.r == core.r && px.g == core.g && px.b == core.b && coreHits < 4000)
                {
                    coreHits++;
                    coreErr = Mathf.Max(coreErr, Dist(ExhaustRemap.RemapColor(px, stock, p), stock.core));
                }
            }
            string who = ShipId.NameOf(id) + " Night";
            Check(who + ": outer texels show the skin's outer band (" + outerHits + ", err " + outerErr.ToString("F3") + ")",
                  outerHits > 0 && outerErr < .02f);
            // Core texels are shared by many ships; with this ship's palette
            // they barely move (the core shift is zero).
            Check(who + ": core texels stay within .03 (" + coreHits + ", err " + coreErr.ToString("F3") + ")",
                  coreHits > 0 && coreErr < .03f);
        }
        Object.DestroyImmediate(tex);
    }

    // ------------------------------------------------------------ renderers

    static void PlumeFollowsTheSkin()
    {
        ResetSkins();
        var go = new GameObject("~xs-plume", typeof(SpriteRenderer));
        var sr = go.GetComponent<SpriteRenderer>();
        var stockMaterial = sr.sharedMaterial;
        var book = ShipFlameFlipbook.Attach(sr, 0, 3);
        Check("plume starts stock (no remap)", !ExhaustRemap.IsRemapped(sr) && sr.sharedMaterial == stockMaterial);

        Wear(3, 1);
        float timeScale = Time.timeScale;
        Time.timeScale = 0f;
        int frame = book.FrameShown;
        book.Step(0f);
        Check("equipping a skin re-colours the plume (frozen world too)", ExhaustRemap.IsRemapped(sr)
              && sr.sharedMaterial != null && sr.sharedMaterial.shader.name == ExhaustRemap.ShaderName);
        Check("frozen world: the plume keeps its drawing", book.FrameShown == frame);
        Time.timeScale = timeScale;

        ShipSkins.SetPreview(3, 2);
        book.Step(0f);
        var block = new MaterialPropertyBlock();
        sr.GetPropertyBlock(block);
        var shift = block.GetVector("_ExShift0");
        var want = ExhaustColors.For(3, 2).outer - ExhaustColors.Stock(3).outer;
        Check("a dock preview re-colours the plume to the previewed skin",
              Mathf.Abs(shift.x - want.r) < .002f && Mathf.Abs(shift.z - want.b) < .002f);

        ShipSkins.ClearPreview();
        Wear(3, ShipSkins.Stock);
        book.Step(0f);
        Check("back to stock: default material, remap off", !ExhaustRemap.IsRemapped(sr)
              && sr.sharedMaterial == stockMaterial);

        // Another ship's skin change leaves this plume's colours alone.
        Wear(5, 1);
        book.Step(0f);
        Check("another ship's skin doesn't touch this plume", !ExhaustRemap.IsRemapped(sr));
        Object.DestroyImmediate(go);

        // The shader is in Resources (ships in builds) and compiles.
        var shader = Shader.Find(ExhaustRemap.ShaderName);
        Check("ExhaustRemap shader found and supported", shader != null && shader.isSupported);
        Check("ExhaustRemap shader lives under Resources",
              AssetDatabase.GetAssetPath(shader).Contains("/Resources/"));
        ResetSkins();
    }

    static void SpinDriftFollowsTheSkin()
    {
        ResetSkins();
        foreach (int id in new[] { 11, 13 })
        {
            var go = new GameObject("ship" + id, typeof(SpriteRenderer));
            go.GetComponent<SpriteRenderer>().sprite = ShipHullArt.Rest(id);
            var drift = go.AddComponent<ShipSpinDrift>();
            drift.respondToPause = false;
            drift.Rebuild();
            drift.Step(0f);
            string who = ShipId.NameOf(id);
            Check(who + ": stock drift not remapped", !ExhaustRemap.IsRemapped(drift.Ring) && !ExhaustRemap.IsRemapped(drift.Wake));
            Wear(id, 1);
            drift.Step(0f);
            Check(who + ": skin re-colours the spin ring and wake",
                  ExhaustRemap.IsRemapped(drift.Ring) && ExhaustRemap.IsRemapped(drift.Wake));
            Wear(id, ShipSkins.Stock);
            drift.Step(0f);
            Check(who + ": back to stock drift", !ExhaustRemap.IsRemapped(drift.Ring) && !ExhaustRemap.IsRemapped(drift.Wake));
            Object.DestroyImmediate(go);
        }
        ResetSkins();
    }

    static void TrafficFollowsTheSkin()
    {
        ResetSkins();
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        foreach (int id in ShipId.All) Wear(id, 1);
        Random.InitState(7);
        var go = new GameObject("~xs-traffic");
        var traffic = go.AddComponent<TitleScreenTraffic>();
        traffic.Init();
        for (int k = 0; k < 30; k++) traffic.Step(1f / 30f);

        int nozzles = 0, remapped = 0, drifts = 0, driftsRemapped = 0, haze = 0;
        foreach (var f in traffic.Pool)
        {
            if (!f.active) continue;
            if (f.nozzles != null)
                foreach (var n in f.nozzles)
                {
                    nozzles++;
                    if (ExhaustRemap.IsRemapped(n)) remapped++;
                    if (n.sharedMaterial != null && n.sharedMaterial.shader.name == "Pause/TitleTrafficHaze") haze++;
                }
            if (f.drift != null && f.drift.Ring != null)
            {
                drifts++;
                if (ExhaustRemap.IsRemapped(f.drift.Ring) && ExhaustRemap.IsRemapped(f.drift.Wake)) driftsRemapped++;
            }
        }
        Check("traffic: every skinned nozzle plume is remapped (" + remapped + "/" + nozzles + ", " + haze + " hazed)",
              nozzles > 0 && remapped == nozzles);
        Check("traffic: skinned spinners' drift remapped (" + driftsRemapped + "/" + drifts + ")", driftsRemapped == drifts);

        foreach (int id in ShipId.All) Wear(id, ShipSkins.Stock);
        for (int k = 0; k < 3; k++) traffic.Step(1f / 30f);
        // the plumes' own LateUpdate (edit mode doesn't tick it)
        foreach (var book in go.GetComponentsInChildren<ShipFlameFlipbook>(true)) book.Step(0f);
        int left = 0, ours = 0;
        foreach (var f in traffic.Pool)
        {
            if (f.nozzles != null)
                foreach (var n in f.nozzles)
                {
                    if (!f.active) continue;
                    if (ExhaustRemap.IsRemapped(n)) left++;
                    if (n.sharedMaterial == ExhaustRemap.Material) ours++;
                }
        }
        Check("traffic: back to stock, no exhaust remapped (" + left + ", " + ours + " on the remap material)",
              left == 0 && ours == 0);
        Object.DestroyImmediate(go);
        ResetSkins();
    }
}
