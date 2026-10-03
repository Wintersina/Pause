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

        // A Retro80s ship (has its own damage art) should get no hearts at all.
        // Destroy() is deferred (and Play-mode oriented) so the component may
        // still be structurally attached right after this call even in the
        // correct-behaviour case -- what actually matters is that Build()
        // did not run, i.e. no heart children exist.
        PlayerPrefs.SetInt("spawnShip", 3);
        var withArt = new GameObject("WithDamageArt", typeof(SpriteRenderer));
        var indicatorA = withArt.AddComponent<ShipLivesIndicator>();
        indicatorA.SendMessage("Start");
        int strayHearts = 0;
        foreach (Transform c in withArt.transform)
            if (c.name.StartsWith("Heart")) strayHearts++;
        Check("ship with its own damage art gets no hearts built", strayHearts == 0);
        Object.DestroyImmediate(withArt);

        // A legacy ship (no damage art) should get exactly MAXLIFE hearts,
        // all visible at full health.
        PlayerPrefs.SetInt("spawnShip", 8); // Lightning
        PlayerPrefs.SetString("boughtship8", "True"); // an unowned selection flies the starter
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

        Debug.Log("[SL] failures: " + fails);
        return fails;
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

        int flames = 0;
        foreach (Transform child in ship.transform)
            if (child.name.StartsWith("~DamageFlame")) flames++;
        Check("damage effects create two small hull flames (" + flames + ")", flames == 2);
        Check("damage effects add a smoke trail",
              ship.transform.Find("~DamageSmoke") != null);
        Check("damage effects add impact bursts",
              ship.transform.Find("~DamageExplosion0") != null &&
              ship.transform.Find("~DamageExplosion1") != null);

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
