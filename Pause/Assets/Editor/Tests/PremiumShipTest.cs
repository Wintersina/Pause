using UnityEngine;

// Paragon, the premium "cheat" ship (PremiumShips): a real-money ship that
// star dust can never buy, with 50,000-dust skins that make it stronger.
//   1  identity: id 16, roster / key / art / tables all have its row
//   2  the IAP gate: no dust purchase, not owned, "COMING SOON" in the dock;
//      developer mode owns it; a Grant (the future IAP) owns it
//   3  stats by skin tier: hearts, start speed, missiles, shield cadence
//   4  skins: four, 50,000 dust each, bought only once the ship is owned
//   5  the economy around it: set bonus, Fleet Admiral and the price tiers
//      ignore it; standard ships' hearts / rows are unchanged
//   6  gameplay: homing missiles fire on their timer at the nearest hazards,
//      the shield raises on its timer, the barrage and the damage states
public static class PremiumShipTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PREM] PASS  " : "[PREM] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const int P = PremiumShips.Paragon;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
        for (int s = 1; s < ShipSkins.PerShip; s++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(P, s));
        Identity();
        Gate();
        Tiers();
        Skins();
        Economy();
        Combat();
        Debug.Log("[PREM] failures: " + fails);
        return fails;
    }

    static void Identity()
    {
        Check("Paragon is ship 16, the last roster id", ShipId.Last == P && ShipId.Count == 16 && shopingShips.shipTotal == 17);
        Check("key and name", ShipId.KeyOf(P) == "Paragon" && ShipId.NameOf(P) == "Paragon" && ShipId.FromKey("Paragon") == P);
        Check("it is premium and the 15 others are not", PremiumShips.IsPremium(P) && PremiumShips.StandardCount == 15 &&
              System.Linq.Enumerable.Count(ShipId.All, id => PremiumShips.IsPremium(id)) == 1);
        Check("hull sheet, nozzles, damage emitters, exhaust, weapon, loadout and hearts all have rows",
              ShipHullArt.Has(P) && ShipHullArt.StockRest(P) != null && ShipNozzles.Has(P) && ShipNozzles.For(P).Length == 3 &&
              ShipDamageTable.Total(P) > 0 && ShipExhaustStyle.Has(P) && ShipExhaustStyle.HasLayer(P, ExhaustLayer.Plume) &&
              ShipExhaustStyle.HasLayer(P, ExhaustLayer.PlumeBoost) && ShipExhaust.SpriteFor(P) != null &&
              WeaponStyleTable.For(P).artKey == "Paragon" && ShipLoadoutTable.For(P).shipId == P &&
              ShipLoadoutTable.AttackFor(P) == ShipAttack.ParagonBarrage && ShipLoadoutTable.PowerFor(P) == SecretPower.AegisShield &&
              ShipWeaponUpgrades.Has(P));
        Check("three nozzles: a big centre engine and two smaller outboard ones",
              ShipNozzles.For(P)[0].scale > 1f && ShipNozzles.For(P)[1].scale < 1f && ShipNozzles.For(P)[2].scale < 1f &&
              ShipNozzles.For(P)[1].x < ShipNozzles.For(P)[0].x && ShipNozzles.For(P)[2].x > ShipNozzles.For(P)[0].x);
        Check("it has the codex's weapon art cells (weapon atlas, attack fx row 16, meter row 16)",
              WeaponArt.Shot(P, 0) != null && WeaponArt.Charge(P, 15) != null && ShipFxArt.AttackLoop(P, 0) != null &&
              ShipFxArt.PowerBurst(P, 0) != null && ShipFxArt.MeterIcon(P) != null);
        var tex = Resources.Load<Texture2D>("Weapons/AttackFx");
        Check("the attack fx atlas has 16 ship rows", tex != null && tex.height == ShipFxArt.Rows * 128 && ShipFxArt.Rows == 16);
    }

    static void Gate()
    {
        Check("a fresh save does not own it", !ShipId.IsOwned(P) && PremiumShips.ComingSoon(P));
        Check("no dust price: it sorts after every dust ship in the dock",
              SpaceDock.SlotOf(P) == ShipId.Count - 1 && PremiumShips.DustPurchasable(1) && !PremiumShips.DustPurchasable(P));
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1e9f);
        bool bought = shopingShips.TryPurchase(P, 0f) | shopingShips.TryPurchase(P, 1f) | shopingShips.TryPurchase(P, 1e9f);
        Check("star dust can never buy it, whatever the cost or the balance",
              !bought && !ShipId.IsOwned(P) && PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey) == 1e9f);
        ShipId.Equip(P);
        Check("an unowned saved selection flies the starter", ShipId.Equipped() == ShipId.Starter);
        PlayerPrefs.SetInt(ShipId.SelectedKey, 1);
        Check("the placeholder IAP product id exists", !string.IsNullOrEmpty(PremiumShips.ParagonProductId));

        PremiumShips.Grant(1);
        Check("Grant only works on a premium ship", PlayerPrefs.GetString(ShipId.OwnedKey(2)) != "True");
        PremiumShips.Grant(P);
        Check("Grant (the IAP hook) owns it with the usual key", ShipId.IsOwned(P) && PlayerPrefs.GetString(ShipId.OwnedKey(P)) == "True");
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
        Check("owned flag is the standard boughtship16 key (cloud save carries it)", ShipId.OwnedKey(P) == "boughtship16" &&
              ProgressSnapshot.MaxShipIndex >= P);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 0f);
    }

    static void Tiers()
    {
        Check("tier 0 is the brief: speed 20, 10 hearts, missiles every 10 s, shield every 8 s",
              PremiumShips.Tier(P, 0).startSpeedHud == 20 && PremiumShips.Tier(P, 0).hearts == 10 &&
              PremiumShips.Tier(P, 0).missileEvery == 10f && PremiumShips.Tier(P, 0).shieldEvery == 8f);
        bool escalates = PremiumShips.TierCount(P) == ShipSkins.PerShip;
        for (int t = 1; t < PremiumShips.TierCount(P); t++)
        {
            var a = PremiumShips.Tier(P, t - 1); var b = PremiumShips.Tier(P, t);
            escalates &= b.hearts > a.hearts && b.startSpeedHud > a.startSpeedHud && b.missileEvery < a.missileEvery &&
                         b.shieldEvery < a.shieldEvery && b.missiles >= a.missiles && b.shieldSeconds >= a.shieldSeconds;
        }
        Check("every skin tier is stronger than the last, in every number", escalates);
        Check("the top start speed stays under the speed cap", PremiumShips.Tier(P, 4).startSpeedHud < SpeedRamp.CapHud);
        Check("hearts: ShipLives.Max, Base and the orbit's room follow the tiers",
              ShipLives.Base(P) == 10 && ShipLives.Max(P) == 10 && ShipLives.MostHearts >= PremiumShips.Tier(P, 4).hearts &&
              ShipLives.Most == 9 && ShipLives.Rows == 5);
        Check("start speed: ShipStartSpeed reads the tier", ShipStartSpeed.HudFor(P, 0) == 20 && ShipStartSpeed.HudFor(P, 4) == 28);
        Check("damage states: wreck on the last two hearts of a 10+ heart hull, the last one below",
              ShipDamageTable.StateFor(0, 10) == 0 && ShipDamageTable.StateFor(1, 10) == 1 && ShipDamageTable.StateFor(7, 10) == 1 &&
              ShipDamageTable.StateFor(8, 10) == 2 && ShipDamageTable.StateFor(9, 10) == 2 &&
              ShipDamageTable.StateFor(4, 5) == 2 && ShipDamageTable.StateFor(3, 5) == 1 && ShipDamageTable.StateFor(8, 9) == 2 &&
              ShipDamageTable.StateFor(7, 9) == 1);
    }

    static void Skins()
    {
        Check("four skins besides stock, none a dust-economy price", ShipSkins.CountFor(P) == ShipSkins.PerShip);
        bool price = true;
        for (int s = 1; s < ShipSkins.PerShip; s++) price &= ShipSkins.PriceOf(P, s) == 50000f;
        Check("every skin costs 50,000 star dust; stock is free", price && ShipSkins.PriceOf(P, 0) == 0f);
        Check("first two skins together = 100,000", ShipSkins.PriceOf(P, 1) + ShipSkins.PriceOf(P, 2) == 100000f);
        var names = new System.Collections.Generic.HashSet<string>();
        for (int s = 0; s < ShipSkins.PerShip; s++) names.Add(ShipSkins.Get(P, s).name);
        Check("five distinct skin names", names.Count == 5);
        bool sheets = true, alpha = true;
        var stock = ShipHullArt.SheetFor(P, 0);
        for (int s = 1; s < ShipSkins.PerShip; s++)
        {
            var bytes = ShipHullArt.SkinPng(P, s);
            sheets &= bytes != null && bytes.Length > 1000;
            var skin = ShipHullArt.SheetFor(P, s);
            alpha &= skin != null && skin.width == 9 * 256 && skin.height == 3 * 256;
        }
        Check("each skin has a sheet of the stock sheet's size", sheets && alpha && stock != null && stock.width == 9 * 256 && stock.height == 3 * 256);

        // buying needs the ship
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 400000f);
        Check("a skin can't be bought before the ship is owned", ShipSkins.TryPurchase(P, 1) == ShipSkins.PurchaseResult.Invalid);
        PremiumShips.Grant(P);
        Check("owning the ship, a skin buys with 50,000 dust",
              ShipSkins.TryPurchase(P, 1) == ShipSkins.PurchaseResult.Bought &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 350000f) && ShipSkins.IsOwnedReal(P, 1));
        Check("tier follows the skins owned: hearts 11, speed 22", ShipLives.Max(P) == 11 && ShipStartSpeed.EquippedHud() >= 0 &&
              PremiumShips.TierOf(P) == 1 && ShipStartSpeed.HudFor(P, PremiumShips.TierOf(P)) == 22);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 20000f);
        Check("too little dust: can't afford", ShipSkins.TryPurchase(P, 2) == ShipSkins.PurchaseResult.CantAfford);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1e6f);
        for (int s = 2; s < ShipSkins.PerShip; s++) ShipSkins.TryPurchase(P, s);
        Check("all four skins: tier 4, 14 hearts, speed 28", PremiumShips.TierOf(P) == 4 && ShipLives.Max(P) == 14 &&
              ShipStartSpeed.HudFor(P, 4) == 28);
        var l = ShipWeaponUpgrades.LoadoutFor(P);
        Check("the barrage grows with the skins (8 lances + 2 per odd step)", l.shots == 12 && ShipWeaponUpgrades.CooldownScale(P) < 1f);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
        for (int s = 1; s < ShipSkins.PerShip; s++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(P, s));
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 0f);
    }

    static void Economy()
    {
        Check("the 15 dust ships keep their prices and rows", shopingShips.CostFor(2) == 600f && shopingShips.CostFor(15) == 3000f &&
              ShipLives.Base(5) == ShipLives.Row(5));
        Check("Paragon is never a top-tier (price) ship, the three priciest still are",
              !ShipLoadoutTable.IsTopTierByPrice(P) && ShipLoadoutTable.IsTopTierByPrice(5) && ShipLoadoutTable.IsTopTierByPrice(6) &&
              ShipLoadoutTable.IsTopTierByPrice(7) && !ShipLoadoutTable.IsTopTierByPrice(15));
        Check("the all-skins set ignores it: owning every dust-ship skin completes the set without Paragon",
              SkinHearts.SkinsMissing >= 0 && SkinHearts.Bonus(P) == SkinHearts.FromColours(P));
        Check("Fleet Admiral counts the 15 dust ships", PremiumShips.StandardCount == 15 && AchievementTracker.OwnedShips() == 1);
    }

    // ---- fixtures (as PowersReworkTest) ---------------------------------

    static void FreshScene()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.aspect = .5625f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        collisionDetection.lifeCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        WorldTimeFx.Reset();
        HostileShots.ResetCounters();
    }

    static GameObject Hazard(float x, float y)
    {
        var go = new GameObject("hazard");
        go.tag = "Enimey";
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static void Combat()
    {
        FreshScene();
        PlayerPrefs.SetInt("spawnShip", P);
        PlayerPrefs.SetString(ShipId.OwnedKey(P), "True");
        var go = new GameObject("ship16", typeof(SpriteRenderer));
        go.GetComponent<SpriteRenderer>().sprite = ShipHullArt.StockRest(P);
        go.transform.localScale = Vector3.one * shopingShips.NormalizedHullScale(go.GetComponent<SpriteRenderer>().sprite);
        go.transform.position = new Vector3(0f, -3f, 0f);
        var power = go.AddComponent<ShipPowerController>();
        power.SendMessage("Awake");
        power.SendMessage("Start");
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        var hazards = new System.Collections.Generic.List<GameObject>();
        try
        {
            var missiles = go.GetComponent<ParagonMissiles>();
            Check("the controller gives a premium ship its missile system, its barrage and its timed shield",
                  missiles != null && power.Runner != null && power.Secret != null && power.Secret.Power == SecretPower.AegisShield &&
                  power.Loadout.attack == ShipAttack.ParagonBarrage);
            for (int i = 0; i < 4; i++) hazards.Add(Hazard(-1.2f + i * .8f, 1.5f + i * .3f));

            // ---- homing missiles on a timer
            Check("the missile timer starts at the tier's 10 s", Mathf.Approximately(missiles.TimeLeft, 10f));
            int before = AttackProjectile.LaunchCount;
            for (int i = 0; i < 95; i++) missiles.Step(.1f);
            Check("nothing fires before 10 s", missiles.Fired == 0 && AttackProjectile.LaunchCount == before);
            for (int i = 0; i < 10; i++) missiles.Step(.1f);
            Check("a volley of 2 homing missiles goes out at 10 s", missiles.Fired == 1 && AttackProjectile.LaunchCount == before + 2);
            int homing = 0, distinct = 0;
            var seen = new System.Collections.Generic.HashSet<Transform>();
            foreach (var p in AttackPool.Projectiles)
                if (p != null && p.Active && p.CurrentKind == AttackProjectile.Kind.Homing)
                { homing++; if (p.Target != null && seen.Add(p.Target)) distinct++; }
            Check("they are homing and each hunts a different target", homing >= 2 && distinct == 2);
            for (int i = 0; i < 99; i++) missiles.Step(.1f);
            Check("the next volley is another 10 s away", missiles.Fired == 1);
            for (int i = 0; i < 10; i++) missiles.Step(.1f);
            Check("the second volley fires at 20 s", missiles.Fired == 2);
            AttackPool.StopAll();
            foreach (var h in hazards) if (h != null) Object.DestroyImmediate(h);
            hazards.Clear();
            for (int i = 0; i < 200; i++) missiles.Step(.1f);
            Check("with nothing on screen it holds fire (no wasted volley)", missiles.Fired == 2);
            hazards.Add(Hazard(0f, 2f));
            missiles.Step(.1f);
            Check("it fires the first frame something appears", missiles.Fired == 3);
            AttackPool.StopAll();
            foreach (var h in hazards) if (h != null) Object.DestroyImmediate(h);
            hazards.Clear();

            // ---- the barrage (the ultimate)
            for (int i = 0; i < 5; i++) hazards.Add(Hazard(-1.5f + i * .75f, 2f));
            int b0 = AttackProjectile.LaunchCount;
            Check("the barrage fires", power.Runner.Fire());
            for (int i = 0; i < 40; i++) power.Runner.Step(.05f);
            Check("it launches its 8 lances, one every 50 ms", AttackProjectile.LaunchCount - b0 == 8 && ShipLoadoutTable.For(P).shots == 8);
            AttackPool.StopAll();
            foreach (var h in hazards) if (h != null) Object.DestroyImmediate(h);
            hazards.Clear();

            // ---- the shield
            var hit = go.AddComponent<collisionDetection>();
            hit.RaiseShield(2.5f);
            Check("RaiseShield lifts the blue-atom shield for the tier's seconds (no boost, no atom)",
                  collisionDetection.atomCheck && collisionDetection.invTimer >= 2.5f && ShipShield.For(go).IsUp);
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            ShipShield.For(go).Hide();

            var secret = power.Secret;
            secret.SetMeter(0f);
            int shieldsBefore = secret.AegisShields;
            for (int i = 0; i < 79; i++) secret.Step(.1f);
            Check("the shield meter is a timer: not ready at 7.9 s", secret.AegisShields == shieldsBefore && secret.Meter01 < 1f);
            secret.Add(100f);
            Check("dust and kills don't fill it", secret.Meter01 < 1f);
            for (int i = 0; i < 3; i++) secret.Step(.1f);
            Check("it raises itself at 8 s", secret.AegisShields == shieldsBefore + 1 && collisionDetection.atomCheck);
            Check("the meter starts over", secret.Meter01 < .1f);
        }
        finally
        {
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            power.SendMessage("OnDestroy");
            if (power.Runner != null) power.Runner.SendMessage("OnDestroy");
            if (power.Secret != null) power.Secret.SendMessage("OnDestroy");
            AttackPool.StopAll();
            foreach (var h in hazards) if (h != null) Object.DestroyImmediate(h);
            Object.DestroyImmediate(go);
            WorldTimeFx.Reset();
        }
    }
}
