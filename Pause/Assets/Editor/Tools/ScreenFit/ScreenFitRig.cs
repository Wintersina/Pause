using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

// Lays the open scene out as one device of the matrix, in edit mode, and
// checks / renders the result.
//
// How a synthetic phone is faked (the editor cannot set Screen.safeArea):
//   * ScreenInfo.Override(...) -- every layout script reads the screen size,
//     safe area and cutouts through ScreenInfo.
//   * The main camera renders into a device-sized RenderTexture at the
//     orthographic size CameraFit would give it.
//   * Every screen-space canvas is moved onto a second, UI-only camera whose
//     world units ARE screen pixels (orthographic size h/2, bottom-left of
//     the view at the world origin, parked 5000 units in front of the scene).
//     A RectTransform's world corners are then its pixel rect, exactly as on
//     an overlay canvas on a device, so layout code that mixes world corners
//     and screen pixels behaves as it does on a phone. Each canvas gets the
//     scale factor its CanvasScaler would compute for the device.
//
// Everything is measured in device pixels, origin bottom-left.
public sealed class ScreenFitRig : IDisposable
{
    [Serializable]
    public class Finding
    {
        public string kind;      // OFFSCREEN SAFE CUTOUT CORNER HOMEBAR TAPSIZE OVERLAP TRUNC SMALLTEXT BLEED BARE WORLD
        public string element;
        public string detail;
        public float x, y, w, h;
        public bool waived;
        public string waiver;
    }

    public class Tap
    {
        public string name;
        public Rect rect;
        public Transform t;
        public bool clipped;
    }

    public readonly FitDevice device;
    public readonly Camera main;
    public Camera ui;
    public readonly List<Finding> findings = new List<Finding>();
    public readonly List<Tap> taps = new List<Tap>();
    public float minTextPt = float.MaxValue;
    public string minTextName = "";
    public float bareFraction;

    readonly List<Tap> extraTaps = new List<Tap>();
    readonly List<KeyValuePair<string, Rect>> important = new List<KeyValuePair<string, Rect>>();
    readonly List<KeyValuePair<string, Rect>> bleeds = new List<KeyValuePair<string, Rect>>();
    readonly List<Transform> ignored = new List<Transform>();
    readonly List<KeyValuePair<string, string>> waivers = new List<KeyValuePair<string, string>>();
    readonly List<string> waiverReasons = new List<string>();

    RenderTexture target;
    IDisposable scope;
    readonly float mainSize, mainAspect;
    readonly RenderTexture mainTarget;
    readonly Vector3[] corners = new Vector3[4];

    public const float UiDepth = -5000f;
    public static readonly Color BareColour = new Color(1f, 0f, 1f, 1f);

    public int W { get { return device.w; } }
    public int H { get { return device.h; } }
    public float PixelsPerWorldUnit { get { return main != null ? device.h / (2f * main.orthographicSize) : 1f; } }
    public float HalfHeight { get { return main != null ? main.orthographicSize : 5f; } }
    public float HalfWidth { get { return HalfHeight * device.Aspect; } }

    // The density the device reports to the game: its real one, none at all
    // (UiScale's fallback), or a fixed value (a test of the rule).
    public enum DpiMode { Reported, Unreported }
    public static DpiMode Dpi = DpiMode.Reported;

    public ScreenFitRig(FitDevice device, float minHalfWidth)
    {
        this.device = device;
        scope = ScreenInfo.Override(device.w, device.h, device.Safe, device.Cutouts,
                                    Dpi == DpiMode.Reported ? device.ReportedDpi : 0f, device.ios);
        target = new RenderTexture(device.w, device.h, 24, RenderTextureFormat.ARGB32);
        target.filterMode = FilterMode.Bilinear;
        main = Camera.main;
        if (main != null)
        {
            mainSize = main.orthographicSize;
            mainAspect = main.aspect;
            mainTarget = main.targetTexture;
            main.targetTexture = target;
            main.aspect = device.Aspect;
            if (main.orthographic)
                main.orthographicSize = CameraFit.ComputeSize(main.orthographicSize, minHalfWidth, device.w, device.h);
            CameraFit.CoverBackdrops(main);   // what CameraFit.Apply does next on a device
        }
        var go = new GameObject("~ScreenFitUiCamera");
        ui = go.AddComponent<Camera>();
        ui.enabled = false;
        ui.orthographic = true;
        ui.orthographicSize = device.h * .5f;
        ui.aspect = device.Aspect;
        ui.nearClipPlane = .1f;
        ui.farClipPlane = 400f;
        ui.clearFlags = CameraClearFlags.Depth;
        ui.cullingMask = ~0;
        ui.targetTexture = target;
        go.transform.position = new Vector3(device.w * .5f, device.h * .5f, UiDepth);
        Sync();
    }

