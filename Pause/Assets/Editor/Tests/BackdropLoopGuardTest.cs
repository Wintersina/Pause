using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Per-frame guards on the ambient loop atlases (smoke, fire, steam, eruption,
// leaks, lights) of every world, and on how the loops are drawn.
//
// Reported, from the pixels (alpha > 8 inside each 256 cell): every frame whose
// art comes within 6 px of the top or a side of its cell (the plume is cut by the
// cell / sprite rect), and every frame with a detached flat SLIVER (a hatched strip
// hovering above the plume: the leftover top row of the crop). Table lines are
// "[LOOPGUARD] world atlas frame ...".
//
// Failing: any frame with art in the top 2 rows or within 2 px of a side,
// or with a detached flat sliver.
//
// Drawing: every loop of a pinned pool draws above the plates of every pool of its
// layer (EmberDirector: a fire's / pipe's plume was hidden behind a landmark plate
// higher up the screen and cut flat at its edge).
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod BackdropLoopGuardTest.Run
public static class BackdropLoopGuardTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LOOPGUARD] PASS  " : "[LOOPGUARD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Root = "Assets/Art/Backgrounds/Resources/Worlds/";
    const byte Opaque = 8;
    const int Margin = 6, HardMargin = 2;

    static readonly string[][] Loops =
    {
        new[] { "Ember", "smoke", "eruption", "lavafire", "leaks", "lights" },
        new[] { "Verdant", "smoke", "firesmoke", "wildfire", "leaks", "lights" },
        new[] { "Frost", "smoke", "steam", "fire_lights", "beacons" },
        new[] { "Tide", "smoke", "smoke_2", "flames", "flames_2", "leaks", "leaks_2", "lights", "lights_2", "surf", "surf_2", "surf_3", "surf_4" },
    };
    static readonly HashSet<string> KnownEdge = new HashSet<string>();
    // Tide/lights: beacon_mint_01's lit frame spreads a flat 38x7 pool of light under the lamp (light spill, not a detached plume strip).
    static readonly HashSet<string> KnownSliver = new HashSet<string> { "Tide/lights" };

    [System.Serializable] class R { public string n; public int x, y, w, h; }
    [System.Serializable] class M { public R[] sprites; }

    public static int Execute()
    {
        fails = 0;
        foreach (var w in Loops) for (int i = 1; i < w.Length; i++) Atlas(w[0], w[i]);
        foreach (string world in new[] { "Ember", "Verdant", "Tide", "Frost" }) Drawing(world);
        Debug.Log("[LOOPGUARD] failures: " + fails);
        return fails;
    }

    static void Atlas(string world, string atlas)
    {
        string png = Root + world + "/Backdrop3/" + atlas + ".png", json = Root + world + "/Backdrop3/" + atlas + ".json";
        if (!File.Exists(png) || !File.Exists(json)) { Debug.Log("[LOOPGUARD] " + world + "/" + atlas + " not installed, skipped"); return; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(png));
        var m = JsonUtility.FromJson<M>(File.ReadAllText(json));
        var px = tex.GetPixels32();
        int W = tex.width;
        int hard = 0, slivers = 0, soft = 0;
        string key = world + "/" + atlas;
        foreach (var r in m.sprites)
        {
            int minX = r.w, maxX = -1, minY = r.h, maxY = -1;
            var mask = new bool[r.w * r.h];
            for (int y = 0; y < r.h; y++)
                for (int x = 0; x < r.w; x++)
                {
                    // json y is from the bottom, like the texture
                    bool on = px[(r.y + (r.h - 1 - y)) * W + r.x + x].a > Opaque;
                    mask[y * r.w + x] = on;
                    if (!on) continue;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            if (maxX < 0) continue;
            int top = minY, left = minX, right = r.w - 1 - maxX;
            string sliver = Sliver(mask, r.w, r.h);
            bool edge = top < Margin || left < Margin || right < Margin;
            if (edge || sliver != "")
                Debug.Log("[LOOPGUARD] " + world + " " + atlas + " " + r.n + " top " + top + " left " + left + " right " + right + (sliver != "" ? " sliver " + sliver : ""));
            if (top < HardMargin || left < HardMargin || right < HardMargin) hard++;
            else if (edge) soft++;
            if (sliver != "") slivers++;
        }
        Object.DestroyImmediate(tex);
        Check(key + ": no frame's art touches the top or a side of its cell (" + hard + " do" + (soft > 0 ? ", " + soft + " within " + Margin + " px" : "") + ")",
              hard == 0 || KnownEdge.Contains(key));
        Check(key + ": no detached flat sliver above a plume (" + slivers + " frames)", slivers == 0 || KnownSliver.Contains(key));
    }

    // "wxh@x,y" of a detached flat strip (>= 24 px wide, <= 12 tall, >= 3x wider than tall, >= 30 px) or "".
    static string Sliver(bool[] m, int w, int h)
    {
        var lab = new int[m.Length];
        var stack = new Stack<int>();
        var sizes = new List<int> { 0 };
        var boxes = new List<int[]> { null };
        for (int i = 0; i < m.Length; i++)
        {
            if (!m[i] || lab[i] != 0) continue;
            int id = sizes.Count;
            var box = new[] { w, h, -1, -1 };
            int n = 0;
            lab[i] = id; stack.Push(i);
            while (stack.Count > 0)
            {
                int p = stack.Pop(); n++;
                int px = p % w, py = p / w;
                if (px < box[0]) box[0] = px; if (py < box[1]) box[1] = py;
                if (px > box[2]) box[2] = px; if (py > box[3]) box[3] = py;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int qx = px + dx, qy = py + dy;
                        if (qx < 0 || qy < 0 || qx >= w || qy >= h) continue;
                        int q = qy * w + qx;
                        if (m[q] && lab[q] == 0) { lab[q] = id; stack.Push(q); }
                    }
            }
            sizes.Add(n); boxes.Add(box);
        }
        int main = 0;
        for (int i = 1; i < sizes.Count; i++) if (sizes[i] > sizes[main]) main = i;
        string o = "";
        for (int i = 1; i < sizes.Count; i++)
        {
            if (i == main || sizes[i] < 30) continue;
            int bw = boxes[i][2] - boxes[i][0] + 1, bh = boxes[i][3] - boxes[i][1] + 1;
            if (bw >= 24 && bh <= 12 && bw >= 3 * bh) o += bw + "x" + bh + "@" + boxes[i][0] + "," + boxes[i][1] + " ";
        }
        return o.Trim();
    }

    static void Drawing(string world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .6f;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        var dir = wb.Current != null ? wb.Current.Director : null;
        if (dir == null) { Check(world + " backdrop director", false); return; }
        int loops = 0, hidden = 0;
        for (float t = 0f; t < 45f; t += 1f / 30f)
        {
            wb.Step(1f / 30f);
            if (((int)(t * 30f)) % 60 != 0) continue;
            int plates = int.MinValue;
            foreach (var pool in dir.Pools)
                if (pool.name == "ground" || pool.name == "landmarks")
                    foreach (var p in pool.items) if (p.active && p.sr.sortingOrder > plates) plates = p.sr.sortingOrder;
            foreach (var pool in dir.Pools)
                if (pool.name == "ground" || pool.name == "landmarks")
                    foreach (var p in pool.items)
                    {
                        if (!p.active) continue;
                        foreach (var sr in p.body.GetComponentsInChildren<SpriteRenderer>())
                        {
                            if (!sr.name.StartsWith("ambient") || !sr.enabled) continue;
                            loops++;
                            if (sr.sortingOrder <= plates) hidden++;
                        }
                    }
        }
        Check(world + ": every live loop (" + loops + " sampled) draws above every ground plate (" + hidden + " do not)", hidden == 0);
        Object.DestroyImmediate(wb.gameObject);
    }
}
