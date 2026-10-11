using UnityEngine;
using UnityEngine.SceneManagement;

// How big the player's ship flies, per scene: the one knob.
//
// Every hull is normalised to shopingShips.ReferenceHullSize (0.58 u across
// its longest side) by spawnShips.ApplyHull / lifeControler. In the main game
// scene (gameS1) AND in the tutorial (tutorialS5: it teaches on the ship the
// run will fly, so it must look exactly as big) the flown ship is drawn Main
// times bigger; everywhere else -- the title screen's traffic, the dock, the
// codex, the shop and death-panel renders -- it stays at the normalised size.
// (The tutorial used to be left out, so its ship was visibly 26% smaller than
// the run's.)
//
// The factor goes on the ship's root transform, so everything that lives in
// the hull's own space follows by itself: the art, the baked hull / shield
// hit polygons (ShipHitbox, so the hitbox-to-art ratio is unchanged), the
// shield contour, exhaust nozzles and flames, gun and nose anchors, damage
// fx, the spin drift ring and wake, heart orbits, death-crash pieces and
// resume ghosts (all lossyScale-aware). The few world-unit numbers that
// stand for the ship's size read Live here: ShipReach.HullBelow / HullAbove
// (its drawing below / above its centre) and HullHalfWidth (its sideways
// clamp against the rails), ShipHitbox's pickup radius. SpawnLane.ShipGap
// (the gap every spawn row keeps open) is deliberately left at the
// normalised ship's: growing it is a difficulty decision.
public static class ShipScale
{
    // ---- tunable ----
    // The flown ship's size in the main game scene, x the normalised hull.
    public const float Main = 1.35f;
    public const string MainScene = "gameS1";
    public const string TutorialScene = "tutorialS5";

    public static float For(string sceneName)
    {
        return sceneName == MainScene || sceneName == TutorialScene ? Main : 1f;
    }

    public static float ForScene(Scene scene)
    {
        return scene.IsValid() ? For(scene.name) : 1f;
    }

    // The active scene's factor. Cached on the scene's handle: reading a
    // scene's name allocates, and this is read every frame (ShipReach).
    public static float Live
    {
        get
        {
            var s = SceneManager.GetActiveScene();
            int handle = s.handle;
            if (handle != liveHandle)
            {
                liveHandle = handle;
                live = ForScene(s);
            }
            return live;
        }
    }

    static int liveHandle = int.MinValue;
    static float live = 1f;

    // The flown hull's half-width at the reference size (its longest side
    // across), in the active scene.
    public static float HullHalfWidth { get { return shopingShips.ReferenceHullSize * .5f * Live; } }
}
