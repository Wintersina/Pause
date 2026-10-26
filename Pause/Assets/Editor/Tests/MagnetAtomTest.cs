using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// The Magnetic Atom (magnetAtom): scarce per-planet spawn rules, the prefab and
// its art, scoring, the shield extension (+2 s only while a shield is up), the
// goodies pull (dust and atoms, always) and the enemy pull while the shield is
// up too (MagnetComboTest has the full combo rules).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod MagnetAtomTest.Run
public static class MagnetAtomTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MA] PASS  " : "[MA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Rules();
            Prefab();
            Art();
            ShieldExtension();
            EnemyRule();
            DustPull();
            ShieldedPullMovesSoftEnemiesOnly();
            ShieldCaughtDuringPull();
        }
        finally
        {
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            ClearTarget.Live.Clear();
        }
        Debug.Log("[MA] failures: " + fails);
        return fails;
    }

    static void Rules()
    {
        // (the base 2 a world, 45-80 s, 90-130 s, grown by the atomic fusions' spawn multiplier)
        float k = FusionTuning.MagnetSpawnMultiplier;
        Check("PickupRules: 3 magnets per world, first (45-80 s) / 1.5, then (90-130 s) / 1.5, plain clock",
              k == 1.5f && PickupRules.MagnetAtomsPerWorld == 3 &&
              Mathf.Approximately(PickupRules.MagnetAtomFirstDelayMin, 45f / k) && Mathf.Approximately(PickupRules.MagnetAtomFirstDelayMax, 80f / k) &&
              Mathf.Approximately(PickupRules.MagnetAtomRepeatDelayMin, 90f / k) && Mathf.Approximately(PickupRules.MagnetAtomRepeatDelayMax, 130f / k) &&
              PickupRules.MagnetAtomRate == 1f);
        string spawner = File.ReadAllText("Assets/Scripts/Gameplay/Pickups/spawnGoodStuff.cs");
        Check("the spawner reads the magnet rules from PickupRules and holds the atom back during a boss",
              spawner.Contains("PickupRules.MagnetAtomFirstDelay()") && spawner.Contains("PickupRules.MagnetAtomRepeatDelay()") &&
              spawner.Contains("PickupRules.MagnetAtomsPerWorld") && spawner.Contains("!BossEncounter.Running"));
        Check("ScoreRules.MagnetAtom is an atom's worth and RunScore scores it by that value",
              ScoreRules.MagnetAtom == ScoreRules.PauseAtom && RunScore.AtomPoints(RunScore.Atom.Magnet) == ScoreRules.MagnetAtom);
        Check("the pull is 6 s, the shield 5.8 s, and synced they both run the longer 6 s",
              MagnetPull.PullSeconds == 6f && collisionDetection.ShieldAtomSeconds == 5.8f && MagnetCombo.SyncSeconds == 6f);
        string coll = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        int mi = coll.IndexOf("MagnetPull.ObjectName)");
        int me = mi < 0 ? -1 : coll.IndexOf("Destroy(hit.gameObject)", mi);
        Check("the magnet atom cuts the weapon charge like every other atom (BoostUltimate, dust: false)",
              mi >= 0 && me > mi && coll.Substring(mi, me - mi).Contains("BoostUltimate(dust: false)"));
        Check("PickupKind numbers didn't shift (Magnet is appended)",
              (int)PickupKind.Cooldown == 5 && (int)PickupKind.Magnet == 6);
    }

    static void Prefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/prefabs/magnetAtom.prefab");
        Check("magnetAtom prefab loads from Resources like the other atoms",
              prefab != null && Resources.Load<GameObject>("prefabs/magnetAtom") != null);
        if (prefab == null) return;
        var cool = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/prefabs/cooldownAtom.prefab");
        PickupKind kind;
        var col = prefab.GetComponent<BoxCollider2D>();
        var coolCol = cool.GetComponent<BoxCollider2D>();
        Check("it is a pickUp of kind Magnet with the same collider as the violet atom's",
              prefab.CompareTag("pickUp") && PickupArt.TryKindOf(prefab, out kind) && kind == PickupKind.Magnet &&
              prefab.GetComponent<PickupFlipbook>().kind == PickupKind.Magnet &&
              col != null && coolCol != null && col.size == coolCol.size && col.isTrigger == coolCol.isTrigger);
        Check("it wanders like the other atoms (same behaviours as the violet atom)",
              prefab.GetComponents<MonoBehaviour>().Length == cool.GetComponents<MonoBehaviour>().Length);
    }

    static void Art()
    {
        bool all = true;
        var idle = PickupArt.Frames(PickupArt.IdleName(PickupKind.Magnet), PickupArt.IdleTicks(PickupKind.Magnet).Length);
        var burst = PickupArt.Frames(PickupArt.BurstName(PickupKind.Magnet), PickupArt.BurstTicks.Length);
        foreach (var s in idle) all &= s != null && s.rect.width == 196f;
        foreach (var s in burst) all &= s != null && s.rect.width == 388f;
        Check("every magnet idle (196 px) and burst (388 px) frame loads, like the other atoms", all);
        Check("the codex lists the Magnetic Atom with an animation",
              System.Array.Exists(CodexCatalogue.All, e => e.id == CodexCatalogue.MagnetAtomId) &&
              CodexAnimations.TryPickupKind(CodexCatalogue.MagnetAtomId, out kindOut) && kindOut == PickupKind.Magnet);
    }
    static PickupKind kindOut;

    static void ShieldExtension()
    {
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 1f;
        Check("no shield up: ExtendShield does nothing", !collisionDetection.ExtendShield(2f) && collisionDetection.invTimer == 1f);
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = 3.5f;
        Check("shield up: +2 s on its clock", collisionDetection.ExtendShield(2f) &&
              Mathf.Approximately(collisionDetection.invTimer, 5.5f));
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
    }

    static void EnemyRule()
    {
        // (the full combo table is MagnetComboTest's)
        Check("the dust pull speeds up over its run",
              MagnetPull.DustSpeed(0f) == MagnetPull.DustSpeedStart && MagnetPull.DustSpeed(99f) == MagnetPull.DustSpeedEnd);
    }

    static GameObject Dust(string name, Vector3 at)
    {
        var go = new GameObject(name);
        go.tag = "pickUp";
        go.transform.position = at;
        var ct = ClearTarget.Ensure(go);
        if (!ClearTarget.Live.Contains(ct)) ClearTarget.Live.Add(ct);   // (OnEnable registers it already)
        return go;
    }

    static GameObject Enemy(Vector3 at)
    {
        var go = new GameObject("enemy");
        go.tag = "Enimey";
        go.transform.position = at;
        var ct = ClearTarget.Ensure(go);
        if (!ClearTarget.Live.Contains(ct)) ClearTarget.Live.Add(ct);   // (OnEnable registers it already)
        return go;
    }

    static GameObject NewShip()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ClearTarget.Live.Clear();
        var ship = new GameObject("~magnetShip");
        ship.transform.position = new Vector3(0f, -3f, 0f);
        return ship;
    }

    static void DustPull()
    {
        var ship = NewShip();
        collisionDetection.atomCheck = false;
        startMenu.youAreInTutorial = false;
        var small = Dust("smStar1", new Vector3(2f, 2f, 0f));
        var big = Dust("LargeStar1", new Vector3(-2f, 4f, 0f));
        var atom = Dust("atom3a", new Vector3(1f, 1f, 0f));   // an atom is a goodie too
        Vector3 smallStart = small.transform.position, atomStart = atom.transform.position, bigStart = big.transform.position;
        var pull = MagnetPull.Begin(ship);
        pull.Step(.1f);
        float d0 = (smallStart - ship.transform.position).magnitude;
        float d1 = (small.transform.position - ship.transform.position).magnitude;
        Check("without a shield the star dust moves toward the hull", d1 < d0 - .5f);
        Check("both kinds of dust and the other atoms are pulled",
              (big.transform.position - ship.transform.position).magnitude < (bigStart - ship.transform.position).magnitude - .5f &&
              (atom.transform.position - ship.transform.position).magnitude < (atomStart - ship.transform.position).magnitude - .5f &&
              pull.DustLast == 2 && pull.AtomsLast == 1);
        for (int i = 0; i < 70; i++) pull.Step(.1f);
        Check("it reaches the hull within the pull and the pull ends",
              (small.transform.position - ship.transform.position).magnitude < .05f &&
              (big.transform.position - ship.transform.position).magnitude < .05f && !pull.Running);
        Object.DestroyImmediate(ship);
        ClearTarget.Live.Clear();
    }

    static void ShieldedPullMovesSoftEnemiesOnly()
    {
        startMenu.youAreInTutorial = false;
        foreach (bool shielded in new[] { false, true })
        {
            var ship = NewShip();
            collisionDetection.atomCheck = shielded;
            var soft = Enemy(new Vector3(2f, 3f, 0f));
            var boss = Enemy(new Vector3(-2f, 3f, 0f));
            boss.AddComponent<BossTarget>();
            Vector3 softStart = soft.transform.position, bossStart = boss.transform.position;
            var pull = MagnetPull.Begin(ship);
            for (int i = 0; i < 5; i++) pull.Step(.1f);
            float moved = (soft.transform.position - softStart).magnitude;
            Check(shielded ? "shielded: a plain enemy is dragged toward the hull" : "unshielded: enemies stay put",
                  shielded ? moved > 1f && (soft.transform.position - ship.transform.position).sqrMagnitude < (softStart - ship.transform.position).sqrMagnitude
                           : moved == 0f);
            Check("a boss is not moved either way (shielded=" + shielded + ")", boss.transform.position == bossStart);
            Object.DestroyImmediate(ship);
            ClearTarget.Live.Clear();
        }
        collisionDetection.atomCheck = false;
    }

    static void ShieldCaughtDuringPull()
    {
        var ship = NewShip();
        startMenu.youAreInTutorial = false;
        var soft = Enemy(new Vector3(2f, 3f, 0f));
        Vector3 start = soft.transform.position;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        var pull = MagnetPull.Begin(ship);
        pull.Step(.1f);
        Check("pull lasts 6 s", Mathf.Approximately(pull.Duration, 6f) && pull.Remaining > 5.8f);
        Check("unshielded pull leaves enemies alone", soft.transform.position == start && !pull.Shielded);
        // the shield atom is caught (collisionDetection's shield branch)
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = collisionDetection.ShieldAtomSeconds;
        collisionDetection.SyncMagnetShield(false, ship.transform.position);
        Check("shield caught mid-pull: both restart to the synced length",
              Mathf.Approximately(pull.Remaining, MagnetCombo.SyncSeconds) && Mathf.Approximately(collisionDetection.invTimer, MagnetCombo.SyncSeconds) && pull.Synced);
        pull.Step(.1f);
        collisionDetection.invTimer -= .1f;
        Check("enemies are now pulled in", (soft.transform.position - start).magnitude > .1f && pull.Shielded);
        collisionDetection.atomCheck = false;   // shield runs out
        Vector3 held = soft.transform.position;
        pull.Step(.1f);
        Check("synced: the pull ends with the shield, enemies let go", !pull.Shielded && !pull.Running && soft.transform.position == held && pull.EnemiesLocked == 0);
        pull.End();
        Check("End stops the pull", !pull.Running);
        Object.DestroyImmediate(ship);
        ClearTarget.Live.Clear();
    }
}
