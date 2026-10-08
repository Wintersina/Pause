using UnityEngine;

// The tutorial's one enemy: a real Bile Mite (EnemyFactory, so the ship's
// collisions, a teleport strike and the weapon all treat it like any alien)
// that drops in from just above the top edge, straight at where the ship
// is, and sinks slowly through the ship's lane with a gentle sway. The
// player dodges it (it leaves past the bottom edge and is gone), rams it
// (a heart, like a real run) or teleports onto it (TeleportFx.Strike).
//
// Why it moves itself: tutorialS5's world scroller starts at speed 0 and
// barely ramps (see TutorialAtomDrift), so a real alien's mover would leave
// it parked above the screen. Its mover and its brain are switched off --
// no weave, no windup, and no shots (the tutorial never lets roster
// enemies shoot: EnemyBrain.ShootingAllowed).
//
// Like everything else it moves on scaled time and only while the world is
// moving, so letting go freezes it.
public class TutorialEnemy : MonoBehaviour
{
    // ---- Tuning (world units, seconds) ----

    public const string DefKey = "space_alien";
    // Descent speed: slow enough to line up a teleport on it.
    public const float Speed = 1.4f;
    // Spawn this far above the camera's top edge (just out of view).
    public const float SpawnAboveTop = .6f;
    // Gone once it is this far below the bottom edge.
    public const float LeaveBelow = .8f;
    public const float SwayAmplitude = .4f;
    public const float SwayHz = .3f;

    static float LaneHalfWidth => RailInset.Lane(2.2f);

    // The enemy on screen, if any (Unity-null once it is destroyed).
    static Transform live;
    public static Transform Live { get { return live; } }

    float baseX;
    float clock;

    public float BaseX { get { return baseX; } }

    // Builds the alien above the top edge at x (clamped into the lane).
    public static TutorialEnemy Spawn(float x)
    {
        var def = EnemyRoster.Find(DefKey) ?? EnemyRoster.One(0, EnemyRole.Alien);
        if (def == null) return null;

        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        x = Mathf.Clamp(x, -LaneHalfWidth + SwayAmplitude, LaneHalfWidth - SwayAmplitude);
        var go = EnemyFactory.Create(def, new Vector3(x, top + SpawnAboveTop, 0f), Quaternion.identity);
        if (go == null) return null;

        var weaver = go.GetComponent<moveEnimes>();
        if (weaver != null) weaver.enabled = false;
        var scroller = go.GetComponent<moveItemEnmInStrightLine>();
        if (scroller != null) scroller.enabled = false;
        var brain = go.GetComponent<EnemyBrain>();
        if (brain != null) brain.enabled = false;
        // with no brain to drive its attack tell, it plays its own
        var flipbook = go.GetComponent<EnemyFlipbook>();
        if (flipbook != null) flipbook.SetBrainDriven(false);

        var enemy = go.AddComponent<TutorialEnemy>();
        enemy.baseX = x;
        live = go.transform;
        return enemy;
    }

    // Removes the alien if it is still out (the tutorial ended or was skipped).
    public static void Clear()
    {
        if (live == null) { live = null; return; }
        var go = live.gameObject;
        live = null;
        ClearTarget.Release(go);
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }

    static bool WorldMoving()
    {
        return (TouchInput.IsPressed || score.pauseCounter <= 0) && !buttonClicks.playerDied;
    }

    void Update()
    {
        Step(Time.deltaTime, WorldMoving());
    }

    // One frame of movement; dt is scaled time, so at timeScale 0 nothing
    // moves. Returns false once it has left the screen (and is destroyed).
    public bool Step(float dt, bool worldMoving)
    {
        if (!worldMoving || dt <= 0f) return true;
        clock += dt;

        var p = transform.position;
        p.y -= Mathf.Max(Speed, Mathf.Max(0f, moveBackGround.speed) * 30f) * dt;
        p.x = baseX + SwayAmplitude * Mathf.Sin(2f * Mathf.PI * SwayHz * clock);
        transform.position = p;

        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        if (p.y >= bottom - LeaveBelow) return true;

        // dodged: it leaves like any enemy that scrolled past
        ClearTarget.Release(gameObject);
        if (live == transform) live = null;
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
        return false;
    }
}
