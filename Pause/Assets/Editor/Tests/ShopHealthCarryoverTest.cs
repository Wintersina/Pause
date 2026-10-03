using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Reflection;

// Covers a bug reported 2026-09-07: after taking damage in a real run and
// quitting back to the space dock, the shop's own ship1-ship3 (authored
// with the same collisionDetection/lifeControler pair the real player ship
// carries) kept reading the last run's damage state -- scorched sprite,
// ShipDamageFx's fire and sparks -- because collisionDetection.lifeCounter
// is a static and nothing reset it outside gameplay. Also covers a related
// bug spotted along the way (the confirm dialog's frozen preview) went away
// with the dialog itself; the dock's hulls are now checked directly.
public static class ShopHealthCarryoverTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SH] PASS  " : "[SH] FAIL  ") + what);
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

        GameStateResetZeroesCombatState();
        DockShipsIgnoreStaleDamageOutsideGameplay();
        GameplayShipsStillShowRealDamage();
        DockShipsAreCleanHulls();

        Debug.Log("[SH] failures: " + fails);
        return fails;
    }

    static void GameStateResetZeroesCombatState()
    {
        collisionDetection.lifeCounter = 2;
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = 3.5f;

        GameStateReset.Clear();

        Check("GameStateReset.Clear() zeroes lifeCounter", collisionDetection.lifeCounter == 0);
        Check("GameStateReset.Clear() clears atomCheck", !collisionDetection.atomCheck);
        Check("GameStateReset.Clear() zeroes invTimer", collisionDetection.invTimer == 0f);
    }

    static void DockShipsIgnoreStaleDamageOutsideGameplay()
    {
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);

        // Simulate a run that ended mid-damage and was never cleaned up --
        // exactly the scenario GameStateReset.Clear() exists to guard
        // against, checked here directly against lifeControler's own gate
        // rather than relying on Clear() having already run.
        collisionDetection.lifeCounter = 2;

        var go = new GameObject("ship1(Clone)", typeof(SpriteRenderer));
        var comp = go.AddComponent<lifeControler>();
        comp.SendMessage("Start");

        var spriteField = typeof(lifeControler).GetField("spriteControl", BindingFlags.NonPublic | BindingFlags.Instance);
        var sprite = spriteField.GetValue(comp) as SpriteRenderer;

        Check("dock ship's sprite renderer resolves", sprite != null);
        Check("dock ship never got the damage fire/spark component",
              go.GetComponent<ShipDamageFx>() == null);

        Check("dock ship shows the intact (frame 0) sprite despite lifeCounter=2 left over from a run",
              MatchesDamageFrame(sprite.sprite, 1, 0));

        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    // applyDamageSprite() prefers shopingShips.IdleSpriteFor's animated
    // frame over the static img[] array when one exists, so the sprite
    // actually shown for a given damage state isn't necessarily reference-
    // equal to img[frame] -- check against every idle-cycle frame
    // IdleSpriteFor could have picked for that damage state instead.
    static bool MatchesDamageFrame(Sprite shown, int shipIndex, int damageFrame)
    {
        // Any drawing of that damage state's flipbook row (idle loop, bank
        // poses, hit flash -- ShipHullArt) shows that state.
        for (int column = 0; column < ShipHullArt.Columns; column++)
            if (shown == ShipHullArt.Get(shipIndex, damageFrame, column))
                return true;
        return false;
    }

    static void GameplayShipsStillShowRealDamage()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        collisionDetection.lifeCounter = 1;

        var go = new GameObject("ship1(Clone)", typeof(SpriteRenderer));
        var comp = go.AddComponent<lifeControler>();
        comp.SendMessage("Start");

        Check("the real player ship still gets ShipDamageFx in actual gameplay",
              go.GetComponent<ShipDamageFx>() != null);

        var spriteField = typeof(lifeControler).GetField("spriteControl", BindingFlags.NonPublic | BindingFlags.Instance);
        var sprite = spriteField.GetValue(comp) as SpriteRenderer;
        Check("the real player ship still shows the damaged (frame 1) sprite in actual gameplay",
              MatchesDamageFrame(sprite.sprite, 1, 1));

        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(go);
    }

    // The dock's hulls are plain sprites built by SpaceDock -- none of the
    // collisionDetection/lifeControler/movePlayer pair the old authored
    // ship1-ship3 carried -- so a run's leftover damage has nothing to read
    // it. With stale damage still set, every parked hull shows its intact
    // art and gets no damage fire.
    static void DockShipsAreCleanHulls()
    {
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        collisionDetection.lifeCounter = 2;
        ShopSceneExtender.Build();

        for (int i = 1; i < shopingShips.shipTotal; i++)
        {
            var ship = SceneUtil.FindAny("ship" + i);
            if (ship == null) { Check("dock ship" + i + " exists", false); continue; }
            Check("dock ship" + i + " has no live-gameplay damage components",
                  ship.GetComponent<lifeControler>() == null && ship.GetComponent<collisionDetection>() == null &&
                  ship.GetComponent<ShipDamageFx>() == null);
            var sr = ship.GetComponent<SpriteRenderer>();
            Check("dock ship" + i + " shows its intact hull despite lifeCounter=2",
                  sr != null && (sr.sprite == shopingShips.SpriteFor(i, 0) || MatchesDamageFrame(sr.sprite, i, 0)));
        }
        collisionDetection.lifeCounter = 0;
    }
}
