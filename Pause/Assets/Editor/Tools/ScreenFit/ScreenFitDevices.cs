using UnityEngine;

// The device matrix the screen-fit suite lays every screen out on: phone and
// tablet shapes, each with the safe area and display cutouts the OS reports
// for it in portrait. Pixel values are the panel's native pixels; cutouts
// are given from the TOP-left (how spec sheets give them) and converted to
// Unity's bottom-left screen space by Cutouts.
//
// iOS insets are Apple's published safe-area insets in points x the screen
// scale. Android ones are typical for the cutout type (a status-bar-high
// band under the cutout); real devices vary by a few pixels, which is why
// every check keeps a margin.
public class FitDevice
{
    public string id, name;
    public int w, h;
    public bool ios;
    public float pxPerPt;                 // iOS: screen scale; Android: density (dpi / 160)
    public int top, bottom, left, right;  // safe-area insets, px
    public RectInt[] cutoutsTopLeft = new RectInt[0];
    public int cornerRadius;              // px
    public bool homeBar;                  // draws a home indicator / gesture pill
    public string cutoutKind = "none";

    public Rect Safe { get { return new Rect(left, bottom, w - left - right, h - top - bottom); } }
    public Rect Full { get { return new Rect(0, 0, w, h); } }
    public float Aspect { get { return (float)w / h; } }

    public Rect[] Cutouts
    {
        get
        {
            var r = new Rect[cutoutsTopLeft.Length];
            for (int i = 0; i < r.Length; i++)
            {
                var c = cutoutsTopLeft[i];
                r[i] = new Rect(c.x, h - c.y - c.height, c.width, c.height);
            }
            return r;
        }
    }

    // Minimum comfortable tap target: 44 pt (Apple HIG) / 48 dp (Material).
    public float MinTapPx { get { return (ios ? 44f : 48f) * pxPerPt; } }
    public float Pt(float points) { return points * pxPerPt; }

    // What the OS reports as Screen.dpi: Android its density x 160, iOS the
    // panel's ppi (@3x iPhones ~460, @2x iPhones 326, iPads 264).
    public float ReportedDpi
    {
        get
        {
            if (!ios) return pxPerPt * 160f;
            if (pxPerPt >= 3f) return 460f;
            return Mathf.Min(w, h) >= 1500 ? 264f : 326f;
        }
    }

    // The home indicator / gesture pill, for drawing and for the "nothing
    // tappable under it" check (px, Unity bottom-left space).
    public Rect HomeBarRect
    {
        get
        {
            if (!homeBar) return new Rect(0, 0, 0, 0);
            float bw = Pt(ios ? 134f : 108f), bh = Pt(ios ? 5f : 4f), by = Pt(ios ? 8f : 10f);
            return new Rect((w - bw) * .5f, by, bw, bh);
        }
    }

    static RectInt Centre(int screenW, int width, int y, int height)
    {
        return new RectInt((screenW - width) / 2, y, width, height);
    }

