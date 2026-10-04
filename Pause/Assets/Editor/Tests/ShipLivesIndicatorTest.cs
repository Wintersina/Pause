using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShipLivesIndicatorTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SL] PASS  " : "[SL] FAIL  ") + what);
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

        // lifeControler now only attaches ShipDamageFx (and only reads
        // collisionDetection.lifeCounter for the scorch tint) in an actual
        // gameplay scene -- the shop's own ship1-ship3 carry the same
        // lifeControler/collisionDetection pair the real player ship does,
        // and used to inherit whatever damage state a previous run left
        // behind. Opening gameS1 here makes that gameplay context explicit
        // instead of relying on whichever scene happened to be left open,
        // matching what DamageEffectsAttachToEveryHull below actually needs.
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        // collisionDetection.Start() is what normally sets these; it never runs
        // in this isolated test, so the real preconditions are set explicitly.
        collisionDetection.MAXLIFE = 3;

        DamageEffectsAttachToEveryHull();
        ShieldBubbleAttachesToAnyHull();

        // Every ship floats one heart per life it flies with (ShipLives):
        // a Retro80s hull with its own damage art too.
        PlayerPrefs.SetInt("spawnShip", 7);   // Gold Warden: 5
        PlayerPrefs.SetString("boughtship7", "True");
        collisionDetection.MAXLIFE = 0;       // not yet set by a collisionDetection: the equipped ship's
        var withArt = new GameObject("ship7(Clone)", typeof(SpriteRenderer));
        var indicatorA = withArt.AddComponent<ShipLivesIndicator>();
        indicatorA.SendMessage("Start");
        int artHearts = 0;
        foreach (Transform c in withArt.transform)
            if (c.name.StartsWith("Heart")) artHearts++;
        Check("a ship with its own damage art gets its hearts too (" + artHearts + ", Gold Warden 5)", artHearts == 5);
        Object.DestroyImmediate(withArt);

        // A legacy ship gets exactly MAXLIFE hearts, all visible at full health.
        PlayerPrefs.SetInt("spawnShip", 8); // Lightning
        PlayerPrefs.SetString("boughtship8", "True"); // an unowned selection flies the starter
        collisionDetection.MAXLIFE = ShipLives.Max(8);
        var legacy = new GameObject("Legacy", typeof(SpriteRenderer));
        var sr = legacy.GetComponent<SpriteRenderer>();
        sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Art/Resources/ShipArt/Originals/Lightning.png"); // may be null; Build() must tolerate that
        var indicatorB = legacy.AddComponent<ShipLivesIndicator>();
        indicatorB.SendMessage("Start");

        int heartCount = 0;
        foreach (Transform c in legacy.transform)
            if (c.name.StartsWith("Heart")) heartCount++;
        Check("legacy ship gets exactly MAXLIFE hearts (" + heartCount + ")",
              heartCount == collisionDetection.MAXLIFE);

        int visible = 0;
        foreach (Transform c in legacy.transform)
            if (c.gameObject.activeSelf) visible++;
        Check("all hearts start visible at full health (" + visible + "/" + heartCount + ")",
              visible == collisionDetection.MAXLIFE);

        // Simulate taking a hit and healing it back.
        int savedLife = collisionDetection.lifeCounter;
        collisionDetection.lifeCounter = 1;
        indicatorB.SendMessage("Update");
        visible = 0;
        foreach (Transform c in legacy.transform)
            if (c.gameObject.activeSelf) visible++;
        Check("one hit hides exactly one heart (" + visible + " left)", visible == collisionDetection.MAXLIFE - 1);

        collisionDetection.lifeCounter = 0; // heal atom restoring a life
        indicatorB.SendMessage("Update");
        visible = 0;
        foreach (Transform c in legacy.transform)
            if (c.gameObject.activeSelf) visible++;
        Check("healing brings a heart back (" + visible + " shown)", visible == collisionDetection.MAXLIFE);

        collisionDetection.lifeCounter = savedLife;
        Object.DestroyImmediate(legacy);
        PlayerPrefs.DeleteKey("spawnShip");

        StarterHeartsFollowItsColour();

        Debug.Log("[SL] failures: " + fails);
        return fails;
    }

    // Neon Comet: 2 hearts, 3 once it owns a colour; a run rebuilds them.
    static void StarterHeartsFollowItsColour()
    {
        PlayerPrefs.SetInt("spawnShip", ShipId.Starter);
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(ShipId.Starter, n));
        collisionDetection.MAXLIFE = 0;
        var ship = new GameObject(ShipId.ObjectName(ShipId.Starter), typeof(SpriteRenderer));
        var hearts = ship.AddComponent<ShipLivesIndicator>();
        hearts.SendMessage("Start");
        Check("the starter floats 2 hearts", hearts.Hearts != null && hearts.Hearts.Length == 2);
        collisionDetection.lifeCounter = 1;
        hearts.SendMessage("Update");
        int shown = 0;
        foreach (var h in hearts.Hearts) if (h.gameObject.activeSelf) shown++;
        Check("one hit leaves the starter its last heart (" + shown + ")", shown == 1);
        collisionDetection.lifeCounter = 0;

        PlayerPrefs.SetInt(ShipSkins.OwnedKey(ShipId.Starter, 2), 1);
        hearts.BuildHearts();
        int built = 0;
        foreach (Transform c in ship.transform) if (c.name.StartsWith("Heart")) built++;
        Check("with a colour of its own the starter floats 3 (" + hearts.Hearts.Length + ", " + built + " built)",
              hearts.Hearts.Length == 3 && built == 3);
        PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(ShipId.Starter, 2));
        Object.DestroyImmediate(ship);
        PlayerPrefs.DeleteKey("spawnShip");
        collisionDetection.MAXLIFE = 3;
    }

    static void DamageEffectsAttachToEveryHull()
    {
        // Legacy sheets do not have authored broken frames, so the shared
        // damage-fire layer is required for every selected hull, while the
        // modern ships additionally swap their damaged sprite frames.
        PlayerPrefs.SetInt("spawnShip", 8);
        var ship = new GameObject("ship8(Clone)", typeof(SpriteRenderer));
        var life = ship.AddComponent<lifeControler>();
        life.SendMessage("Start");
        var damageFx = ship.GetComponent<ShipDamageFx>();
        Check("a hurt ship gets persistent damage effects", damageFx != null);
        // No separate Start() for damageFx: SendMessage reaches every
        // component on the GameObject, so life.SendMessage("Start") above
        // already started the ShipDamageFx it added. A second call built a
        // second set of flames.

        collisionDetection.lifeCounter = 2;
        life.SendMessage("Update");
        Check("damage keeps the authored hull colour",
              ship.GetComponent<SpriteRenderer>().color == Color.white);
        collisionDetection.lifeCounter = 0;

        // The per-ship emitters and the particle pool (ShipDamageTest covers
        // what they do), built once under one "~DamageFx" child.
        var fxRoot = ship.transform.Find("~DamageFx");
        Check("damage effects build their emitters and pool under ~DamageFx",
              fxRoot != null && damageFx.EmitterCount == ShipDamageTable.Total(8) &&
              damageFx.PoolSize == ShipDamageFx.MaxParticles &&
              fxRoot.childCount == ShipDamageTable.Total(8) + ShipDamageFx.MaxParticles);

        for (int i = 1; i < shopingShips.shipTotal; i++)
        {
            var frames = shopingShips.DamageSpritesFor(i);
            Check("ship" + i + " resolves intact, damaged and critical frames",
                  frames != null && frames.Length == 3 &&
                  frames[0] != null && frames[1] != null && frames[2] != null);
        }
        Object.DestroyImmediate(ship);
    }

    static void ShieldBubbleAttachesToAnyHull()
    {
        var ship = new GameObject("AnyRosterHull", typeof(SpriteRenderer));
        var bubble = ShipShield.For(ship);
        Check("every hull receives a shield visual", bubble.Visual != null);
        bubble.Show();
        Check("blue atom shield can be shown on every hull",
              bubble.Visual != null && bubble.Visual.activeSelf);
        bubble.Hide();
        Check("blue atom shield can be hidden after its timer", !bubble.Visual.activeSelf);
        Object.DestroyImmediate(ship);
    }
}
