using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// The Akira hull redraw (ShipHullArt + Art/Resources/ShipArt/Hulls):
//   - every roster ship has its sheet, rest, idle, bank, hit and damage frames
//   - in-game hull size and collider stay within 10% of the 2016 art's
//   - each sheet uses only the style guide's palette plus its identity hue
//   - clean alpha: no faint pixels outside a 2 px band of the ink outline
//   - the idle flipbook advances no frames at timeScale 0 (but does run),
//     banks on a steer, not on a teleport, and flashes on a hit
//   - the exhaust is the drawn tail-light flipbook
public static class ShipArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SA] PASS  " : "[SA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    // The old sprites' rects, in px at 100 PPU (shopingShips.LoadRuntimeSprite
    // and OriginalShipArt before the redraw): the size the game was balanced on.
    static readonly Vector2[] oldRects =
    {
        Vector2.zero,
        new Vector2(32, 29), new Vector2(28, 23), new Vector2(29, 28), new Vector2(46, 57),
        new Vector2(47, 55), new Vector2(56, 55), new Vector2(51, 55), new Vector2(28, 27),
        new Vector2(20, 29), new Vector2(30, 22), new Vector2(30, 30), new Vector2(24, 30),
        new Vector2(26, 26), new Vector2(20, 24), new Vector2(22, 29),
    };

    // docs/art-style.md section 1 (docs/art-samples/src/akira.py), plus
    // BONE's shadow tone used on stripes in shade.
    static readonly string[] guidePalette =
    {
        "#070A16", "#0E1424", "#1A1F45", "#2A2E6B", "#3A2A5C", "#D8232C", "#86121F", "#FF5B45",
        "#F2862B", "#FFB43C", "#A9481A", "#1FB5B9", "#6EF2EE", "#0F5E6A", "#FF2E88", "#8E1450",
        "#140C14", "#F4EAD4", "#2C2D40", "#1A1A28", "#5A5C78", "#5A6A88", "#262D44", "#A3B4CC",
        "#74409A", "#3A1E52", "#A86CD0", "#8FA84E", "#3E5229", "#D4E68E", "#C8FF3A", "#605878",
        "#2C2638", "#958AA4", "#2EE6A6", "#0E5A50", "#0A3238", "#B9AE98",
    };

    const float SizeTolerance = .10f;
    const byte Opaque = 250;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        foreach (int id in ShipId.All) Frames(id);
        foreach (int id in ShipId.All) Size(id);
        foreach (int id in ShipId.All) Pixels(id);
        IdleClock();
        Exhaust();

        Debug.Log("[SA] failures: " + fails);
        return fails;
    }

    static string Label(int id) { return id + " " + ShipId.NameOf(id); }

    static void Frames(int id)
    {
        string who = Label(id);
        var sheet = ShipHullArt.SheetFor(id);
        Check(who + " has its Akira sheet (" + ShipHullArt.Folder + ShipId.KeyOf(id) + ")",
              sheet != null && sheet.name == ShipId.KeyOf(id));
        if (sheet == null) return;
        Check(who + " sheet is " + ShipHullArt.Columns + "x" + ShipHullArt.States + " cells of " + ShipHullArt.Cell + " px",
              sheet.width == ShipHullArt.Columns * ShipHullArt.Cell && sheet.height == ShipHullArt.States * ShipHullArt.Cell);
        Check(who + " roster art is the new hull", shopingShips.SpriteFor(id) == ShipHullArt.Rest(id));

        bool all = true;
        var distinct = new HashSet<Sprite>();
        for (int s = 0; s < ShipHullArt.States; s++)
            for (int c = 0; c < ShipHullArt.Columns; c++)
            {
                var sprite = ShipHullArt.Get(id, s, c);
                all &= sprite != null && sprite.texture == sheet;
                if (sprite != null) distinct.Add(sprite);
            }
        Check(who + " every damage state x drawing loads", all);
        Check(who + " frames are distinct sprites", distinct.Count == ShipHullArt.States * ShipHullArt.Columns);
        for (int f = 0; f < ShipHullArt.IdleDrawings; f++)
            Check(who + " idle drawing " + f + " loads", shopingShips.IdleSpriteFor(id, 0, f) == ShipHullArt.Get(id, 0, f));
        var damage = shopingShips.DamageSpritesFor(id);
        Check(who + " has intact/damaged/critical art",
              damage.Length == 3 && damage[0] != damage[1] && damage[1] != damage[2] && damage[2] != null);
    }

    // World size and collider: compare the hull as spawnShips.ApplyHull
    // dresses it now against the same maths on the old rect. The collider is
    // the tight baked polygon now (ShipHitbox, ShipHitboxTest): it must fit
    // inside the drawn hull, never bigger.
    static void Size(int id)
    {
        string who = Label(id);
        var sprite = shopingShips.SpriteFor(id);
        if (sprite == null) { Check(who + " sprite for sizing", false); return; }

        Vector2 oldBounds = oldRects[id] / 100f;
        float oldScale = shopingShips.ReferenceHullSize / Mathf.Max(oldBounds.x, oldBounds.y);
        Vector2 oldWorld = oldBounds * oldScale;

        var go = new GameObject("ship" + id + "(Clone)", typeof(SpriteRenderer), typeof(BoxCollider2D));
        try
        {
            spawnShips.ApplyHull(go, id);
            var sr = go.GetComponent<SpriteRenderer>();
            float scale = go.transform.localScale.x;
            Vector2 world = (Vector2)sr.sprite.bounds.size * scale;
            var hb = ShipHitbox.Of(go);
            Bounds hit = hb != null ? hb.Hull.bounds : new Bounds();
            Vector2 collider = hb != null ? (Vector2)hit.size : Vector2.zero;
            Check(who + " hull size " + world.ToString("F3") + " within 10% of " + oldWorld.ToString("F3"),
                  Within(world, oldWorld));
            Check(who + " hull hitbox " + collider.ToString("F3") + " is a polygon inside the hull (" + world.ToString("F3") + ")",
                  hb != null && go.GetComponent<BoxCollider2D>() == null && collider.x > 0f &&
                  collider.x <= world.x && collider.y <= world.y);
            Check(who + " sprite bounds keep the old local size (PPU-matched)",
                  Within(sr.sprite.bounds.size, oldBounds));
        }
        finally { Object.DestroyImmediate(go); }
    }

    static bool Within(Vector2 a, Vector2 b)
    {
        return Mathf.Abs(a.x - b.x) <= b.x * SizeTolerance && Mathf.Abs(a.y - b.y) <= b.y * SizeTolerance;
    }

    static Texture2D ReadPng(Texture2D texture)
    {
        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return copy.LoadImage(File.ReadAllBytes(path)) ? copy : null;
    }

    static List<Color32> Palette(params string[] hex)
    {
        var list = new List<Color32>();
        foreach (var h in hex)
        {
            Color c;
            if (ColorUtility.TryParseHtmlString(h, out c)) list.Add(c);
        }
        return list;
    }

    static bool Near(Color32 c, List<Color32> palette, int tolerance)
    {
        foreach (var p in palette)
        {
            int dr = c.r - p.r, dg = c.g - p.g, db = c.b - p.b;
            if (dr * dr + dg * dg + db * db <= tolerance * tolerance) return true;
        }
        return false;
    }

    // Palette compliance on flat fills (a pixel whose 3x3 neighbourhood is one
    // colour; antialiased seams are blends by nature), and clean alpha.
    static void Pixels(int id)
    {
        string who = Label(id);
        var png = ReadPng(ShipHullArt.SheetFor(id));
        if (png == null) { Check(who + " sheet png readable", false); return; }
        try
        {
            var r = ShipHullArt.RectFor(id);
            var palette = Palette(guidePalette);
            palette.AddRange(Palette(r.hue, r.hueShadow, r.hueHighlight));
            var inks = Palette("#140C14", "#D8232C");   // INK, and RED for the hit flash's ink
            int w = png.width, h = png.height;
            Color32[] px = png.GetPixels32();

            var ink = new bool[px.Length];
            for (int i = 0; i < px.Length; i++)
                ink[i] = px[i].a >= Opaque && Near(px[i], inks, 40);

            int flat = 0, off = 0, faint = 0, stray = 0;
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    var c = px[y * w + x];
                    if (c.a >= Opaque)
                    {
                        bool uniform = true;
                        for (int dy = -1; dy <= 1 && uniform; dy++)
                            for (int dx = -1; dx <= 1 && uniform; dx++)
                            {
                                var n = px[(y + dy) * w + x + dx];
                                uniform = n.a >= Opaque &&
                                          Mathf.Abs(n.r - c.r) + Mathf.Abs(n.g - c.g) + Mathf.Abs(n.b - c.b) <= 12;
                            }
                        if (!uniform) continue;
                        flat++;
                        if (!Near(c, palette, 12)) off++;
                    }
                    else if (c.a > 0)
                    {
                        faint++;
                        bool banded = false;
                        for (int dy = -2; dy <= 2 && !banded; dy++)
                            for (int dx = -2; dx <= 2 && !banded; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx >= 0 && yy >= 0 && xx < w && yy < h) banded = ink[yy * w + xx];
                            }
                        if (!banded) stray++;
                    }
                }
            Check(who + " flat fills are guide palette + identity hue (" + off + " of " + flat + " off)",
                  flat > 0 && off <= flat / 200);
            Check(who + " clean alpha: no faint pixel beyond 2 px of the ink outline (" + stray + " of " + faint + ")",
                  stray == 0);
            // the sheet's border cells are empty: nothing bleeds between frames
            bool edgesClear = true;
            for (int x = 0; x < w && edgesClear; x++) edgesClear = px[x].a == 0 && px[(h - 1) * w + x].a == 0;
            Check(who + " sheet edges are transparent", edgesClear);
        }
        finally { Object.DestroyImmediate(png); }
    }

    static void IdleClock()
    {
        var go = new GameObject("ship1(Clone)");
        try
        {
            var anim = new ShipHullAnimator(1, go.transform, 0);
            anim.Step(.2f, .2f, 0);
            int column = anim.Column;
            float ticks = anim.Ticks;
            for (int i = 0; i < 120; i++) anim.Step(0f, 1f / 60f, 0);   // two seconds frozen
            Check("idle advances no frames at timeScale 0", anim.Column == column && anim.Ticks == ticks);

            var seen = new HashSet<int>();
            for (int i = 0; i < 120; i++) { anim.Step(1f / 60f, 1f / 60f, 0); seen.Add(anim.Column); }
            Check("idle plays its drawings when time runs (" + seen.Count + " seen)", seen.Count >= 4);
            Check("a full idle loop is 1-2 s", ShipHullArt.IdleLoopTicks >= 24 && ShipHullArt.IdleLoopTicks <= 48);

            anim.Step(1f / 60f, 1f / 60f, 1);
            Check("a hit flashes the hull", anim.Column == ShipHullArt.Hit);
            for (int i = 0; i < 12; i++) anim.Step(0f, 1f / 60f, 1);
            Check("the hit flash is a few ticks, even with the world frozen", anim.Column != ShipHullArt.Hit);
            anim.Step(1f / 60f, 1f / 60f, 0);
            Check("healing does not flash", anim.Column != ShipHullArt.Hit);

            for (int i = 0; i < 4; i++) { go.transform.position += new Vector3(-.03f, 0f, 0f); anim.Step(1f / 60f, 1f / 60f, 0); }
            Check("steering left banks left", anim.Column == ShipHullArt.BankLeft);
            for (int i = 0; i < 4; i++) { go.transform.position += new Vector3(.03f, 0f, 0f); anim.Step(1f / 60f, 1f / 60f, 0); }
            Check("steering right banks right", anim.Column == ShipHullArt.BankRight);
            for (int i = 0; i < 30; i++) anim.Step(1f / 60f, 1f / 60f, 0);
            Check("holding still levels out", anim.Bank == 0);
            go.transform.position += new Vector3(2f, 0f, 0f);
            anim.Step(1f / 60f, 1f / 60f, 0);
            Check("a teleport does not bank", anim.Bank == 0);

            var spinner = new ShipHullAnimator(11, go.transform, 0);
            for (int i = 0; i < 4; i++) { go.transform.position += new Vector3(-.03f, 0f, 0f); spinner.Step(1f / 60f, 1f / 60f, 0); }
            Check("a spinning craft never banks", spinner.Bank == 0);
        }
        finally { Object.DestroyImmediate(go); }
    }

    static void Exhaust()
    {
        // Each ship's plume is its own flipbook in the shared exhaust atlas
        // (ShipExhaustStyle; ExhaustStyleTest covers the per-ship details).
        bool all = true;
        foreach (int id in ShipId.All)
        {
            if (ShipExhaust.UsesSpinDrift(id)) continue;
            for (int f = 0; f < ShipExhaust.FrameCount(id); f++)
            {
                var s = ShipExhaust.Frame(id, false, f);
                all &= s != null && s.texture.name == "exhaust_atlas";
            }
        }
        Check("every nozzle ship's exhaust is a flipbook in the exhaust atlas", all);
        var seen = new HashSet<int>();
        for (int t = 0; t < 16; t++) seen.Add(ShipExhaust.FrameAt(ShipId.Starter, t));
        Check("the exhaust loop visits every frame", seen.Count == ShipExhaust.FrameCount(ShipId.Starter));
        var head = ShipExhaust.Frame(ShipId.Starter, false, 0);
        Check("the plume is pivoted at its flame head", head != null && head.pivot.y > head.rect.height * .95f);
    }
}
