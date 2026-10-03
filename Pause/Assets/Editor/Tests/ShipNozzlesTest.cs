using System.IO;
using UnityEditor;
using UnityEngine;

// Every roster hull must have engine nozzles defined, and each one must sit
// on the ship's own painted art -- inside the sprite's bounds and on (or
// within a pixel of) an opaque pixel. Guards the Paranoid regression, where a
// shared "bottom edge, +/- a fraction of the width" rule put both plumes
// under the belly instead of on its two outboard engine pods.
public static class ShipNozzlesTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[NZ] PASS  " : "[NZ] FAIL  ") + what);
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

        for (int i = 1; i < shopingShips.shipTotal; i++)
        {
            string name = shopingShips.NameFor(i);
            Check(name + " has nozzle points defined", ShipNozzles.Has(i));
            var hull = shopingShips.SpriteFor(i, 0);
            Check(name + " has hull art", hull != null);
            if (hull == null || !ShipNozzles.Has(i)) continue;

            Texture2D pixels = ReadablePixels(hull.texture);
            var nozzles = ShipNozzles.For(i);
            var mounts = ShipExhaust.MountsFor(hull, i);
            Check(name + " mounts one plume per nozzle", mounts.Length == nozzles.Length);

            for (int n = 0; n < nozzles.Length; n++)
            {
                var nz = nozzles[n];
                Vector2 local = ShipNozzles.ToLocal(hull, nz);
                Bounds b = hull.bounds;
                Check(name + " nozzle " + n + " (" + nz.x + "," + nz.y + ") lies inside the sprite bounds",
                      local.x > b.min.x && local.x < b.max.x && local.y > b.min.y && local.y < b.max.y);
                Check(name + " nozzle " + n + " sits in the lower half of the hull",
                      local.y < b.center.y);
                Check(name + " nozzle " + n + " has a sensible plume scale",
                      nz.scale > .2f && nz.scale <= 1f);
                if (pixels != null)
                    Check(name + " nozzle " + n + " sits on painted hull pixels",
                          NearOpaque(pixels, hull.rect, nz.x, nz.y, 1.5f));
                if (n < mounts.Length)
                    Check(name + " plume " + n + " is mounted on that nozzle",
                          Vector2.Distance((Vector2)mounts[n], local) < .0001f);
            }
            if (pixels != null) Object.DestroyImmediate(pixels);
        }

        // Paranoid specifically: two plumes, one on each outboard pod.
        var paranoid = shopingShips.SpriteFor(10, 0);
        if (paranoid != null)
        {
            var m = ShipExhaust.MountsFor(paranoid, 10);
            float halfWidth = paranoid.bounds.extents.x;
            Check("Paranoid has two engine plumes", m.Length == 2);
            if (m.Length == 2)
                Check("Paranoid's plumes leave its outboard pods (|x| > 70% of half-width)",
                      Mathf.Abs(m[0].x) > halfWidth * .7f && Mathf.Abs(m[1].x) > halfWidth * .7f &&
                      m[0].x < 0f && m[1].x > 0f);
        }

        Debug.Log("[NZ] failures: " + fails);
        return fails;
    }

    // Ship textures are imported non-readable; read the source PNG instead.
    static Texture2D ReadablePixels(Texture2D texture)
    {
        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return copy.LoadImage(File.ReadAllBytes(path)) ? copy : null;
    }

    static bool NearOpaque(Texture2D tex, Rect rect, float x, float y, float radius)
    {
        int r = Mathf.CeilToInt(radius);
        int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            int px = cx + dx, py = cy + dy;
            if (px < 0 || py < 0 || px >= rect.width || py >= rect.height) continue;
            if (new Vector2(px + .5f - x, py + .5f - y).magnitude > radius + .75f) continue;
            if (tex.GetPixel((int)rect.x + px, (int)rect.y + py).a > .5f) return true;
        }
        return false;
    }
}
