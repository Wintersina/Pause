using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// "The ship picked in the space dock is the ship that flies."
//
// For every roster ship this drives the real dock (shopS6): tap its berth,
// press the popup's LAUNCH (or LIFT-OFF), then opens gameS1 and runs the real
// spawner, and checks the ship that appears is that ship -- by its art, not
// just its index: the hull's texture is the roster ship's own art, the same
// art its dock berth showed, and stays that way once the 2016 prefab's own
// components (lifeControler, the hull Animator) have had their turn. The
// hand-off between the scenes is PlayerPrefs alone ("spawnShip"), so each
// LAUNCH's saved value is replayed into gameS1.
//
// Also: developer mode, partial ownership and the not-owned fallback, the
// tutorial, BACK then PLAY from the start menu, and the per-ship tables.
public static class ShipSelectionTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SS] PASS  " : "[SS] FAIL  ") + what);
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
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        DeveloperUnlocks.SetEnabled(false);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetFloat("PlayerCurrecny", 0f);

        RosterTables();
        EveryShipLaunchesItself("all owned", Own(AllIds()), devMode: false);
        EveryShipLaunchesItself("developer mode", Own(), devMode: true);
        DeveloperModeOffFallsBack();
        PartialOwnership();
        LiftOffUsesTheSelection();
        BackThenPlayKeepsTheSelection();
        Tutorial();

        DeveloperUnlocks.SetEnabled(false);
        Debug.Log("[SS] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------- helpers

    static int[] AllIds()
    {
        var ids = new List<int>(ShipId.All);
        return ids.ToArray();
    }

    // Exactly these ships bought (the starter is owned regardless).
    static int[] Own(params int[] ids)
    {
        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(i));
        foreach (int id in ids) PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
        return ids;
    }

    // Runs one component's own Start/Update (SendMessage would also run
    // every other component's on that GameObject).
    static void Call(Component c, string method)
    {
        var m = c.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) return;
        try { m.Invoke(c, null); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }

    static string Label(int id)
    {
        return "ship" + id + " (" + shopingShips.NameFor(id) + ")";
    }

    // The roster art a sprite was cut from: "NeonComet_intact_idle1" and
    // "Lightning" both name their ship's ShipId.KeyOf.
    static bool IsArtOf(Sprite sprite, int id)
    {
        if (sprite == null || sprite.texture == null) return false;
        string tex = sprite.texture.name, key = ShipId.KeyOf(id);
        return key != null && (tex == key || tex.StartsWith(key + "_"));
    }

    static string ArtName(Sprite sprite)
    {
        return sprite == null ? "null" : sprite.texture == null ? "no texture" : sprite.texture.name;
    }

    static SpaceDock FreshDock()
    {
        if (SpaceDock.Instance != null) Object.DestroyImmediate(SpaceDock.Instance.gameObject);
        foreach (var old in Object.FindObjectsByType<SpaceDock>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        if (dock != null) dock.Relayout();
        return dock;
    }

    // Tap the ship's berth -- found by its slot in the cheapest-first rack,
    // not by its bay object, so the slot -> ShipId mapping is what's tested --
    // and press the popup button (LAUNCH if owned). Returns the art the berth
    // showed and what LAUNCH saved.
    static int TapAndPress(SpaceDock dock, int id, out Sprite dockArt)
    {
        dockArt = dock.bays[id] != null ? dock.bays[id].hull.sprite : null;
        int slot = SpaceDock.SlotOf(id);
        dock.Tap(dock.rack.TransformPoint(dock.layout.BayCenter(slot)));
        tappedRight = dock.Selected == id && dock.popup.ShipIndex == id;
        if (dock.Selected == id && dock.popup.Visible) dock.popup.Press();
        return PlayerPrefs.GetInt(ShipId.SelectedKey, -1);
    }

    // Runs gameS1's spawner (the scene must be open) and the ship's own
    // Start/Update, then returns the spawned ship.
    static GameObject SpawnInGame()
    {
        foreach (var old in Object.FindObjectsByType<movePlayer>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        var spawner = Object.FindFirstObjectByType<spawnShips>();
        if (spawner == null) return null;
        Call(spawner, "Start");
        var player = Object.FindFirstObjectByType<movePlayer>();
        if (player == null) return null;
        var ship = player.gameObject;
        var spawnedHull = ship.GetComponent<SpriteRenderer>();
        spawnedArt = spawnedHull != null ? spawnedHull.sprite : null;
        var life = ship.GetComponent<lifeControler>();
        if (life != null)
        {
            Call(life, "Start");
            Call(life, "Update");
        }
        return ship;
    }

    static bool tappedRight;

    // The hull art right after spawn, before the ship's own Start ran.
    static Sprite spawnedArt;

    static bool AnimatesSprite(Animator animator)
    {
        if (animator == null || !animator.enabled || animator.runtimeAnimatorController == null) return false;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                if (binding.propertyName == "m_Sprite") return true;
        return false;
    }

    // Everything that makes "the ship that flies" ship `id`.
    static void CheckIsShip(string context, GameObject ship, int id, Sprite dockArt)
    {
        string who = context + ": " + Label(id);
        Check(who + " spawned", ship != null);
        if (ship == null) return;
        Check(who + " carries its id (" + ship.name + ")", ShipId.Of(ship) == id);
        var hull = ship.GetComponent<SpriteRenderer>();
        if (dockArt != null)
            Check(who + " spawns with the art its berth showed (" + ArtName(dockArt) + " / " + ArtName(spawnedArt) + ")",
                  IsArtOf(dockArt, id) && spawnedArt != null && dockArt.texture == spawnedArt.texture);
        Check(who + " has no 2016 animator repainting its hull",
              !AnimatesSprite(ship.GetComponent<Animator>()));
        // Let any animator on the hull run, as it would in play mode, where it
        // writes after Update -- over the sprite lifeControler just set.
        var animator = ship.GetComponent<Animator>();
        if (animator != null && animator.enabled && animator.runtimeAnimatorController != null)
        {
            animator.Rebind();
            animator.Update(.3f);
        }
        Sprite art = hull != null ? hull.sprite : null;
        Check(who + " flies its own art (" + ArtName(art) + ")", IsArtOf(art, id));
        Check(who + ": the exhaust resolves the same id", ShipExhaust.IndexFor(ship) == id);
        if (art != null)
        {
            float expected = shopingShips.NormalizedHullScale(shopingShips.SpriteFor(id));
            Check(who + " is sized from its own hull", Mathf.Abs(ship.transform.localScale.x - expected) < .001f);
        }
    }

    // --------------------------------------------------------------- tests

    static void RosterTables()
    {
        Check("roster has 15 ships", ShipId.Count == 15 && ShipId.Last == 15);
        Check("roster, prices and ids agree",
              shopingShips.Roster.Length == shopingShips.shipTotal &&
              shopingShips.Prices.Length == shopingShips.shipTotal);
        Check("the starter is ship 1", ShipId.Starter == 1 && ShipId.IsOwned(1));
        Check("the save keys are the released ones",
              ShipId.SelectedKey == "spawnShip" && ShipId.OwnedKey(7) == "boughtship7");

        var keys = new HashSet<string>();
        foreach (int id in ShipId.All)
        {
            string who = Label(id);
            string key = ShipId.KeyOf(id);
            Check(who + " has a unique art key (" + key + ")", key != null && keys.Add(key));
            Check(who + " key round-trips", ShipId.FromKey(key) == id);
            Check(who + " name matches the roster", ShipId.NameOf(id) == shopingShips.Roster[id]);
            Check(who + " key is its roster name without spaces",
                  key == shopingShips.Roster[id].Replace(" ", ""));
            Check(who + " art is its own (" + ArtName(shopingShips.SpriteFor(id)) + ")",
                  IsArtOf(shopingShips.SpriteFor(id), id));
            Check(who + " idle art is its own", IsArtOf(shopingShips.IdleSpriteFor(id, 0, 1), id));
            Check(who + " has nozzles", ShipNozzles.Has(id));
            Check(who + " has a loadout entry", ShipLoadoutTable.Has(id) && ShipLoadoutTable.For(id).shipId == id);

            var prefab = Resources.Load<GameObject>(spawnShips.PrefabPathFor(id));
            Check(who + " spawns from a gameplay prefab (" + spawnShips.PrefabPathFor(id) + ")",
                  prefab != null && prefab.GetComponent<movePlayer>() != null &&
                  prefab.name == "ship" + (id <= 7 ? id : 1));
        }
        Check("Turtle's art is not shadowed by the Turtle prefab in Resources",
              Resources.Load<Texture2D>(ShipHullArt.Folder + "Turtle") != null &&
              IsArtOf(shopingShips.SpriteFor(ShipId.FromKey("Turtle")), 15));
        Check("an object's id comes from its name",
              ShipId.Of(new GameObject("ship12(Clone)")) == 12 && ShipId.Of(new GameObject("ship3")) == 3 &&
              ShipId.Of(new GameObject("Shield")) == ShipId.None && ShipId.Of(new GameObject("ship99")) == ShipId.None);
    }

    static void EveryShipLaunchesItself(string context, int[] owned, bool devMode)
    {
        Own(owned);
        DeveloperUnlocks.SetEnabled(devMode);
        PlayerPrefs.SetInt(ShipId.SelectedKey, ShipId.Starter);

        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        var saved = new Dictionary<int, int>();
        var dockArt = new Dictionary<int, Sprite>();
        // Through the rack's own order: cheapest berth first.
        foreach (int id in SpaceDock.BayOrder)
        {
            var dock = FreshDock();
            if (dock == null) { Check(context + ": the dock builds", false); return; }
            Sprite art;
            saved[id] = TapAndPress(dock, id, out art);
            dockArt[id] = art;
            Check(context + ": berth slot " + SpaceDock.SlotOf(id) + " selects " + Label(id), tappedRight);
            Check(context + ": " + Label(id) + " is parked in full colour, not as a silhouette", !dock.bays[id].Silhouetted);
            Check(context + ": LAUNCH on " + Label(id) + " starts the launch", dock.Launching);
            Check(context + ": LAUNCH on " + Label(id) + " saves it (" + saved[id] + ")", saved[id] == id);
        }

        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        foreach (int id in SpaceDock.BayOrder)
        {
            PlayerPrefs.SetInt(ShipId.SelectedKey, saved[id]);
            var ship = SpawnInGame();
            CheckIsShip(context, ship, id, dockArt[id]);
            PerShipComponents(context, ship, id);
        }
        DeveloperUnlocks.SetEnabled(false);
    }

    // The flown ship's own components resolve the same id.
    static void PerShipComponents(string context, GameObject ship, int id)
    {
        if (ship == null) return;
        string who = context + ": " + Label(id);

        var hearts = ship.AddComponent<ShipLivesIndicator>();
        Call(hearts, "Start");
        int count = 0;
        foreach (Transform c in ship.transform) if (c.name.StartsWith("Heart")) count++;
        Check(who + " shows life hearts only if it has no damage art (" + count + ")",
              (count > 0) == (id >= ShipLivesIndicator.FirstShipWithoutDamageArt));

        var power = ship.AddComponent<ShipPowerController>();
        try { Call(power, "Start"); } catch (System.Exception) { }
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object i = typeof(ShipPowerController).GetField("shipIndex", flags).GetValue(power);
        Check(who + " gets its own attack, secret power and weapon id",
              power.Loadout.shipId == id && (int)i == id);

        if (!ShipExhaust.UsesWind(id))
        {
            Transform boost = ship.transform.Find("Boost" + id);
            int nozzles = 0;
            if (boost != null) foreach (Transform c in boost) if (c.name.StartsWith("Nozzle")) nozzles++;
            Check(who + " exhaust has its own nozzle count (" + nozzles + ")",
                  nozzles == ShipNozzles.For(id).Length);
        }
    }

    static void DeveloperModeOffFallsBack()
    {
        Own();
        DeveloperUnlocks.SetEnabled(true);
        PlayerPrefs.SetInt(ShipId.SelectedKey, 9);
        Check("developer mode: an unbought ship is equipped as itself", ShipId.Equipped() == 9);
        DeveloperUnlocks.SetEnabled(false);
        Check("developer mode off: the unbought ship falls back to the starter", ShipId.Equipped() == ShipId.Starter);
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var ship = SpawnInGame();
        CheckIsShip("developer mode off", ship, ShipId.Starter, null);
        PerShipComponents("developer mode off (selection 9 unowned)", ship, ShipId.Starter);
    }

    static void PartialOwnership()
    {
        int[] owned = { 4, 9, 15 };
        Own(owned);
        PlayerPrefs.SetInt(ShipId.SelectedKey, 4);

        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        var saved = new Dictionary<int, int>();
        foreach (int id in ShipId.All)
        {
            PlayerPrefs.SetInt(ShipId.SelectedKey, 4);
            var dock = FreshDock();
            Sprite art;
            saved[id] = TapAndPress(dock, id, out art);
            bool isOwned = id == 1 || System.Array.IndexOf(owned, id) >= 0;
            Check("partial: " + Label(id) + (isOwned ? " shows its art" : " is a black silhouette until bought"),
                  tappedRight && dock.bays[id].Silhouetted == !isOwned);
            if (isOwned)
                Check("partial: LAUNCH on owned " + Label(id) + " saves it", saved[id] == id && dock.Launching);
            else
                Check("partial: unowned " + Label(id) + " offers BUY, keeps the selection",
                      saved[id] == 4 && !dock.Launching && !ShipId.IsOwned(id));
        }

        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        foreach (int id in ShipId.All)
        {
            bool isOwned = id == 1 || System.Array.IndexOf(owned, id) >= 0;
            PlayerPrefs.SetInt(ShipId.SelectedKey, saved[id]);
            CheckIsShip("partial", SpawnInGame(), isOwned ? id : 4, null);
        }

        // A stale save pointing at a ship that isn't owned (or isn't a ship)
        // flies the starter -- and only then.
        foreach (int bad in new[] { 7, 0, -3, 16, 99 })
        {
            PlayerPrefs.SetInt(ShipId.SelectedKey, bad);
            var ship = SpawnInGame();
            CheckIsShip("partial, saved " + bad + " (not an owned ship)", ship, ShipId.Starter, null);
            if (bad == 7) PerShipComponents("partial, saved 7 unowned", ship, ShipId.Starter);
        }
        PlayerPrefs.DeleteKey(ShipId.SelectedKey);
        CheckIsShip("partial, nothing saved", SpawnInGame(), ShipId.Starter, null);

        PlayerPrefs.DeleteKey(ShipId.OwnedKey(1));
        PlayerPrefs.SetInt(ShipId.SelectedKey, 1);
        CheckIsShip("starter without a bought key", SpawnInGame(), 1, null);

        PlayerPrefs.SetString(ShipId.OwnedKey(9), "False");
        PlayerPrefs.SetInt(ShipId.SelectedKey, 9);
        CheckIsShip("bought key \"False\"", SpawnInGame(), ShipId.Starter, null);
    }

    static void LiftOffUsesTheSelection()
    {
        Own(9, 12);
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);

        // Equipped 12, tap 9, press LIFT-OFF (menuButton.play): flies 9.
        PlayerPrefs.SetInt(ShipId.SelectedKey, 12);
        var dock = FreshDock();
        dock.Tap(dock.bays[9].transform.position);
        new GameObject("~menu").AddComponent<menuButton>().play();
        Check("LIFT-OFF after tapping a ship launches that ship",
              dock.Launching && PlayerPrefs.GetInt(ShipId.SelectedKey) == 9);

        // The legacy flyOffChecker hand-off does the same.
        PlayerPrefs.SetInt(ShipId.SelectedKey, 12);
        dock = FreshDock();
        dock.Tap(dock.bays[9].transform.position);
        rotateRight.flyOffChecker = true;
        Call(new GameObject("~rr").AddComponent<rotateRight>(), "Update");
        Check("the flyOffChecker hand-off launches the tapped ship",
              dock.Launching && PlayerPrefs.GetInt(ShipId.SelectedKey) == 9);

        // Tapping an unowned ship, then LIFT-OFF, flies the equipped one.
        PlayerPrefs.SetInt(ShipId.SelectedKey, 12);
        dock = FreshDock();
        dock.Tap(dock.bays[5].transform.position);
        new GameObject("~menu2").AddComponent<menuButton>().play();
        Check("LIFT-OFF with an unowned ship tapped launches the equipped ship",
              dock.Launching && PlayerPrefs.GetInt(ShipId.SelectedKey) == 12);

        // Nothing tapped: the equipped ship.
        PlayerPrefs.SetInt(ShipId.SelectedKey, 9);
        dock = FreshDock();
        new GameObject("~menu3").AddComponent<menuButton>().play();
        Check("LIFT-OFF with nothing tapped launches the equipped ship",
              dock.Launching && PlayerPrefs.GetInt(ShipId.SelectedKey) == 9);
    }

    static void BackThenPlayKeepsTheSelection()
    {
        Own(6, 11);
        PlayerPrefs.SetInt(ShipId.SelectedKey, ShipId.Starter);
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        var dock = FreshDock();
        Sprite art;
        TapAndPress(dock, 6, out art);
        Check("back/play: LAUNCH saved ship 6", PlayerPrefs.GetInt(ShipId.SelectedKey) == 6);

        // Back in the dock (statics re-run, shopingShips.Start runs again),
        // tap another ship but leave with BACK instead of launching.
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        var shop = Object.FindFirstObjectByType<shopingShips>();
        if (shop != null) Call(shop, "Start");
        dock = FreshDock();
        dock.Tap(dock.bays[11].transform.position);
        GameStateReset.Clear();                       // what BACK does
        EditorSceneLoader.Open("startS4", OpenSceneMode.Single);
        Check("back/play: the launched ship stays equipped (" + ShipId.Equipped() + ")", ShipId.Equipped() == 6);

        // PLAY on the start menu goes straight to gameS1.
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        CheckIsShip("back then PLAY", SpawnInGame(), 6, null);
    }

    static void Tutorial()
    {
        PlayerPrefs.SetString("HasDoneTut", "false");
        Check("before the tutorial, launch goes to the tutorial", SpaceDock.Destination == TutorialShipHull.Scene);

        foreach (int id in new[] { 1, 5, 12, 15 })
        {
            Own(id);
            PlayerPrefs.SetInt(ShipId.SelectedKey, id);
            EditorSceneLoader.Open("tutorialS5", OpenSceneMode.Single);
            var ship = TutorialShipHull.Apply();
            if (ship != null)
            {
                var life = ship.GetComponent<lifeControler>();
                if (life != null) { Call(life, "Start"); Call(life, "Update"); }
            }
            CheckIsShip("tutorial", ship, id, null);
        }

        Own();
        PlayerPrefs.SetInt(ShipId.SelectedKey, 8);
        EditorSceneLoader.Open("tutorialS5", OpenSceneMode.Single);
        var fallback = TutorialShipHull.Apply();
        if (fallback != null) Call(fallback.GetComponent<lifeControler>(), "Start");
        CheckIsShip("tutorial, unowned selection", fallback, ShipId.Starter, null);
        PlayerPrefs.SetString("HasDoneTut", "true");
    }
}
