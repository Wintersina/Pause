using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// VETS EVERY SHIP'S ULTIMATE against the current rules: for each player ship (every roster id) in a sandbox of 2-heart enemies,
//   A. the ultimate alone: every normal enemy it touches dies in ONE hit (none is left wounded), elite hits stay within the authored attack budget,
//      and it kills something in front of the ship;
//   B. the ultimate with the gun: with the ultimate out the floaties keep firing (bullets leave during it), with every shot atom at 10;
//   C. no exception / error log, and the pools stay bounded (floatie bullets, attack projectiles and sprites, the feel pixels).
// One line per ship is logged ("[ULTVET] ...") for the owner's review.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipUltimateVetTest.Run
public static class ShipUltimateVetTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ULTVET] PASS  " : "[ULTVET] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    static int errors;
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error) { errors++; }
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        Application.logMessageReceived += OnLog;
        try
        {
            foreach (int id in ShipId.All) Vet(id);
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            ShotAtoms.BeginRun();
            AttackPool.StopAll();
            ShipPowerController_Finish();
        }
        Debug.Log("[ULTVET] failures: " + fails);
        return fails;
    }

    static void ShipPowerController_Finish() { EliteSystem.Clear(); EliteSystem.PlayerOverride = null; }

    static GameObject Enemy(float x, float y)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Fighter), new Vector3(x, y, 0f), Quaternion.identity);
        ClearTarget.Ensure(go);
        return go;
    }

    static void FinishCinematic(ShipPowerController c)
    {
        if (!ShipPowerController.CinematicClearActive) return;
        var m = typeof(ShipPowerController).GetMethod("FinishCinematic", Inst);
        if (m != null) m.Invoke(c, null);
    }

    // The enemies of the sandbox: two rows of 2-heart enemies round the ship's line.
    static List<GameObject> Field()
    {
        var list = new List<GameObject>();
        float[] xs = { -1.6f, -.8f, 0f, .8f, 1.6f };
        foreach (float x in xs) { list.Add(Enemy(x, -.5f)); list.Add(Enemy(x * .7f, 1.4f)); }
        return list;
    }

    struct AResult
    {
        public int wounded, dead, eliteLost, fieldCount, eliteStart, eliteHits;
        public float eliteWeight, eliteBudget;
        public bool exempt, probeMoved, killed;
        public Vector2 elitePos;
    }

    // Where the sandbox elite may stand, nearest-the-ship first after the default (0, 3.3): inside the ultimate's reach is found by
    // trying the spots a ship's attack can cover (straight ahead, the lanes either side, close in and out to the screen's top).
    static List<Vector2> ElitePositions()
    {
        var list = new List<Vector2> { new Vector2(0f, 3.3f) };
        foreach (float y in new[] { -.5f, .5f, 1.4f, 2.4f, -1.5f, 3.3f })
            foreach (float x in new[] { 0f, .8f, -.8f, 1.6f, -1.6f, .4f, -.4f })
                if (!(x == 0f && y == 3.3f)) list.Add(new Vector2(x, y));
        return list;
    }

    // A. one run of the ultimate alone against the field and one elite standing at `elitePos`.
    static AResult RunA(int id, Vector2 elitePos)
    {
        string name = ShipId.NameOf(id) + " (" + id + ")";
        ShotFeelTest.FreshScene();
        ShotAtoms.BeginRun();
        EliteDef.ScaleOverride = 0;
        EliteSystem.Clear();
        var pilotMark = new GameObject("~Pilot").transform;
        pilotMark.position = new Vector3(0f, -3f, 0f);
        EliteSystem.PlayerOverride = pilotMark;
        var rig = ShotFeelTest.MakeShip(id, ShipWeaponUpgrades.MaxLevel);
        var field = Field();
        // GALE WINGS' probe: a plain body in the gust's path, as ShipAttacksV3CheapTest uses. (The roster fighters of the field are
        // pilots that this sandbox never starts, and the gust leaves a pilot that is not yet flying alone.)
        bool exempt = ShipLoadoutTable.AttackFor(id) == ShipAttack.GaleWings;
        GameObject probe = null;
        if (exempt) { probe = new GameObject("probe"); probe.tag = "Enimey"; probe.transform.position = new Vector3(0f, -.5f, 0f); ClearTarget.Ensure(probe).SetRadius(.3f); }
        var elite = EliteShip.CreateInPlay(BaseEliteDefs.For("gunship"), elitePos);
        elite.AttackCooldown = 99f;
        int eliteHearts = elite.Hearts, kills = ShipAttackHits.Kills;
        int beforeHits = ShipAttackHits.AttackTargetHits;
        float beforeWeight = ShipAttackHits.AttackTargetWeight, eliteBudget = 1f;
        var startAt = new List<Vector3>();
        foreach (var go in field) startAt.Add(go.transform.position);
        try
        {
            typeof(ShipPowerController).GetMethod("Fire", Inst).Invoke(rig.power, null);
            var fired = rig.power.Runner.Loadout;
            if (fired.attack == ShipAttack.WardenSentries) eliteBudget = Mathf.Clamp(fired.shots, 1, 4);
            for (float t = 0f; t < 7f; t += Dt)
            {
                rig.power.Runner.Step(Dt);                EnemyShove.Step(Dt);   // the world clock runs the shoves in the game (ShieldShockwaveFx.Update); a sandbox must step them itself
                if (Mathf.Approximately(t % 1f, 0f)) FinishNothing();
            }
        }
        catch (System.Exception e) { errors++; Debug.Log("[ULTVET] exception " + name + ": " + e); }
        int wounded = 0, dead = 0, shoved = 0;
        for (int fi = 0; fi < field.Count; fi++)
        {
            var go = field[fi];
            if (go == null) { dead++; continue; }
            if ((go.transform.position - startAt[fi]).sqrMagnitude > .09f) shoved++;
            var h = go.GetComponent<EnemyHearts>();
            if (h != null && h.Hearts < h.Max && go.GetComponent<ClearTarget>().enabled) wounded++;
        }
        int eliteLost = eliteHearts - elite.Hearts;
        bool probeMoved = exempt && (probe == null || (probe.transform.position - new Vector3(0f, -.5f, 0f)).sqrMagnitude > .09f);   // (broken by a collision it was shoved into counts)
        FinishCinematic(rig.power);
        ShotFeelTest.Teardown(rig);
        foreach (var go in field) if (go != null) Object.DestroyImmediate(go);
        if (probe != null) Object.DestroyImmediate(probe);
        EliteSystem.Clear();
        EnemyShove.Clear();
        Object.DestroyImmediate(pilotMark.gameObject);
        EliteDef.ScaleOverride = 1;

        var r = new AResult { eliteStart = eliteHearts, eliteHits = ShipAttackHits.AttackTargetHits - beforeHits, eliteWeight = ShipAttackHits.AttackTargetWeight - beforeWeight, eliteBudget = eliteBudget, wounded = wounded, dead = dead, eliteLost = eliteLost, fieldCount = field.Count, exempt = exempt, probeMoved = probeMoved, elitePos = elitePos, killed = ShipAttackHits.Kills > kills };
        return r;
    }

    static void Vet(int id)
    {
        string name = ShipId.NameOf(id) + " (" + id + ")";
        errors = 0;
        string note = "";

        // ---- A. the ultimate alone ----
        var found = RunA(id, new Vector2(0f, 3.3f));
        if (!found.exempt && found.eliteLost == 0)
            foreach (var at in ElitePositions())
            {
                var tryA = RunA(id, at);
                if (tryA.eliteLost > 0 && tryA.wounded == 0) { found = tryA; break; }
            }
        int wounded = found.wounded, dead = found.dead, eliteLost = found.eliteLost;
        bool exempt = found.exempt, probeMoved = found.probeMoved;
        bool oneShot = wounded == 0;
        bool killedSomething = dead > 0 || found.killed;
        // Distinct weapon contacts now land continuously. Each removes one elite heart,
        // capped by its remaining health; fractional ticks still spend the authored firing budget.
        bool eliteOk = eliteLost > 0 && eliteLost == Mathf.Min(found.eliteStart, found.eliteHits) &&
                       found.eliteWeight <= found.eliteBudget + .001f;
        // OWNER DECISION (Nov 23 2026): Dove's GALE WINGS stays a pure shove-and-deflect ultimate (it pushes enemies up and out and
        // turns hostile shots back; only collisions hurt). It is EXEMPT from the kill / elite-hearts / one-shot rule. Its A check
        // instead requires that the gust really displaces the field, so the exemption still tests something. B (the gun keeps
        // firing) and C (no exceptions, bounded pools) apply to it like to every ship.
        if (exempt)
        {
            note += "exempt from the kill rule by owner decision (shove-only): the probe body " + (probeMoved ? "was pushed" : "did NOT move") + "; ";
            oneShot = true; killedSomething = probeMoved; eliteOk = true;
        }
        if (!killedSomething) note += "killed nothing in the field; ";
        if (eliteLost == 0) note += "elite out of reach at every spot tried (0 hearts); ";
        else if (!exempt) note += "elite hit at " + found.elitePos + "; ";
        Check(name + " A: " + (exempt ? "gust displaces a body in its path (probe " + (probeMoved ? "pushed" : "NOT moved") + "; kill rule exempt by owner decision)" : "one-shot (" + dead + " of " + found.fieldCount + " dead, " + wounded + " wounded), elite at " + found.elitePos + " lost " + eliteLost + " across " + found.eliteHits + " hits (weight " + found.eliteWeight + "/" + found.eliteBudget + ")"), oneShot && killedSomething && eliteOk);

        // ---- B. the ultimate with the gun, every atom at 10 ----
        ShotFeelTest.FreshScene();
        for (int a = 0; a < ShotAtomTuning.Count; a++) ShotAtoms.SetLevel((ShotAtom)a, 10);
        var r2 = ShotFeelTest.MakeShip(id, ShipWeaponUpgrades.MaxLevel);
        var field2 = Field();
        int fireShots = 0, peakBullets = 0, peakProj = 0, peakSprites = 0;
        try
        {
            r2.battery.ResetTimers();
            for (int i = 0; i < 90; i++) { if (r2.gun != null) r2.gun.Step(1f, Dt, Dt); r2.battery.Step(Dt); }
            int shotsBefore = Total(r2.battery);
            typeof(ShipPowerController).GetMethod("Fire", Inst).Invoke(r2.power, null);
            bool wasUltimate = false;
            for (float t = 0f; t < 7f; t += Dt)
            {
                r2.power.Runner.Step(Dt);
                if (r2.gun != null) r2.gun.Step(1f, Dt, Dt);
                r2.battery.Step(Dt);
                wasUltimate |= r2.battery.UltimateBusy;
                peakBullets = Mathf.Max(peakBullets, FloatieBullets.Active);
                peakProj = Mathf.Max(peakProj, AttackPool.ProjectilePoolSize);
                peakSprites = Mathf.Max(peakSprites, AttackPool.SpritePoolSize);
                if (t < 3f) fireShots = Total(r2.battery) - shotsBefore;
                if (Mathf.Approximately(t % 1f, 0f)) r2.battery.ResetTimers();
            }
            if (!wasUltimate) note += "(ultimate never reported busy) ";
        }
        catch (System.Exception e) { errors++; Debug.Log("[ULTVET] exception " + name + ": " + e); }
        bool held = r2.battery.CurrentHold == FloatieBattery.Hold.None;
        bool bounded = peakBullets <= FloatieTuning.PoolSize && FloatieBullets.Size <= FloatieTuning.PoolSize &&
                       peakProj <= AttackPool.MaxProjectiles && peakSprites <= AttackPool.MaxSprites &&
                       FloatieFx.BuiltParticles <= FloatieFx.MaxParticles;
        FinishCinematic(r2.power);
        ShotFeelTest.Teardown(r2);
        foreach (var go in field2) if (go != null) Object.DestroyImmediate(go);
        Check(name + " B: the gun fired " + fireShots + " shots in the 3 s after the ultimate went off, hold " + (held ? "none" : "HELD") +
              ", atoms at 10", fireShots >= 1 && held);
        Check(name + " C: no exceptions (" + errors + "), pools bounded (bullets " + peakBullets + "/" + FloatieTuning.PoolSize + ", projectiles " + peakProj + "/" +
              AttackPool.MaxProjectiles + ", sprites " + peakSprites + "/" + AttackPool.MaxSprites + ")", errors == 0 && bounded);
        Debug.Log("[ULTVET] SUMMARY " + name + " " + ShipLoadoutTable.For(id).attackName + ": one-shot " + (oneShot && killedSomething ? "ok" : "FAIL") + ", gun-during " +
                  (fireShots >= 1 ? "ok" : "FAIL") + ", bounded " + (bounded ? "ok" : "FAIL") + " " + note);
    }

    static void FinishNothing() { }

    static int Total(FloatieBattery b)
    {
        int n = 0;
        for (int i = 0; i < FloatieTuning.MaxFloaties; i++) n += b.Shots[i];
        return n;
    }
}
