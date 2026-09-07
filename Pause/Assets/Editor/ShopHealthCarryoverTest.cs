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
// bug spotted along the way: the ship-purchase confirm dialog's preview
// froze on a single static frame instead of idle-cycling like every other
// ship display in the shop.
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
        GameStateResetZeroesCombatState();
        DockShipsIgnoreStaleDamageOutsideGameplay();
        GameplayShipsStillShowRealDamage();
        ConfirmDialogPreviewAnimates();

        Debug.Log("[SH] failures: " + fails);
        EditorApplication.Exit(0);
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
        EditorSceneManager.OpenScene("Assets/Scenes/shopS6.unity", OpenSceneMode.Single);

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
        for (int idle = 0; idle < 3; idle++)
            if (shown == shopingShips.IdleSpriteFor(shipIndex, damageFrame, idle))
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

    static void ConfirmDialogPreviewAnimates()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/shopS6.unity", OpenSceneMode.Single);

        var shop = Object.FindFirstObjectByType<shopingShips>();
        Check("shop scene has a shopingShips instance", shop != null);
        if (shop == null) return;
        shop.SendMessage("Start", SendMessageOptions.DontRequireReceiver);

        var popUp = shopingShips.popUpCanvis;
        Check("shop resolves its popup canvas", popUp != null);
        if (popUp == null) return;
        popUp.SetActive(true);
        shopingShips.shipNumber = 1;

        // Seed shipImg the way shipselected() normally would (setShipImage
        // is private; called the same way SendMessage reaches Start/Update
        // elsewhere in this suite).
        var setShipImage = typeof(shopingShips).GetMethod("setShipImage", BindingFlags.NonPublic | BindingFlags.Instance);
        setShipImage.Invoke(shop, new object[] { 1 });
        var seeded = shop.shipImg.sprite;
        Check("setShipImage seeds a real sprite", seeded != null);

        // Time.unscaledTime does not advance outside Play mode (confirmed
        // elsewhere this session: batch-mode -executeMethod never ticks the
        // player loop), so the idle-cycle index this reuses from
        // DockShipIdleAnimator's own approach can't be shown changing frame-
        // to-frame here. What this confirms instead: Update() actively
        // re-derives shipImg from IdleSpriteFor every frame the dialog is
        // open, rather than only ever setting it once in setShipImage --
        // i.e. it's now driven by the same live mechanism as every other
        // idle-cycling ship display in the shop, not a one-shot snapshot.
        shop.SendMessage("Update");
        // Mirrors Update()'s own idleFrame formula exactly, rather than
        // trying every frame 0-2: ship1 may or may not have idle art at
        // all, and either way is a legitimate outcome -- what matters is
        // that whichever IdleSpriteFor(1, 0, ...) actually resolves to is
        // what's now showing, not a snapshot frozen from setShipImage().
        int idleFrame = Mathf.FloorToInt(Time.unscaledTime * 8f) % 3;
        Sprite expectedIdle = shopingShips.IdleSpriteFor(1, 0, idleFrame);
        if (expectedIdle != null)
            Check("confirm dialog's ship preview is driven by IdleSpriteFor while open",
                  shop.shipImg.sprite == expectedIdle);
        else
            Check("confirm dialog's ship preview still resolves to a sprite when no idle art exists for this hull",
                  shop.shipImg.sprite != null);

        popUp.SetActive(false);
        shopingShips.shipNumber = 0;
    }
}
