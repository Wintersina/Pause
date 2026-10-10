using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// The shield's release shockwave (ShieldShockwave / EnemyShove): when the
// blue-atom shield runs out, hazards near the ship are pushed away from it
// and everything in front of the ship in its column is pushed up-screen.
//
//   - the shield ending is what fires it, with a cyan ring the size of the
//     push radius and a streak up the column, both built ahead of time
//   - inside the radius: pushed outward, harder the closer; outside: not at all
//   - the ship's column: pushed up-screen however far away; the neighbouring
//     column and anything behind the ship: not
//   - hazards with a brain keep running their behaviour from the new place
//     and stay in the lane; chasers are knocked back and close in again
//   - pilots: one waiting above the view is not moved; one entering or on
//     station is pushed (the column push included), keeps its station, flies
//     back to it over time and finishes its script; a windup carries on
//   - rail mines slide along their rail and stay on it
//   - bosses, parked elites and hostile shots do not move; an elite in play
//     is kicked away
//   - pushed bodies do not land on each other
//   - nothing moves while the world is frozen; the push plays out on resume
//   - nothing is scored or destroyed, and a release allocates nothing
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShieldShockwaveTest.Run
public static class ShieldShockwaveTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WAVE] PASS  " : "[WAVE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static readonly Vector2 Ship = new Vector2(0f, -3f);
    const float HullHalf = .29f;
    static float clock;
    static Transform ship;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            Debug.Log("[WAVE] tunables: Radius " + ShieldShockwave.Radius + ", RadialPush " + ShieldShockwave.RadialPush +
                      ", RadialFalloff " + ShieldShockwave.RadialFalloff + ", ColumnMargin " + ShieldShockwave.ColumnMargin +
                      ", ColumnPush " + ShieldShockwave.ColumnPush + ", PushSeconds " + ShieldShockwave.PushSeconds +
                      ", MineMaxSlide " + ShieldShockwave.MineMaxSlide + ", EliteKick " + ShieldShockwave.EliteKick +
                      ", EliteColumnKick " + ShieldShockwave.EliteColumnKick);
            TheShieldEndingFiresIt();
            RadiusPushesOutward();
            ColumnPushesUpScreen();
            HazardsKeepTheirBehaviour();
            PilotsAreShovedAndReturn();
            AShovedPilotsWindupContinues();
            ChasersComeBack();
            MinesStayOnTheirRail();
            BossesAndParkedElitesHold();
            BodiesStayApart();
            NothingMovesWhilePaused();
            NoScoreNoKills();
            ReleaseAllocatesNothing();
            GuardsAndFeel();
        }
        finally
        {
            EnemyShove.Clear();
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            SpawnSpace.ClockOverride = null;
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            Clear();
            buttonClicks.playerDied = false;
            collisionDetection.atomCheck = false;
        }
        Debug.Log("[WAVE] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----------------------------------------------------------

    static readonly List<GameObject> made = new List<GameObject>();

    static void Fresh()
    {
        ShieldShockwave.Clock = () => clock;
        ShieldShockwave.EntryOverride = null;
        Clear();
        EliteSystem.Clear();
        EnemyShove.Clear();
        EnemyThreat.Reset();
        PilotAirspace.Clear();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;   // running without a touch in batch mode
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = .2f;
        clock = 100f;
        SpawnSpace.ClockOverride = clock;
        ship = new GameObject("~WaveTestShip").transform;
        ship.position = new Vector3(Ship.x, Ship.y, 0f);
        made.Add(ship.gameObject);
        EliteSystem.PlayerOverride = ship;
    }

    static void Clear()
    {
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
        foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) Object.DestroyImmediate(r.gameObject);
        foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
        made.Clear();
        ship = null;
    }

    // A plain hazard: tag, trigger box, registry entries, nothing moving it.
    static GameObject Hazard(Vector2 at, float size = .4f, string tag = "Enimey")
    {
        var go = new GameObject("~waveHazard");
        go.tag = tag;
        go.transform.position = new Vector3(at.x, at.y, 0f);
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = Vector2.one * size;
        ClearTarget.Ensure(go).SetRadius(size * .5f);
        SpawnFootprint.Attach(go, Vector2.one * size * .5f);
        made.Add(go);
        return go;
    }

    static int Release() { return ShieldShockwave.Release(Ship, HullHalf); }

    // Running frames of the shoves alone.
    static void Run(float seconds)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt) EnemyShove.Step(Dt);
    }

    // ---- 1 -------------------------------------------------------------------

    static void TheShieldEndingFiresIt()
    {
        Fresh();
        ShieldShockwave.ResetGuards();
        var hull = new GameObject(ShipId.ObjectName(ShipId.Starter) + "(Clone)", typeof(SpriteRenderer));
        hull.transform.position = new Vector3(Ship.x, Ship.y, 0f);
        hull.GetComponent<SpriteRenderer>().sprite = ShipHullArt.Rest(ShipId.Starter);
        float scale = shopingShips.NormalizedHullScale(ShipHullArt.Rest(ShipId.Starter));
        hull.transform.localScale = new Vector3(scale, scale, 1f);
        made.Add(hull);
        var near = Hazard(Ship + new Vector2(.7f, .5f));
        var shield = ShipShield.For(hull);
        shield.remainingOverride = 5f;

        int before = ShieldShockwave.Releases;
        shield.Hide();
        Check("a shield that never came up releases nothing", ShieldShockwave.Releases == before);

        shield.Show();
        for (int i = 0; i < 40; i++) shield.Tick(Dt);
        Check("raising the shield and holding it releases nothing", ShieldShockwave.Releases == before && EnemyShove.Active == 0);
        Vector3 was = near.transform.position;
        shield.Hide();   // collisionDetection.turnTextsOff, when the 5.8 s run out
        Check("the shield running out fires the shockwave, once", ShieldShockwave.Releases == before + 1);
        Check("... with the hull's own width as the column (" + ShieldShockwave.LastColumnHalf.ToString("F2") + " u half, hull " +
              (shopingShips.ReferenceHullSize * .5f).ToString("F2") + " + margin " + ShieldShockwave.ColumnMargin + ")",
              ShieldShockwave.LastColumnHalf > ShieldShockwave.ColumnMargin + .1f &&
              ShieldShockwave.LastColumnHalf < shopingShips.ReferenceHullSize * .5f + ShieldShockwave.ColumnMargin + .05f);
        shield.Hide();
        Check("... and not again while it is down", ShieldShockwave.Releases == before + 1);

        var fx = ShieldShockwaveFx.Instance;
        Check("the ring and the column streak show on the release frame", fx != null && fx.RingShowing && fx.StreakShowing);
        var tint = fx != null ? fx.RingTint : Color.clear;
        Check("the ring is shield cyan, not the player's red (" + ColorUtility.ToHtmlStringRGB(tint) + ")",
              tint.b > .8f && tint.g > .8f && tint.r < .6f);
        float widest = 0f;
        bool streakUp = false;
        for (int i = 0; i < 40 && fx != null; i++)
        {
            fx.Tick(Dt);
            if (fx.RingShowing) widest = Mathf.Max(widest, fx.RingWorldRadius);
            if (fx.StreakShowing) streakUp |= fx.StreakBounds.max.y > CameraFit.ViewTop - .6f && Mathf.Abs(fx.StreakBounds.center.x - Ship.x) < .01f;
        }
        Check("the ring grows to the push radius (" + widest.ToString("F2") + " of " + ShieldShockwave.Radius + " u)",
              widest > ShieldShockwave.Radius * .9f && widest <= ShieldShockwave.Radius * 1.02f);
        Check("the streak runs up the ship's column to the top of the view", streakUp);
        Check("both are gone when it is over", fx != null && !fx.RingShowing && !fx.StreakShowing);
        Check("the hazard beside the ship was shoved away by it", (near.transform.position - was).magnitude > .2f);
        int created = ShieldShockwaveFx.Created;
        shield.Show();
        shield.Hide();
        Check("a second release reuses the same ring and streak (nothing new is built)", ShieldShockwaveFx.Created == created);
    }

    // ---- 2 -------------------------------------------------------------------

    static void RadiusPushesOutward()
    {
        Fresh();
        // (none in the column: all beside or behind the ship)
        var close = Hazard(Ship + new Vector2(.75f, .1f));
        var mid = Hazard(Ship + new Vector2(-1.2f, -.3f));
        var below = Hazard(Ship + new Vector2(.1f, -.9f));
        var outside = Hazard(Ship + new Vector2(3.4f, .9f));
        var rock = Hazard(Ship + new Vector2(-.8f, .7f), .4f, "Astr");
        var all = new[] { close, mid, below, outside, rock };
        var start = new Vector2[all.Length];
        for (int i = 0; i < all.Length; i++) start[i] = all[i].transform.position;

        Release();
        Vector2 early = close.transform.position;
        Run(ShieldShockwave.PushSeconds * .5f);
        float half = ((Vector2)close.transform.position - start[0]).magnitude;
        Run(ShieldShockwave.PushSeconds);
        var moved = new Vector2[all.Length];
        for (int i = 0; i < all.Length; i++) moved[i] = (Vector2)all[i].transform.position - start[i];

        bool outward = true;
        foreach (int i in new[] { 0, 1, 2, 4 })
        {
            Vector2 away = (start[i] - Ship).normalized;
            outward &= moved[i].magnitude > .05f && Vector2.Dot(moved[i].normalized, away) > .99f;
        }
        Check("hazards inside the radius are pushed straight away from the ship (" + moved[0].magnitude.ToString("F2") + ", " +
              moved[1].magnitude.ToString("F2") + ", " + moved[2].magnitude.ToString("F2") + " u)", outward);
        Check("... a rock like any hazard (" + moved[4].magnitude.ToString("F2") + " u)", moved[4].magnitude > .05f);
        Check("the closer, the harder (" + moved[0].magnitude.ToString("F2") + " > " + moved[1].magnitude.ToString("F2") + ")",
              moved[0].magnitude > moved[1].magnitude + .05f);
        Check("no push is longer than RadialPush (" + ShieldShockwave.RadialPush + ")",
              moved[0].magnitude <= ShieldShockwave.RadialPush + 1e-3f);
        Check("a hazard outside the radius is not moved at all", moved[3] == Vector2.zero);
        Check("nothing jumps on the release frame; the push eases out (" + (half / Mathf.Max(1e-4f, moved[0].magnitude)).ToString("P0") +
              " of the way at half time)", early == start[0] && half > moved[0].magnitude * .7f && half < moved[0].magnitude);
        Check("the pushes are over after PushSeconds (" + EnemyShove.Active + " still active)", EnemyShove.Active == 0);
        Vector3 rest = close.transform.position;
        Run(.5f);
        Check("... and leave the hazard where they put it", close.transform.position == rest);
    }

    // ---- 3 -------------------------------------------------------------------

    static void ColumnPushesUpScreen()
    {
        Fresh();
        float top = CameraFit.ViewTop;
        var far = Hazard(new Vector2(Ship.x + .05f, top - .8f));                  // far up the ship's column
        var edge = Hazard(new Vector2(Ship.x + HullHalf + .2f, 1f));              // its body reaches into the strip
        var next = Hazard(new Vector2(Ship.x + 1.1f, top - .8f));                 // the neighbouring column
        var behind = Hazard(new Vector2(Ship.x, Ship.y - 3.4f));                  // same column, behind the ship
        var offTop = Hazard(new Vector2(Ship.x, top + 2f));                       // not on the playfield yet
        var all = new[] { far, edge, next, behind, offTop };
        var start = new Vector2[all.Length];
        for (int i = 0; i < all.Length; i++) start[i] = all[i].transform.position;
        Check("the far hazard is well outside the radius (" + (start[0] - Ship).magnitude.ToString("F1") + " u away)",
              (start[0] - Ship).magnitude > ShieldShockwave.Radius * 2f);

        Release();
        Run(ShieldShockwave.PushSeconds + .1f);
        var moved = new Vector2[all.Length];
        for (int i = 0; i < all.Length; i++) moved[i] = (Vector2)all[i].transform.position - start[i];
        Check("a hazard far up the ship's column is pushed straight up-screen by ColumnPush (" + moved[0].y.ToString("F2") + " u)",
              Mathf.Abs(moved[0].x) < 1e-4f && Mathf.Abs(moved[0].y - ShieldShockwave.ColumnPush) < 1e-3f);
        Check("... and one whose body overlaps the strip", Mathf.Abs(moved[1].y - ShieldShockwave.ColumnPush) < 1e-3f);
        Check("the neighbouring column is not moved", moved[2] == Vector2.zero);
        Check("a hazard behind the ship in the same column is not moved", moved[3] == Vector2.zero);
        Check("a hazard still above the view is not moved", moved[4] == Vector2.zero);
    }

    // ---- 4 -------------------------------------------------------------------

    static void StepBrain(EnemyBrain brain)
    {
        clock += Dt;
        SpawnSpace.ClockOverride = clock;
        EnemyShove.Step(Dt);            // ShieldShockwaveFx (-30) runs before the brain (-20)
        brain.Step(Dt);
        var mount = brain.GetComponent<RailMineMount>();
        if (mount != null) mount.SendMessage("LateUpdate");
    }

    // Hazards with a brain (rocks: an offset over a scrolling mover). The
    // shove moves the body and the pattern's base (EnemyBrain.Base) with it,
    // for good.
    static void HazardsKeepTheirBehaviour()
    {
        int tried = 0, shoved = 0, offLane = 0, stuck = 0, outOfEnvelope = 0;
        var bad = new List<string>();
        foreach (var def in EnemyRoster.All)
        {
            if (def.role == EnemyRole.Chaser || def.role == EnemyRole.Mine) continue;
            var b = EnemyBehaviours.For(def);
            if (b == null) continue;
            // against the right-hand rail, the ship just inside it: the push is into the wall
            foreach (float x in new[] { 1.9f, -.6f })
            {
                Fresh();
                ship.position = new Vector3(x - .7f, Ship.y, 0f);
                UnityEngine.Random.InitState(4242);   // the twin below rolls the same enemy
                var go = EnemyFactory.Create(def, new Vector3(x, Ship.y + .5f, 0f), Quaternion.identity);
                var brain = go.GetComponent<EnemyBrain>();
                if (brain == null || brain.IsPilot) { Object.DestroyImmediate(go); continue; }
                brain.TargetOverride = ship;
                for (int i = 0; i < 30; i++) StepBrain(brain);
                // what the same enemy does there left alone, for comparison
                float alone = 0f;
                {
                    UnityEngine.Random.InitState(4242);
                    var twin = EnemyFactory.Create(def, new Vector3(x, Ship.y + .5f, 0f), Quaternion.identity).GetComponent<EnemyBrain>();
                    twin.TargetOverride = ship;
                    for (int i = 0; i < 30; i++) twin.Step(Dt);
                    Vector2 was = twin.Offset;
                    for (int i = 0; i < 60 * 4; i++) { twin.Step(Dt); alone += (twin.Offset - was).magnitude; was = twin.Offset; }
                    Object.DestroyImmediate(twin.gameObject);
                }
                tried++;
                Vector2 baseBefore = brain.Base;
                float halfX = SpawnSpace.BodyHalf(def).x;
                float lane = SpawnLane.LaneHalf - halfX;
                bool wasInLane = Mathf.Abs(baseBefore.x) <= lane;
                ShieldShockwave.Release(ship.position, HullHalf);
                bool isShoved = EnemyShove.IsShoved(go.transform);
                float worst = 0f, travelled = 0f;
                Vector2 last = brain.Offset;
                bool envelope = true;
                for (int i = 0; i < 60 * 4; i++)
                {
                    StepBrain(brain);
                    if (wasInLane) worst = Mathf.Max(worst, Mathf.Abs(brain.Base.x) - lane);
                    travelled += (brain.Offset - last).magnitude;
                    last = brain.Offset;
                    envelope &= Mathf.Abs(brain.Offset.x) <= b.bandX + 1e-3f && brain.Offset.y <= b.Up + 1e-3f && brain.Offset.y >= -b.Down - 1e-3f;
                }
                Vector2 baseMoved = brain.Base - baseBefore;
                if (isShoved && baseMoved.magnitude > .05f) shoved++;
                if (worst > 1e-3f) { offLane++; bad.Add(def.key + "@" + x + " base " + worst.ToString("F3") + " past the lane"); }
                if (!envelope) { outOfEnvelope++; bad.Add(def.key + "@" + x + " left its envelope"); }
                if (alone > .1f && travelled < alone * .25f) { stuck++; bad.Add(def.key + "@" + x + " moved " + travelled.ToString("F2") + " of " + alone.ToString("F2")); }
                Object.DestroyImmediate(go);
            }
        }
        Check("every brained hazard beside the ship is shoved, its pattern's base moving with it for good (" + shoved + " of " + tried + ")",
              tried >= 20 && shoved == tried);
        Check("no shove carries a pattern's base past the lane, even pushed at the rail (" + offLane + ")", offLane == 0);
        Check("each keeps inside its behaviour's envelope afterwards (" + outOfEnvelope + " did not)", outOfEnvelope == 0);
        Check("each moving pattern carries on moving afterwards (" + stuck + " stuck) " + string.Join("; ", bad), stuck == 0);
    }


    // ---- 4b: pilots ------------------------------------------------------------

    // A pilot built above the view, as the spawner admits it (waiting).
    static EnemyBrain Pilot(EnemyDef def, float x, bool armed = false)
    {
        for (int k = 0; k < 200; k++)
        {
            var go = EnemyFactory.Create(def, new Vector3(x, CameraFit.ViewTop + PilotAirspace.WaitAbove, 0f), Quaternion.identity);
            var brain = go.GetComponent<EnemyBrain>();
            brain.TargetOverride = ship;
            if (!armed || brain.Armed) return brain;
            Object.DestroyImmediate(go);
        }
        return null;
    }

    // How far the pilot is from where its script wants it (its line).
    static Vector2 OffLine(EnemyBrain b)
    {
        return (Vector2)b.transform.position - (b.Base + b.Offset);
    }

    // Flown in until it is engaging, in view and on its line.
    static bool ToStation(EnemyBrain b)
    {
        for (int i = 0; i < 60 * 12; i++)
        {
            StepBrain(b);
            if (b.Stage == EnemyBrain.PilotStage.Engaging && !b.Displaced &&
                b.transform.position.y < CameraFit.ViewTop - .8f && OffLine(b).magnitude < .08f) return true;
            if (b.Stage == EnemyBrain.PilotStage.Gone) return false;
        }
        return false;
    }

    struct Shoved
    {
        public bool taken, held, noSnap, inLane, stationKept, back, scriptEnds;
        public float peak, backAfter;
    }

    // Releases the shield at `from` and follows the pilot through the push
    // and its flight back.
    static Shoved ShoveAndFollow(EnemyBrain b, Vector2 from)
    {
        var r = new Shoved { noSnap = true, inLane = true, stationKept = true };
        // (a pilot keeps its own body inside the lane by its collider's half width)
        float lane = Mathf.Max(SpawnLane.LaneHalf - b.Def.ColliderSize.x * .5f, Mathf.Abs(b.transform.position.x));
        float stationX = b.Base.x;
        ShieldShockwave.Release(from, HullHalf);
        r.taken = EnemyShove.IsShoved(b.transform);
        float last = OffLine(b).magnitude;
        int frames = Mathf.CeilToInt(ShieldShockwave.PushSeconds / Dt) + 1;
        for (int i = 0; i < frames + 60 * 4; i++)
        {
            StepBrain(b);
            if (b.Stage == EnemyBrain.PilotStage.Gone) { r.back = true; break; }
            float off = OffLine(b).magnitude;
            r.peak = Mathf.Max(r.peak, off);
            // never a snap: it closes on its line no faster than it flies (its pattern moves the line a little too)
            if (off < last - (EnemyBrain.ShoveReturnSpeed * Dt + .08f)) r.noSnap = false;
            last = off;
            if (Mathf.Abs(b.transform.position.x) > lane + 1e-3f) r.inLane = false;
            if (Mathf.Abs(b.Base.x - stationX) > 1e-4f) r.stationKept = false;
            if (i == frames - 1) r.held = off > r.peak * .8f;      // the whole push arrived
            if (i >= frames && !r.back && off < .08f && !b.Displaced) { r.back = true; r.backAfter = (i - frames + 1) * Dt; }
        }
        // ... and its script still runs to its end
        for (int i = 0; i < 60 * 30 && b.Stage != EnemyBrain.PilotStage.Gone; i++) StepBrain(b);
        r.scriptEnds = b.Stage == EnemyBrain.PilotStage.Gone;
        return r;
    }

    static void PilotsAreShovedAndReturn()
    {
        int pilots = 0, waitingMoved = 0, columnBad = 0, radialBad = 0, enteringBad = 0, entering = 0;
        float leastColumn = 99f, leastRadial = 99f, slowestBack = 0f, fastestBack = 99f;
        var bad = new List<string>();
        foreach (var def in EnemyRoster.All)
        {
            var beh = EnemyBehaviours.For(def);
            if (beh == null || !beh.IsPilot || def.role == EnemyRole.Chaser || def.role == EnemyRole.Mine) continue;
            pilots++;

            // waiting above the view, the ship right under its column: not in play, not moved
            Fresh();
            var b = Pilot(def, .4f);
            if (!b.IsPilot) { bad.Add(def.key + " is not flying as a pilot"); Object.DestroyImmediate(b.gameObject); continue; }
            Vector3 at = b.transform.position;
            ship.position = new Vector3(.4f, CameraFit.ViewTop - 1f, 0f);
            ShieldShockwave.Release(ship.position, HullHalf);
            for (int i = 0; i < 3; i++) EnemyShove.Step(Dt);
            if (b.Stage != EnemyBrain.PilotStage.Waiting || EnemyShove.IsShoved(b.transform) || b.transform.position != at)
            { waitingMoved++; bad.Add(def.key + " moved while waiting"); }
            EnemyShove.Clear();
            ship.position = new Vector3(Ship.x, Ship.y, 0f);

            // on its way in (pilots with a station: an alien line has none): shoved, and it still arrives
            if (beh.entry != PilotEntry.Descend)
            {
                bool isEntering = false;
                for (int i = 0; i < 60 * 6 && !isEntering; i++)
                {
                    StepBrain(b);
                    isEntering = b.Stage == EnemyBrain.PilotStage.Entering && b.transform.position.y < CameraFit.ViewTop - .3f;
                }
                if (isEntering)
                {
                    entering++;
                    Vector2 p = b.transform.position;
                    ShieldShockwave.Release(new Vector2(p.x, p.y - 3f), HullHalf);
                    bool taken = EnemyShove.IsShoved(b.transform);
                    float peak = 0f;
                    for (int i = 0; i < 20; i++) { StepBrain(b); peak = Mathf.Max(peak, OffLine(b).magnitude); }
                    bool arrives = ToStation(b);
                    if (!taken || peak < .5f || !arrives)
                    { enteringBad++; bad.Add(def.key + " entering: taken " + taken + " peak " + peak.ToString("F2") + " arrives " + arrives); }
                }
            }
            Object.DestroyImmediate(b.gameObject);

            // on station in the ship's column, far outside the radius: up-screen by ColumnPush, then back
            Fresh();
            b = Pilot(def, .4f);
            if (!ToStation(b)) { columnBad++; bad.Add(def.key + " never reached its station"); Object.DestroyImmediate(b.gameObject); continue; }
            {
                Vector2 p = b.transform.position;
                ship.position = new Vector3(p.x + .05f, p.y - 3f, 0f);
                var r = ShoveAndFollow(b, ship.position);
                leastColumn = Mathf.Min(leastColumn, r.peak);
                if (r.back && r.backAfter > 0f) { slowestBack = Mathf.Max(slowestBack, r.backAfter); fastestBack = Mathf.Min(fastestBack, r.backAfter); }
                if (!r.taken || r.peak < ShieldShockwave.ColumnPush * .85f || !r.held || !r.noSnap || !r.inLane || !r.stationKept || !r.back || !r.scriptEnds)
                {
                    columnBad++;
                    bad.Add(def.key + " column: taken " + r.taken + " peak " + r.peak.ToString("F2") + " held " + r.held + " noSnap " + r.noSnap +
                            " lane " + r.inLane + " station " + r.stationKept + " back " + r.back + " ends " + r.scriptEnds);
                }
            }
            Object.DestroyImmediate(b.gameObject);

            // on station by the right-hand rail, the ship just inside it: pushed at the wall
            Fresh();
            b = Pilot(def, 1.75f);
            if (!ToStation(b)) { radialBad++; bad.Add(def.key + " never reached its station by the rail"); Object.DestroyImmediate(b.gameObject); continue; }
            {
                Vector2 p = b.transform.position;
                ship.position = new Vector3(p.x - .75f, p.y - .45f, 0f);
                var r = ShoveAndFollow(b, ship.position);
                leastRadial = Mathf.Min(leastRadial, r.peak);
                if (!r.taken || r.peak < .12f || !r.noSnap || !r.inLane || !r.stationKept || !r.back || !r.scriptEnds)
                {
                    radialBad++;
                    bad.Add(def.key + " radial: taken " + r.taken + " peak " + r.peak.ToString("F2") + " noSnap " + r.noSnap +
                            " lane " + r.inLane + " station " + r.stationKept + " back " + r.back + " ends " + r.scriptEnds);
                }
            }
            Object.DestroyImmediate(b.gameObject);
        }
        string notes = bad.Count == 0 ? "" : " " + string.Join("; ", bad);
        Check("a pilot still waiting above the view is not moved, the ship right under its column (" + waitingMoved + " of " + pilots + " moved)",
              pilots >= 24 && waitingMoved == 0);
        Check("a pilot on its way in is shoved and still reaches its station (" + (entering - enteringBad) + " of " + entering + ")",
              entering >= 12 && enteringBad == 0);
        Check("a pilot hovering in the ship's column, far outside the radius, is pushed up-screen by ColumnPush (least " +
              leastColumn.ToString("F2") + " of " + ShieldShockwave.ColumnPush + " u), with its station where it was (" + columnBad + " of " + pilots + " wrong)",
              columnBad == 0);
        Check("... and flies back to its station over time, never a snap (back on its line " + fastestBack.ToString("F2") + " to " +
              slowestBack.ToString("F2") + " s after the push)", columnBad == 0 && fastestBack >= .15f && slowestBack <= 3f);
        Check("a pilot beside the ship by the rail is pushed away (least " + leastRadial.ToString("F2") + " u), never past the lane, and returns (" +
              radialBad + " of " + pilots + " wrong)", radialBad == 0);
        Check("every shoved pilot's script still runs to its exit" + notes, bad.Count == 0);
    }

    // A shove does not touch the attack state machine: a windup in progress
    // carries on and fires.
    static void AShovedPilotsWindupContinues()
    {
        int tried = 0, kept = 0, fired = 0;
        EnemyThreat.ForceShooting = true;
        foreach (var def in EnemyRoster.All)
        {
            var beh = EnemyBehaviours.For(def);
            if (beh == null || !beh.IsPilot || !beh.Shoots || def.role == EnemyRole.Chaser) continue;
            Fresh();
            EnemyThreat.ForceShooting = true;
            var b = Pilot(def, .3f, true);
            if (b == null) continue;
            ship.position = new Vector3(.3f, CameraFit.ViewBottom + 1.2f, 0f);   // the pilot's ship, below it in its column
            bool winding = false;
            for (int i = 0; i < 60 * 14 && !winding && b.Stage != EnemyBrain.PilotStage.Gone; i++)
            {
                StepBrain(b);
                EliteSystem.Step(Dt);
                winding = b.State == EnemyBrain.Phase.Windup && b.StateTime > .1f;
            }
            if (!winding) { Object.DestroyImmediate(b.gameObject); continue; }
            tried++;
            int volleys = b.Volleys, windups = b.Windups;
            float told = b.StateTime;
            ShieldShockwave.Release(ship.position, HullHalf);
            bool shoved = EnemyShove.IsShoved(b.transform);
            StepBrain(b);
            if (shoved && b.State == EnemyBrain.Phase.Windup && b.StateTime > told && b.Windups == windups) kept++;
            for (int i = 0; i < 60 * 3 && b.Volleys == volleys; i++) { StepBrain(b); EliteSystem.Step(Dt); }
            if (b.Volleys > volleys && b.Windups == windups) fired++;
            Object.DestroyImmediate(b.gameObject);
        }
        EnemyThreat.ForceShooting = false;
        Check("a pilot shoved mid-windup keeps winding up (" + kept + " of " + tried + " shooting pilots)", tried >= 8 && kept == tried);
        Check("... and fires that same volley from where it was pushed to (" + fired + " of " + tried + ")", fired == tried);
    }

    static void ChasersComeBack()
    {
        Fresh();
        var def = EnemyRoster.One(0, EnemyRole.Chaser);
        var go = EnemyFactory.Create(def, new Vector3(Ship.x + .2f, Ship.y - 1.1f, 0f), Quaternion.identity);
        var c = go.GetComponent<ChaserEnemy>();
        c.Target = ship;
        for (int i = 0; i < 10; i++) c.Step(Dt);
        float before = Vector2.Distance(go.transform.position, Ship);
        Release();
        float knocked = before;
        for (int i = 0; i < 20; i++) { EnemyShove.Step(Dt); c.Step(Dt); knocked = Mathf.Max(knocked, Vector2.Distance(go.transform.position, Ship)); }
        float after = knocked;
        for (int i = 0; i < 90; i++) { EnemyShove.Step(Dt); c.Step(Dt); after = Mathf.Min(after, Vector2.Distance(go.transform.position, Ship)); }
        Check("a chaser beside the ship is knocked back (" + before.ToString("F2") + " -> " + knocked.ToString("F2") + " u)", knocked > before + .15f);
        Check("... and then closes in again (" + after.ToString("F2") + " u)", c.IsChasing && after < knocked - .3f);
        Object.DestroyImmediate(go);
    }

    // ---- 5 -------------------------------------------------------------------

    static void MinesStayOnTheirRail()
    {
        int mines = 0, slid = 0, offRail = 0, tooFar = 0, wrongWay = 0;
        foreach (var def in EnemyRoster.All)
        {
            if (def.role != EnemyRole.Mine) continue;
            var b = EnemyBehaviours.For(def.key);
            foreach (float x in new[] { -2.35f, 2.35f })
                foreach (float dy in new[] { .5f, -.5f })
                {
                    Fresh();
                    ship.position = new Vector3(x * .7f, Ship.y, 0f);
                    var rail = new GameObject("RailMineLane");
                    rail.transform.position = new Vector3(x, Ship.y + dy, 0f);
                    rail.AddComponent<RailLaneScroller>();
                    var go = EnemyFactory.Create(def, new Vector3(x, Ship.y + dy, 0f), Quaternion.identity);
                    var brain = go.GetComponent<EnemyBrain>();
                    var mount = go.AddComponent<RailMineMount>();
                    mount.MountTo(rail.transform);
                    if (brain != null) { mount.brain = brain; brain.TargetOverride = ship; }
                    mines++;
                    float worstRail = 0f, worstShove = 0f;
                    // several shields running out next to the same mine
                    for (int wave = 0; wave < 4; wave++)
                    {
                        ShieldShockwave.Release(ship.position, HullHalf);
                        for (int i = 0; i < 40; i++)
                        {
                            rail.transform.position += Vector3.down * .3f * Dt;   // the rail scrolls; the mine rides it
                            clock += Dt;
                            SpawnSpace.ClockOverride = clock;
                            EnemyShove.Step(Dt);
                            if (brain != null) brain.Step(Dt);
                            mount.SendMessage("LateUpdate");
                            worstRail = Mathf.Max(worstRail, mount.AlignmentError);
                            worstShove = Mathf.Max(worstShove, Mathf.Abs(mount.Shove));
                        }
                        if (wave == 0)
                        {
                            if (Mathf.Abs(mount.Shove) > .1f) slid++;
                            if (Mathf.Sign(mount.Shove) != Mathf.Sign(dy)) wrongWay++;
                        }
                        ship.position = new Vector3(x * .7f, rail.transform.position.y - dy, 0f);   // keep it beside the mine
                    }
                    if (worstRail > .015f) offRail++;
                    if (worstShove > ShieldShockwave.MineMaxSlide + 1e-3f) tooFar++;
                    float slide = brain != null && b != null ? Mathf.Max(mount.Slide - b.Up, -mount.Slide - b.Down) : 0f;
                    if (slide > 1e-3f) tooFar++;
                    Object.DestroyImmediate(go);
                    Object.DestroyImmediate(rail);
                }
        }
        Check("a mine beside the ship slides along its rail (" + slid + " of " + mines + ")", mines >= 8 && slid == mines);
        Check("... away from the ship (" + wrongWay + " the wrong way)", wrongWay == 0);
        Check("... and never leaves the rail's line (" + offRail + " off it)", offRail == 0);
        Check("... nor slides past MineMaxSlide from its clamp, however many shields end beside it (" + tooFar + ")", tooFar == 0);
    }

    // ---- 6 -------------------------------------------------------------------

    static EliteDef AnyElite()
    {
        foreach (var d in EliteCatalog.All) return d;
        return null;
    }

    static void BossesAndParkedElitesHold()
    {
        Fresh();
        // a boss's hitbox, right in the ship's column and inside the radius
        var boss = Hazard(Ship + new Vector2(0f, 1.2f), 1.2f);
        boss.AddComponent<BossTarget>();
        // a hostile shot's hitbox next to the ship
        var shotRoot = new GameObject("~waveBossShot");
        shotRoot.AddComponent<BossProjectile>();
        made.Add(shotRoot);
        var shot = Hazard(Ship + new Vector2(.6f, .4f), .2f);
        shot.transform.SetParent(shotRoot.transform, true);

        var def = AnyElite();
        var pad = new GameObject("~Pad").transform;
        pad.position = new Vector3(Ship.x + .5f, Ship.y + .8f, 0f);
        made.Add(pad.gameObject);
        var site = new LandingSite { anchor = pad, local = Vector3.zero, scale = .3f, order = -420, id = 1 };
        var parked = EliteShip.Create(def, site, 30f, new Vector2(-1.5f, 1.5f));
        EliteSystem.Step(Dt);

        Vector3 bossAt = boss.transform.position, shotAt = shot.transform.position, parkedAt = parked.transform.position;
        int moved = Release();
        Check("a boss, a hostile shot and a parked elite beside the ship: the release moves none of them (" + moved + ")", moved == 0);
        for (int i = 0; i < 30; i++) { EnemyShove.Step(Dt); EliteSystem.Step(Dt); }
        Check("a boss does not move", boss.transform.position == bossAt);
        Check("a hostile shot is left alone", shot.transform.position == shotAt);
        Check("a parked elite stays on its landing site", parked.State == EliteState.Parked &&
              Vector2.Distance(parked.transform.position, pad.position) < 1e-3f && parked.transform.position == parkedAt);

        // an elite in play, on a board of its own
        Fresh();
        var flying = EliteShip.CreateInPlay(def, Ship + new Vector2(.9f, .6f));
        flying.AttackCooldown = 99f;
        flying.Velocity = Vector2.zero;
        Vector2 flyingAt = flying.Position;
        Vector2 away = (flyingAt - Ship).normalized;
        moved = Release();
        Check("the release kicks the elite in play (" + ShieldShockwave.LastKicked + " kicked, " +
              ShieldShockwave.LastPushed + " pushed)", moved == 1 && ShieldShockwave.LastKicked == 1 && ShieldShockwave.LastPushed == 0);
        Check("an elite in play is kicked away from the ship (" + flying.Velocity.magnitude.ToString("F1") + " u/s)",
              flying.Velocity.magnitude > .3f && flying.Velocity.magnitude < 1.2f && Vector2.Dot(flying.Velocity.normalized, away) > .95f);
        float farthest = 0f;
        for (int i = 0; i < 30; i++)
        {
            EnemyShove.Step(Dt);
            EliteSystem.Step(Dt);
            farthest = Mathf.Max(farthest, Vector2.Distance(flying.Position, Ship) - Vector2.Distance(flyingAt, Ship));
        }
        Check("... and is carried off by it (" + farthest.ToString("F2") + " u further away), still in play, hearts intact",
              farthest > .03f && flying.InPlay && flying.Hearts == def.hearts);
        EliteSystem.Clear();
    }

    // ---- 7 -------------------------------------------------------------------

    static void BodiesStayApart()
    {
        Fresh();
        // a line of bodies leading away from the ship, a crowd up the column, one waiting just past the ring
        var bodies = new List<GameObject>();
        for (int i = 0; i < 4; i++) bodies.Add(Hazard(Ship + new Vector2(.55f + i * .46f, .25f + i * .2f)));
        for (int i = 0; i < 5; i++) bodies.Add(Hazard(new Vector2(Ship.x + (i % 2 == 0 ? .1f : -.1f), Ship.y + .7f + i * .5f)));
        bodies.Add(Hazard(new Vector2(Ship.x + .6f, Ship.y + 4f)));
        bodies.Add(Hazard(new Vector2(2.1f, Ship.y + .3f)));     // against the rail
        SpawnFootprint a, b;
        Check("the fixture starts with no two bodies overlapping", !SpawnSpace.AnyBodiesOverlap(out a, out b));
        var start = new List<Vector3>();
        foreach (var g in bodies) start.Add(g.transform.position);
        bool everOverlapAtRest = false;
        for (int wave = 0; wave < 3; wave++)
        {
            Release();
            Run(ShieldShockwave.PushSeconds + .1f);
            everOverlapAtRest |= SpawnSpace.AnyBodiesOverlap(out a, out b);
        }
        int movedCount = 0;
        float worstX = 0f;
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].transform.position != start[i]) movedCount++;
            worstX = Mathf.Max(worstX, Mathf.Abs(bodies[i].transform.position.x) + .2f - SpawnLane.LaneHalf);
        }
        // (the packed line's middle bodies have nowhere to go without landing
        // on the next one out, so they hold: staying apart wins over the push)
        Check("a crowd is still shoved where there is room (" + movedCount + " of " + bodies.Count + " moved)", movedCount >= 6);
        Check("three releases never leave two bodies overlapping", !everOverlapAtRest);
        Check("nothing is pushed into a rail (" + worstX.ToString("F3") + " u past the lane)", worstX <= 1e-3f);
    }

    // ---- 8 -------------------------------------------------------------------

    static void NothingMovesWhilePaused()
    {
        Fresh();
        var near = Hazard(Ship + new Vector2(.7f, .2f));
        var column = Hazard(new Vector2(Ship.x, Ship.y + 3f));
        var fx = ShieldShockwaveFx.Ensure();
        Vector3 nearAt = near.transform.position, columnAt = column.transform.position;

        // the shield runs out on the last running frame; the pilot lets go
        Release();
        score.pauseCounter = 3;
        Check("the pause rule reads as frozen (finger up, pauses left)", !ShieldShockwaveFx.Running);
        int active = EnemyShove.Active;
        for (int i = 0; i < 300; i++)
        {
            TestHarness.Send(fx, "LateUpdate");   // what Unity calls on a frozen frame
            fx.Tick(0f);                          // a zero-dt step
            EnemyShove.Step(0f);
        }
        Check("300 frozen frames: nothing has moved and the pushes are still waiting (" + EnemyShove.Active + ")",
              near.transform.position == nearAt && column.transform.position == columnAt && EnemyShove.Active == active && active == 2);
        Check("... the ring holds its drawing", fx.RingShowing);

        score.pauseCounter = 0;
        buttonClicks.playerDied = true;
        for (int i = 0; i < 60; i++) TestHarness.Send(fx, "LateUpdate");
        Check("a dead pilot: nothing moves either", !ShieldShockwaveFx.Running && near.transform.position == nearAt);
        buttonClicks.playerDied = false;

        Check("on resume the world runs again", ShieldShockwaveFx.Running);
        for (int i = 0; i < 30; i++) fx.Tick(Dt);
        Check("... and the push plays out in full (" + (near.transform.position - nearAt).magnitude.ToString("F2") + " u, column " +
              (column.transform.position.y - columnAt.y).ToString("F2") + " u)",
              (near.transform.position - nearAt).magnitude > .2f &&
              Mathf.Abs(column.transform.position.y - columnAt.y - ShieldShockwave.ColumnPush) < 1e-3f && EnemyShove.Active == 0);
    }

    // ---- 9 -------------------------------------------------------------------

    static void NoScoreNoKills()
    {
        Fresh();
        var bodies = new List<GameObject>();
        for (int i = 0; i < 6; i++) bodies.Add(Hazard(Ship + new Vector2(-1f + i * .4f, .6f + (i % 2) * .5f)));
        long total = RunScore.Total;
        int kills = EliteShip.Kills, live = ClearTarget.Count;
        float dust = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
        Release();
        Run(ShieldShockwave.PushSeconds + .1f);
        bool alive = true;
        foreach (var g in bodies) alive &= g != null && g.activeInHierarchy && g.GetComponent<ClearTarget>().enabled;
        Check("the push scores nothing (" + RunScore.Total + ") and pays no dust", RunScore.Total == total &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), dust));
        Check("... and destroys nothing: every hazard is still a live target (" + ClearTarget.Count + ")",
              alive && ClearTarget.Count == live && EliteShip.Kills == kills);
    }

    // ---- 10 ------------------------------------------------------------------

    static void ReleaseAllocatesNothing()
    {
        Fresh();
        ShieldShockwave.Prewarm();   // with the shield, as the ship spawns
        int roster = 0;
        foreach (var def in EnemyRoster.All)
        {
            if (roster >= 14 || def.role == EnemyRole.Chaser || def.role == EnemyRole.Mine) continue;
            var go = EnemyFactory.Create(def, new Vector3(-1.8f + (roster % 7) * .6f, Ship.y + .4f + (roster / 7) * 1.4f, 0f), Quaternion.identity);
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null) brain.TargetOverride = ship;
            roster++;
        }
        for (int i = 0; i < 10; i++) Hazard(new Vector2(-2f + i * .45f, Ship.y + 3.2f));
        var bodies = new List<Transform>();
        var home = new List<Vector3>();
        foreach (var target in ClearTarget.Live)
            if (ClearTarget.IsHazard(target.gameObject)) { bodies.Add(target.transform); home.Add(target.transform.position); }
        // warm: radii measured, the fx built
        Release();
        Run(ShieldShockwave.PushSeconds + .1f);
        int least = int.MaxValue;

        int created = ShieldShockwaveFx.Created, objects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
        object sink = null;
        bool leak = false;
        Action releases = () =>
        {
            for (int n = 0; n < 200; n++)
            {
                for (int i = 0; i < bodies.Count; i++) bodies[i].position = home[i];   // the same crowded board every time
                Release();
                if (ShieldShockwave.LastPushed < least) least = ShieldShockwave.LastPushed;
                for (int i = 0; i < 20; i++) EnemyShove.Step(Dt);
                if (leak) sink = new byte[32];
            }
        };
        releases();   // warm
        least = int.MaxValue;
        long control;
        bool meterWorks = TestHarness.AllocMeterWorks(out control);
        Check("the allocation meter passes its positive control (" + TestHarness.AllocControlCount + " small arrays read as " + control + " bytes)", meterWorks);
        long bytes = TestHarness.AllocatedBytes(releases);
        // The same stretch with one small array per release must read as
        // allocating. (The recorder under-reads -- a single stray array can
        // read 0 -- so what a zero rules out is an allocation per release.)
        leak = true;
        long withLeak = TestHarness.AllocatedBytes(releases);
        leak = false;
        Check("... and sees one 32-byte array per release dropped into the measured stretch (" + withLeak + " bytes read for 200)",
              withLeak >= 200 * 8 && sink != null);
        Check("200 releases with " + (roster + 10) + " hazards and pilots on the board, and their pushes, allocate nothing (" + bytes + " bytes)",
              meterWorks && bytes == 0);
        Check("... and create no objects (" + (Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length - objects) + ")",
              ShieldShockwaveFx.Created == created && Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length == objects);
        Check("every one of those releases shoved a busy board (at least " + least + " bodies, capacity " + EnemyShove.Capacity + ")",
              least > 5 && least <= EnemyShove.Capacity);
    }

    // ---- guards, feel ----------------------------------------------------------

    static void GuardsAndFeel()
    {
        Fresh();
        ShieldShockwave.ResetGuards();
        var inside = Hazard(Ship + new Vector2(1.9f, .6f));    // beyond the old 1.7 radius, inside 2.6
        var outside = Hazard(Ship + new Vector2(3.4f, .6f));   // outside both
        var inColumn = Hazard(Ship + new Vector2(0f, 4f));
        Vector3 outsideAt = outside.transform.position, insideAt = inside.transform.position, colAt = inColumn.transform.position;
        int hit = 0, moved = -9;
        System.Action<int> on = n => { hit++; moved = n; };
        AchievementEvents.ShieldReleased += on;
        try
        {
            Check("a shield held under " + ShieldShockwave.MinHeldSeconds + " s does not fire",
                  ShieldShockwave.TryRelease(Ship, HullHalf, .1f) == -1 && hit == 0);
            startMenu.youAreInTutorial = true;
            Check("the tutorial never fires it", ShieldShockwave.TryRelease(Ship, HullHalf, 3f) == -1 && hit == 0);
            startMenu.youAreInTutorial = false;
            ShieldShockwave.EntryOverride = true;
            Check("a world-entry sequence never fires it", ShieldShockwave.TryRelease(Ship, HullHalf, 3f) == -1 && hit == 0);
            ShieldShockwave.EntryOverride = null;
            Check("refusals are counted (" + ShieldShockwave.Suppressed + ")", ShieldShockwave.Suppressed == 3);

            int releases = ShieldShockwave.Releases;
            int r = ShieldShockwave.TryRelease(Ship, HullHalf, 3f);
            Check("a held shield fires (" + r + " moved)", r >= 2 && ShieldShockwave.Releases == releases + 1);
            Check("the achievements event carries the count once", hit == 1 && moved == r);
            Run(.5f);
            Check("radius " + ShieldShockwave.Radius + ": a body 2 u away is pushed, one 3.4 u away and the lane beside are not",
                  (inside.transform.position - insideAt).magnitude > .05f && outside.transform.position == outsideAt);
            Check("the column body is pushed up-screen", inColumn.transform.position.y > colAt.y + .5f);
            Check("... and stays below the top of the view", inColumn.transform.position.y < CameraFit.ViewTop + 1f);

            clock += .3f;
            Check("a second release inside the cooldown is refused", ShieldShockwave.TryRelease(Ship, HullHalf, 3f) == -1 && hit == 1);
            clock += .3f;
            Check("... and allowed after it", ShieldShockwave.TryRelease(Ship, HullHalf, 3f) >= 0 && hit == 2);
            Check("no damage by default", ShieldShockwave.ShockwaveDamage == 0f);
        }
        finally { AchievementEvents.ShieldReleased -= on; ShieldShockwave.Clock = null; }

        EnemyDeathAudio.ClearCache();
        Check("three authored whump variants load", EnemyDeathAudio.Variants(ShieldShockwave.SoundKey) == 3);
        for (int i = 0; i < 3; i++)
        {
            var c = EnemyDeathAudio.AuthoredClip(ShieldShockwave.SoundKey, i);
            Check("whump " + i + " is a short soft cue (" + (c != null ? c.length.ToString("F2") : "null") + " s)", c != null && c.length > .15f && c.length < .6f);
        }
        EnemyDeathAudio.ClearCache();
    }
}
