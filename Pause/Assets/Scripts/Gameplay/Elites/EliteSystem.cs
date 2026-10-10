using System.Collections.Generic;
using UnityEngine;

// The elite ships' shared world: every live elite (EliteShip.Live), their
// shots (EliteShots) and their placeholder FX (EliteFx), stepped together
// from one place on the world's clock.
//
// EliteDirector calls Step(Time.deltaTime) on running frames only, so a
// frozen world (timeScale 0, a finger lifted with pauses left, death)
// freezes every elite, shot, puff and shard. Tests and previews call Step
// with their own dt. Nothing here allocates per frame.
public static class EliteSystem
{
    // What the elites hunt: the player ship (found once), or a stand-in.
    public static Transform PlayerOverride;
    static Transform player;
    static float nextLookup;

    public static Transform Player
    {
        get
        {
            if (PlayerOverride != null) return PlayerOverride;
            if (player != null) return player;
            if (Application.isPlaying && Time.unscaledTime < nextLookup) return null;
            nextLookup = Time.unscaledTime + .5f;
            var mover = Object.FindFirstObjectByType<movePlayer>();
            player = mover != null ? mover.transform : null;
            return player;
        }
    }

    // The side rails' inner faces (BossRails, measured when a fight starts;
    // the authored edge otherwise).
    public static float RailEdge => BossRails.InnerEdge;

    // The visible play area (world).
    public static float ViewTop => CameraFit.ViewTop;
    public static float ViewBottom => CameraFit.ViewBottom;

    // World scroll (u/s) every board-locked hazard falls at.
    public static float Scroll => SpawnSpace.ScrollSpeed;

    static GameObject root;
    public static Transform Root
    {
        get
        {
            if (root == null) root = new GameObject("~Elites");
            return root.transform;
        }
    }

    static EliteShots shots;
    public static EliteShots Shots => shots != null && shots.Alive ? shots : (shots = new EliteShots(Root));
    // The pool if it has been built (a reader must not build it).
    public static EliteShots ShotsIfAny => shots != null && shots.Alive ? shots : null;

    static EliteFx fx;
    public static EliteFx Fx => fx != null && fx.Alive ? fx : (fx = new EliteFx(Root));

    static readonly List<EliteShip> stepping = new List<EliteShip>(8);

    // Counters (tests, previews).
    public static int Steps;

    public static void Step(float dt)
    {
        if (dt <= 0f) return;
        Steps++;
        EliteEvasion.Sense(dt);   // the threat picture every elite reads this step
        stepping.Clear();
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++) if (live[i] != null) stepping.Add(live[i]);
        for (int i = 0; i < stepping.Count; i++)
            if (stepping[i] != null && stepping[i].State != EliteState.Dead) stepping[i].Step(dt);
        if (shots != null && shots.Alive) shots.Step(dt);
        if (fx != null && fx.Alive) fx.Step(dt);
    }

    // Everything gone (scene change, tests).
    public static void Clear()
    {
        AttackPools.ClearAll();   // the themed hazards (jets, bands, rings, strikes, lashes) and their previews
        var live = EliteShip.Live;
        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i] != null) BossUtil.Kill(live[i].gameObject);
        live.Clear();
        if (root != null) BossUtil.Kill(root);
        root = null;
        shots = null;
        fx = null;
        player = null;
    }
}
