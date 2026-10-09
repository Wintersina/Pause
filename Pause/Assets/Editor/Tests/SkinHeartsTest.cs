using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Hull colours add hearts (SkinHearts): per ship, the same tier as the weapon
// (its non-stock colours owned: +1 at 1, +2 at 3), and +2 on every ship once
// every skin of every ship is owned. Covers the table for every ship, the
// all-skins bonus (and one skin missing: none), that it is derived from the
// owned-skin keys alone, full hearts at a run's start, heals against the new
// maximum, the tutorial's bare hull, the heart orbit at the most hearts on
// four screens (round the 1.35x hull, inside the rails and the safe area,
// hearts apart), no per-frame garbage in the orbit, the dock's heart numbers
// and the purchase / all-skins texts.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod SkinHeartsTest.Run
public static class SkinHeartsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SH] PASS  " : "[SH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ClearSkins();
            TuningBlock();
            TablePerShip();
            AllSkinsBonus();
            DerivedFromOwnedSkins();
            FullHeartsAtStartAndHeals();
            TutorialFliesTheBareHull();
            RingFitsOnEveryScreen();
            NoGarbagePerFrame();
            DockShowsHearts();
            DockPurchases();
            PurchaseTexts();
        }
        finally
        {
            ShipUiSlots.ScreenOverride = null;
            ClearSkins();
            collisionDetection.MAXLIFE = 3;
            collisionDetection.lifeCounter = 0;
        }
        Debug.Log("[SH] failures: " + fails);
        return fails;
    }

    // ---- helpers -------------------------------------------------------

    static void ClearSkins()
    {
        foreach (int id in ShipId.All)
            for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
    }

    static void Own(int id, int count)
    {
        for (int n = 1; n < ShipSkins.CountFor(id); n++)
        {
            if (n <= count) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
            else PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
    }

    static void OwnEverything()
    {
        foreach (int id in ShipId.All) Own(id, ShipSkins.PerShip - 1);
    }

    // ---- cases ---------------------------------------------------------

    static void TuningBlock()
    {
        var t = SkinHearts.ByColoursOwned;
        Check("one entry per colour count 0.." + (ShipSkins.PerShip - 1), t.Length == ShipSkins.PerShip);
        bool up = t[0] == 0;
        for (int i = 1; i < t.Length; i++) up &= t[i] >= t[i - 1] && t[i] - t[i - 1] <= 1;
        Check("the colour curve starts at 0 and never goes down (" + string.Join(",", t) + ")", up);
        Check("the colours add at most +2 (" + SkinHearts.MostFromColours + ")", SkinHearts.MostFromColours == 2);
        Check("the all-skins bonus is +2", SkinHearts.AllSkinsBonus == 2);
        Check("the most hearts any ship can fly with: 5 + 2 + 2 = 9 (" + ShipLives.Most + ")", ShipLives.Most == 9);
    }

    static void TablePerShip()
    {
        ClearSkins();
        var table = new List<string>();
        bool all = true, monotonic = true, capped = true;
        foreach (int id in ShipId.All)
        {
            int last = -1;
            string row = ShipId.KeyOf(id) + ":";
            for (int n = 0; n < ShipSkins.CountFor(id); n++)
            {
                Own(id, n);
                int max = ShipLives.Max(id);
                int want = ShipLives.Base(id) + SkinHearts.ByColoursOwned[n];
                row += " " + max;
                if (max != want) all = false;
                if (max < last) monotonic = false;
                if (max > ShipLives.Base(id) + SkinHearts.MostFromColours) capped = false;
                if (SkinHearts.ColoursOwned(id) != ShipWeaponUpgrades.Level(id)) all = false;
                last = max;
            }
            table.Add(row);
            Own(id, 0);
        }
        Debug.Log("[SH] hearts by colours owned (0..4): " + string.Join(" | ", table));
        Check("every ship: hull + the colour step for 0..4 colours owned", all);
        Check("hearts never go down as colours are bought", monotonic);
        Check("the colours alone never add more than +2", capped);

        // The five rows' numbers.
        int gw = ShipId.FromKey("GoldWarden"), viper = ShipId.FromKey("VoltViper"), turtle = ShipId.FromKey("Turtle");
        int[] Row(int id) { var r = new int[5]; for (int n = 0; n < 5; n++) { Own(id, n); r[n] = ShipLives.Max(id); } Own(id, 0); return r; }
        Check("Neon Comet 1,2,2,3,3", string.Join(",", Row(ShipId.Starter)) == "1,2,2,3,3");
        Check("Volt Viper 1,2,2,3,3", string.Join(",", Row(viper)) == "1,2,2,3,3");
        Check("Turtle 4,5,5,6,6", string.Join(",", Row(turtle)) == "4,5,5,6,6");
        Check("Gold Warden 5,6,6,7,7", string.Join(",", Row(gw)) == "5,6,6,7,7");

        // Per ship: another ship's colours don't count; which is equipped doesn't matter.
        Own(viper, 4);
        Check("Volt Viper's colours don't touch the starter (1) or Gold Warden (5)",
              ShipLives.Max(ShipId.Starter) == 1 && ShipLives.Max(gw) == 5);
        ShipSkins.Equip(viper, ShipSkins.Stock);
        Check("flown in its stock colour it still has its 3", ShipLives.Max(viper) == 3);
        Own(viper, 0);
    }

    static void AllSkinsBonus()
    {
        ClearSkins();
        OwnEverything();
        Check("every skin owned: the set is complete", SkinHearts.AllSkinsOwned && SkinHearts.SkinsMissing == 0);
        bool every = true;
        foreach (int id in ShipId.All)
            every &= ShipLives.Max(id) == ShipLives.Base(id) + SkinHearts.MostFromColours + 2;
        Check("every ship flies with hull + 2 + 2 (starter 5, Gold Warden 9)", every &&
              ShipLives.Max(ShipId.Starter) == 5 && ShipLives.Max(ShipId.FromKey("GoldWarden")) == 9);

        // Any one skin missing: no set bonus anywhere.
        bool none = true;
        foreach (int id in ShipId.All)
            for (int n = 1; n < ShipSkins.CountFor(id); n++)
            {
                PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
                if (SkinHearts.AllSkinsOwned || SkinHearts.SetBonus != 0 || SkinHearts.SkinsMissing != 1) none = false;
                int other = id == ShipId.Starter ? 2 : ShipId.Starter;
                if (ShipLives.Max(other) != ShipLives.Base(other) + SkinHearts.MostFromColours) none = false;
                PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
            }
        Check("any one of the 60 skins missing: no +2 (on any ship)", none);

        // The last one bought completes it: GainIfBought says so.
        int gw = ShipId.FromKey("GoldWarden");
        PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(gw, ShipSkins.Special));
        bool set;
        int gain = SkinHearts.GainIfBought(gw, ShipSkins.Special, out set);
        // (the last skin is always its ship's 4th colour, which adds no colour
        // heart of its own: 3 -> 4 colours is +2 either way)
        Check("the last skin's gain: the set's +2 (" + gain + ")", set && gain == 2);
        Check("  Gold Warden reads 7 before it (" + ShipLives.Max(gw) + ")", ShipLives.Max(gw) == 7);
        PlayerPrefs.SetString(ShipId.OwnedKey(gw), "True");
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 5000f);
        var r = ShipSkins.TryPurchase(gw, ShipSkins.Special);
        Check("buying it (" + r + ") makes 9", r == ShipSkins.PurchaseResult.Bought && ShipLives.Max(gw) == 9);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(gw, ShipSkins.Special), 1);
        Check("  and 9 with it owned", ShipLives.Max(gw) == 9);

        // Developer mode owns them all; off again: back to the saved ones.
        ClearSkins();
        DeveloperUnlocks.SetEnabled(true);
        Check("developer mode: the full bonus (Gold Warden 9, starter 5)",
              ShipLives.Max(gw) == 9 && ShipLives.Max(ShipId.Starter) == 5);
        DeveloperUnlocks.SetEnabled(false);
        Check("developer mode off: the bare hulls again", ShipLives.Max(gw) == 5 && ShipLives.Max(ShipId.Starter) == 1);
        ShipSkins.Equip(gw, ShipSkins.Stock);
    }

    static void DerivedFromOwnedSkins()
    {
        ClearSkins();
        int viper = ShipId.FromKey("VoltViper");
        // Written raw, as a cloud restore (ProgressSnapshot) or a restored
        // purchase does: the bonus follows with nothing else saved.
        Own(viper, 3);
        Check("owned-skin keys alone give the bonus (Volt Viper 3 colours: 3)", ShipLives.Max(viper) == 3);
        OwnEverything();
        Check("...and the set's (Volt Viper 5)", ShipLives.Max(viper) == 5);
        ClearSkins();
        Check("removing the keys takes it all away (nothing else remembers it)", ShipLives.Max(viper) == 1);
        Check("no save key of its own", !PlayerPrefs.HasKey("skinHearts") && !PlayerPrefs.HasKey("allSkinsBonus"));
    }

    static void FullHeartsAtStartAndHeals()
    {
        ClearSkins();
        OwnEverything();
        int gw = ShipId.FromKey("GoldWarden");
        PlayerPrefs.SetInt("spawnShip", gw);
        PlayerPrefs.SetString(ShipId.OwnedKey(gw), "True");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var r = new ShipLivesTest.Rig(gw);
        Check("a run starts with full hearts at the new max (" + ShipLives.Left + "/" + collisionDetection.MAXLIFE + ")",
              collisionDetection.MAXLIFE == 9 && ShipLives.Left == 9 && ShipLives.FullHealth);
        r.Heal();
        Check("a heal at full health stays at 9", ShipLives.Left == 9);
        for (int i = 0; i < 8; i++) r.Hit();
        Check("8 hits leave the last heart, still flying", ShipLives.Left == 1 && !buttonClicks.playerDied);
        Check("  critical damage art on the last heart", ShipLives.RunDamageState == 2);
        for (int i = 0; i < 12; i++) r.Heal();
        Check("heals stop at the new max (" + ShipLives.Left + "/9)", ShipLives.Left == 9 && collisionDetection.lifeCounter == 0);
        r.Hit();
        Check("  one hit on 9 reads damaged, not critical", ShipLives.RunDamageState == 1);
        r.Dispose();
        ClearSkins();
        PlayerPrefs.DeleteKey("spawnShip");
    }

    static void TutorialFliesTheBareHull()
    {
        ClearSkins();
        Own(ShipId.Starter, 4);
        PlayerPrefs.SetInt("spawnShip", ShipId.Starter);
        Check("the starter with every colour: 3 hearts in a run", ShipLives.Max(ShipId.Starter) == 3);
        EditorSceneManager.OpenScene("Assets/Scenes/" + score.TutorialScene + ".unity", OpenSceneMode.Single);
        var r = new ShipLivesTest.Rig(ShipId.Starter);
        Check("the tutorial flies its bare hull: 1 (" + collisionDetection.MAXLIFE + ")",
              r.ship.scene.name == score.TutorialScene && collisionDetection.MAXLIFE == ShipLives.Base(ShipId.Starter) &&
              ShipLives.TutorialMax(ShipId.Starter) == 1);
        r.Dispose();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ClearSkins();
        PlayerPrefs.DeleteKey("spawnShip");
    }

    // ---- the heart orbit at the most hearts ----

    public static readonly (string name, int w, int h)[] Screens =
    {
        ("9:16 1080x1920", 1080, 1920),
        ("9:19.5 1080x2340", 1080, 2340),
        ("9:21 1080x2520", 1080, 2520),
        ("3:4 1536x2048", 1536, 2048),
    };

    // A flown hull as gameS1 draws it (normalised, then ShipScale.Main) with
    // `count` hearts.
    static ShipLivesIndicator Hull(int id, int count, out GameObject ship)
    {
        collisionDetection.MAXLIFE = count;
        collisionDetection.lifeCounter = 0;
        PlayerPrefs.SetInt("spawnShip", id);
        ship = new GameObject(ShipId.ObjectName(id), typeof(SpriteRenderer));
        var sr = ship.GetComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(id);
        sr.sortingOrder = 10;
        float scale = shopingShips.NormalizedHullScale(sr.sprite) * ShipScale.Main;
        ship.transform.localScale = new Vector3(scale, scale, 1f);
        var hearts = ship.AddComponent<ShipLivesIndicator>();
        hearts.BuildHearts(count);
        return hearts;
    }

    static void RingFitsOnEveryScreen()
    {
        const float Dt = 1f / 60f;
        int most = ShipLives.Most;
        foreach (var (name, w, h) in Screens)
        {
            float halfW = CameraFit.GameplayHalfWidth;
            float size = halfW * h / w;
            var safe = Rect.MinMaxRect(-halfW, -size, halfW, size);
            ShipUiSlots.ScreenOverride = () => safe;
            float rail = BossRails.ReinforcedInnerEdge + RailInset.ShiftFor(w, h);
            string worst = null;
            float closest = float.MaxValue, overlapShare = 0f, ringClear = float.MaxValue;
            int samples = 0, overlapped = 0;
            bool insideRails = true, insideSafe = true, built = true;
            foreach (int id in ShipId.All)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject ship;
                var hearts = Hull(id, most, out ship);
                if (hearts.Hearts == null || hearts.Hearts.Length != most) { built = false; Object.DestroyImmediate(ship); continue; }
                // the middle of the lane, a little above the floor
                ship.transform.position = new Vector3(0f, -size * .45f, 0f);
                hearts.Measure();
                var hull = ShipUiSlots.HullBounds(ship.transform, id);
                // the ring goes round the hull, clear of it by half a heart
                Vector2 r = hearts.OrbitRadii;
                float clear = Mathf.Min(r.x - hull.extents.x, r.y - hull.extents.y) - hearts.heartSize * .5f;
                ringClear = Mathf.Min(ringClear, clear);
                for (int f = 0; f < 12 * 60; f++)
                {
                    if (ShipUiSlots.Spins(id)) ship.transform.Rotate(0f, 0f, 6f);
                    hearts.Place(Dt, Dt);
                    var hs = hearts.Hearts;
                    bool any = false;
                    for (int i = 0; i < hs.Length; i++)
                    {
                        var b = hs[i].GetComponent<SpriteRenderer>().bounds;
                        if (b.min.x < -rail || b.max.x > rail) { insideRails = false; worst = worst ?? ShipId.KeyOf(id) + " over a rail"; }
                        if (!ShipUiSlots.Inside(safe, b)) { insideSafe = false; worst = worst ?? ShipId.KeyOf(id) + " off screen"; }
                        for (int j = i + 1; j < hs.Length; j++)
                        {
                            float d = ((Vector2)(hs[i].position - hs[j].position)).magnitude;
                            closest = Mathf.Min(closest, d);
                            if (d < hearts.heartSize * .5f) any = true;
                        }
                    }
                    samples++;
                    if (any) overlapped++;
                }
                Object.DestroyImmediate(ship);
            }
            overlapShare = samples > 0 ? (float)overlapped / samples : 1f;
            Check(name + ": every ship builds " + most + " hearts", built);
            Check(name + ": the " + most + "-heart ring clears the 1.35x hull (by " + ringClear.ToString("F2") + " u at the tightest)", ringClear > 0f);
            Check(name + ": the ring stays between the rails (inner edge " + rail.ToString("F2") + ")" + (worst != null ? " -- " + worst : ""), insideRails);
            Check(name + ": and on screen", insideSafe);
            Check(name + ": hearts only pass each other (two half overlap " + (overlapShare * 100f).ToString("F0") + "% of the time)",
                  overlapShare < .2f);
        }
        ShipUiSlots.ScreenOverride = null;
        PlayerPrefs.DeleteKey("spawnShip");
    }

    static void NoGarbagePerFrame()
    {
        ShipUiSlots.ScreenOverride = () => Rect.MinMaxRect(-3.72f, -8f, 3.72f, 8f);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject ship;
        var hearts = Hull(ShipId.FromKey("GoldWarden"), ShipLives.Most, out ship);
        for (int f = 0; f < 120; f++) { hearts.Place(1f / 60f, 1f / 60f); hearts.StepBreaks(1f / 60f); }
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 600; f++) { hearts.Place(1f / 60f, 1f / 60f); hearts.StepBreaks(1f / 60f); }
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("the 9-heart orbit allocates nothing per frame (" + bytes + " bytes over 600 frames)", bytes == 0);
        Object.DestroyImmediate(ship);
        ShipUiSlots.ScreenOverride = null;
        PlayerPrefs.DeleteKey("spawnShip");
    }

    // ---- the dock ----

    static void DockShowsHearts()
    {
        ClearSkins();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var anchor = new GameObject("~Anchor").transform;
        var popup = DockPopup.Create(null, font, null);
        int viper = ShipId.FromKey("VoltViper");
        PlayerPrefs.SetString(ShipId.OwnedKey(viper), "True");

        popup.Show(viper, anchor, .3f, true, false, 0f, 5000f);
        popup.ShowSkins(viper, ShipSkins.Stock, 5000f);
        Check("dock: Volt Viper stock shows 1 heart (" + popup.LivesBadgeText + ", " + popup.HeartsLineText + ")",
              popup.LivesShown == 1 && popup.LivesGainShown == 0 && popup.HeartsLineText == "HEARTS 1");

        popup.ShowSkins(viper, 1, 5000f);   // previewing an unbought colour that adds a heart
        Check("dock: previewing its first colour: badge 1+1 (" + popup.LivesBadgeText + ")",
              popup.LivesShown == 1 && popup.LivesGainShown == 1 && popup.LivesBadgeText.Contains("+1"));
        Check("dock: the hearts line says what buying adds (" + popup.HeartsLineText + ")", popup.HeartsLineText == "BUY: +1 HEART");
        Check("dock: the start speed line is unchanged (" + popup.StartSpeedText + ")", popup.StartSpeedText.StartsWith("START SPD "));
        Check("dock: the colour's name still stops before the heart badge",
              popup.SkinNameRight <= popup.LivesLeft + .5f);

        Own(viper, 1);
        popup.ShowSkins(viper, 1, 5000f);
        Check("dock: owned, it reads 2 = 1 +1 (" + popup.HeartsLineText + ")",
              popup.LivesShown == 2 && popup.LivesGainShown == 0 && popup.HeartsLineText == "HEARTS 2: 1 +1 COLOURS");
        popup.ShowSkins(viper, 2, 5000f);
        Check("dock: a colour that adds no heart shows none (" + popup.HeartsLineText + ")",
              popup.LivesGainShown == 0 && popup.HeartsLineText == "HEARTS 2: 1 +1 COLOURS");

        OwnEverything();
        popup.ShowSkins(viper, 1, 5000f);
        Check("dock: every skin: 5 = 1 +2 COLOURS +2 SET (" + popup.HeartsLineText + ")",
              popup.LivesShown == 5 && popup.HeartsLineText == "HEARTS 5: 1 +2 COLOURS +2 SET");

        // The last skin missing, previewed: the set's +2 too.
        PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(viper, 4));
        popup.ShowSkins(viper, 4, 5000f);
        Check("dock: previewing the very last skin: +0 colour +2 set (" + popup.LivesBadgeText + ", " + popup.HeartsLineText + ")",
              popup.LivesGainShown == 2 && popup.HeartsLineText == "BUY: +2 HEARTS ALL SKINS");
        Own(viper, 1);
        for (int n = 2; n < 5; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(viper, n));
        // viper has 1, everything else owned: 3 missing -- not the last
        popup.ShowSkins(viper, 2, 5000f);
        Check("dock: not the last skin: no set bonus promised", popup.HeartsLineText == "" || !popup.HeartsLineText.Contains("ALL SKINS"));

        Object.DestroyImmediate(popup.gameObject);
        Object.DestroyImmediate(anchor.gameObject);
        ClearSkins();
    }

    // The real dock: BUY on a previewed colour says what it added; the
    // purchase that completes the set says +2 and raises the toast.
    static void DockPurchases()
    {
        ClearSkins();
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        int fang = 3;
        PlayerPrefs.SetString(ShipId.OwnedKey(fang), "True");
        PlayerPrefs.SetInt("spawnShip", fang);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        Check("dock built", dock != null);
        if (dock == null) return;
        var cam = Camera.main;
        cam.aspect = 1080f / 1920f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, 1080, 1920);
        dock.Relayout();
        var popup = dock.popup;

        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 5000f);
        dock.Select(fang);
        popup.TapSwatch(1);
        popup.Press();
        Check("dock: buying Solar Fang's first colour: " + dock.LastPurchaseMessage + ", hearts " + popup.LivesShown,
              dock.LastPurchaseMessage == "ACQUIRED  +1 HEART" && popup.LivesShown == 3 && ShipSkins.IsOwned(fang, 1));
        popup.TapSwatch(2);
        popup.Press();
        Check("dock: its second adds no heart: " + dock.LastPurchaseMessage, dock.LastPurchaseMessage == "ACQUIRED" && popup.LivesShown == 3);
        popup.TapSwatch(3);
        popup.Press();
        Check("dock: its third: " + dock.LastPurchaseMessage + ", hearts " + popup.LivesShown,
              dock.LastPurchaseMessage == "ACQUIRED  +1 HEART" && popup.LivesShown == 4);

        // Everything else owned: Solar Fang's special is the last skin.
        foreach (int id in ShipId.All) if (id != fang) Own(id, ShipSkins.PerShip - 1);
        var old = CodexToast.Current;
        if (old != null) Object.DestroyImmediate(old.gameObject);
        popup.TapSwatch(4);
        Check("dock: previewing the last skin promises the set's +2 (" + popup.HeartsLineText + ")",
              popup.HeartsLineText == "BUY: +2 HEARTS ALL SKINS" && popup.LivesGainShown == 2);
        popup.Press();
        var toast = CodexToast.Current;
        Check("dock: the last skin: " + dock.LastPurchaseMessage + ", hearts " + popup.LivesShown,
              dock.LastPurchaseMessage == "ACQUIRED  +2 HEARTS" && popup.LivesShown == 6 && SkinHearts.AllSkinsOwned);
        Check("dock: and the one-time ALL SKINS: +2 HEARTS toast",
              toast != null && toast.Showing && toast.ShowingName == SkinHearts.AllSkinsTitle);
        if (toast != null) Object.DestroyImmediate(toast.gameObject);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ClearSkins();
        PlayerPrefs.DeleteKey("spawnShip");
    }

    static void PurchaseTexts()
    {
        Check("a purchase that adds no heart: ACQUIRED", SkinHearts.PurchaseMessage(0) == "ACQUIRED");
        Check("one that adds a heart: ACQUIRED  +1 HEART", SkinHearts.PurchaseMessage(1) == "ACQUIRED  +1 HEART");
        Check("the set's announcement: ALL SKINS: +2 HEARTS", SkinHearts.AllSkinsTitle == "ALL SKINS: +2 HEARTS");

        // The toast shows it, in any scene, with its own heading.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CodexToast.Announce(SkinHearts.AllSkinsHeading, SkinHearts.AllSkinsTitle, Resources.Load<Sprite>("Vfx/lifeHeart"));
        var toast = CodexToast.Current;
        Check("the all-skins toast shows (" + (toast != null ? toast.ShowingHeading + " / " + toast.ShowingName : "none") + ")",
              toast != null && toast.Showing && toast.ShowingName == "ALL SKINS: +2 HEARTS" &&
              toast.ShowingHeading == SkinHearts.AllSkinsHeading);
        if (toast != null) Object.DestroyImmediate(toast.gameObject);

        // The codex says how it works.
        string lore = CodexCatalogue.LivesLore(ShipId.FromKey("GoldWarden"));
        Check("the codex lore tells the hearts story (" + lore.Replace("\n", " / ") + ")",
              lore.Contains("HULL  5 HEARTS") && lore.Contains("9 with every skin") && lore.Contains("ALL SKINS  +2"));
    }
}
