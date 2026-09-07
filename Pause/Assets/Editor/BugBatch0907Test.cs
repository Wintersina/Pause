using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;

// Covers a batch of six user-reported bugs from 2026-09-07:
//   1. replay/hamburger menu buttons showed whenever pauses ran out, not
//      only on death.
//   2. the tutorial's end-of-tutorial popup had no working continue button.
//   3. fixed-Y spawn points let enemies/pickups appear inside the visible
//      area on tall phones where CameraFit grows the camera.
//   4. the tutorial ship never got the idle/boost engine flame gameS1 ships
//      have, because the attach script only looked for movePlayer.
//   5. star-dust clusters spawned in a single vertical line (one shared x).
//   6. each planet ran 8 minutes; now 5.
public static class BugBatch0907Test
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BB] PASS  " : "[BB] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        ReplayMenuButtonsAreDeathOnly();
        TutorialContinueButtonIsUsable();
        SpawnPointTracksCamera();
        ThrusterAttachFindsTutorialShip();
        StarClustersSpreadHorizontally();
        WorldLengthIsFiveMinutes();

        Debug.Log("[BB] failures: " + fails);
        EditorApplication.Exit(0);
    }

    // ---- 1: replay/menu buttons ----------------------------------------

    static void ReplayMenuButtonsAreDeathOnly()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        // moveBackground() (called from Update) expects a Renderer to scroll,
        // same as the real starfield object it normally runs on.
        var go = new GameObject("~MoveStarsTest", typeof(SpriteRenderer));
        var comp = go.AddComponent<moveStarsBackground>();
        comp.SendMessage("Start");

        // replyB/mainMenuB are public fields on moveStarsBackground.
        var reply = comp.replyB;
        var menu = comp.mainMenuB;

        Check("moveStarsBackground finds the replay button", reply != null);
        Check("moveStarsBackground finds the main-menu button", menu != null);

        if (reply != null && menu != null)
        {
            buttonClicks.playerDied = false;
            score.pauseCounter = 0; // out of pauses, but alive
            comp.SendMessage("Update");
            Check("out of pauses but alive: replay stays hidden", !reply.gameObject.activeSelf);
            Check("out of pauses but alive: menu stays hidden", !menu.gameObject.activeSelf);

            buttonClicks.playerDied = true;
            comp.SendMessage("Update");
            Check("actually dead: replay shows", reply.gameObject.activeSelf);
            Check("actually dead: menu shows", menu.gameObject.activeSelf);

            buttonClicks.playerDied = false;
            score.pauseCounter = 3;
            comp.SendMessage("Update");
            Check("alive with pauses left: replay stays hidden", !reply.gameObject.activeSelf);
            Check("alive with pauses left: menu stays hidden", !menu.gameObject.activeSelf);
        }

        buttonClicks.playerDied = false;
        Object.DestroyImmediate(go);
    }

    // ---- 2: tutorial continue button ------------------------------------

    static void TutorialContinueButtonIsUsable()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);

        var btnGo = SceneUtil.FindAny("playMainGameButton");
        Check("tutorial: end-screen continue button exists", btnGo != null);
        if (btnGo == null) return;

        var rt = btnGo.GetComponent<RectTransform>();
        Check("continue button has a real hit area", rt != null && rt.sizeDelta.x > 0f && rt.sizeDelta.y > 0f);

        var img = btnGo.GetComponent<Image>();
        Check("continue button has a visible sprite", img != null && img.sprite != null);

        bool hasLabel = false;
        foreach (Transform child in btnGo.transform)
            if (child.GetComponent<Text>() != null) hasLabel = true;
        Check("continue button has a text label", hasLabel);

        var button = btnGo.GetComponent<Button>();
        bool wired = false;
        if (button != null)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentMethodName(i) == "replay") wired = true;
        }
        Check("continue button is wired to tutButtonClicks.replay()", wired);
    }

    // ---- 3: spawn point tracks the camera --------------------------------

    static void SpawnPointTracksCamera()
    {
        foreach (var scenePath in new[] { "Assets/Scenes/gameS1.unity", "Assets/Scenes/tutorialS5.unity" })
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            string scene = scenePath.Contains("tutorial") ? "tutorialS5" : "gameS1";

            var spawnGo = SceneUtil.FindAny("Enemey_Item_Position");
            Check(scene + ": spawn point exists", spawnGo != null);
            if (spawnGo == null) continue;

            var comp = spawnGo.GetComponent<SpawnAboveCamera>();
            if (comp == null) comp = spawnGo.AddComponent<SpawnAboveCamera>();

            var cam = Camera.main;
            Check(scene + ": scene has a main camera", cam != null);
            if (cam == null) continue;

            float baseline = cam.orthographicSize;
            cam.orthographicSize = baseline; // default aspect
            comp.SendMessage("Reposition");
            float yAtDefault = spawnGo.transform.position.y;

            // Simulate CameraFit growing the camera for a tall phone.
            cam.orthographicSize = 6.65f;
            comp.SendMessage("Reposition");
            float yAtTallPhone = spawnGo.transform.position.y;

            Check(scene + ": spawn point stays above the camera's visible top edge",
                  yAtTallPhone >= cam.transform.position.y + cam.orthographicSize);
            Check(scene + ": spawn point actually moved for the larger camera size",
                  yAtTallPhone > yAtDefault);

            cam.orthographicSize = baseline;
        }
    }

    // ---- 4: tutorial ship gets the idle/boost flame ----------------------

    static void ThrusterAttachFindsTutorialShip()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);

        var tutShip = Object.FindFirstObjectByType<movePlayerInTut>();
        Check("tutorial: scene has a movePlayerInTut ship to attach to", tutShip != null);
        if (tutShip == null) return;

        var findPlayer = typeof(ShipThrusterAttach).GetMethod("FindPlayer",
            BindingFlags.NonPublic | BindingFlags.Static);
        var found = findPlayer.Invoke(null, null) as GameObject;

        Check("ShipThrusterAttach.FindPlayer resolves the tutorial ship (not just movePlayer)",
              found == tutShip.gameObject);
    }

    // ---- 5: star clusters spread horizontally ----------------------------

    static void StarClustersSpreadHorizontally()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        var spawnerGo = new GameObject("~SpawnGoodStuffTest");
        var comp = spawnerGo.AddComponent<spawnGoodStuff>();
        comp.smStar = new GameObject("~smStarPrefab");
        comp.midStar = new GameObject("~midStarPrefab");
        comp.Atom = new GameObject("~atomPrefab");
        comp.redAtom = new GameObject("~redAtomPrefab");

        var method = typeof(spawnGoodStuff).GetMethod("spawnSmStar", BindingFlags.NonPublic | BindingFlags.Instance);
        var vPos = new Vector3(0.4f, 0f, 0f);

        float minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 0; i < 12; i++)
            method.Invoke(comp, new object[] { i, vPos });

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith("~smStarPrefab")) continue;
            minX = Mathf.Min(minX, t.position.x);
            maxX = Mathf.Max(maxX, t.position.x);
        }

        Check("a spawned cluster of stars is not all at the same x (was: shared vPos.x)",
              maxX - minX > 0.05f);

        Object.DestroyImmediate(comp.midStar);
        Object.DestroyImmediate(comp.Atom);
        Object.DestroyImmediate(comp.redAtom);
        // Covers both the original template and every Instantiate()'d "(Clone)".
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("~smStarPrefab"))
                Object.DestroyImmediate(t.gameObject);
        Object.DestroyImmediate(spawnerGo);
    }

    // ---- 6: level length ---------------------------------------------------

    static void WorldLengthIsFiveMinutes()
    {
        var go = new GameObject("~WorldManagerTest");
        var wm = go.AddComponent<WorldManager>();
        Check("a planet now runs 5 minutes (300s), not 8 (480s)",
              Mathf.Approximately(wm.secondsPerWorld, 300f));
        Object.DestroyImmediate(go);
    }
}
