using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// The app icon (Art/AppIcon/OrbitalRail, built by src~/new_icon/build_icon.py,
// wired by AppIconSetter):
//  - all six files exist at the right pixel size; master, iOS and the
//    adaptive background are fully opaque (iOS also has no alpha channel);
//  - the adaptive foreground is transparent around the wordmark + ship, and
//    all of it sits inside the 132 px safe radius of the 432 px layer;
//  - the adaptive background alone is a decent icon (opaque, lively, no
//    foreground art baked in -- never the old pause bars);
//  - legacy and round are full composites (opaque centre, not blank);
//  - Player Settings use the set: default icon, Android adaptive
//    (background layer 0, foreground layer 1), round and legacy icons, and
//    every iOS slot with the alpha-free 1024;
//  - no orphans: the folder holds exactly these six PNGs, and re-applying
//    is idempotent.
public static class AppIconTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ICON] PASS  " : "[ICON] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        Check("the recommended icon is Orbital Rail", AppIconSetter.Recommended == "OrbitalRail");
        Check("Orbital Rail is the only candidate", AppIconSetter.Candidates.Length == 1);
        foreach (var ship in AppIconSetter.Candidates) Files(ship);
        SafeZone(AppIconSetter.Recommended);
        Composites(AppIconSetter.Recommended);
        NoOrphans();

        AppIconSetter.ApplyRecommended();   // re-runnable
        AppIconSetter.ApplyRecommended();
        Check("ApplyRecommended makes Orbital Rail current", AppIconSetter.Current() == AppIconSetter.Recommended);
        PlayerSettingsUse(AppIconSetter.Recommended);
        return fails;
    }

    // PNG header: width, height, colour type (2 = RGB, 6 = RGBA).
    static bool Header(string path, out int w, out int h, out int colourType)
    {
        w = h = colourType = -1;
        if (!File.Exists(path)) return false;
        var b = new byte[26];
        using (var fs = File.OpenRead(path)) if (fs.Read(b, 0, 26) < 26) return false;
        if (b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return false;
        w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
        h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
        colourType = b[25];
        return true;
    }

    static void Files(string ship)
    {
        foreach (var (part, size) in AppIconSetter.Parts)
        {
            string path = AppIconSetter.PathOf(ship, part);
            bool ok = Header(path, out int w, out int h, out int ct);
            Check(ship + " " + part + " exists", ok);
            if (!ok) continue;
            Check(ship + " " + part + " is " + size + "x" + size + " (got " + w + "x" + h + ")", w == size && h == size);
            bool opaque = part == AppIconSetter.Master || part == AppIconSetter.Ios || part == AppIconSetter.AdaptiveBg;
            if (part == AppIconSetter.Ios) Check(ship + " " + part + " has no alpha channel (colour type " + ct + ")", ct == 2);
            else if (opaque) Check(ship + " " + part + " is fully opaque", AllOpaque(path));
            else Check(ship + " " + part + " has an alpha channel", ct == 6);
        }
    }

    static Texture2D Pixels(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        return tex;
    }

    static bool AllOpaque(string path)
    {
        var t = Pixels(path);
        bool ok = true;
        foreach (var px in t.GetPixels32()) if (px.a != 255) { ok = false; break; }
        Object.DestroyImmediate(t);
        return ok;
    }

    // legacy / round are full composites: opaque middle, a lively picture
    // (not a flat fill), and the round one is clear in the corners.
    static void Composites(string ship)
    {
        foreach (var part in new[] { AppIconSetter.Legacy, AppIconSetter.Round })
        {
            var t = Pixels(AppIconSetter.PathOf(ship, part));
            int n = t.width;
            Check(part + " centre is opaque", t.GetPixel(n / 2, n / 2).a > .99f);
            float min = 1f, max = 0f;
            for (int y = n / 4; y < n * 3 / 4; y += 4)
                for (int x = n / 4; x < n * 3 / 4; x += 4)
                {
                    var c = t.GetPixel(x, y);
                    float l = (c.r + c.g + c.b) / 3f;
                    if (l < min) min = l;
                    if (l > max) max = l;
                }
            Check(part + " is a full composite (luma range " + (max - min).ToString("0.00") + ")", max - min > .3f);
            if (part == AppIconSetter.Round) Check("round corners are transparent", t.GetPixel(0, 0).a == 0f);
            Object.DestroyImmediate(t);
        }
        var bg = Pixels(AppIconSetter.PathOf(ship, AppIconSetter.AdaptiveBg));
        int m = bg.width;
        float lo = 1f, hi = 0f;
        for (int y = 0; y < m; y += 6)
            for (int x = 0; x < m; x += 6)
            {
                var c = bg.GetPixel(x, y);
                float l = (c.r + c.g + c.b) / 3f;
                if (l < lo) lo = l;
                if (l > hi) hi = l;
            }
        Check("adaptive background alone is a decent icon (luma range " + (hi - lo).ToString("0.00") + ")", hi - lo > .3f);
        Object.DestroyImmediate(bg);
    }

    static void NoOrphans()
    {
        var expected = new System.Collections.Generic.HashSet<string>();
        foreach (var (part, _) in AppIconSetter.Parts) expected.Add(AppIconSetter.PathOf(AppIconSetter.Recommended, part));
        int extra = 0;
        foreach (var f in Directory.GetFiles(AppIconSetter.Root, "*.png", SearchOption.AllDirectories))
        {
            string p = f.Replace('\\', '/');
            if (p.Contains("/src~/") ) continue;
            if (!expected.Contains(p)) { extra++; Debug.Log("[ICON] orphan " + p); }
        }
        Check("no orphan icon PNGs (" + extra + ")", extra == 0);
        extra = 0;
        foreach (var d in Directory.GetDirectories(AppIconSetter.Root))
            if (!d.EndsWith("src~") && !d.EndsWith("OrbitalRail")) extra++;
        Check("no old candidate folders left", extra == 0);
    }

    static void SafeZone(string ship)
    {
        var fg = Pixels(AppIconSetter.PathOf(ship, AppIconSetter.AdaptiveFg));
        int n = fg.width;
        float c = n / 2f, safe = 132f * n / 432f;  // 66dp circle of the 108dp layer = 132 px at 432
        Check("adaptive foreground corners are transparent",
              fg.GetPixel(0, 0).a == 0f && fg.GetPixel(n - 1, n - 1).a == 0f && fg.GetPixel(0, n - 1).a == 0f);
        Check("adaptive foreground has opaque art", CountOpaque(fg) > 2000);
        // everything (hull, glow AND exhaust) must sit in the safe circle, or the
        // launcher mask cuts it
        int outside = 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (fg.GetPixel(x, y).a > .2f && new Vector2(x + .5f - c, y + .5f - c).magnitude > safe) outside++;
        Check("ship + glow stay inside the 132 px safe radius (" + outside + " px outside)", outside == 0);
        Object.DestroyImmediate(fg);

        // opaque background layer: no pixel under the mask may be see-through
        var bg = Pixels(AppIconSetter.PathOf(ship, AppIconSetter.AdaptiveBg));
        bool opaque = true;
        foreach (var px in bg.GetPixels32()) if (px.a != 255) { opaque = false; break; }
        Check("adaptive background is fully opaque", opaque);
        Object.DestroyImmediate(bg);
    }

    static int CountOpaque(Texture2D t)
    {
        int k = 0;
        foreach (var px in t.GetPixels32()) if (px.a > 200) k++;
        return k;
    }

    static string PathOf(Texture2D t) { return t == null ? "(none)" : AssetDatabase.GetAssetPath(t); }

    static void PlayerSettingsUse(string ship)
    {
        var def = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
        Check(ship + ": default icon is the master",
              def.Length > 0 && PathOf(def[0]) == AppIconSetter.PathOf(ship, AppIconSetter.Master));

        bool adaptive = false, round = false, legacy = false, allSet = true;
        foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
        {
            string k = AppIconSetter.AndroidKind(kind);
            foreach (var icon in PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind))
            {
                if (k == "Adaptive")
                {
                    adaptive = true;
                    allSet &= PathOf(icon.GetTexture(0)) == AppIconSetter.PathOf(ship, AppIconSetter.AdaptiveBg)
                              && PathOf(icon.GetTexture(1)) == AppIconSetter.PathOf(ship, AppIconSetter.AdaptiveFg);
                }
                else if (k == "Round")
                {
                    round = true;
                    allSet &= PathOf(icon.GetTexture()) == AppIconSetter.PathOf(ship, AppIconSetter.Round);
                }
                else
                {
                    legacy = true;
                    allSet &= PathOf(icon.GetTexture()) == AppIconSetter.PathOf(ship, AppIconSetter.Legacy);
                }
            }
        }
        Check(ship + ": Android has adaptive, round and legacy icon slots", adaptive && round && legacy);
        if (!(adaptive && round && legacy)) allSet = false;
        Check(ship + ": every Android slot uses the candidate (adaptive bg = layer 0, fg = layer 1)", allSet);


        int slots = 0;
        bool iosSet = true;
        foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS))
            foreach (var icon in PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind))
            {
                slots++;
                iosSet &= PathOf(icon.GetTexture()) == AppIconSetter.PathOf(ship, AppIconSetter.Ios);
            }
        Check(ship + ": every iOS icon slot (" + slots + ") uses the alpha-free 1024", slots > 0 && iosSet);
        var importer = AssetImporter.GetAtPath(AppIconSetter.PathOf(ship, AppIconSetter.Ios)) as TextureImporter;
        Check(ship + ": iOS icon imports without alpha",
              importer != null && importer.alphaSource == TextureImporterAlphaSource.None);
    }
}
