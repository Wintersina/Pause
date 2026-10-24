using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// The Magnetic Atom (magnetAtom): scarce per-planet spawn rules, the prefab and
// its art, scoring, the shield extension (+2 s only while a shield is up), the
// dust pull (always) and the enemy pull rule (shielded only; bosses, armoured
// elites, shots and rail-mounted mines are never dragged).
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
        Check("PickupRules: 2 magnets per world, first 45-80 s, then 90-130 s, plain clock",
              PickupRules.MagnetAtomsPerWorld == 2 &&
              PickupRules.MagnetAtomFirstDelayMin == 45f && PickupRules.MagnetAtomFirstDelayMax == 80f &&
              PickupRules.MagnetAtomRepeatDelayMin == 90f && PickupRules.MagnetAtomRepeatDelayMax == 130f &&
              PickupRules.MagnetAtomRate == 1f);
        string spawner = File.ReadAllText("Assets/Scripts/Gameplay/Pickups/spawnGoodStuff.cs");
        Check("the spawner reads the magnet rules from PickupRules and holds the atom back during a boss",
              spawner.Contains("PickupRules.MagnetAtomFirstDelay()") && spawner.Contains("PickupRules.MagnetAtomRepeatDelay()") &&
              spawner.Contains("PickupRules.MagnetAtomsPerWorld") && spawner.Contains("!BossEncounter.Running"));
        Check("ScoreRules.MagnetAtom is an atom's worth and RunScore scores it by that value",
              ScoreRules.MagnetAtom == ScoreRules.PauseAtom && RunScore.AtomPoints(RunScore.Atom.Magnet) == ScoreRules.MagnetAtom);
        Check("the shield bonus is 2 s and the pull is short",
              MagnetPull.ShieldBonusSeconds == 2f && MagnetPull.PullSeconds > .5f && MagnetPull.PullSeconds < 3f);
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
              CodexCatalogue.All.Exists(e => e.id == CodexCatalogue.MagnetAtomId) &&
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
        Check("shield up: +2 s on its clock", collisionDetection.ExtendShield(MagnetPull.ShieldBonusSeconds) &&
              Mathf.Approximately(collisionDetection.invTimer, 5.5f));
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
    }

    static void EnemyRule()
    {
        var R = MagnetPull.Rule(true, true, false, false, false, false, false, false);
        Check("shielded: a plain enemy is dragged", R == MagnetPull.Effect.Drag);
        Check("no shield: nothing is pulled", MagnetPull.Rule(false, true, false, false, false, false, false, false) == MagnetPull.Effect.None);
        Check("a boss is never pulled", MagnetPull.Rule(true, true, true, false, false, false, false, false) == MagnetPull.Effect.None);
        Check("an armoured elite is never pulled", MagnetPull.Rule(true, true, false, false, false, true, true, true) == MagnetPull.Effect.None);
        Check("an unarmoured elite in play is only nudged", MagnetPull.Rule(true, true, false, false, false, true, true, false) == MagnetPull.Effect.Nudge);
        Check("an elite not in play, an enemy shot, a rail mine and a non-enemy are never pulled",
              MagnetPull.Rule(true, true, false, false, false, true, false, false) == MagnetPull.Effect.None &&
              MagnetPull.Rule(true, true, false, true, false, false, false, false) == MagnetPull.Effect.None &&
              MagnetPull.Rule(true, true, false, false, true, false, false, false) == MagnetPull.Effect.None &&
              MagnetPull.Rule(true, false, false, false, false, false, false, false) == MagnetPull.Effect.None);
        Check("the dust pull speeds up over its run",
              MagnetPull.DustSpeed(0f) == MagnetPull.DustSpeedStart && MagnetPull.DustSpeed(99f) == MagnetPull.DustSpeedEnd);
    }

    static GameObject Dust(string name, Vector3 at)
    {
        var go = new GameObject(name);
        go.tag = "pickUp";
        go.transform.position = at;
        ClearTarget.Live.Add(go.AddComponent<ClearTarget>());
        return go;
    }

    static GameObject Enemy(Vector3 at)
    {
        var go = new GameObject("enemy");
        go.tag = "Enimey";
        go.transform.position = at;
        ClearTarget.Live.Add(go.AddComponent<ClearTarget>());
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
        var small = Dust("smStar1", new Vector3(2f, 2f, 0f));
        var big = Dust("LargeStar1", new Vector3(-2f, 4f, 0f));
        var atom = Dust("atom3a", new Vector3(1f, 1f, 0f));   // an atom is not dust
        Vector3 smallStart = small.transform.position, atomStart = atom.transform.position;
        var pull = MagnetPull.Begin(ship, false);
        pull.Step(.1f);
        float d0 = (smallStart - ship.transform.position).magnitude;
        float d1 = (small.transform.position - ship.transform.position).magnitude;
        Check("without a shield the star dust moves toward the hull", d1 < d0 - .5f);
        Check("both kinds of dust are pulled, atoms are left where they are",
              pull.DustLast == 2 && atom.transform.position == atomStart);
        for (int i = 0; i < 30; i++) pull.Step(.1f);
        Check("it reaches the hull within the pull and the pull ends",
              (small.transform.position - ship.transform.position).magnitude < .05f &&
              (big.transform.position - ship.transform.position).magnitude < .05f && !pull.Running);
        Object.DestroyImmediate(ship);
        ClearTarget.Live.Clear();
    }

    static void ShieldedPullMovesSoftEnemiesOnly()
    {
        foreach (bool shielded in new[] { false, true })
        {
            var ship = NewShip();
            var soft = Enemy(new Vector3(2f, 3f, 0f));
            var boss = Enemy(new Vector3(-2f, 3f, 0f));
            boss.AddComponent<BossTarget>();
            Vector3 softStart = soft.transform.position, bossStart = boss.transform.position;
            var pull = MagnetPull.Begin(ship, shielded);
            for (int i = 0; i < 5; i++) pull.Step(.1f);
            float moved = (soft.transform.position - softStart).magnitude;
            Check(shielded ? "shielded: a plain enemy is dragged toward the hull" : "unshielded: enemies stay put",
                  shielded ? moved > 1f && (soft.transform.position - ship.transform.position).sqrMagnitude < (softStart - ship.transform.position).sqrMagnitude
                           : moved == 0f);
            Check("a boss is not moved either way (shielded=" + shielded + ")", boss.transform.position == bossStart);
            Object.DestroyImmediate(ship);
            ClearTarget.Live.Clear();
        }
    }
}
