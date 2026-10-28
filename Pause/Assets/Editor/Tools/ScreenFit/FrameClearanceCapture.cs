using System;
using System.IO;
using System.Text;
using UnityEngine;

// Measures, per world and device, the gap between the flight-complete frame's painted art and the
// panel's content (cards, buttons, banner), counts content pixels the art covers, and renders the
// panel with the populated sample data (FlightSampleCapture).
//   SCREEN_FIT_DIR = output folder, FRAME_TAG = file-name prefix (before / after)
//   Unity -projectPath Pause -executeMethod FrameClearanceCapture.Run   (needs a GPU: no -nographics)
public static class FrameClearanceCapture
{
    static readonly string[] Names = { "space", "frost", "verdant", "ember", "tide", "storm", "desert" };
    static FitDevice dev;
    static readonly StringBuilder csv = new StringBuilder();

    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SCREEN_FIT_DIR") ?? "Builds/FrameClearance";
        string tag = Environment.GetEnvironmentVariable("FRAME_TAG") ?? "";
        var devices = new[]
        {
            FitDevice.Find("and-1080x2340-notch"), FitDevice.Find("and-720x1280"), FitDevice.Find("and-480x854"),
            new FitDevice { id = "and-1080x2520", name = "Android 21:9 1080x2520", w = 1080, h = 2520, pxPerPt = 2.625f },
        };
        Directory.CreateDirectory(dir);
        ScreenFitScreens.DeathSample = FlightSampleCapture.Sample;
        ScreenFitScreens.DeathMeasured = Measure;
        using (new TestHarness.Sandbox())
        {
            for (int w = 0; w < Names.Length; w++)
                foreach (var d in devices)
                {
                    dev = d;
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.DeathOn(w), d, Path.Combine(dir, tag + Names[w] + "_" + d.w + "x" + d.h + ".png"), 0);
                    Debug.Log("[FRAMESHOT] " + Names[w] + " " + d.id + " err=" + shot.error + " failures=" + shot.failures + " minTextPt=" + shot.minTextPt.ToString("F1") + " (" + shot.minTextName + ")");
                }
        }
        File.WriteAllText(Path.Combine(dir, tag + "clearance.csv"), csv.ToString());
        UnityEditor.EditorApplication.Exit(0);
    }

    struct Item { public string name; public Rect r; public Item(string n, Rect rr) { name = n; r = rr; } }
    static Rect Down(Rect r, float by) { return new Rect(r.x, r.y - by, r.width, r.height); }

    static void Measure(int world, float pxPerUnit)
    {
        float shift = DeathPanelView.StackShift(world);
        var cards = DeathPanelView.CardRectsFor(world);
        var btn = DeathPanelView.ButtonRects(world);
        float bt = DeathPanelView.BannerTop(world);
        var items = new[]
        {
            new Item("score", Down(cards[0], shift)), new Item("breakdown", Down(cards[1], shift)),
            new Item("stardust", Down(cards[2], shift)), new Item("killedby", Down(cards[3], shift)),
            new Item("replay", btn[0]), new Item("menu", btn[1]), new Item("dock", btn[2]),
            new Item("banner", Rect.MinMaxRect(-318f, bt - DeathPanelView.BannerHeight + 4f, 318f, bt - 4f)),
        };
        var sprite = HudSkin.DeathPanel(world);
        float fw = DeathPanelView.FrameWidth(world), fh = DeathPanelView.FrameHeight;
        float dpPerUnit = pxPerUnit / dev.pxPerPt;
        foreach (var it in items)
        {
            float l = FrameInsets.SideClearance(sprite, DeathPanelView.FrameBorderScale, fw, fh, it.r, true);
            float r = FrameInsets.SideClearance(sprite, DeathPanelView.FrameBorderScale, fw, fh, it.r, false);
            int cover = Mathf.RoundToInt(Covered(sprite, fw, fh, it.r) * pxPerUnit * pxPerUnit * .25f);
            Debug.Log("[FRAMECLR] " + Names[world] + " " + dev.id + " " + it.name + " pxPerUnit=" + pxPerUnit.ToString("F3") +
                " left=" + (l * pxPerUnit).ToString("F1") + "px/" + (l * dpPerUnit).ToString("F1") + "dp" +
                " right=" + (r * pxPerUnit).ToString("F1") + "px/" + (r * dpPerUnit).ToString("F1") + "dp" +
                " box=" + (cover > 0 ? coverBox.ToString("F0") : "-") + " rect=" + it.r.ToString("F0") + " leftU=" + l.ToString("F1") + " rightU=" + r.ToString("F1") + " overlapPx=" + cover);
            csv.AppendLine(Names[world] + "," + dev.id + "," + it.name + "," + pxPerUnit.ToString("F4") + "," + l.ToString("F2") + "," + r.ToString("F2") + "," + cover);
        }
        Debug.Log("[FRAMECLR] " + Names[world] + " " + dev.id + " topGap(bannerTop-innerTop)=" + (DeathPanelView.FrameInnerTop(world) - bt).ToString("F1") +
                  "u bottomGap(dock-innerBottom)=" + (btn[2].yMin - DeathPanelView.FrameInnerBottom(world)).ToString("F1") + "u");
    }

    // Samples (0.5 unit grid) of the content rect that lie on frame art: an independent re-implementation
    // of the sliced mapping, reading the sprite pixels directly.
    static Rect coverBox;
    static int Covered(Sprite s, float fw, float fh, Rect c)
    {
        float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
        var t = s.texture; var px = t.GetPixels32();
        int w = t.width, h = t.height;
        float perPx = 100f / s.pixelsPerUnit / DeathPanelView.FrameBorderScale;
        float bl = s.border.x, br = s.border.z, bt = s.border.w, bb = s.border.y;
        float mx = (fw - (bl + br) * perPx) / (w - bl - br), my = (fh - (bt + bb) * perPx) / (h - bt - bb);
        int n = 0;
        for (float y = c.yMin + .25f; y < c.yMax; y += .5f)
        {
            float fy = y + fh * .5f;   // from bottom
            float ty = fy < bb * perPx ? fy / perPx : fy > fh - bt * perPx ? h - (fh - fy) / perPx : bb + (fy - bb * perPx) / my;
            int row = Mathf.Clamp((int)ty, 0, h - 1);
            Color32 rf = px[Mathf.Clamp(row, (int)(h * .29f), (int)(h * .71f)) * w + w / 2];
            for (float x = c.xMin + .25f; x < c.xMax; x += .5f)
            {
                float fx = x + fw * .5f;
                float tx = fx < bl * perPx ? fx / perPx : fx > fw - br * perPx ? w - (fw - fx) / perPx : bl + (fx - bl * perPx) / mx;
                Color32 p = px[row * w + Mathf.Clamp((int)tx, 0, w - 1)];
                if (Mathf.Abs(p.r - rf.r) + Mathf.Abs(p.g - rf.g) + Mathf.Abs(p.b - rf.b) + Mathf.Abs(p.a - rf.a) > 60) { n++; x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            }
        }
        coverBox = n > 0 ? Rect.MinMaxRect(x0, y0, x1, y1) : default(Rect);
        return n;
    }
}
