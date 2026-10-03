using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Per-ship exhaust (ShipExhaustStyle): every ship has its own themed,
// coloured flipbook; spinners get the spin drift; frames sit on the nozzles,
// freeze with the world, and have boost variants.
public static class ExhaustStyleTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EX] PASS  " : "[EX] FAIL  ") + what);
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

        Texture2D pixels = ReadableAtlas();
        Check("the exhaust atlas loads", ShipExhaustStyle.Atlas != null);
        Check("the exhaust atlas source is readable for the pixel checks", pixels != null);
        Check("the atlas stays modest (<= 2048 x 2048)", ShipExhaustStyle.Atlas != null &&
              ShipExhaustStyle.Atlas.width <= 2048 && ShipExhaustStyle.Atlas.height <= 2048);

        StylesAndFrames(pixels);
        Colours();
        Spinners();
        Freezes();
        BoostVariants(pixels);
        Mounting();
        HullSwap();
        Dock();

        if (pixels != null) Object.DestroyImmediate(pixels);
        Debug.Log("[EX] failures: " + fails);
        return fails;
    }

    static readonly ExhaustLayer[] PlumeLayers = { ExhaustLayer.Plume, ExhaustLayer.PlumeBoost };
    static readonly ExhaustLayer[] SpinLayers =
        { ExhaustLayer.Ring, ExhaustLayer.RingBoost, ExhaustLayer.Wake, ExhaustLayer.WakeBoost };

    static ExhaustLayer[] LayersOf(int id)
    {
        return ShipExhaustStyle.IsSpinDrift(id) ? SpinLayers : PlumeLayers;
    }

    static void StylesAndFrames(Texture2D pixels)
    {
        var kinds = new HashSet<ExhaustKind>();
        foreach (int id in ShipId.All)
        {
            string who = ShipId.NameOf(id) + " (" + id + ")";
            Check(who + " has an exhaust style", ShipExhaustStyle.Has(id));
            var style = ShipExhaustStyle.For(id);
            Check(who + " style is keyed to its ShipId", style.shipId == id && style.key == ShipId.KeyOf(id));
            kinds.Add(style.kind);

            int smears = 0;
            foreach (int t in style.ticks) if (t == 1) smears++;
            Check(who + " loop has 6-8 drawings and exactly one 1-tick smear",
                  style.ticks.Length >= 6 && style.ticks.Length <= 8 && smears == 1);
            var seen = new HashSet<int>();
            for (int t = 0; t < style.loopTicks; t++) seen.Add(ShipExhaustStyle.FrameAt(id, t));
            Check(who + " loop visits every drawing", seen.Count == style.ticks.Length);

            foreach (var layer in LayersOf(id))
            {
                int n = ShipExhaustStyle.FrameCount(id, layer);
                Check(who + " " + layer + " has one drawing per tick-table entry (" + n + ")",
                      n == style.ticks.Length);
                bool load = true, painted = true;
                for (int f = 0; f < n; f++)
                {
                    var s = ShipExhaustStyle.Frame(id, layer, f);
                    load &= s != null && s.texture != null && s.texture.name == "exhaust_atlas";
                    if (s != null && pixels != null) painted &= Coverage(pixels, s.rect) > .01f;
                }
                Check(who + " " + layer + " frames load from the atlas", load);
                Check(who + " " + layer + " frames are all drawn (not blank)", painted);
            }
        }
        Check("every ship has its own exhaust theme (" + kinds.Count + " kinds)", kinds.Count == ShipId.Count);
    }

    static bool Same(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < .003f && Mathf.Abs(a.g - b.g) < .003f && Mathf.Abs(a.b - b.b) < .003f;
    }

    static bool In(Color c, IEnumerable<Color> set)
    {
        foreach (var s in set) if (Same(c, s)) return true;
        return false;
    }

    static void Colours()
    {
        var palettes = new List<Color[]>();
        foreach (int id in ShipId.All)
        {
            string who = ShipId.NameOf(id);
            var style = ExhaustColors.For(id);
            var tones = new[] { style.outer, style.mid, style.core, style.dark, style.accent };
            var hue = ShipHullArt.IdentityHue(id);
            var weapon = WeaponStyleTable.For(id);
            var weaponTones = new[] { weapon.main, weapon.shade, weapon.energy };

            bool hasHue = false;
            foreach (var t in tones) hasHue |= In(t, hue);
            Check(who + " exhaust carries the ship's identity hue", hasHue);
            Check(who + " exhaust silhouette is the hull hue or the weapon's colour",
                  In(style.outer, hue) || In(style.outer, weaponTones));
            Check(who + " exhaust carries its weapon family's palette",
                  In(style.outer, weaponTones) || In(style.mid, weaponTones) || In(style.accent, weaponTones));

            foreach (var other in palettes)
            {
                bool same = true;
                for (int k = 0; k < tones.Length; k++) same &= Same(tones[k], other[k]);
                if (same) { Check(who + " shares an identical exhaust palette", false); break; }
            }
            palettes.Add(tones);
        }
        Check("no two ships share an identical exhaust palette", palettes.Count == ShipId.Count);
        Check("Neon Comet keeps the red comet light-trail",
              ShipExhaustStyle.For(1).kind == ExhaustKind.CometTrail && Same(ShipExhaustStyle.For(1).outer, ShipHullArt.IdentityHue(1)[0]));
    }

    static void Spinners()
    {
        foreach (int id in ShipId.All)
        {
            bool spinner = id == 11 || id == 13;
            string who = ShipId.NameOf(id);
            Check(who + (spinner ? " uses" : " does not use") + " the spin drift",
                  ShipExhaustStyle.IsSpinDrift(id) == spinner && ShipExhaust.UsesSpinDrift(id) == spinner);
            Check(who + (spinner ? " has no nozzle plume" : " has a nozzle plume"),
                  ShipExhaustStyle.HasLayer(id, ExhaustLayer.Plume) != spinner);
            Check(who + (spinner ? " has" : " has no") + " ring and wake drawings",
                  ShipExhaustStyle.HasLayer(id, ExhaustLayer.Ring) == spinner &&
                  ShipExhaustStyle.HasLayer(id, ExhaustLayer.Wake) == spinner);
        }
        Check("Ninja's drift is the blade-arc spin", ShipExhaustStyle.For(11).kind == ExhaustKind.SpinBlades);
        Check("UFO's drift is the vortex spin", ShipExhaustStyle.For(13).kind == ExhaustKind.SpinVortex);
    }

    static GameObject Ship(int id, string name = null)
    {
        var go = new GameObject(name ?? ("ship" + id + "(Clone)"), typeof(SpriteRenderer));
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(id, 0);
        sr.sortingOrder = 5;
        return go;
    }

    static void Freezes()
    {
        // A plume flipbook holds its drawing at timeScale 0 (no game time).
        var go = Ship(2);
        try
        {
            var sr = go.GetComponent<SpriteRenderer>();
            var flame = new GameObject("~Thruster", typeof(SpriteRenderer));
            flame.transform.SetParent(go.transform, false);
            var book = ShipFlameFlipbook.Attach(flame.GetComponent<SpriteRenderer>(), 0, 2);
            var before = flame.GetComponent<SpriteRenderer>().sprite;
            for (int i = 0; i < 60; i++) book.Step(0f);
            Check("a plume does not animate at timeScale 0", flame.GetComponent<SpriteRenderer>().sprite == before);
            bool moved = false;
            for (int i = 0; i < 8; i++) { book.Step(2f / 24f); moved |= flame.GetComponent<SpriteRenderer>().sprite != before; }
            Check("a plume animates when the world runs", moved);
            Check("the plume is the ship's own flipbook", book.ShipIdShown == 2 &&
                  flame.GetComponent<SpriteRenderer>().sprite.texture.name == "exhaust_atlas");
        }
        finally { Object.DestroyImmediate(go); }

        // The spin drift holds too, and the hull does not turn.
        var ufo = Ship(13);
        try
        {
            var drift = ufo.AddComponent<ShipSpinDrift>();
            drift.respondToPause = false;
            drift.Rebuild();
            var ring = drift.Ring.sprite;
            var wake = drift.Wake.sprite;
            var rot = ufo.transform.rotation;
            for (int i = 0; i < 60; i++) drift.Step(0f);
            Check("the spin drift does not animate at timeScale 0",
                  drift.Ring.sprite == ring && drift.Wake.sprite == wake && ufo.transform.rotation == rot);
            bool moved = false;
            for (int i = 0; i < 8; i++) { drift.Step(2f / 24f); moved |= drift.Ring.sprite != ring; }
            Check("the spin drift animates when the world runs", moved);
        }
        finally { Object.DestroyImmediate(ufo); }
    }

    static float Coverage(Texture2D tex, Rect rect)
    {
        float sum = 0f;
        int n = 0;
        for (int y = (int)rect.y; y < (int)rect.yMax; y += 2)
        for (int x = (int)rect.x; x < (int)rect.xMax; x += 2) { sum += tex.GetPixel(x, y).a; n++; }
        return n > 0 ? sum / n : 0f;
    }

    static float Brightness(Texture2D tex, Rect rect)
    {
        float sum = 0f;
        for (int y = (int)rect.y; y < (int)rect.yMax; y += 2)
        for (int x = (int)rect.x; x < (int)rect.xMax; x += 2)
        {
            var c = tex.GetPixel(x, y);
            sum += c.a * (c.r + c.g + c.b);
        }
        return sum;
    }

    static int LowestPaintedRow(Texture2D tex, Rect rect)
    {
        for (int y = (int)rect.y; y < (int)rect.yMax; y++)
            for (int x = (int)rect.x; x < (int)rect.xMax; x++)
                if (tex.GetPixel(x, y).a > .3f) return y - (int)rect.y;
        return (int)rect.height;
    }

    static void BoostVariants(Texture2D pixels)
    {
        foreach (int id in ShipId.All)
        {
            string who = ShipId.NameOf(id);
            bool spinner = ShipExhaustStyle.IsSpinDrift(id);
            var normal = spinner ? ExhaustLayer.Ring : ExhaustLayer.Plume;
            var boost = spinner ? ExhaustLayer.RingBoost : ExhaustLayer.PlumeBoost;
            Check(who + " has a boost variant", ShipExhaustStyle.HasLayer(id, boost) &&
                  ShipExhaustStyle.FrameCount(id, boost) == ShipExhaustStyle.FrameCount(id, normal));
            if (pixels == null) continue;
            float b0 = 0f, b1 = 0f;
            int len0 = 0, len1 = 0;
            int n = ShipExhaustStyle.FrameCount(id, normal);
            for (int f = 0; f < n; f++)
            {
                var a = ShipExhaustStyle.Frame(id, normal, f).rect;
                var c = ShipExhaustStyle.Frame(id, boost, f).rect;
                b0 += Brightness(pixels, a);
                b1 += Brightness(pixels, c);
                len0 += (int)a.height - LowestPaintedRow(pixels, a);
                len1 += (int)c.height - LowestPaintedRow(pixels, c);
            }
            Check(who + " boost drawings are brighter (" + (b1 / Mathf.Max(1f, b0)).ToString("F2") + "x)", b1 > b0 * 1.08f);
            if (!spinner)
                Check(who + " boost plumes are longer", len1 > len0);
        }
    }

    static void Mounting()
    {
        // Plume frames are pivoted at the flame head...
        foreach (int id in ShipId.All)
        {
            if (ShipExhaustStyle.IsSpinDrift(id)) continue;
            bool head = true;
            foreach (var layer in PlumeLayers)
                for (int f = 0; f < ShipExhaustStyle.FrameCount(id, layer); f++)
                {
                    var s = ShipExhaustStyle.Frame(id, layer, f);
                    head &= s.pivot.y > s.rect.height * .95f && Mathf.Abs(s.pivot.x - s.rect.width * .5f) < .01f;
                }
            Check(ShipId.NameOf(id) + " plumes are pivoted at the flame head", head);
        }
        // ...and the boost holder's plumes are the ship's boost drawings, one
        // per nozzle, sitting on the nozzles.
        foreach (int id in new[] { 1, 7, 10, 15 })
        {
            var go = Ship(id);
            try
            {
                var boost = ShipExhaust.ConfigureBoost(go, id);
                var hull = go.GetComponent<SpriteRenderer>().sprite;
                var nozzles = ShipNozzles.For(id);
                bool ok = true;
                for (int n = 0; n < nozzles.Length; n++)
                {
                    var plume = boost.transform.Find("Nozzle" + n);
                    ok &= plume != null && Vector2.Distance(plume.localPosition, ShipNozzles.ToLocal(hull, nozzles[n])) < .001f;
                    var sr = plume != null ? plume.GetComponent<SpriteRenderer>() : null;
                    ok &= sr != null && sr.sprite != null && sr.sprite.name.Contains("PlumeBoost") &&
                          sr.sprite.name.StartsWith(id + "_");
                }
                Check(ShipId.NameOf(id) + " boost plumes sit on its nozzles and use its boost drawings", ok);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }

    static void HullSwap()
    {
        // Title-screen traffic reuses one object for many hulls and is not
        // named "ship<N>": the engine follows whichever hull it shows.
        var go = Ship(2, "~TitleShip");
        try
        {
            var th = go.AddComponent<ShipThruster>();
            th.respondToPause = false;
            th.SendMessage("Start");
            th.SendMessage("LateUpdate");
            Check("traffic knows its hull by its art (Volt Viper)", th.ShipIndex == 2);
            var flame = go.transform.Find("~Thruster");
            var book = flame != null ? flame.GetComponent<ShipFlameFlipbook>() : null;
            Check("traffic burns that ship's own exhaust", book != null && book.ShipIdShown == 2);

            go.GetComponent<SpriteRenderer>().sprite = shopingShips.SpriteFor(13, 0);
            th.SendMessage("LateUpdate");
            Check("a UFO hull switches the engine to its spin drift",
                  th.ShipIndex == 13 && th.Drift != null && go.transform.Find("~Thruster") == null);

            go.GetComponent<SpriteRenderer>().sprite = shopingShips.SpriteFor(10, 0);
            th.SendMessage("LateUpdate");
            th.Drift.Step(0f);
            Check("a Paranoid hull gets its two pod plumes back",
                  th.ShipIndex == 10 && go.transform.Find("~Thruster") != null && go.transform.Find("~Thruster1") != null &&
                  !th.Drift.Ring.enabled);

            th.idleScale = 1.6f;
            th.SendMessage("LateUpdate");
            var b = go.transform.Find("~Thruster").GetComponent<ShipFlameFlipbook>();
            Check("a launch-size flame switches to the boost drawings", b.Boost &&
                  go.transform.Find("~Thruster").GetComponent<SpriteRenderer>().sprite.name.Contains("PlumeBoost"));
        }
        finally { Object.DestroyImmediate(go); }
    }

    static void Dock()
    {
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        PlayerPrefs.SetString("boughtship1", "True");
        PlayerPrefs.SetInt("spawnShip", 1);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        Check("the dock was built", dock != null);
        if (dock == null) return;
        foreach (int id in new[] { 11, 13 })
        {
            var bay = dock.bays[id];
            Check(ShipId.NameOf(id) + " berth has a spin drift and no thruster",
                  bay.drift != null && bay.thruster == null);
            if (bay.drift == null) continue;
            bay.drift.SendMessage("Start");
            bay.SnapPower();
            bay.drift.Step(0f);
            Check(ShipId.NameOf(id) + " parked powered down shows no drift", !bay.drift.Ring.enabled && !bay.drift.Wake.enabled);
            dock.Select(id);
            bay.SnapPower();
            bay.drift.Step(1f / 24f);
            Check(ShipId.NameOf(id) + " selected shows its spin drift", bay.drift.Ring.enabled && bay.drift.Wake.enabled &&
                  bay.drift.Ring.sprite != null && bay.drift.Ring.sprite.name.StartsWith(id + "_Ring"));
        }
        dock.Select(2);
        dock.bays[2].SnapPower();
        var th = dock.bays[2].thruster;
        th.SendMessage("Start", SendMessageOptions.DontRequireReceiver);
        th.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        var flame = dock.bays[2].ship.Find("~Thruster");
        Check("the selected Volt Viper burns its own exhaust",
              th.IsBurning && flame != null && flame.GetComponent<SpriteRenderer>().sprite.name.StartsWith("2_Plume"));
    }

    // The atlas is imported compressed and non-readable; read the source PNG.
    static Texture2D ReadableAtlas()
    {
        var tex = ShipExhaustStyle.Atlas;
        string path = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return copy.LoadImage(File.ReadAllBytes(path)) ? copy : null;
    }
}