    public static readonly FitDevice[] All =
    {
        // ---- Android ----
        new FitDevice { id = "and-480x854", name = "Android low-dpi 480x854", w = 480, h = 854, pxPerPt = 1.5f },
        new FitDevice { id = "and-720x1280", name = "Android 16:9 720x1280", w = 720, h = 1280, pxPerPt = 2f },
        new FitDevice { id = "and-1080x1920", name = "Android 16:9 1080x1920", w = 1080, h = 1920, pxPerPt = 2.625f },
        new FitDevice { id = "and-1080x1920-navbar", name = "Android 16:9 + 3-button bar", w = 1080, h = 1920, pxPerPt = 2.625f,
                        bottom = 126, cutoutKind = "nav bar" },
        new FitDevice { id = "and-1080x2160", name = "Android 18:9 1080x2160", w = 1080, h = 2160, pxPerPt = 2.625f,
                        cornerRadius = 60 },
        new FitDevice { id = "and-1080x2340-notch", name = "Android 19.5:9 wide notch", w = 1080, h = 2340, pxPerPt = 2.625f,
                        top = 96, cornerRadius = 90, cutoutKind = "wide centre notch",
                        cutoutsTopLeft = new[] { Centre(1080, 440, 0, 90) } },
        new FitDevice { id = "and-1080x2340-waterfall", name = "Android 19.5:9 waterfall", w = 1080, h = 2340, pxPerPt = 2.625f,
                        top = 110, left = 48, right = 48, cornerRadius = 90, cutoutKind = "waterfall + centre hole",
                        cutoutsTopLeft = new[] { Centre(1080, 80, 30, 80) } },
        new FitDevice { id = "and-1080x2400-corner", name = "Android 20:9 corner hole", w = 1080, h = 2400, pxPerPt = 2.625f,
                        top = 120, cornerRadius = 100, cutoutKind = "corner punch-hole",
                        cutoutsTopLeft = new[] { new RectInt(45, 30, 90, 90) } },
        new FitDevice { id = "and-1080x2400-gesture", name = "Android 20:9 + gesture bar", w = 1080, h = 2400, pxPerPt = 2.625f,
                        top = 110, bottom = 63, cornerRadius = 100, homeBar = true, cutoutKind = "centre hole + gesture bar",
                        cutoutsTopLeft = new[] { Centre(1080, 80, 30, 80) } },
        new FitDevice { id = "flip7-1080x2520", name = "Galaxy Z Flip7 21:9", w = 1080, h = 2520, pxPerPt = 2.625f,
                        top = 110, cornerRadius = 110, cutoutKind = "centre punch-hole",
                        cutoutsTopLeft = new[] { Centre(1080, 80, 30, 80) } },
        new FitDevice { id = "and-1080x2640", name = "Android 22:9 1080x2640", w = 1080, h = 2640, pxPerPt = 2.625f,
                        top = 110, cornerRadius = 110, cutoutKind = "centre punch-hole",
                        cutoutsTopLeft = new[] { Centre(1080, 80, 30, 80) } },
        new FitDevice { id = "fold-1812x2176", name = "Fold inner 1812x2176", w = 1812, h = 2176, pxPerPt = 2.625f,
                        top = 110, cornerRadius = 60, cutoutKind = "corner punch-hole",
                        cutoutsTopLeft = new[] { new RectInt(1812 - 70 - 60, 40, 70, 70) } },
        new FitDevice { id = "tab-1600x2560", name = "Android tablet 1600x2560", w = 1600, h = 2560, pxPerPt = 2f },

        // ---- iOS ----
        new FitDevice { id = "iphone-se", name = "iPhone SE 750x1334", w = 750, h = 1334, ios = true, pxPerPt = 2f },
        new FitDevice { id = "iphone-13", name = "iPhone 13/14 notch", w = 1170, h = 2532, ios = true, pxPerPt = 3f,
                        top = 141, bottom = 102, cornerRadius = 142, homeBar = true, cutoutKind = "notch",
                        cutoutsTopLeft = new[] { Centre(1170, 486, 0, 99) } },
        new FitDevice { id = "iphone-15", name = "iPhone 15/16 island", w = 1179, h = 2556, ios = true, pxPerPt = 3f,
                        top = 177, bottom = 102, cornerRadius = 165, homeBar = true, cutoutKind = "Dynamic Island",
                        cutoutsTopLeft = new[] { Centre(1179, 378, 33, 111) } },
        new FitDevice { id = "iphone-15pm", name = "iPhone 15 Pro Max", w = 1290, h = 2796, ios = true, pxPerPt = 3f,
                        top = 177, bottom = 102, cornerRadius = 165, homeBar = true, cutoutKind = "Dynamic Island",
                        cutoutsTopLeft = new[] { Centre(1290, 378, 33, 111) } },
        new FitDevice { id = "iphone-16pro", name = "iPhone 16 Pro", w = 1206, h = 2622, ios = true, pxPerPt = 3f,
                        top = 186, bottom = 102, cornerRadius = 186, homeBar = true, cutoutKind = "Dynamic Island",
                        cutoutsTopLeft = new[] { Centre(1206, 378, 42, 111) } },
        new FitDevice { id = "iphone-16pm", name = "iPhone 16 Pro Max", w = 1320, h = 2868, ios = true, pxPerPt = 3f,
                        top = 186, bottom = 102, cornerRadius = 186, homeBar = true, cutoutKind = "Dynamic Island",
                        cutoutsTopLeft = new[] { Centre(1320, 378, 42, 111) } },
        new FitDevice { id = "ipad-9", name = "iPad 10.2 1620x2160", w = 1620, h = 2160, ios = true, pxPerPt = 2f },
        new FitDevice { id = "ipad-pro", name = "iPad Pro 12.9", w = 2048, h = 2732, ios = true, pxPerPt = 2f,
                        top = 48, bottom = 40, cornerRadius = 36, homeBar = true, cutoutKind = "home indicator" },
    };

    public static FitDevice Find(string id)
    {
        foreach (var d in All) if (d.id == id) return d;
        return null;
    }
}
