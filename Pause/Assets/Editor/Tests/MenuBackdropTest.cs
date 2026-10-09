using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The menus' backdrop (MenuBackdrop): the real in-game BackdropCatalog tile
// layers, a random unlocked world + variant per arrival, never the same twice
// in a row, kept across the menu pages, nothing drawn around the UI.
public static class MenuBackdropTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MBD] PASS  " : "[MBD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Unlocks();
            NeverRepeats();
            Persistence();
            RealAssets();
            NoUiObjects();
            Allocation();
        }
        finally
        {
            MenuBackdropSelection.ResetForTests();
            foreach (var m in Object.FindObjectsByType<MenuBackdrop>(FindObjectsSortMode.None)) Object.DestroyImmediate(m.gameObject);
        }
        Debug.Log("[MBD] failures: " + fails);
        return fails;
    }

    static List<MenuBackdropSelection.Pick> List(int highest)
    {
        var l = new List<MenuBackdropSelection.Pick>();
        MenuBackdropSelection.Candidates(highest, l);
        return l;
    }

    static void Unlocks()
    {
        var space = List(0);
        bool onlySpace = space.Count > 0;
        foreach (var p in space) if (p.world != "Space") onlySpace = false;
        Check("a new player (highestWorld 0) is offered only Space: " + space.Count + " backdrops", onlySpace && space.Count >= 2);

        int last = WorldManager.Worlds.Length - 1;
        for (int h = 0; h <= last; h++)
        {
            var l = List(h);
            bool ok = true;
            foreach (var p in l)
            {
                int idx = -1;
                for (int i = 0; i < WorldManager.Worlds.Length; i++) if (WorldManager.Worlds[i].displayName == p.world) idx = i;
                if (idx < 0 || idx > h) ok = false;
            }
            Check("highestWorld " + h + " offers only worlds 0.." + h + " (" + l.Count + " backdrops)", ok);
        }
        var all = List(last);
        var seen = new HashSet<string>();
        foreach (var p in all) seen.Add(p.world);
        Check("space + frost + verdant are all offered once reached (" + seen.Count + " worlds)",
              seen.Contains("Space") && seen.Contains("Frost") && seen.Contains("Verdant"));

        // Tide (world 4) has its own backdrop now: all four ground sets join the pool once the player has reached it
        // (highestWorld 4: a developer unlock, or the release switch flipped), and not before
        int tideAt = -1;
        for (int i = 0; i < WorldManager.Worlds.Length; i++) if (WorldManager.Worlds[i].displayName == "Tide") tideAt = i;
        int tideSets = 0, tideBefore = 0;
        foreach (var p in List(tideAt)) if (p.world == "Tide") tideSets++;
        foreach (var p in List(tideAt - 1)) if (p.world == "Tide") tideBefore++;
        Check("Tide's four ground sets are offered once reached (" + tideSets + ") and not before (" + tideBefore + ")",
              tideAt == 4 && tideSets == 4 && tideBefore == 0);

        // the PlayerPrefs hook
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 1);
        Check("HighestWorld reads the saved progress", MenuBackdropSelection.HighestWorld() == 1);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 99);
        Check("HighestWorld is clamped to the world list", MenuBackdropSelection.HighestWorld() == last);
    }

    static void NeverRepeats()
    {
        foreach (int h in new[] { 0, 1, WorldManager.Worlds.Length - 1 })
        {
            var l = List(h);
            var rng = new System.Random(7 + h);
            var last = new MenuBackdropSelection.Pick();
            bool repeat = false, offList = false;
            var worldsSeen = new HashSet<string>();
            for (int i = 0; i < 400; i++)
            {
                var p = MenuBackdropSelection.Choose(l, last, rng);
                if (last.Valid && p.Same(last)) repeat = true;
                bool found = false;
                foreach (var c in l) if (c.Same(p)) found = true;
                if (!found) offList = true;
                worldsSeen.Add(p.world);
                last = p;
            }
            Check("highest " + h + ": 400 rolls never repeat the previous world+variant and stay on the unlocked list ("
                  + worldsSeen.Count + " worlds seen)", !repeat && !offList);
        }
        var single = new List<MenuBackdropSelection.Pick> { new MenuBackdropSelection.Pick { world = "Space", variant = 1 } };
        var only = MenuBackdropSelection.Choose(single, single[0], new System.Random(1));
        Check("with a single backdrop available it is kept rather than failing", only.Valid && only.variant == 1);
    }

    static void Persistence()
    {
        MenuBackdropSelection.ResetForTests();
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, WorldManager.Worlds.Length - 1);
        var rng = new System.Random(3);
        var home = MenuBackdropSelection.Enter("startS4", rng);
        int rolls = MenuBackdropSelection.Rolls;
        bool same = true;
        foreach (string page in new[] { "creditsS7", "startS4", "shopS6", "startS4", "leaderboardS3", "startS4" })
            same &= MenuBackdropSelection.Enter(page, rng).Same(home);
        Check("credits / shop / options and back keep the same backdrop (" + home + ")", same && MenuBackdropSelection.Rolls == rolls);

        MenuBackdropSelection.Leave("gameS1");
        var next = MenuBackdropSelection.Enter("startS4", rng);
        Check("returning home from a run rolls a new one, different from the last (" + home + " -> " + next + ")",
              MenuBackdropSelection.Rolls == rolls + 1 && !next.Same(home));

        MenuBackdropSelection.Leave("spashS7");
        var third = MenuBackdropSelection.Enter("startS4", rng);
        Check("an app start rolls again, never repeating (" + next + " -> " + third + ")", !third.Same(next));

        // a menu page opened straight from a run (death -> shop) also rolls
        MenuBackdropSelection.Leave("gameS1");
        int r2 = MenuBackdropSelection.Rolls;
        MenuBackdropSelection.Enter("shopS6", rng);
        Check("a menu page entered from a run rolls too", MenuBackdropSelection.Rolls == r2 + 1);
    }

    static void RealAssets()
    {
        var all = List(WorldManager.Worlds.Length - 1);
        int built = 0;
        foreach (var pick in all)
        {
            var go = new GameObject("~mbdTest");
            var mb = go.AddComponent<MenuBackdrop>();
            bool ok = mb.Build(pick);
            var spec = BackdropCatalog.For(pick.world);
            if (!ok) { Check(pick + " builds from the catalog", false); Object.DestroyImmediate(go); continue; }
            built++;
            var set = mb.Set;
            int tileLayers = 0;
            foreach (var l in spec.layers) if (l.kind != BackdropCatalog.Kind.Pieces) tileLayers++;
            bool ratesMatch = set.Tiles.Count == tileLayers;
            foreach (var t in set.Tiles) ratesMatch &= Mathf.Approximately(t.layer.rate, spec.Rate(t.layer.name));
            bool realArt = true;
            string folder = BackdropCatalog.TileFolder(pick.world, pick.variant);
            foreach (var t in set.Tiles)
            {
                string tex = pick.world == "Space" && t.layer.name == "sky" ? SpaceSkySelection.TextureFor(pick.variant) : t.layer.texture;
                var sprite = Resources.Load<Sprite>(folder + tex);
                realArt &= sprite != null && t.SpriteName == sprite.name;
            }
            Check(pick + ": " + set.Tiles.Count + " catalog tile layers at the catalog's rates, from " + folder + " (real art)",
                  ratesMatch && realArt && set.Tiles.Count > 0);
            Check(pick + ": tiles only (no director, no atlases loaded)",
                  set.Director == null && set.Fx.Count == 0 && set.Anim.Count == 0 && set.Textures.Count == set.Tiles.Count);
            Check(pick + ": graded dimmer than a run, never brighter than the game's lift",
                  AllTilesNotBrighter(set, spec, pick.variant));

            // it scrolls, slowly: the slowest layers move, none faster than a run's start
            var before = new float[set.Tiles.Count];
            for (int i = 0; i < before.Length; i++) before[i] = (float)set.Tiles[i].travel;
            mb.Step(1f);
            bool moves = true;
            for (int i = 0; i < before.Length; i++)
            {
                float d = (float)set.Tiles[i].travel - before[i];
                moves &= d > 0f && d <= MenuBackdrop.Velocity * set.Tiles[i].layer.rate + set.Tiles[i].layer.flow + 1e-4f;
            }
            Check(pick + ": every layer scrolls at the game's own rate at a run's opening speed", moves);
            Object.DestroyImmediate(go);
        }
        Check("a backdrop was built for every unlocked candidate (" + built + "/" + all.Count + ")", built == all.Count && built > 0);

        // only the chosen backdrop is alive: rebuilding replaces the set
        var host = new GameObject("~mbdTest2");
        var m2 = host.AddComponent<MenuBackdrop>();
        m2.Build(all[0]);
        var root1 = m2.Set.Root;
        m2.Build(all[all.Count - 1]);
        Check("a new pick tears the previous set down (one backdrop root alive)",
              root1 == null && host.GetComponentsInChildren<SpriteRenderer>(true).Length > 0 && host.transform.childCount == 1);
        Object.DestroyImmediate(host);
    }

    static bool AllTilesNotBrighter(BackdropSet set, BackdropCatalog.Spec spec, int variant)
    {
        foreach (var t in set.Tiles)
        {
            float game = BackdropGrade.Lift(spec, t.layer, variant);
            if (t.GradeMaterial != null && t.GradeLift > game + 1e-4f) return false;
            if (t.WrapMaterial != null)   // wrap sky: the tint carries the dim
            {
                if (t.layer.tint.r > spec.Find(t.layer.name).tint.r + 1e-4f) return false;
            }
        }
        return true;
    }

    static void NoUiObjects()
    {
        var all = List(WorldManager.Worlds.Length - 1);
        var go = new GameObject("~mbdTest3");
        var mb = go.AddComponent<MenuBackdrop>();
        mb.Build(all[0]);
        int ui = go.GetComponentsInChildren<Graphic>(true).Length + go.GetComponentsInChildren<Canvas>(true).Length
               + go.GetComponentsInChildren<Shadow>(true).Length + go.GetComponentsInChildren<LineRenderer>(true).Length
               + go.GetComponentsInChildren<MeshRenderer>(true).Length;
        bool onlySprites = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) onlySprites &= r is SpriteRenderer;
        Check("the backdrop adds only tile sprites: no UI graphics, canvases, outlines, lines or meshes", ui == 0 && onlySprites);
        bool neverOverUi = true;
        foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true)) neverOverUi &= r.sortingOrder < 0;
        Check("every backdrop sprite sorts behind the menu and the title traffic", neverOverUi);
        Object.DestroyImmediate(go);
    }

    static void Allocation()
    {
        var all = List(WorldManager.Worlds.Length - 1);
        var go = new GameObject("~mbdTest4");
        var mb = go.AddComponent<MenuBackdrop>();
        mb.Build(all[all.Count - 1]);
        for (int i = 0; i < 60; i++) mb.Step(1f / 60f);
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long used = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 600; i++) mb.Step(1f / 60f); });
        Check("scrolling allocates nothing per frame: " + used + " bytes over 600 frames (meter control " + control + ")",
              !meter || used == 0);
        Object.DestroyImmediate(go);
    }
}
