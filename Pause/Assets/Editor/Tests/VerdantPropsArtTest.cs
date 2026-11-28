using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Verdant's living props ART (r1, 2026-11-28; VerdantPropsArt.cs): installed per ground set as Backdrop3/v<n>/props.png + props.json, NOT drawn yet (the
// placement is another agent's: VerdantTuning.PropsEnabled false). Held here:
//   * ATLAS: every layout carries 11 kinds x 4 variants x 8 frames (crashplane, outpost, terraces, flowers, beasts, stilt, ropebridge, waterfall, karst,
//     ridge, starship) and the six beast walkers x 8 frames, every sprite inside the sheet, no larger than its kind's drawn px (the 1080 px screen at
//     the .49 altitude), its pivot inside its rect, sheetW / sheetH the texture's own;
//   * ART: no pink texel (WorldCue.IsPink, any alpha); the props drawn under the landmark ceiling as drawn (mean luminance <= .66, WorldBackdropTest),
//     v4's night grade darker and cooler than the day; the lamps blink gently (no strip's mean brightness steps more than 25% between frames);
//   * TONE-MATCH: each layout's props sit within 1.6 x the mean luminance of the ground under them (a pale prop on a dark ground would stand out);
//   * NOT DRAWN: with PropsEnabled false the director loads no props atlas and the preload does not queue one; with it on the atlas resolves
//     through the layout's folder (the hook the placement uses: BackdropSet.VariantAtlas) and its strips play as flipbooks.
public static class VerdantPropsArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[VPA] PASS  " : "[VPA] FAIL  ") + what);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }

    const string ArtDir = "Assets/Art/Backgrounds/Resources/Worlds/Verdant/Backdrop3/";
    [System.Serializable] class JRect { public string n; public int x, y, w, h; public float px, py; }
    [System.Serializable] class JAtlas { public int sheetW, sheetH; public JRect[] sprites; }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool saved = VerdantTuning.PropsEnabled;
        try
        {
            Table();
            for (int v = 1; v <= 4; v++) Atlas(v);
            for (int v = 1; v <= 4; v++) Art(v);
            Hook();
        }
        finally
        {
            VerdantTuning.PropsEnabled = saved;
            BackdropVariants.For("Verdant").Reset();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[VPA] failures: " + fails);
        return fails;
    }

    static void Table()
    {
        string[] want = { "crashplane", "outpost", "terraces", "flowers", "beasts", "stilt", "ropebridge", "waterfall", "karst", "ridge", "starship" };
        bool names = VerdantPropsArt.Count == want.Length;
        for (int i = 0; i < want.Length && names; i++) names &= VerdantPropsArt.IndexOf(want[i]) == i;
        Check("the table holds the 11 approved kinds (" + VerdantPropsArt.Count + ")", names);
        bool landforms = true;
        foreach (var k in VerdantPropsArt.Kinds) landforms &= k.landform == (k.name == "waterfall" || k.name == "karst" || k.name == "ridge" || k.name == "starship");
        Check("... the four landforms (waterfall, karst, ridge, starship) flagged, drawn 400 - 560 px; the rest 150 - 210 px",
              landforms && VerdantPropsArt.Kinds[VerdantPropsArt.IndexOf("ridge")].px == 560 && VerdantPropsArt.Kinds[VerdantPropsArt.IndexOf("flowers")].px == 150);
        Check("the props are not drawn yet (VerdantTuning.PropsEnabled " + VerdantTuning.PropsEnabled + "), the atlas not preloaded",
              !VerdantTuning.PropsEnabled && WorldArtPreload.VariantAtlasesOf("Verdant").Length == 0);
    }

    static JAtlas Load(int v, out int texW, out int texH, out Color[] px)
    {
        string png = ArtDir + "v" + v + "/" + VerdantPropsArt.Atlas + ".png";
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(png));
        texW = tex.width; texH = tex.height; px = tex.GetPixels();
        Object.DestroyImmediate(tex);
        return JsonUtility.FromJson<JAtlas>(File.ReadAllText(Path.ChangeExtension(png, ".json")));
    }

    static void Atlas(int v)
    {
        int W, H; Color[] px;
        var json = Load(v, out W, out H, out px);
        var byName = new Dictionary<string, JRect>();
        foreach (var r in json.sprites) byName[r.n] = r;
        int missing = 0, outside = 0, oversize = 0, badPivot = 0, count = 0;
        string bad = "";
        System.Action<string, float> one = (name, maxPx) =>
        {
            JRect r;
            if (!byName.TryGetValue(name, out r)) { missing++; if (bad.Length < 200) bad += name + " "; return; }
            count++;
            if (r.x < 0 || r.y < 0 || r.x + r.w > W || r.y + r.h > H) outside++;
            if (r.w > maxPx + 2 || r.h > maxPx + 2) { oversize++; if (bad.Length < 200) bad += name + " "; }
            if (r.px < 0f || r.px > 1f || r.py < 0f || r.py > 1f) badPivot++;
        };
        for (int k = 0; k < VerdantPropsArt.Count; k++)
            for (int variant = 0; variant < VerdantPropsArt.Variants; variant++)
                for (int f = 0; f < VerdantPropsArt.Frames; f++) one(VerdantPropsArt.Sprite(VerdantPropsArt.Kinds[k].name, variant, f), VerdantPropsArt.Kinds[k].px);
        for (int w = 0; w < VerdantPropsArt.Walkers; w++)
            for (int f = 0; f < VerdantPropsArt.Frames; f++) one(VerdantPropsArt.Sprite(VerdantPropsArt.Beastwalk, w, f), 80f);
        int want = VerdantPropsArt.Count * VerdantPropsArt.Variants * VerdantPropsArt.Frames + VerdantPropsArt.Walkers * VerdantPropsArt.Frames;
        Check("v" + v + " props atlas: " + VerdantPropsArt.Count + " kinds x " + VerdantPropsArt.Variants + " variants x " + VerdantPropsArt.Frames + " frames + " +
              VerdantPropsArt.Walkers + " beast walkers x " + VerdantPropsArt.Frames + " = " + want + " sprites (" + count + " found, " + missing + " missing " + bad + ")",
              missing == 0 && count == want && json.sprites.Length == want);
        Check("v" + v + " ... every cell inside the " + W + " x " + H + " sheet (sheetW / sheetH " + json.sheetW + " x " + json.sheetH + "), none larger than its drawn px (" +
              oversize + "), every pivot inside its rect (" + badPivot + ")",
              outside == 0 && oversize == 0 && badPivot == 0 && json.sheetW == W && json.sheetH == H && W <= 4096 && H <= 4096);
    }

    static float dayLum, dayBr;

    static void Art(int v)
    {
        int W, H; Color[] px;
        var json = Load(v, out W, out H, out px);
        int pink = 0;
        double lum = 0, wsum = 0, rSum = 0, bSum = 0;
        foreach (var c in px)
        {
            if (c.a <= 0f) continue;
            if (WorldCue.IsPink(c)) pink++;
            if (c.a <= .1f) continue;
            lum += (.2126f * c.r + .7152f * c.g + .0722f * c.b) * c.a; wsum += c.a; rSum += c.r * c.a; bSum += c.b * c.a;
        }
        float mean = (float)(lum / System.Math.Max(1e-6, wsum));
        // per strip: the mean premultiplied luminance of each frame; the largest step between consecutive frames
        var strips = new Dictionary<string, List<JRect>>();
        foreach (var r in json.sprites)
        {
            string key = r.n.Substring(0, r.n.LastIndexOf('_'));
            List<JRect> l;
            if (!strips.TryGetValue(key, out l)) strips[key] = l = new List<JRect>();
            l.Add(r);
        }
        float worstStep = 0f; string worst = "";
        foreach (var kv in strips)
        {
            var means = new List<float>();
            foreach (var r in kv.Value)
            {
                double s = 0;
                for (int y = r.y; y < r.y + r.h; y++)
                    for (int x = r.x; x < r.x + r.w; x++) { var c = px[y * W + x]; s += (.2126f * c.r + .7152f * c.g + .0722f * c.b) * c.a; }
                means.Add((float)(s / Mathf.Max(1, r.w * r.h)));
            }
            for (int i = 0; i < means.Count; i++)
            {
                float step = Mathf.Abs(means[i] - means[(i + 1) % means.Count]) / Mathf.Max(.02f, means[i]);
                if (step > worstStep) { worstStep = step; worst = kv.Key; }
            }
        }
        // the ground under them
        var mid = new Texture2D(2, 2);
        mid.LoadImage(File.ReadAllBytes(ArtDir + "v" + v + "/mid.png"));
        var mp = mid.GetPixels();
        Object.DestroyImmediate(mid);
        double g = 0; int n = 0;
        for (int i = 0; i < mp.Length; i += 37) { g += .2126f * mp[i].r + .7152f * mp[i].g + .0722f * mp[i].b; n++; }
        float ground = (float)(g / n);
        float br = (float)(bSum / System.Math.Max(1e-6, rSum));
        Check("v" + v + " props atlas: no pink texel (" + pink + ")", pink == 0);
        Check("v" + v + " the lights blink gently: no strip's mean brightness steps more than 25% between frames (worst " + worst + " " + worstStep.ToString("P0") + ")", worstStep <= .25f);
        Check("v" + v + " props under the landmark ceiling (mean luminance " + mean.ToString("F3") + " <= .66) and toned to the ground (" + ground.ToString("F3") +
              ": x" + (mean / ground).ToString("F2") + " <= 1.6)", mean <= .66f && mean <= ground * 1.6f);
        if (v == 1) { dayLum = mean; dayBr = br; }
        if (v == 4)
            Check("v4 the night grade: darker than the day (" + mean.ToString("F3") + " < .85 x " + dayLum.ToString("F3") + ") and cooler (blue / red " + br.ToString("F2") +
                  " > day " + dayBr.ToString("F2") + " + .1)", mean < dayLum * .85f && br > dayBr + .1f);
    }

    // The hook: nothing loads the atlas while the props are off; with them on it resolves per layout through BackdropSet.VariantAtlas and the strips are flipbooks.
    static void Hook()
    {
        for (int v = 1; v <= 4; v += 3)
        {
            foreach (bool on in new[] { false, true })
            {
                VerdantTuning.PropsEnabled = on;
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                var vars = BackdropVariants.For("Verdant");
                vars.Reset(); vars.Seed(11); vars.Force = v;
                var cam = Camera.main;
                cam.orthographic = true; cam.aspect = 1080f / 2400f;
                cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
                var wb = WorldBackdrop.Create("Verdant");
                wb.Show("Verdant", false);
                var d = wb.Current != null ? wb.Current.Director as VerdantDirector : null;
                if (d == null) { Check("v" + v + " Verdant director", false); continue; }
                if (!on) Check("v" + v + " PropsEnabled false: the director loads no props atlas", d.PropArt == null);
                else
                {
                    var a = d.PropArt;
                    bool ok = a != null && a.Frames("crashplane_0").Length == 8 && a.Frames("starship_3").Length == 8 && a.Frames("ridge_2").Length == 8 &&
                              a.Frames("beastwalk_5").Length == 8 && a.Get("flowers_1_07") != null;
                    Check("v" + v + " PropsEnabled true: the layout's atlas resolves and its strips play as 8-frame flipbooks", ok);
                }
                Object.DestroyImmediate(wb.gameObject);
            }
        }
        VerdantTuning.PropsEnabled = false;
        Check("... and the preload queues the atlas only when the props are on", WorldArtPreload.VariantAtlasesOf("Verdant").Length == 0 &&
              Toggle());
    }

    static bool Toggle()
    {
        VerdantTuning.PropsEnabled = true;
        bool queued = WorldArtPreload.VariantAtlasesOf("Verdant").Length == 1 && WorldArtPreload.VariantAtlasesOf("Verdant")[0] == VerdantPropsArt.Atlas;
        VerdantTuning.PropsEnabled = false;
        return queued;
    }
}
