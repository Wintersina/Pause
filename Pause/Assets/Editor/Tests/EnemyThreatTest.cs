using UnityEditor.SceneManagement;
using UnityEngine;

// Enemy threat at high power (EnemyTierThreat, DroneHunters, DroneOrbit): tiered enemies fight back, hunters appear only at power, the
// squad orbits the ship. Pure rules first, then a few real enemies.
public static class EnemyThreatTest
{
    static int fails;
    static void Check(string label, bool ok)
    {
        Debug.Log((ok ? "[THREAT] PASS " : "[THREAT] FAIL ") + label);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }
    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try { Cap(); Tiers(); Loops(); Hunters(); Shots(); Colours(); Knock(); Orbit(); Live(); }
        finally
        {
            HeartTierRun.ForceTier = -1; HeartTierRun.Live = false; RunPower.Override = -1f;
            EnemyTierThreat.LoopOverride = -1; EnemyTierThreat.ClearCache(); DroneHunters.ResetAll(); DroneOrbit.Reset();
        }
        Debug.Log("[THREAT] failures: " + fails);
        return fails;
    }

    // The squad is capped at six: the constant, a saved or forced count above it, and every recruit / offer.
    static void Cap()
    {
        Check("the squad maximum is 6 (and the starting maximum stays 3)", DroneSquad.MaxSlots == 6 && DroneSquad.MaxStartingSlots == 3);
        DroneSquad.BeginRun(12);   // a run, save or tool asking for 12 starting slots
        Check("asking for 12 starting slots clamps to the starting cap", DroneSquad.Slots <= DroneSquad.MaxStartingSlots);
        DroneSquad.SetSlots(12);   // (what a restored squad of 12 would do)
        Check("a squad of 12 clamps to 6 slots", DroneSquad.Slots == 6);
        Check("the HUD and loadout sizes follow the cap", ShotAtomHud.MaxSlots == ShotAtomHud.Columns + 6 && ShotAtoms.MaxDrones == 6 && FloatieTuning.MaxFloaties == 6);
        Check("no recruit past six: a full squad offers none and keeps its size", DroneSquad.AddBossDrone() == -1 && DroneSquad.Slots == 6);
        DroneSquad.BeginRun(1);
    }

    static void Tiers()
    {
        bool same = true, blueFaster = true, crystalFaster = true, speedUp = true, any = false;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def);
            if (b == null || !b.IsPilot || !b.Shoots || b.IsAreaHazard) continue;
            any = true;
            var red = EnemyTierThreat.Tuned(def, b, 0);
            var blue = EnemyTierThreat.Tuned(def, b, 1);
            var cry = EnemyTierThreat.Tuned(def, b, 2);
            same &= red == b && red.cooldown == b.cooldown && red.shotSpeed == b.shotSpeed && red.shotCount == b.shotCount && red.firstDelay == b.firstDelay;
            // one path: the fire rate and shot speed are the shared PlayerPower.ThreatScale in EnemyBrain, so a tier no longer multiplies them here
            blueFaster &= blue.cooldown == red.cooldown && cry.cooldown == red.cooldown && blue.firstDelay == red.firstDelay;
            speedUp &= blue.shotSpeed == red.shotSpeed && cry.shotSpeed == red.shotSpeed;
        }
        Check("there are shooting pilots to tune", any);
        Check("red-only enemies keep their exact table row (same object, same fire rate)", same);
        Check("the tuned row keeps the table's own fire interval (the rate is PlayerPower.ThreatScale, not doubled here)", blueFaster && crystalFaster);
        Check("... and its own shot speed", speedUp);
        Check("table: tier interval / speed multipliers are all x1 (no double application)",
            Mathf.Approximately(EnemyTierThreat.Interval(1), 1f) && Mathf.Approximately(EnemyTierThreat.Interval(2), 1f) &&
            Mathf.Approximately(EnemyTierThreat.Speed(1), 1f) && Mathf.Approximately(EnemyTierThreat.Speed(2), 1f));
        bool capped = true;
        for (int t = 0; t <= 2; t++) capped &= EnemyTierThreat.Interval(t) >= EnemyTierThreat.MinIntervalMul && EnemyTierThreat.Speed(t) <= EnemyTierThreat.MaxSpeedMul;
        Check("loop 1 multipliers are capped", capped);
        var frost = EnemyRoster.Find("frost_fighter_2");   // Icicle: one aimed lance bolt
        var fb = EnemyBehaviours.For(frost);
        var crystal = EnemyTierThreat.Tuned(frost, fb, 2);
        Check("a crystal lone shot becomes a spread pair (one extra shot)", fb.attack == EnemyAttack.Shot && fb.shotCount == 1 && crystal.shotCount == 2 && crystal.shotSpread >= EnemyTierThreat.ExtraSpreadDegrees && EnemyTierThreat.Tuned(frost, fb, 1).shotCount == 1);
        var alien = EnemyRoster.One(1, EnemyRole.Alien);
        var ab = EnemyBehaviours.For(alien);
        var ac = EnemyTierThreat.Tuned(alien, ab, 2);
        Check("a crystal enemy that never shot gets a simple shot (frost alien)", !ab.Attacks && ac.attack == EnemyAttack.Shot && ac.armedChance >= 1f && EnemyTierThreat.Tuned(alien, ab, 1).attack == EnemyAttack.None);
    }

    static void Loops()
    {
        Check("loop 1 factor is 1", Mathf.Approximately(EnemyTierThreat.LoopFactor(0), 1f));
        Check("tier multipliers are x1 on loop 1", Mathf.Approximately(EnemyTierThreat.Interval(2, 0), 1f) && Mathf.Approximately(EnemyTierThreat.Speed(2, 0), 1f));
        Check("later loops are exponentially harder", EnemyTierThreat.LoopFactor(3) > EnemyTierThreat.LoopFactor(2) && EnemyTierThreat.LoopFactor(2) > EnemyTierThreat.LoopFactor(1) && EnemyTierThreat.LoopFactor(1) > 1f);
        bool ok = true, mono = true;
        float lastI = 9f, lastS = 0f;
        for (int loop = 0; loop <= 20; loop++)
        {
            float i = EnemyTierThreat.Interval(2, loop), s = EnemyTierThreat.Speed(2, loop);
            ok &= i >= EnemyTierThreat.LoopMinIntervalMul - 1e-5f && s <= EnemyTierThreat.LoopMaxSpeedMul + 1e-5f;
            mono &= i <= lastI + 1e-5f && s >= lastS - 1e-5f;
            ok &= DroneHunters.TelegraphSeconds(loop) >= .4f - 1e-5f;
            ok &= DroneHunters.MaxAlive(loop) <= DroneHunters.HardMaxAlive;
            ok &= DroneHunters.Share(1f, DroneSquad.MaxSlots, loop) <= DroneHunters.ShareCap + 1e-5f;
            lastI = i; lastS = s;
        }
        Check("at every loop to 20 the interval floor, speed cap, telegraph >= 0.4 s, hunter count and share caps hold", ok);
        Check("the tier multipliers stay x1 at every loop (the loop's pace is LoopDifficulty in EnemyBrain)", mono && Mathf.Approximately(EnemyTierThreat.Interval(2, 20), 1f) && Mathf.Approximately(EnemyTierThreat.Speed(2, 20), 1f));
        Check("at loop 20 the hunter caps are reached exactly", Mathf.Approximately(DroneHunters.TelegraphSeconds(20), .4f) && DroneHunters.MaxAlive(20) == DroneHunters.HardMaxAlive);
        Check("loop 1 hunter numbers are the base ones", DroneHunters.MaxAlive(0) == DroneHunters.BaseMaxAlive && Mathf.Approximately(DroneHunters.TelegraphSeconds(0), DroneHunters.BaseTelegraphSeconds));
        // the ordinary enemy's budget: tiered volleys reserve their extras, so the roster shot budget is still the one ceiling
        EnemyTierThreat.LoopOverride = 20;
        Check("the live enemy shot budget stays under its hard ceiling at loop 20", EnemyThreat.ShotBudget <= Mathf.Max(EnemyThreat.MaxEnemyShots, PortalPressure.ShotCap));
        EnemyTierThreat.LoopOverride = -1;
    }

    static void Hunters()
    {
        Check("no hunters at zero power", DroneHunters.Share(0f, DroneSquad.MaxSlots) == 0f && DroneHunters.Share(DroneHunters.StartPower, DroneSquad.MaxSlots) == 0f);
        bool mono = true, droneMono = true, cap = true;
        float last = 0f;
        for (int i = 0; i <= 100; i++)
        {
            float p = i / 100f, s = DroneHunters.Share(p, 6);
            mono &= s >= last - 1e-6f; last = s;
            cap &= s <= DroneHunters.MaxShare + 1e-5f;
        }
        for (int d = 0; d <= DroneSquad.MaxSlots; d++) droneMono &= DroneHunters.Share(.8f, d + 1) >= DroneHunters.Share(.8f, d) - 1e-6f;
        Check("hunter share grows monotonically with power", mono && last > .1f);
        Check("hunter share grows with the squad", droneMono);
        Check("loop 1 share never passes 20%", cap);
        Check("hunters never pass the alive cap on loop 1", DroneHunters.MaxAlive(0) == 3);
        Check("a diver's telegraph is at least 0.4 s", DroneHunters.MinTelegraphSeconds >= .4f && DroneHunters.TelegraphSeconds(0) >= .4f);
    }

    static void Shots()
    {
        var root = new GameObject("shots").transform;
        var pool = new EliteShots(root);
        int built = pool.All.Count;
        Check("enemy shots are pooled and prewarmed (built up front)", built == EliteShots.MaxShots);
        var def = EnemyRoster.One(0, EnemyRole.Fighter);
        var style = EnemyBehaviours.For(def).ShotStyle;
        for (int i = 0; i < 20; i++) pool.Fire(null, style, EliteShots.Kind.Bolt, Vector2.zero, Vector2.down);
        Check("firing never grows the pool", pool.All.Count == built);
        Object.DestroyImmediate(root.gameObject);
    }

    static void Colours()
    {
        bool clean = true;
        foreach (var def in EnemyRoster.All)
        {
            if (def.world <= 0) continue;
            var b = EnemyBehaviours.For(def);
            if (b == null) continue;
            for (int tier = 0; tier <= 2; tier++)
            {
                var t = EnemyTierThreat.Tuned(def, b, tier);
                clean &= !WorldCue.IsPink(t.ShotStyle.ShotColor);
            }
        }
        Check("no tiered or hunter shot is pink outside Space", clean && !WorldCue.IsPink(EnemyBehaviours.ShotColor(1)) && !WorldCue.IsPink(EnemyBehaviours.ShotColor(6)));
        Check("Space keeps its neon pink", WorldCue.IsPink(EnemyBehaviours.ShotColor(0)));
    }

    static void Knock()
    {
        DroneSquad.BeginRun(2);
        DroneSquad.Activate(0); DroneSquad.Activate(1);
        bool hit = DroneSquad.KnockOut(1, out bool lost);
        Check("a sniper hit on a level-1 drone takes it out of the squad (drone-loss path)", hit && lost && !DroneSquad.IsActive(1) && DroneSquad.IsKnockedOut(1) && DroneSquad.ActiveCount == 1);
        DroneSquad.TickKnockOuts(DroneSquad.KnockOutSeconds * .5f);
        Check("it is still gone half way", !DroneSquad.IsActive(1));
        DroneSquad.TickKnockOuts(DroneSquad.KnockOutSeconds);
        Check("it comes back through Activate", DroneSquad.IsActive(1) && !DroneSquad.IsKnockedOut(1) && DroneSquad.CoreLevel(1) == 1);
        DroneSquad.CollectCore(DroneMode.Aimed);   // level up the empty / lowest slot
        int slot = 0;
        for (int i = 0; i < 2; i++) if (DroneSquad.CoreLevel(i) > DroneSquad.CoreLevel(slot)) slot = i;
        int before = DroneSquad.CoreLevel(slot);
        bool h2 = DroneSquad.KnockOut(slot, out bool lost2);
        Check("a drone with a levelled core loses one level and stays", before < 2 || (h2 && !lost2 && DroneSquad.CoreLevel(slot) == before - 1 && DroneSquad.IsActive(slot)));
        Check("a hit on an empty slot does nothing", !DroneSquad.KnockOut(5, out bool _));
        DroneSquad.BeginRun(1);
    }

    static void Orbit()
    {
        var e = new Vector2(.45f, .55f);
        float size = e.y * .72f;
        bool even = true, grows = true, clear = true;
        float lastR = 0f;
        for (int n = 1; n <= DroneSquad.MaxSlots; n++)
        {
            DroneOrbit.Reset();
            int mask = (1 << n) - 1;
            DroneOrbit.Advance(0f, mask, n, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
            float r = DroneOrbit.Rx;
            grows &= r >= lastR - 1e-5f; lastR = r;
            clear &= r >= DroneOrbit.HullClear(e.x, e.y, size) - 1e-5f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = DroneOrbit.Local(i), b = DroneOrbit.Local((i + 1) % n);
                clear &= a.magnitude >= DroneOrbit.HullClear(e.x, e.y, size) - 1e-4f;
                if (n >= 2) even &= Mathf.Abs(Vector2.Distance(a, b) - 2f * r * Mathf.Sin(Mathf.PI / n)) < 1e-3f;
                if (n >= 2) even &= Vector2.Distance(a, b) >= size * DroneOrbit.ChordClearance - 1e-3f;
            }
        }
        Check("drones sit evenly spaced (equal chords, none touching) for 1..max", even);
        Check("the ring never shrinks as the squad grows", grows);
        DroneOrbit.Reset();
        DroneOrbit.Advance(0f, (1 << DroneSquad.MaxSlots) - 1, DroneSquad.MaxSlots, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        float r12 = DroneOrbit.Rx;
        DroneOrbit.Reset();
        DroneOrbit.Advance(0f, 1, 1, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        Check("the ring is wider with a full squad than with one drone", r12 > DroneOrbit.Rx);
        Check("no drone inside the hull's clearance at any count", clear);

        DroneOrbit.Reset();
        DroneOrbit.Advance(0f, 7, 3, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        float a0 = DroneOrbit.Angle;
        DroneOrbit.Advance(1f, 7, 3, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        float turned = Mathf.Abs(Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, DroneOrbit.Angle * Mathf.Rad2Deg));
        Check("the shared angle advances: a full turn takes about 7 s", Mathf.Abs(turned - 360f / DroneOrbit.TurnSeconds) < .1f);
        for (int i = 0; i < 70; i++) DroneOrbit.Advance(.1f, 7, 3, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        Check("after seven seconds it is back where it began", Mathf.Abs(Mathf.DeltaAngle((a0 + Mathf.PI * 2f / DroneOrbit.TurnSeconds) * Mathf.Rad2Deg, DroneOrbit.Angle * Mathf.Rad2Deg)) < 1f);

        // a drone leaves: the others ease, not snap, then settle on the new even spacing
        DroneOrbit.Reset();
        DroneOrbit.Advance(0f, 7, 3, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        float p2 = DroneOrbit.Phase(2), want = DroneOrbit.PhaseFor(1, 2);
        DroneOrbit.Advance(1f / 60f, 3, 3, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));   // slot 2 knocked out
        float moved = Mathf.Abs(Mathf.DeltaAngle(p2 * Mathf.Rad2Deg, DroneOrbit.Phase(1) * Mathf.Rad2Deg));
        float jump = Mathf.Abs(Mathf.DeltaAngle(DroneOrbit.PhaseFor(1, 3) * Mathf.Rad2Deg, want * Mathf.Rad2Deg));
        float stepped = Mathf.Abs(Mathf.DeltaAngle(DroneOrbit.PhaseFor(1, 3) * Mathf.Rad2Deg, DroneOrbit.Phase(1) * Mathf.Rad2Deg));
        Check("a leaving drone: the others move toward the new spacing but not instantly", stepped > 0f && stepped < jump && moved >= 0f);
        for (int i = 0; i < 120; i++) DroneOrbit.Advance(1f / 60f, 3, 3, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        Check("and settle on the even spacing", Mathf.Abs(Mathf.DeltaAngle(DroneOrbit.Phase(1) * Mathf.Rad2Deg, want * Mathf.Rad2Deg)) < 1f);
        // a joining drone: the first one eases, the radius grows smoothly
        DroneOrbit.Advance(1f / 60f, 15, 4, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        float step1 = Mathf.Abs(Mathf.DeltaAngle(DroneOrbit.Phase(1) * Mathf.Rad2Deg, DroneOrbit.PhaseFor(1, 4) * Mathf.Rad2Deg));
        Check("a joining drone: the others ease (not at their new place yet)", step1 > .5f);
        // near an edge the ring narrows but never under the hull clearance, and Clamp keeps a body in view
        DroneOrbit.Reset();
        var view = new Rect(-2f, -4f, 4f, 8f);
        DroneOrbit.Advance(0f, 0xFF, 8, e, size, 0f, new Vector2(1.8f, 0f), 1f, view);
        var c = DroneOrbit.Clamp(new Vector3(5f, 9f, 0f), new Vector2(.3f, .3f), view);
        Check("a drone body is kept inside the view", c.x <= view.xMax && c.y <= view.yMax);
        DroneOrbit.Reset();
        Separation();
        Edges();
    }

    // Drones vs hearts: the hearts live inside their orbit radius H (+ a loop and half a heart); sampled over a full turn, for 1..max
    // drones and a range of hearts' radii, no drone body comes nearer to the ship than H + half a drone.
    static void Separation()
    {
        var e = new Vector2(.45f, .55f);
        float size = e.y * .72f;
        bool apart = true;
        foreach (float h in new[] { 0f, .9f, 1.3f, 1.8f })
        {
            float heartReach = h > 0f ? h + .1f + .11f : 0f;
            for (int n = 1; n <= DroneSquad.MaxSlots; n++)
            {
                DroneOrbit.Reset();
                int mask = (1 << n) - 1;
                DroneOrbit.Advance(0f, mask, n, e, size, heartReach, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
                for (int step = 0; step < 140; step++)
                {
                    DroneOrbit.Advance(DroneOrbit.TurnSeconds / 140f, mask, n, e, size, heartReach, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
                    for (int i = 0; i < n; i++) apart &= DroneOrbit.Local(i).magnitude >= heartReach + size * .5f - 1e-3f;
                }
            }
        }
        Check("drones keep clear of the hearts' orbit at every count over a full turn", apart);
        // and the battery feeds the hearts' LIVE reach: a bigger heart orbit pushes the ring out
        DroneOrbit.Reset();
        DroneOrbit.Advance(0f, 7, 3, e, size, 0f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        float plain = DroneOrbit.Rx;
        DroneOrbit.Reset();
        DroneOrbit.Advance(0f, 7, 3, e, size, 3f, Vector2.zero, 1f, new Rect(0, 0, 0, 0));
        Check("a wider heart orbit widens the drone ring", DroneOrbit.Rx > plain);
        DroneOrbit.Reset();
    }

    // The ship hugging either edge, on a narrow cover and a wide screen: every drone, over a full turn, stays inside the view and
    // outside the hull rectangle (and outside the hearts' reach).
    static void Edges()
    {
        var e = new Vector2(.45f, .55f);
        float size = e.y * .72f, hearts = 1.0f;
        bool ok = true, inView = true;
        // view half width x half height: foldable cover 1080x2520 (ortho 8.7 tall -> 3.7 wide), phone 9:16, a wide 16:9
        foreach (var half in new[] { new Vector2(1.85f, 4.35f), new Vector2(2.4f, 4.3f), new Vector2(3.9f, 2.2f) })
        {
            var view = new Rect(-half.x, -half.y, half.x * 2f, half.y * 2f);
            foreach (float shipX in new[] { -half.x + e.x, 0f, half.x - e.x })
            foreach (float shipY in new[] { -half.y + e.y, 0f, half.y - e.y })
            for (int n = 1; n <= DroneSquad.MaxSlots; n += 2)
            {
                DroneOrbit.Reset();
                int mask = (1 << n) - 1;
                var ship = new Vector2(shipX, shipY);
                DroneOrbit.Advance(0f, mask, n, e, size, hearts, ship, 1f, view);
                for (int step = 0; step < 140; step++)
                {
                    DroneOrbit.Advance(DroneOrbit.TurnSeconds / 140f, mask, n, e, size, hearts, ship, 1f, view);
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 l = DroneOrbit.Local(i);
                        // never over the hull rectangle (a drone's half size around it)
                        ok &= Mathf.Abs(l.x) >= e.x + size * .5f - 1e-3f || Mathf.Abs(l.y) >= e.y + size * .5f - 1e-3f;
                        ok &= l.magnitude >= hearts + size * .5f - 1e-3f;
                        Vector2 w = ship + l;
                        inView &= w.x >= view.xMin + size * .5f - .06f && w.x <= view.xMax - size * .5f + .06f && w.y >= view.yMin + size * .5f - .06f && w.y <= view.yMax - size * .5f + .06f;
                    }
                }
            }
        }
        Check("with the ship at any edge or corner of a narrow, phone or wide view, no drone covers the hull or the hearts", ok);
        Check("and every drone stays inside the view", inView);
        DroneOrbit.Reset();
    }

    static void Live()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
        cam.tag = "MainCamera"; cam.orthographic = true; cam.orthographicSize = 5f; cam.aspect = .5625f;
        cam.transform.position = new Vector3(0, 0, -10);
        buttonClicks.playerDied = false; startMenu.youAreInTutorial = false; Time.timeScale = 1f;
        RunScore.EndRun(RunScore.RunId); RunScore.BeginRun(true, true);
        DroneHunters.ResetAll();

        HeartTierRun.ForceTier = 2;
        RunPower.Override = 0f;
        var fighter = EnemyRoster.One(1, EnemyRole.Fighter);
        var go = EnemyFactory.Create(fighter, Vector3.up * 8f, Quaternion.identity, 1f, true);
        var brain = go.GetComponent<EnemyBrain>();
        Check("a crystal enemy flies a tuned copy of its row", brain != null && brain.Tier == 2 && brain.Behaviour != EnemyBehaviours.For(fighter));
        Check("no hunter is made at zero power", DroneHunters.Alive == 0 && go.GetComponent<DroneHunter>() == null);

        HeartTierRun.ForceTier = 1;
        var alien = EnemyFactory.Create(EnemyRoster.One(1, EnemyRole.Alien), Vector3.up * 8f, Quaternion.identity, 1f, true);
        Check("a blue enemy that never shot still does not", !alien.GetComponent<EnemyBrain>().Armed);
        HeartTierRun.ForceTier = 2;
        var alien2 = EnemyFactory.Create(EnemyRoster.One(1, EnemyRole.Alien), Vector3.up * 8f, Quaternion.identity, 1f, true);
        Check("a crystal frost alien is armed with its simple shot", alien2.GetComponent<EnemyBrain>().Armed && alien2.GetComponent<EnemyBrain>().Behaviour.Shoots);

        RunPower.Override = 1f;
        DroneHunters.RollOverride = 0f;
        for (int i = 0; i < 10; i++) EnemyFactory.Create(fighter, new Vector3(i * .1f, 8f, 0f), Quaternion.identity, 1f, true);
        Check("hunters appear at full power but never more than the cap", DroneHunters.Alive > 0 && DroneHunters.Alive <= DroneHunters.MaxAlive(0));
        var hunter = Object.FindFirstObjectByType<DroneHunter>();
        Check("a hunter's own brain is switched off (it flies its own script)", hunter != null && !hunter.GetComponent<EnemyBrain>().enabled);
        DroneHunters.ResetAll();
        RunPower.Override = -1f;
    }
}
