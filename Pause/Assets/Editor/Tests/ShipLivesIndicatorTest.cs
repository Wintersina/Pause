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
        LostHeartsShieldThenCrumble();

        Debug.Log("[SL] failures: " + fails);
        return fails;
    }

    // Neon Comet: 1 heart, 2 once it owns a colour; a run rebuilds them.
    static void StarterHeartsFollowItsColour()
    {
        PlayerPrefs.SetInt("spawnShip", ShipId.Starter);
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(ShipId.Starter, n));
        collisionDetection.MAXLIFE = 0;
        var ship = new GameObject(ShipId.ObjectName(ShipId.Starter), typeof(SpriteRenderer));
        var hearts = ship.AddComponent<ShipLivesIndicator>();
        hearts.SendMessage("Start");
        Check("the starter floats 1 heart (a single heart orbits too)", hearts.Hearts != null && hearts.Hearts.Length == 1 && hearts.Hearts[0].gameObject.activeSelf);

        PlayerPrefs.SetInt(ShipSkins.OwnedKey(ShipId.Starter, 2), 1);
        hearts.BuildHearts();
        int built = 0;
        foreach (Transform c in ship.transform) if (c.name.StartsWith("Heart")) built++;
        Check("with a colour of its own the starter floats 2 (" + hearts.Hearts.Length + ", " + built + " built)",
              hearts.Hearts.Length == 2 && built == 2);
        collisionDetection.lifeCounter = 1;
        hearts.SendMessage("Update");
        int shown = 0;
        foreach (var h in hearts.Hearts) if (h.gameObject.activeSelf) shown++;
        Check("one hit leaves the coloured starter its last heart (" + shown + ")", shown == 1);
        collisionDetection.lifeCounter = 0;
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
            Check(ShipId.KeyOf(id) + " orbits its hearts as " + style + (ShipUiSlots.Spins(id) ? " (spinner)" : ""),
                  (style == HeartStyle.ShieldRing) == ShipUiSlots.Spins(id));
        }
        Check("all six orbit styles are in use (" + used.Count + ")", used.Count == 6);
    }

    // A hit costs a heart as a shield: the best-placed heart darts to the
    // impact point between the hull and the hit, bursts as a shield there,
    // then cracks into falling shards (pooled, frozen with the world, gone
    // after BreakSeconds); a heal pops one back into orbit. The spray API
    // gives the shown hearts' positions without allocating.
    static void LostHeartsShieldThenCrumble()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ShipUiSlots.ScreenOverride = () => Rect.MinMaxRect(-100f, -100f, 100f, 100f);
        var cases = new[]
        {
            (7, 5, new Vector3(.2f, 1.1f, 0f)),     // Gold Warden, from ahead
            (7, 2, new Vector3(-1.2f, -.1f, 0f)),   // from the left
            (1, 3, new Vector3(.9f, -.8f, 0f)),     // Neon Comet, from below right
            (11, 4, new Vector3(1.1f, .6f, 0f)),    // Ninja (spins)
            (13, 3, new Vector3(-.3f, -1.2f, 0f)),  // UFO, from below
        };
        foreach (var (id, count, from) in cases)
        {
            collisionDetection.lifeCounter = 0;
            var rig = HeartsPlacementTest.Build(id, new Vector3(.5f, -1f, 0f), withPower: false, hearts: count);
            var hearts = rig.hearts;
            hearts.SendMessage("Update");
            for (int f = 0; f < 50; f++) hearts.Place(1f / 60f, 1f / 60f);
            string who = ShipId.KeyOf(id) + ", " + count + " hearts: ";
            var positions = new System.Collections.Generic.List<Vector3>(8);
            Check(who + "HeartPositions gives every shown heart",
                  hearts.HeartPositions(positions) == count && positions.Count == count && hearts.ShownCount == count);

            Vector3 ship = rig.ship.transform.position;
            Vector3 impact = ship + from;
            Vector3 shield = ship + (Vector3)hearts.ShieldOffset(impact);
            // which heart is best placed to block it
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float d = ((Vector2)(hearts.Hearts[i].position - shield)).magnitude + (hearts.Depth(i) < 0f ? .25f * -hearts.Depth(i) : 0f);
                if (d < best) { best = d; nearest = i; }
            }
            var nearestHeart = hearts.Hearts[nearest];
            Vector3 startAt = nearestHeart.position;

            ShipLivesIndicator.Impact(impact);
            collisionDetection.lifeCounter = 1;
            hearts.SendMessage("Update");
            Check(who + "a hit starts one shield and hides one heart", hearts.ActiveBreaks == 1 && hearts.ShownCount == count - 1);
            Check(who + "the best-placed heart is the one that goes", !nearestHeart.gameObject.activeSelf);
            Vector2 toHit = (Vector2)(impact - ship), toShield = (Vector2)(hearts.ShieldPoint(0) - ship);
            Check(who + "it shields between the hull and the hit (" + toShield.magnitude.ToString("F2") + " of " +
                  toHit.magnitude.ToString("F2") + " out, " + Vector2.Angle(toHit, toShield).ToString("F0") + " deg off)",
                  Vector2.Angle(toHit, toShield) < 25f && toShield.magnitude <= toHit.magnitude + .01f);

            var pieces = rig.ship.transform.Find("~HeartBreaks");
            var ghost = pieces.Find("Ghost0").GetComponent<SpriteRenderer>();
            var burst = pieces.Find("Burst0").GetComponent<SpriteRenderer>();
            hearts.StepBreaks(.001f);
            Check(who + "it leaves from where it was flying",
                  ghost.enabled && ((Vector2)(ghost.transform.position - startAt)).magnitude < .05f);
            float before = ((Vector2)(ghost.transform.position - hearts.ShieldPoint(0))).magnitude;
            hearts.StepBreaks(ShipLivesIndicator.DartSeconds * .5f);
            float mid = ((Vector2)(ghost.transform.position - hearts.ShieldPoint(0))).magnitude;
            hearts.StepBreaks(ShipLivesIndicator.DartSeconds * .5f + ShipLivesIndicator.ShieldSeconds * .4f);
            float there = ((Vector2)(ghost.transform.position - hearts.ShieldPoint(0))).magnitude;
            Check(who + "darts to the impact point (" + before.ToString("F2") + " -> " + mid.ToString("F2") + " -> " + there.ToString("F3") + ")",
                  mid < before || before < .02f);
            Check(who + "and blocks it there in a shield burst", there < .03f && ghost.enabled && burst.enabled);

            hearts.StepBreaks(ShipLivesIndicator.ShieldSeconds * .6f + ShipLivesIndicator.BreakCrack);
            int shardsOn = 0;
            foreach (Transform c in pieces) if (c.name.StartsWith("Shard0_") && c.GetComponent<SpriteRenderer>().enabled) shardsOn++;
            Check(who + "then crumbles into shards (" + shardsOn + ")", shardsOn >= 4 && !ghost.enabled && !burst.enabled);
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

            // A heal pops a heart back into orbit.
            collisionDetection.lifeCounter = 0;
            hearts.SendMessage("Update");
            hearts.Place(0f, 0f);
            var back = hearts.Hearts[count - 1];
            float start = back.localScale.x / (1f + ShipLivesIndicator.DepthScale * hearts.Depth(count - 1));
            hearts.Place(0f, ShipLivesIndicator.PopSeconds * .55f);
            float midScale = back.localScale.x / (1f + ShipLivesIndicator.DepthScale * hearts.Depth(count - 1));
            hearts.Place(0f, ShipLivesIndicator.PopSeconds);
            float end = back.localScale.x / (1f + ShipLivesIndicator.DepthScale * hearts.Depth(count - 1));
            Check(who + "a heal pops the heart back in (" + start.ToString("F3") + " -> " + midScale.ToString("F3") + " -> " +
                  end.ToString("F3") + ")", back.gameObject.activeSelf && start < end * .2f && midScale > end * .95f && hearts.ActiveBreaks == 0);
            Vector3 p0 = back.position;
            for (int f = 0; f < 30; f++) hearts.Place(1f / 60f, 1f / 60f);
            Check(who + "and it flies on round its orbit", (back.position - p0).magnitude > .05f);

            // No garbage per frame: the orbit, the shield and the spray API.
            ShipLivesIndicator.Impact(impact);
            collisionDetection.lifeCounter = 1;
            hearts.SendMessage("Update");
            for (int f = 0; f < 5; f++) { hearts.Place(1f / 60f, 1f / 60f); hearts.StepBreaks(1f / 60f); }
            long allocBefore = System.GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 60; f++)
            {
                hearts.Place(1f / 240f, 1f / 60f);
                hearts.StepBreaks(1f / 60f);
                positions.Clear();
                hearts.HeartPositions(positions);
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Check(who + "no allocation per frame (" + allocated + " bytes over 60 frames)", allocated == 0);
            collisionDetection.lifeCounter = 0;
            HeartsPlacementTest.Teardown(rig);
        }
        ShipUiSlots.ScreenOverride = null;
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
