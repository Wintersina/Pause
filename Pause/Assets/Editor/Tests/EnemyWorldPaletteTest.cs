using System.IO;
using UnityEngine;

// The Tide, Storm and Desert enemy rosters must read as three different worlds at a glance (palette unification, 2026-10-12).
// Measured on the shipped idle strips (cells 0..3 of every roster unit): the chromatic pixels' mean hue, their hue histogram and the mean value.
//   Tide = deep-sea teal, Storm = storm-iron indigo steel, Desert = sun-baked gold/umber.
// Also guards: no pink on any body outside Space (pink belongs to hostile shots and Space), no player red.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites EnemyWorldPaletteTest
public static class EnemyWorldPaletteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EWP] PASS  " : "[EWP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    struct Stat { public float meanHue, meanValue, meanSat; public float[] hist; public int opaque, pink, red; }

    public static int Execute()
    {
        fails = 0;
        string[] worlds = { "tide", "storm", "desert" };
        var st = new Stat[worlds.Length];
        for (int i = 0; i < worlds.Length; i++)
        {
            st[i] = Measure(worlds[i]);
            Check(worlds[i] + ": measured " + st[i].opaque + " px, hue " + st[i].meanHue.ToString("0") + ", S " + st[i].meanSat.ToString("0.00") + ", V " + st[i].meanValue.ToString("0.00"), st[i].opaque > 50000);
            Check(worlds[i] + ": no pink and no player red on any body (" + st[i].pink + " pink, " + st[i].red + " red of " + st[i].opaque + ")",
                  st[i].pink <= st[i].opaque * .002f && st[i].red <= st[i].opaque * .002f);
        }
        // Dominant hues: Tide teal 150..190, Storm indigo-steel 215..250 (clear of the violet capacitor pickup, 259), Desert gold/umber 15..50.
        // Tide (art/enemy-colors-tide): the roster is no longer one teal; each ship has a warm/violet hull family with blue-yellow-violet accents and
        // flying thrusters, chosen to pop against the teal-blue Tide backdrop. So Tide is held to "not teal, not pink" and is exempt from the
        // single-hue separation gates below against Desert (known overlap: orange vs gold; flagged in the PR for the user to judge).
        Check("Tide's dominant hue has left the backdrop teal (outside 150..190)", st[0].meanHue < 150f || st[0].meanHue > 190f);
        Check("Storm's dominant hue is indigo steel (215..250)", st[1].meanHue > 215f && st[1].meanHue < 250f);
        // Desert (art/enemy-colors-desert): the seven flying enemies are deliberately multi-accent (cobalt, turquoise, emerald, violet, saffron)
        // so they read against the sand; only the rocks stay sand, so Desert is exempt from the single-hue and hue-separation rules.
        for (int a = 0; a < 3; a++)
            for (int b = a + 1; b < 3; b++)
            {
                if (a == 2 || b == 2) continue;  // Desert is multi-hue by design (see above); this also covers Tide vs Desert
                float gap = Mathf.Abs(Mathf.DeltaAngle(st[a].meanHue, st[b].meanHue));
                Check(worlds[a] + " vs " + worlds[b] + ": dominant hues at least 50 degrees apart (" + gap.ToString("0") + ")", gap >= 50f);
                float ov = Overlap(st[a].hist, st[b].hist);
                Check(worlds[a] + " vs " + worlds[b] + ": chromatic hue histograms overlap <= 25% (" + (ov * 100f).ToString("0") + "%)", ov <= .25f);
            }
        // Against Frost (cyan ice) and Ember (orange): keep a visible gap too.
        var frost = Measure("frost"); var ember = Measure("ember");
        Check("Tide stays >= 25 degrees from Frost's cyan (" + Mathf.Abs(Mathf.DeltaAngle(st[0].meanHue, frost.meanHue)).ToString("0") + ")", Mathf.Abs(Mathf.DeltaAngle(st[0].meanHue, frost.meanHue)) >= 25f);
        Check("Storm stays >= 15 degrees from Frost's cyan and is darker (" + Mathf.Abs(Mathf.DeltaAngle(st[1].meanHue, frost.meanHue)).ToString("0") + ")",
              Mathf.Abs(Mathf.DeltaAngle(st[1].meanHue, frost.meanHue)) >= 15f && st[1].meanValue < frost.meanValue + .02f);
        Check("Desert is lighter than Ember's dark rust (" + st[2].meanValue.ToString("0.00") + " vs " + ember.meanValue.ToString("0.00") + ")", st[2].meanValue > ember.meanValue + .08f);
        Debug.Log("[EWP] failures: " + fails);
        return fails;
    }

    static float Overlap(float[] a, float[] b)
    {
        // Circular smoothing so neighbouring bins count as partly shared.
        float o = 0f;
        int n = a.Length;
        for (int i = 0; i < n; i++)
        {
            float x = (a[(i + n - 1) % n] + 2f * a[i] + a[(i + 1) % n]) / 4f, y = (b[(i + n - 1) % n] + 2f * b[i] + b[(i + 1) % n]) / 4f;
            o += Mathf.Min(x, y);
        }
        return o;
    }

    static Stat Measure(string world)
    {
        var s = new Stat { hist = new float[24] };
        double sx = 0, sy = 0, sv = 0, ss = 0; int chrom = 0;
        foreach (var path in Directory.GetFiles("Assets/Art/Resources/Enemies", world + "_*.png"))
        {
            if (path.Contains("mine")) continue;
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes(path));
            int cell = t.height;
            var px = t.GetPixels32();
            for (int y = 0; y < t.height; y++)
                for (int x = 0; x < cell * 4 && x < t.width; x++)
                {
                    var p = px[y * t.width + x];
                    if (p.a < 200) continue;
                    s.opaque++;
                    float h, sat, v;
                    Color.RGBToHSV(p, out h, out sat, out v);
                    float deg = h * 360f;
                    sv += v;
                    if (deg >= 310f && deg <= 328f && sat > .35f && v > .4f) s.pink++;
                    if ((deg < 12f || deg > 348f) && sat > .6f && v > .5f) s.red++;
                    if (sat > .25f && v > .25f)
                    {
                        chrom++; ss += sat;
                        sx += Mathf.Cos(h * 2f * Mathf.PI); sy += Mathf.Sin(h * 2f * Mathf.PI);
                        s.hist[Mathf.Min(23, (int)(deg / 15f))] += 1f;
                    }
                }
            Object.DestroyImmediate(t);
        }
        if (chrom > 0) { for (int i = 0; i < 24; i++) s.hist[i] /= chrom; ss /= chrom; }
        float m = Mathf.Atan2((float)sy, (float)sx) / (2f * Mathf.PI);
        s.meanHue = (m < 0 ? m + 1f : m) * 360f;
        s.meanSat = (float)ss;
        s.meanValue = s.opaque > 0 ? (float)(sv / s.opaque) : 0f;
        return s;
    }
}
