using System;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

// The one-sided pale outline pass (Art/Enemies/src~/ship_scale_border~: add_border.py, modes.json, lacking.json, original/).
// For every ship in the plan: the live strip is the original with only an outline added (the drawing untouched, same size), the
// outline is world-tinted pale and never pink outside Space, sits on the configured side only (bottom | top | right) with a soft
// fade across the middle (no hard cut), and the plan is complete (every listed ship has its original and a changed live strip).
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod ShipOutlineTest.Run
public static class ShipOutlineTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[OUTL] PASS  " : "[OUTL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Enemies/src~/ship_scale_border~/";
    const int Solid = 96;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var modes = new Dictionary<string, string>();
        string def = "bottom";
        float fade = .3f;
        string cfg = File.ReadAllText(Dir + "modes.json");
        var dm = Regex.Match(cfg, "\"default\"\\s*:\\s*\"(\\w+)\""); if (dm.Success) def = dm.Groups[1].Value;
        var fm = Regex.Match(cfg, "\"fade\"\\s*:\\s*([0-9.]+)"); if (fm.Success) fade = float.Parse(fm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var sm = Regex.Match(cfg, "\"ships\"\\s*:\\s*\\{([^}]*)\\}");
        foreach (Match m in Regex.Matches(sm.Groups[1].Value, "\"([a-z0-9_]+)\"\\s*:\\s*\"(\\w+)\"")) modes[m.Groups[1].Value] = m.Groups[2].Value;
        var keys = new SortedDictionary<string, string>();
        foreach (Match m in Regex.Matches(File.ReadAllText(Dir + "lacking.json"), "\"([a-z0-9_]+)\"\\s*:\\s*\"(\\w+)\"")) keys[m.Groups[1].Value] = m.Groups[2].Value;
        foreach (var k in modes.Keys) if (!keys.ContainsKey(k)) keys[k] = k.Split('_')[0];

        Check("the default mode is bottom and the fade is a share of the silhouette (" + def + ", " + fade + ")", def == "bottom" && fade >= .15f && fade <= .5f);
        foreach (var m in modes.Values) Check("modes.json has no full outline (" + m + ")", m != "full");
        Check("the user's override: Resin Warden top (the Ember ships and the Tide elites carry no outline any more)",
              modes["verdant_elite_resin_warden"] == "top");
        Check("the flying Snap Sprout and Glacier Golem were redrawn without any outline (opted out)", modes["verdant_alien"] == "none" && modes["frost_big"] == "none");
        int wired = 0;
        foreach (var kv in keys)
        {
            string key = kv.Key, world = kv.Value;
            string mode = modes.ContainsKey(key) ? modes[key] : def;
            if (mode == "none") continue;
            Check(key + ": not a full outline (" + mode + ")", mode != "full");
            bool elite = key.Contains("_elite_");
            string live = elite ? "Assets/Art/Resources/Elites/" + char.ToUpper(world[0]) + world.Substring(1) + "/" + key + ".png" : "Assets/Art/Resources/Enemies/" + key + ".png";
            string orig = Dir + "original/" + key + ".png";
            if (!File.Exists(orig) || !File.Exists(live)) { Check(key + ": original and live strips exist", false); continue; }
            Check(key + ": mode " + mode + " is one-sided (bottom | top | right): never a full outline", mode == "bottom" || mode == "top" || mode == "right");
            if (elite) Check(key + ": original/ is the untouched Codex strip", OriginalMatchesSource(key, world, orig));
            var a = Load(orig); var b = Load(live);
            if (a == null || b == null || a.width != b.width || a.height != b.height) { Check(key + ": same size as the original", false); continue; }
            wired++;
            Verify(key, world, mode, fade, a, b);
            Object.DestroyImmediate(a); Object.DestroyImmediate(b);
        }
        bool spaceClean = true;
        foreach (var k in keys.Keys) if (k.StartsWith("space_") && (k.Contains("_elite_") || !k.Contains("_rock_"))) spaceClean = false;
        Check("Space's enemies and elites carry no outline pass (only its rocks do)", spaceClean);
        bool tideEliteClean = true;
        foreach (var k in keys.Keys) if (k.StartsWith("tide_elite_")) tideEliteClean = false;
        Check("the Tide elites carry no outline pass (redrawn from scratch)", tideEliteClean);
        Check("every ship of the plan is wired (" + wired + ")", wired >= 22);   // Desert (15), Ember (6), Space's 7 enemies + 4 elites, Storm's 5 and the 5 Tide elites left the plan
        Debug.Log("[OUTL] failures: " + fails);
        return fails;
    }

    // The elite strips are Codex's: original/ must still be byte-identical to the strip in Art/Enemies/Elite (re-run add_border.py --init after a new delivery).
    static bool OriginalMatchesSource(string key, string world, string orig)
    {
        string dir = "Assets/Art/Enemies/Elite/" + char.ToUpper(world[0]) + world.Substring(1) + "/";
        string src = dir + key + ".png";
        if (!File.Exists(src)) src = dir + key.Substring(world.Length + "_elite_".Length) + ".png";
        if (!File.Exists(src)) return false;
        return File.ReadAllBytes(src).AsSpan().SequenceEqual(File.ReadAllBytes(orig));
    }

    static Texture2D Load(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return ImageConversion.LoadImage(t, File.ReadAllBytes(path)) ? t : null;
    }

    static void Verify(string key, string world, string mode, float fade, Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels32(); var pb = b.GetPixels32();
        int h = a.height, w = a.width, cell = h, cells = w / h;
        bool interior = true, nonPink = true, side = true, soft = true;
        int changed = 0;
        for (int c = 0; c < cells; c++)
        {
            int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = c * cell; x < (c + 1) * cell; x++)
                    if (pa[y * w + x].a >= Solid) { int yt0 = h - 1 - y; x0 = Mathf.Min(x0, x - c * cell); x1 = Mathf.Max(x1, x - c * cell); y0 = Mathf.Min(y0, yt0); y1 = Mathf.Max(y1, yt0); }   // (rows counted from the TOP, as the art is drawn: Unity's row 0 is the bottom)
            if (x1 < 0) continue;
            float midX = (x0 + x1) * .5f, midY = (y0 + y1) * .5f;
            float halfX = Mathf.Max(1f, (x1 - x0) * fade * .5f), halfY = Mathf.Max(1f, (y1 - y0) * fade * .5f);
            for (int y = 0; y < h; y++)
                for (int x = c * cell; x < (c + 1) * cell; x++)
                {
                    var o = pa[y * w + x]; var n = pb[y * w + x];
                    bool same = o.r == n.r && o.g == n.g && o.b == n.b && o.a == n.a;
                    if (o.a >= Solid) { interior &= same; continue; }
                    if (same) continue;
                    changed++;
                    if (world != "space" && IsPink(n)) nonPink = false;
                    // which side: the outline's opacity is ~0 on the far side of the fade band
                    float lx = x - c * cell;
                    int yt = h - 1 - y;
                    float far = mode == "bottom" ? (midY - halfY) - yt : mode == "top" ? yt - (midY + halfY) : mode == "right" ? (midX - halfX) - lx : -1f;
                    if (far > 1f && n.a > o.a + 8) side = false;
                    // soft: neighbours of changed pixels never jump by more than the ramp's slope allows
                    if (x + 1 < (c + 1) * cell && y < h)
                    {
                        var q = pb[y * w + x + 1]; var qo = pa[y * w + x + 1];
                        bool qchanged = !(qo.r == q.r && qo.g == q.g && qo.b == q.b && qo.a == q.a) && qo.a < Solid;
                        if (qchanged && o.a == 0 && qo.a == 0 && Mathf.Abs(q.a - n.a) > 48) soft = false;   // (pixels the pass added: not the art's own anti-aliased edge)
                    }
                }
        }
        Check(key + ": the drawing is untouched (every solid pixel identical)", interior);
        Check(key + ": it got an outline (" + changed + " px)", changed > 40);
        Check(key + ": the outline is not pink" + (world == "space" ? " (Space may)" : " outside Space"), nonPink);
        Check(key + ": the outline sits on the " + mode + " side only", side);
        Check(key + ": no hard cut across the middle", soft);
    }

    static bool IsPink(Color32 c)
    {
        float hh, s, v;
        Color.RGBToHSV(c, out hh, out s, out v);
        return s > .35f && v > .4f && hh > .78f && hh < .96f;
    }
}