    // ---- canvases -------------------------------------------------------

    // Puts every screen-space canvas (also ones a screen built since the last
    // call) on the pixel camera with the scale its scaler asks for. Call
    // again after anything that creates a canvas or changes a scaler.
    public void Sync()
    {
        UiScaleFloor.ApplyAll();   // UiScale's floors, for this device
        foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            var scaler = canvas.GetComponent<CanvasScaler>();
            float scale = ScaleFor(canvas, scaler, new Vector2(device.w, device.h));
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != ui)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = ui;
                canvas.planeDistance = 100f;
            }
            if (scaler != null && scaler.enabled) scaler.enabled = false;
            if (!Mathf.Approximately(canvas.scaleFactor, scale)) canvas.scaleFactor = scale;
        }
        Canvas.ForceUpdateCanvases();
    }

    // CanvasScaler's own arithmetic (it only runs in Update, on the real screen).
    public static float ScaleFor(Canvas canvas, CanvasScaler scaler, Vector2 screen)
    {
        if (scaler == null) return 1f;
        switch (scaler.uiScaleMode)
        {
            case CanvasScaler.ScaleMode.ConstantPixelSize:
                return scaler.scaleFactor;
            case CanvasScaler.ScaleMode.ScaleWithScreenSize:
                Vector2 r = scaler.referenceResolution;
                switch (scaler.screenMatchMode)
                {
                    case CanvasScaler.ScreenMatchMode.Expand: return Mathf.Min(screen.x / r.x, screen.y / r.y);
                    case CanvasScaler.ScreenMatchMode.Shrink: return Mathf.Max(screen.x / r.x, screen.y / r.y);
                    default:
                        float lw = Mathf.Log(screen.x / r.x, 2f), lh = Mathf.Log(screen.y / r.y, 2f);
                        return Mathf.Pow(2f, Mathf.Lerp(lw, lh, scaler.matchWidthOrHeight));
                }
            default:
                return canvas.scaleFactor;
        }
    }

    // ---- what a stager can declare ---------------------------------------

    // A tap target that is not a uGUI Selectable (a world-space ship bay...).
    public void AddTap(string name, Rect pixels) { extraTaps.Add(new Tap { name = name, rect = pixels }); }
    // Content that must sit inside the safe area, clear of cutouts (a logo...).
    public void AddImportant(string name, Rect pixels) { important.Add(new KeyValuePair<string, Rect>(name, pixels)); }
    // Art that must cover the whole screen, cutouts included.
    public void AddBleed(string name, Rect pixels) { bleeds.Add(new KeyValuePair<string, Rect>(name, pixels)); }
    // A subtree the generic checks skip (another agent's HUD band, hidden legacy UI).
    public void Ignore(Transform t) { if (t != null) ignored.Add(t); }
    public void Ignore(string objectName)
    {
        var go = SceneUtil.FindAny(objectName);
        if (go != null) ignored.Add(go.transform);
    }
    // A finding accepted on purpose; stays in the report, does not fail.
    public void Waive(string kind, string elementContains, string reason)
    {
        waivers.Add(new KeyValuePair<string, string>(kind, elementContains));
        waiverReasons.Add(reason);
    }
    public void Fail(string kind, string element, string detail, Rect r = default(Rect))
    {
        var f = new Finding { kind = kind, element = element, detail = detail, x = r.x, y = r.y, w = r.width, h = r.height };
        for (int i = 0; i < waivers.Count; i++)
            if (waivers[i].Key == kind && element.Contains(waivers[i].Value)) { f.waived = true; f.waiver = waiverReasons[i]; break; }
        findings.Add(f);
    }

    public int Failures
    {
        get { int n = 0; foreach (var f in findings) if (!f.waived) n++; return n; }
    }

    // ---- geometry ---------------------------------------------------------

    public bool IsWorldSpace(Transform t)
    {
        var canvas = t.GetComponentInParent<Canvas>(true);
        if (canvas == null) return true;
        return canvas.rootCanvas.renderMode == RenderMode.WorldSpace;
    }

    public Vector2 Pixel(Vector3 world) { return main.WorldToScreenPoint(world); }

    public Rect PixelRect(RectTransform rt)
    {
        rt.GetWorldCorners(corners);
        bool world = IsWorldSpace(rt);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = world ? Pixel(corners[i]) : (Vector2)corners[i];
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    public Rect PixelRect(Rect worldRect) { return PixelRect(new Bounds(worldRect.center, worldRect.size)); }

    public Rect PixelRect(Bounds b)
    {
        Vector2 a = Pixel(b.min), c = Pixel(b.max);
        return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
    }

    public Rect PixelRect(Renderer r) { return PixelRect(r.bounds); }

    // The sprite's drawn outline (its tight mesh), not its padded rect.
    public Rect TightPixelRect(SpriteRenderer sr)
    {
        if (sr == null || sr.sprite == null) return PixelRect(sr.bounds);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var v in sr.sprite.vertices)
        {
            var local = new Vector3(sr.flipX ? -v.x : v.x, sr.flipY ? -v.y : v.y, 0f);
            Vector2 p = Pixel(sr.transform.TransformPoint(local));
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // A Selectable's hit area: its target graphic's rect grown by any
    // (negative) raycastPadding, which is how a small-looking button is
    // given a finger-sized target without changing its art.
    public Rect HitRect(Selectable s)
    {
        var g = s.targetGraphic != null && s.targetGraphic.raycastTarget ? s.targetGraphic : s.GetComponent<Graphic>();
        var rt = (RectTransform)s.transform;
        if (g == null) return PixelRect(rt);
        var grt = g.rectTransform;
        Vector4 pad = g.raycastPadding;   // left, bottom, right, top; negative grows
        Rect local = grt.rect;
        local = Rect.MinMaxRect(local.xMin + pad.x, local.yMin + pad.y, local.xMax - pad.z, local.yMax - pad.w);
        bool world = IsWorldSpace(grt);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            var c = new Vector3(i == 0 || i == 3 ? local.xMin : local.xMax, i < 2 ? local.yMin : local.yMax, 0f);
            Vector3 w = grt.TransformPoint(c);
            Vector2 p = world ? Pixel(w) : (Vector2)w;
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // The camera's view in world units.
    public Rect WorldView
    {
        get
        {
            Vector2 c = main.transform.position;
            return new Rect(c.x - HalfWidth, c.y - HalfHeight, HalfWidth * 2f, HalfHeight * 2f);
        }
    }

    // The safe area in world units.
    public Rect WorldSafe
    {
        get
        {
            float k = 1f / PixelsPerWorldUnit;
            Rect v = WorldView, s = device.Safe;
            return new Rect(v.x + s.x * k, v.y + s.y * k, s.width * k, s.height * k);
        }
    }

    static bool Contains(Rect outer, Rect inner, float tol)
    {
        return inner.xMin >= outer.xMin - tol && inner.xMax <= outer.xMax + tol &&
               inner.yMin >= outer.yMin - tol && inner.yMax <= outer.yMax + tol;
    }

    static Rect Grow(Rect r, float by) { return Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by); }

    static bool Intersect(Rect a, Rect b, out Rect hit)
    {
        float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
        float y0 = Mathf.Max(a.yMin, b.yMin), y1 = Mathf.Min(a.yMax, b.yMax);
        hit = Rect.MinMaxRect(x0, y0, Mathf.Max(x0, x1), Mathf.Max(y0, y1));
        return x1 > x0 && y1 > y0;
    }

    // True when a point of `r` falls in the part of a screen corner that the
    // display's rounding (radius R, less `margin`) cuts away.
    bool InRoundedCorner(Rect r, float margin)
    {
        float R = device.cornerRadius;
        if (R <= 0f) return false;
        float keep = Mathf.Max(0f, R - margin);
        for (int cx = 0; cx < 2; cx++)
            for (int cy = 0; cy < 2; cy++)
            {
                // the rect's corner nearest this screen corner
                float px = cx == 0 ? r.xMin : r.xMax, py = cy == 0 ? r.yMin : r.yMax;
                float ox = cx == 0 ? R : device.w - R, oy = cy == 0 ? R : device.h - R;
                bool inBoxX = cx == 0 ? px < R : px > device.w - R;
                bool inBoxY = cy == 0 ? py < R : py > device.h - R;
                if (!inBoxX || !inBoxY) continue;
                float dx = px - ox, dy = py - oy;
                if (dx * dx + dy * dy > keep * keep) return true;
            }
        return false;
    }

    bool Ignored(Transform t)
    {
        foreach (var root in ignored) if (root != null && t.IsChildOf(root)) return true;
        return false;
    }

    static float GroupAlpha(Transform t)
    {
        float a = 1f;
        for (; t != null; t = t.parent)
        {
            var g = t.GetComponent<CanvasGroup>();
            if (g == null) continue;
            a *= g.alpha;
            if (g.ignoreParentGroups) break;
        }
        return a;
    }

    static bool GroupInteractable(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            var g = t.GetComponent<CanvasGroup>();
            if (g == null) continue;
            if (!g.interactable || !g.blocksRaycasts) return false;
            if (g.ignoreParentGroups) break;
        }
        return true;
    }

    static bool CanvasOn(Transform t)
    {
        var c = t.GetComponentInParent<Canvas>();
        for (; c != null; c = c.transform.parent != null ? c.transform.parent.GetComponentInParent<Canvas>() : null)
            if (!c.enabled) return false;
        return true;
    }

    bool ClipOf(Transform t, out Rect clip)
    {
        clip = default(Rect);
        bool any = false;
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            bool masks = p.GetComponent<RectMask2D>() != null;
            var m = p.GetComponent<Mask>();
            masks |= m != null && m.enabled;
            if (!masks) continue;
            Rect r = PixelRect((RectTransform)p);
            if (!any) { clip = r; any = true; }
            else Intersect(clip, r, out clip);
        }
        return any;
    }

    public static string PathOf(Transform t)
    {
        string s = t.name;
        int depth = 0;
        for (Transform p = t.parent; p != null && depth < 3; p = p.parent, depth++) s = p.name + "/" + s;
        return s;
    }

    // ---- text ---------------------------------------------------------------

    static readonly Regex RichTag = new Regex("<[^>]+>");
    static readonly TextGenerator generator = new TextGenerator();

    // Where the glyphs of `t` actually land (px), the size they are drawn at
    // (px) and whether the box cut any of them off.
    public bool MeasureText(Text t, out Rect glyphs, out float fontPx, out bool truncated)
    {
        glyphs = default(Rect);
        fontPx = 0f;
        truncated = false;
        if (t.font == null) return false;
        var rt = t.rectTransform;
        var settings = t.GetGenerationSettings(rt.rect.size);
        generator.Invalidate();
        generator.PopulateWithErrors(t.text, settings, t.gameObject);
        float unitsPerPixel = 1f / Mathf.Max(t.pixelsPerUnit, .0001f);
        var verts = generator.verts;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        bool world = IsWorldSpace(rt);
        for (int i = 0; i + 3 < verts.Count; i += 4)
        {
            Vector3 a = verts[i].position, c = verts[i + 2].position;
            if (Mathf.Approximately(a.x, c.x) || Mathf.Approximately(a.y, c.y)) continue;   // whitespace
            for (int k = 0; k < 4; k++)
            {
                Vector3 w = rt.TransformPoint(verts[i + k].position * unitsPerPixel);
                Vector2 p = world ? Pixel(w) : (Vector2)w;
                x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
            }
        }
        if (x0 > x1) return false;
        glyphs = Rect.MinMaxRect(x0, y0, x1, y1);
        float units = t.resizeTextForBestFit ? generator.fontSizeUsedForBestFit * unitsPerPixel : t.fontSize;
        float scale = Mathf.Abs(rt.lossyScale.y) * (world ? PixelsPerWorldUnit : 1f);
        fontPx = units * scale;
        string plain = t.supportRichText ? RichTag.Replace(t.text, "") : t.text;
        int expected = plain.TrimEnd().Length;
        truncated = generator.characterCountVisible < expected;
        return true;
    }

    // ---- the checks ----------------------------------------------------------

    public const float MinTextPt = 7f;        // below this, type is not readable on a phone
    public const float CutoutMarginPt = 4f;
    public const float CornerMarginPt = 2f;

    void Placement(string what, string name, Rect r, bool tap)
    {
        if (!Contains(device.Full, r, 1.5f))
        {
            Fail("OFFSCREEN", name, what + " leaves the screen " + Describe(r), r);
            return;
        }
        if (!Contains(device.Safe, r, 1.5f))
            Fail("SAFE", name, what + " leaves the safe area " + Describe(r) + " safe " + Describe(device.Safe), r);
        foreach (var c in device.Cutouts)
        {
            Rect hit;
            if (Intersect(Grow(c, device.Pt(CutoutMarginPt)), r, out hit))
                Fail("CUTOUT", name, what + " is under / within " + CutoutMarginPt + "pt of the " + device.cutoutKind, r);
        }
        if (InRoundedCorner(r, device.Pt(CornerMarginPt)))
            Fail("CORNER", name, what + " is clipped by the rounded corner (r " + device.cornerRadius + "px)", r);
        if (tap && device.homeBar)
        {
            Rect hit;
            if (Intersect(Grow(device.HomeBarRect, device.Pt(4f)), r, out hit))
                Fail("HOMEBAR", name, what + " sits under the home indicator / gesture bar", r);
        }
    }

    static string Describe(Rect r)
    {
        return "[" + r.xMin.ToString("F0") + ".." + r.xMax.ToString("F0") + " x " + r.yMin.ToString("F0") + ".." + r.yMax.ToString("F0") + "]";
    }

    public void RunChecks()
    {
        Canvas.ForceUpdateCanvases();
        taps.Clear();

        foreach (var s in UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None))
        {
            if (!s.IsActive() || !s.IsInteractable() || s is Scrollbar) continue;
            var t = s.transform;
            if (Ignored(t) || !CanvasOn(t) || GroupAlpha(t) < .05f || !GroupInteractable(t)) continue;
            var rt = t as RectTransform;
            if (rt == null) continue;
            Rect r = HitRect(s);
            if (r.width < .5f || r.height < .5f) continue;
            bool clipped = false;
            Rect clip;
            if (ClipOf(t, out clip))
            {
                Rect vis;
                if (!Intersect(r, clip, out vis)) continue;
                clipped = vis.width * vis.height < r.width * r.height * .98f;
                r = vis;
            }
            taps.Add(new Tap { name = PathOf(t), rect = r, t = t, clipped = clipped });
        }
        taps.AddRange(extraTaps);

        float minTap = device.MinTapPx;
        foreach (var tap in taps)
        {
            if (!tap.clipped) Placement("tap target", tap.name, tap.rect, true);
            if (!tap.clipped && (tap.rect.width < minTap - .5f || tap.rect.height < minTap - .5f))
                Fail("TAPSIZE", tap.name, "tap target " + tap.rect.width.ToString("F0") + "x" + tap.rect.height.ToString("F0") +
                     "px is under " + minTap.ToString("F0") + "px (" + (device.ios ? "44pt" : "48dp") + "): " +
                     (tap.rect.width / device.pxPerPt).ToString("F0") + "x" + (tap.rect.height / device.pxPerPt).ToString("F0"), tap.rect);
        }
        for (int i = 0; i < taps.Count; i++)
            for (int j = i + 1; j < taps.Count; j++)
            {
                var a = taps[i]; var b = taps[j];
                if (a.t != null && b.t != null && (a.t.IsChildOf(b.t) || b.t.IsChildOf(a.t))) continue;
                Rect hit;
                if (!Intersect(a.rect, b.rect, out hit)) continue;
                float area = hit.width * hit.height;
                float small = Mathf.Min(a.rect.width * a.rect.height, b.rect.width * b.rect.height);
                if (area > 16f && area > small * .03f)
                    Fail("OVERLAP", a.name + " + " + b.name, "tap targets overlap by " + hit.width.ToString("F0") + "x" + hit.height.ToString("F0") + "px", hit);
            }

        foreach (var t in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
        {
            if (!t.IsActive() || string.IsNullOrWhiteSpace(t.text)) continue;
            if (Ignored(t.transform) || !CanvasOn(t.transform)) continue;
            if (t.color.a * GroupAlpha(t.transform) < .05f) continue;
            if (t.canvasRenderer != null && t.canvasRenderer.cull) continue;
            Rect glyphs; float fontPx; bool truncated;
            if (!MeasureText(t, out glyphs, out fontPx, out truncated)) continue;
            string name = PathOf(t.transform) + " \"" + Short(t.text) + "\"";
            Rect clip;
            if (ClipOf(t.transform, out clip))
            {
                Rect vis;
                if (!Intersect(glyphs, clip, out vis)) continue;
                if (!Contains(clip, glyphs, 1.5f)) continue;   // scrolling content, partly out of its viewport
            }
            Placement("text", name, glyphs, false);
            if (truncated) Fail("TRUNC", name, "text does not fit its box and is cut off", glyphs);
            float pt = fontPx / device.pxPerPt;
            if (pt < minTextPt) { minTextPt = pt; minTextName = name; }
            if (pt < MinTextPt)
                Fail("SMALLTEXT", name, "type is " + pt.ToString("F1") + (device.ios ? "pt" : "dp") + " (" + fontPx.ToString("F1") + "px), under the " + MinTextPt + " floor", glyphs);
        }

        foreach (var kv in important) Placement("content", kv.Key, kv.Value, false);
        foreach (var kv in bleeds)
            if (!Contains(kv.Value, device.Full, .75f))
                Fail("BLEED", kv.Key, "does not cover the whole screen: " + Describe(kv.Value) + " of " + device.w + "x" + device.h, kv.Value);
    }

    static string Short(string s)
    {
        s = s.Replace("\n", " ");
        return s.Length > 28 ? s.Substring(0, 28) + "..." : s;
    }

    // ---- rendering -------------------------------------------------------------

    // Renders the device frame; returns it `maxHeight` px tall (0 = full size).
    // With `countBare`, also renders once with the camera clearing to a marker
    // colour and records how much of the frame nothing drew over.
    public Texture2D Capture(int maxHeight, bool countBare)
    {
        Canvas.ForceUpdateCanvases();
        float k = maxHeight > 0 ? Mathf.Min(1f, maxHeight / (float)device.h) : 1f;
        int ow = Mathf.Max(1, Mathf.RoundToInt(device.w * k)), oh = Mathf.Max(1, Mathf.RoundToInt(device.h * k));
        if (countBare && main != null)
        {
            var flags = main.clearFlags;
            var colour = main.backgroundColor;
            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = BareColour;
            var probe = Read(96, Mathf.Max(1, Mathf.RoundToInt(96f * device.h / device.w)), FilterMode.Point);
            main.clearFlags = flags;
            main.backgroundColor = colour;
            var px = probe.GetPixels32();
            int bare = 0;
            foreach (var p in px) if (p.r > 250 && p.g < 5 && p.b > 250) bare++;
            bareFraction = bare / (float)px.Length;
            UnityEngine.Object.DestroyImmediate(probe);
        }
        return Read(ow, oh, FilterMode.Bilinear);
    }

    Texture2D Read(int ow, int oh, FilterMode filter)
    {
        if (main != null) main.Render();
        else
        {
            var prevActive = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prevActive;
        }
        ui.Render();
        target.filterMode = filter;
        var small = RenderTexture.GetTemporary(ow, oh, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(target, small);
        var prev = RenderTexture.active;
        RenderTexture.active = small;
        var tex = new Texture2D(ow, oh, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, ow, oh), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(small);
        return tex;
    }

    public void Dispose()
    {
        if (main != null)
        {
            main.targetTexture = mainTarget;
            main.orthographicSize = mainSize;
            main.aspect = mainAspect;
            main.ResetAspect();
        }
        if (ui != null)
        {
            ui.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(ui.gameObject);
        }
        if (target != null) UnityEngine.Object.DestroyImmediate(target);
        target = null;
        if (scope != null) scope.Dispose();
        scope = null;
    }
}
