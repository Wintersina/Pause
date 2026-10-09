using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Per-ship lives (ShipLives): the hull's own -- its row in the dock grid,
// 1 (top row, with the starter) to 5 -- plus what its colours add
// (SkinHearts: +1 with the first, +2 with the third; SkinHeartsTest covers
// the whole table and the all-skins bonus).
// Covers the table, the first colour's heart (bought or developer mode),
// a run's hits and heals against the ship's own maximum, the damage state,
// the dock-to-run carry-over and the dock popup's heart count.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipLivesTest.Run
public static class ShipLivesTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LV] PASS  " : "[LV] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo Trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // The editor shares PlayerPrefs with the Mac player: start the
        // starter without any colour bought (the sandbox puts them back).
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(ShipId.Starter, n));

        TiersByPrice();
        FirstColourAddsAHeart();
        HitsAndHealsAgainstTheShipsOwnMax();
        DamageStates();
        CarryOverResetsToFull();
        DockPopupShowsHearts();

        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        Debug.Log("[LV] failures: " + fails);
        return fails;
    }

    static void TiersByPrice()
    {
        var expected = new System.Collections.Generic.Dictionary<string, int>
        {
            { "NeonComet", 1 }, { "VoltViper", 1 }, { "Lightning", 1 },
            { "Ligher", 2 }, { "Paranoid", 2 }, { "SolarFang", 2 },
            { "Ninja", 3 }, { "Saboteur", 3 }, { "UFO", 3 },
            { "CrimsonHalo", 4 }, { "Dove", 4 }, { "Turtle", 4 },
            { "IonLancer", 5 }, { "JadePhantom", 5 }, { "GoldWarden", 5 },
        };
        foreach (int id in ShipId.All)
        {
            string key = ShipId.KeyOf(id);
            int want;
            Check(key + " is in the lives table", expected.TryGetValue(key, out want));
            Check(key + " (" + shopingShips.CostFor(id) + " dust) flies with " + want + " hearts (" + ShipLives.Max(id) + ")",
                  ShipLives.Max(id) == want && ShipLives.Base(id) == want);
            Check(key + ": within 1..5", ShipLives.Max(id) >= ShipLives.Fewest && ShipLives.Max(id) <= ShipLives.MostBase);
        }
        // A pricier ship never has fewer hearts than a cheaper one.
        bool monotonic = true;
        foreach (int a in ShipId.All)
            foreach (int b in ShipId.All)
                if (shopingShips.CostFor(a) < shopingShips.CostFor(b) && ShipLives.Base(a) > ShipLives.Base(b)) monotonic = false;
        Check("hearts never go down as the price goes up", monotonic);
    }

    // (Was the starter's own "3 once it wears a colour" rule; SkinHearts made
    // it every ship's, so Volt Viper's colour now lifts Volt Viper too, and
    // developer mode -- every skin owned -- flies the full bonus.)
    static void FirstColourAddsAHeart()
    {
        int comet = ShipId.Starter;
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(comet, n));
        Check("the stock starter has 1 heart", ShipLives.Max(comet) == 1);

        for (int n = 1; n < ShipSkins.PerShip; n++)
        {
            PlayerPrefs.SetInt(ShipSkins.OwnedKey(comet, n), 1);
            Check("owning " + ShipSkins.Get(comet, n).name + " gives it 2", ShipLives.Max(comet) == 2);
            // whichever skin is on: the stock one still counts
            ShipSkins.Equip(comet, ShipSkins.Stock);
            Check("  even flown in its stock colour", ShipLives.Max(comet) == 2);
            PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(comet, n));
        }
        Check("a colour for another ship doesn't count", ShipLives.Max(comet) == 1);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(2, 1), 1);
        Check("  (Volt Viper's Night: the starter still 1; Volt Viper itself 1 -> 2)", ShipLives.Max(comet) == 1 && ShipLives.Max(2) == 2);
        PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(2, 1));

        // Buying one through the shop rules.
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 5000f);
        var result = ShipSkins.TryPurchase(comet, 1);
        Check("buying a Neon Comet colour takes it from 1 to 2 hearts", result == ShipSkins.PurchaseResult.Bought && ShipLives.Max(comet) == 2);
        PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(comet, 1));
        ShipSkins.Equip(comet, ShipSkins.Stock);

        // Developer mode owns every colour of every ship: hull +2 +2 (the
        // starter 5, Gold Warden 9) -- and back to 1 when switched off.
        DeveloperUnlocks.SetEnabled(true);
        Check("developer mode: the starter has 5 (every colour, every skin)", ShipLives.Max(comet) == 5);
        Check("developer mode: Gold Warden 9, Volt Viper 5", ShipLives.Max(7) == 9 && ShipLives.Max(2) == 5);
        DeveloperUnlocks.SetEnabled(false);
        Check("developer mode off: back to 1", ShipLives.Max(comet) == 1);
    }

    // ---------------------------------------------------------------- run

    internal sealed class Rig
    {
        public GameObject ship;
        public collisionDetection cd;

        public Rig(int id)
        {
            ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
            ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            ship.AddComponent<BoxCollider2D>().isTrigger = true;
            cd = ship.AddComponent<collisionDetection>();
            cd.explosionAnimation = new GameObject("~TestExplosion");
            // The scene objects collisionDetection.Start looks up.
            new GameObject("hypeText", typeof(RectTransform)).AddComponent<Text>();
            new GameObject("boostText", typeof(RectTransform)).AddComponent<Text>();
            new GameObject("RocketsSound", typeof(AudioSource));
            new GameObject("AstroidExplotionSound", typeof(AudioSource));
            new GameObject("~boost").tag = "boost";
            collisionDetection.MAXLIFE = -1;
            collisionDetection.lifeCounter = 3;
            typeof(collisionDetection).GetMethod("Start", Inst).Invoke(cd, null);
            collisionDetection.atomCheck = false;
            collisionDetection.cloakTimer = 0f;
            buttonClicks.playerDied = false;
        }

        public void Hit()
        {
            PlayerInvuln.Reset();   // each hit lands after the last one's post-hit window
            var go = new GameObject("rock", typeof(CircleCollider2D));
            go.tag = "Astr";
            Trigger.Invoke(cd, new object[] { go.GetComponent<Collider2D>() });
            if (go != null) Object.DestroyImmediate(go);
        }

        public void Heal()
        {
            var go = new GameObject(HealAtom.ObjectName + "(Clone)", typeof(CircleCollider2D));
            go.tag = "pickUp";
            Trigger.Invoke(cd, new object[] { go.GetComponent<Collider2D>() });
            if (go != null) Object.DestroyImmediate(go);
        }

        public void Dispose()
        {
            if (cd.explosionAnimation != null) Object.DestroyImmediate(cd.explosionAnimation);
            foreach (var name in new[] { "~fx", "~TestExplosion(Clone)", "~PickupBurst", "~boost", "hypeText", "boostText",
                                         "RocketsSound", "AstroidExplotionSound" })
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                    Object.DestroyImmediate(go);
            if (ship != null) Object.DestroyImmediate(ship);
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            buttonClicks.playerDied = false;
        }
    }

    static void HitsAndHealsAgainstTheShipsOwnMax()
    {
        foreach (int id in new[] { ShipId.Starter, 3, 15, 7 })
        {
            string name = ShipId.KeyOf(id);
            var r = new Rig(id);
            int max = ShipLives.Max(id);
            Check(name + ": collisionDetection.Start gives the run its own " + max + " lives, all left",
                  collisionDetection.MAXLIFE == max && ShipLives.RunMax == max && ShipLives.Left == max && ShipLives.FullHealth);

            // Heal at full health: nothing to repair, never past the max.
            r.Heal();
            Check(name + ": a green atom at full health stays at " + max, ShipLives.Left == max && collisionDetection.lifeCounter == 0);

            // Down to the last heart -- still flying.
            for (int i = 0; i < max - 1; i++) r.Hit();
            Check(name + ": " + (max - 1) + " hits leave the last heart, still flying",
                  ShipLives.Left == 1 && !buttonClicks.playerDied);

            // A heal gives one back, a second (if hurt) another; never past max.
            // (A one-heart ship has none to repair: it stays at its single heart.)
            r.Heal();
            Check(name + ": a green atom gives one back (" + ShipLives.Left + ")", ShipLives.Left == (max > 1 ? 2 : 1));
            for (int i = 0; i < max + 2; i++) r.Heal();
            Check(name + ": heals stop at its own max (" + ShipLives.Left + "/" + max + ")",
                  ShipLives.Left == max && collisionDetection.lifeCounter == 0);
            r.Dispose();
        }
    }

    static void DamageStates()
    {
        Check("full health reads intact", ShipLives.DamageState(0, 2) == 0 && ShipLives.DamageState(0, 5) == 0);
        Check("1 heart: the first hit is the last life", ShipLives.DamageState(0, 1) == 0 && ShipLives.DamageState(1, 1) == 2);
        Check("2 hearts: one hit is the last life (critical)", ShipLives.DamageState(1, 2) == 2);
        Check("3 hearts: damaged, then critical", ShipLives.DamageState(1, 3) == 1 && ShipLives.DamageState(2, 3) == 2);
        Check("5 hearts: damaged through 2 left, critical at 1",
              ShipLives.DamageState(1, 5) == 1 && ShipLives.DamageState(3, 5) == 1 && ShipLives.DamageState(4, 5) == 2);
    }

    static void CarryOverResetsToFull()
    {
        foreach (int id in new[] { ShipId.Starter, 7 })
        {
            var r = new Rig(id);
            r.Hit();
            GameStateReset.Clear();
            Check(ShipId.KeyOf(id) + ": leaving a run resets to full (" + ShipLives.Left + "/" + ShipLives.RunMax + ")",
                  collisionDetection.lifeCounter == 0 && ShipLives.Left == ShipLives.RunMax);
            r.Dispose();
        }
        // The next run's ship sets its own maximum: a 5-heart Gold Warden run
        // then a starter run gives the starter 1, not 5.
        new Rig(7).Dispose();
        var starter = new Rig(ShipId.Starter);
        Check("the next ship's own maximum replaces the last one's", collisionDetection.MAXLIFE == 1);
        starter.Dispose();
    }

    static void DockPopupShowsHearts()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var anchor = new GameObject("~Anchor").transform;
        var popup = DockPopup.Create(null, font, null);
        int comet = ShipId.Starter;
        PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(comet, 1));
        popup.Show(comet, anchor, .3f, true, true, 0f, 0f);
        Check("dock popup: Neon Comet shows 1 heart", popup.LivesShown == 1 && popup.LivesBadgeVisible);
        popup.Show(7, anchor, .3f, false, false, 5800f, 0f);
        Check("dock popup: Gold Warden (unbought) shows 5", popup.LivesShown == 5 && popup.LivesBadgeVisible);
        popup.Show(15, anchor, .3f, false, false, 3000f, 0f);
        Check("dock popup: Turtle shows 4", popup.LivesShown == 4);
        popup.Show(2, anchor, .3f, false, false, 600f, 0f);
        Check("dock popup: Volt Viper (row 1) shows 1", popup.LivesShown == 1);

        popup.Show(comet, anchor, .3f, true, true, 0f, 1000f);
        popup.ShowSkins(comet, 1, 1000f);   // previewing an unbought colour: still 1
        Check("dock popup: previewing a colour isn't owning it (1, and +1 promised: " + popup.LivesBadgeText + ")",
              popup.LivesShown == 1 && popup.LivesGainShown == 1);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1000f);
        ShipSkins.TryPurchase(comet, 1);
        popup.ShowSkins(comet, 1, PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey));
        Check("dock popup: buying a Neon Comet colour turns 1 heart into 2", popup.LivesShown == 2);
        PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(comet, 1));
        ShipSkins.Equip(comet, ShipSkins.Stock);

        // The name line stops before the close button; on the line under it
        // the colour's name stops before the hearts.
        Check("dock popup: the heart badge and title exist", popup.transform.Find("Panel/Lives") != null && popup.transform.Find("Panel/Title") != null);
        popup.Show(comet, anchor, .3f, true, true, 0f, 1000f);
        popup.ShowSkins(comet, 1, 1000f);
        Check("dock popup: the title stops before the close button (" + popup.TitleRight.ToString("F0") + " <= " +
              popup.CloseLeft.ToString("F0") + ")", popup.TitleRight <= popup.CloseLeft + .5f);
        Check("dock popup: the colour's name stops before the heart badge (" + popup.SkinNameRight.ToString("F0") + " <= " +
              popup.LivesLeft.ToString("F0") + ")", popup.SkinNameText.Length > 0 && popup.SkinNameRight <= popup.LivesLeft + .5f);
        Object.DestroyImmediate(popup.gameObject);
        Object.DestroyImmediate(anchor.gameObject);
    }
}
