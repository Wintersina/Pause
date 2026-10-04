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
            if (c.name.StartsWith("Heart") && !c.name.StartsWith("HeartBreaks") && c.gameObject.activeSelf) visible++;
        Check("all hearts start visible at full health (" + visible + "/" + heartCount + ")",
              visible == collisionDetection.MAXLIFE);

        // Simulate taking a hit and healing it back.
        int savedLife = collisionDetection.lifeCounter;
        collisionDetection.lifeCounter = 1;
        indicatorB.SendMessage("Update");
        visible = 0;
        foreach (Transform c in legacy.transform)
            if (c.name.StartsWith("Heart") && !c.name.StartsWith("HeartBreaks") && c.gameObject.activeSelf) visible++;
        Check("one hit hides exactly one heart (" + visible + " left)", visible == collisionDetection.MAXLIFE - 1);

        collisionDetection.lifeCounter = 0; // heal atom restoring a life
        indicatorB.SendMessage("Update");
        visible = 0;
        foreach (Transform c in legacy.transform)
            if (c.name.StartsWith("Heart") && !c.name.StartsWith("HeartBreaks") && c.gameObject.activeSelf) visible++;
        Check("healing brings a heart back (" + visible + " shown)", visible == collisionDetection.MAXLIFE);

        collisionDetection.lifeCounter = savedLife;
        Object.DestroyImmediate(legacy);
        PlayerPrefs.DeleteKey("spawnShip");

        StarterHeartsFollowItsColour();
        EveryShipHasItsOwnStyle();
        LostHeartsCrumbleHealedOnesPop();

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

    static void EveryShipHasItsOwnStyle()
    {
        var used = new System.Collections.Generic.HashSet<HeartStyle>();
        foreach (int id in ShipId.All)
        {
            var style = ShipHeartStyles.For(id);
            used.Add(style);
            Check(ShipId.KeyOf(id) + " wears its hearts as " + style + (ShipUiSlots.Spins(id) ? " (spinner)" : ""),
                  (style == HeartStyle.ShieldRing) == ShipUiSlots.Spins(id));
        }
        Check("all five heart styles are in use (" + used.Count + ")", used.Count == 5);
    }

    // A hit breaks the lost heart away into falling shards (pooled, frozen
    // with the world, gone after BreakSeconds); a heal pops one back in.
    // The spray API gives the shown hearts' positions without allocating.
    static void LostHeartsCrumbleHealedOnesPop()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (int count in new[] { 2, 5 })
        {
            collisionDetection.lifeCounter = 0;
            var rig = HeartsPlacementTest.Build(7, Vector3.zero, withPower: false, hearts: count);
            var hearts = rig.hearts;
            hearts.SendMessage("Update");
            hearts.Place(0f);
            string who = count + " hearts: ";
            var positions = new System.Collections.Generic.List<Vector3>(8);
            Check(who + "HeartPositions gives every shown heart",
                  hearts.HeartPositions(positions) == count && positions.Count == count && hearts.ShownCount == count);
            Vector3 lostAt = hearts.HeartPosition(count - 1);

            collisionDetection.lifeCounter = 1;
            hearts.SendMessage("Update");
            Check(who + "a hit starts one break-up", hearts.ActiveBreaks == 1 && hearts.ShownCount == count - 1);
            var pieces = rig.ship.transform.Find("~HeartBreaks");
            hearts.StepBreaks(ShipLivesIndicator.BreakShake * .5f);
            var ghost = pieces.Find("Ghost0").GetComponent<SpriteRenderer>();
            Check(who + "it shakes loose where the heart was",
                  ghost.enabled && ((Vector2)(ghost.transform.position - lostAt)).magnitude < .1f);
            hearts.StepBreaks(ShipLivesIndicator.BreakShake + ShipLivesIndicator.BreakCrack);
            int shardsOn = 0;
            foreach (Transform c in pieces) if (c.name.StartsWith("Shard0_") && c.GetComponent<SpriteRenderer>().enabled) shardsOn++;
            Check(who + "then cracks into shards (" + shardsOn + ")", shardsOn >= 4 && !ghost.enabled);
            var shard = pieces.Find("Shard0_0");
            Vector3 held = shard.position;
            for (int f = 0; f < 10; f++) hearts.StepBreaks(0f);
            Check(who + "frozen with the world", shard.position == held);
            hearts.StepBreaks(.2f);
            Check(who + "the shards fall and fade", shard.position.y < held.y &&
                  shard.GetComponent<SpriteRenderer>().color.a < 1f);
            hearts.StepBreaks(ShipLivesIndicator.BreakSeconds);
            bool anyOn = false;
            foreach (Transform c in pieces) if (c.GetComponent<SpriteRenderer>().enabled) anyOn = true;
            Check(who + "and are gone after the break-up", hearts.ActiveBreaks == 0 && !anyOn);

            collisionDetection.lifeCounter = 0;
            hearts.SendMessage("Update");
            hearts.Place(0f, 0f);
            var back = hearts.Hearts[count - 1];
            float start = back.localScale.x;
            hearts.Place(0f, ShipLivesIndicator.PopSeconds * .55f);
            float mid = back.localScale.x;
            hearts.Place(0f, ShipLivesIndicator.PopSeconds);
            float end = back.localScale.x;
            Check(who + "a heal pops the heart back in (" + start.ToString("F3") + " -> " + mid.ToString("F3") + " -> " +
                  end.ToString("F3") + ")", back.gameObject.activeSelf && start < end * .2f && mid > end * .95f && hearts.ActiveBreaks == 0);

            // No garbage per frame: placement, sway, the break-up and the spray API.
            collisionDetection.lifeCounter = 1;
            hearts.SendMessage("Update");
            hearts.Place(0f, 1f / 60f);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 60; f++)
            {
                hearts.Place(1f / 240f, 1f / 60f);
                hearts.StepBreaks(1f / 60f);
                positions.Clear();
                hearts.HeartPositions(positions);
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check(who + "no allocation per frame (" + allocated + " bytes over 60 frames)", allocated == 0);
            collisionDetection.lifeCounter = 0;
            HeartsPlacementTest.Teardown(rig);
        }
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
