using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Renders the death crash (DeathCrash) for review, frame by frame, in
// gameS1 (its rails and backdrop): the killer flies in, the last heart
// shields and crumbles, the hull breaks up and every piece -- the drone and
// a solid killer too -- slams into a rail; the frames run on until the
// Flight Complete panel would come up (marked), then a beat more.
//
//   crash-<ship>-<killer>/NNN.png   30 fps frames for a GIF
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod DeathCrashPreview.Run
//   (writes to $CRASH_PREVIEW_DIR, else Builds/DeathCrashPreview)
public static class DeathCrashPreview
{
    const float Fps = 30f;
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("CRASH_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/DeathCrashPreview";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            Random.InitState(1234);
            DeathCrash.EditorBlasts = true;
            Clip(dir, 1, "rock", new Vector3(-.6f, -2.2f, 1f));      // Neon Comet by a rock
            Clip(dir, 11, "alien", new Vector3(.8f, -1.4f, 1f));     // Ninja by an alien
            Clip(dir, 7, "mine", new Vector3(1.9f, -2.4f, 1f));      // Gold Warden by a rail mine
            Clip(dir, 13, "bossshot", new Vector3(-.2f, -2.6f, 1f)); // UFO by a boss shot
            DeathCrash.EditorBlasts = false;
        }
        EditorApplication.Exit(0);
    }

    // The domino (DeathCrashDomino): a board of enemies around the ship; the
    // wreckage takes them down in a chain, the board moving on in slow
    // motion; DOMINO / MEGA DOMINO / DEATH COMBO shown as world labels
    // (the HUD canvas isn't in a camera render).
    //
    //   domino-<name>/NNN.png
    //
    //   Unity -batchmode -quit -projectPath Pause -executeMethod DeathCrashPreview.RunDomino
    public static void RunDomino()
    {
        string dir = System.Environment.GetEnvironmentVariable("CRASH_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/DeathCrashPreview";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            DeathCrash.EditorBlasts = true;
            EliteCatalog.Reload();
            Domino(dir, "chain", 1, 2101, false, false, 9);
            Domino(dir, "chain-elite", 11, 2203, false, true, 11);
            Domino(dir, "mega", 7, 2307, true, true, 16);
            DeathCrash.ForceMega = null;
            DeathCrash.EditorBlasts = false;
        }
        EditorApplication.Exit(0);
    }

    static void Domino(string dir, string name, int id, int seed, bool mega, bool elite, int enemies)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        Random.InitState(seed);
        DeathCrash.ForceMega = mega;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .3f;
        collisionDetection.lifeCounter = 0;
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, false);
        ShipUiSlots.ScreenOverride = () => Rect.MinMaxRect(-100f, -100f, 100f, 100f);
        var paused = Object.FindFirstObjectByType<PausedLabel>();
        if (paused != null) paused.gameObject.SetActive(false);

        var cam = Camera.main;
        cam.orthographicSize = CameraFit.ComputeSize(cam.orthographicSize, 2.85f, 1080, 2340);
        cam.aspect = 1080f / 2340f;
        string clip = "domino-" + name;
        string frames = Path.Combine(dir, clip);
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);

        Vector3 shipAt = new Vector3(Random.Range(-.6f, .6f), -2.3f, 1f);
        var ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
        ship.transform.position = shipAt;
        var hull = ship.GetComponent<SpriteRenderer>();
        hull.sprite = ShipHullArt.Rest(id, 2);
        float scale = shopingShips.NormalizedHullScale(hull.sprite);
        ship.transform.localScale = new Vector3(scale, scale, 1f);
        hull.sortingOrder = 10;
        ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        ship.AddComponent<BoxCollider2D>().isTrigger = true;
        var cd = ship.AddComponent<collisionDetection>();
        cd.explosionAnimation = new GameObject("~PreviewExplosion");
        if (GameObject.Find("hypeText") == null) new GameObject("hypeText", typeof(RectTransform)).AddComponent<Text>();
        if (GameObject.Find("boostText") == null) new GameObject("boostText", typeof(RectTransform)).AddComponent<Text>();
        if (GameObject.Find("RocketsSound") == null) new GameObject("RocketsSound", typeof(AudioSource));
        if (GameObject.Find("AstroidExplotionSound") == null) new GameObject("AstroidExplotionSound", typeof(AudioSource));
        new GameObject("~boost").tag = "boost";
        typeof(collisionDetection).GetMethod("Start", Inst).Invoke(cd, null);
        buttonClicks.playerDied = false;
        var gun = UltimateGun.Attach(ship);
        if (gun.transform.childCount == 0) gun.SendMessage("Awake");
        collisionDetection.lifeCounter = collisionDetection.MAXLIFE - 1;

        // the board: rocks, aliens, fighters, a mine, spread over the screen
        BossRails.Measure();
        float half = BossRails.InnerEdge - .4f;
        float top = CameraFit.ViewTop - .8f, bottom = shipAt.y + .9f;
        var roles = new[] { EnemyRole.Rock, EnemyRole.Alien, EnemyRole.Fighter, EnemyRole.Rock, EnemyRole.Big, EnemyRole.Mine, EnemyRole.Chaser };
        int cols = 3, rows = Mathf.CeilToInt(enemies / 3f);
        for (int i = 0; i < enemies; i++)
        {
            var def = EnemyRoster.Pick(0, roles[i % roles.Length]) ?? EnemyRoster.One(0, EnemyRole.Rock);
            float x = Mathf.Lerp(-half, half, (i % cols + .5f) / cols) + Random.Range(-.25f, .25f);
            float y = Mathf.Lerp(bottom, top, (i / cols + .5f) / rows) + Random.Range(-.2f, .2f);
            var go = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity);
            ClearTarget.Ensure(go);
            var chaser = go.GetComponent<ChaserEnemy>();
            if (chaser != null) chaser.enabled = false;   // no pilot to chase in a still
            var flip = go.GetComponent<EnemyFlipbook>();
            if (flip != null) flip.Advance(.01f);
        }
        if (elite && EliteCatalog.All != null && EliteCatalog.All.Length > 0)
        {
            var e = EliteShip.CreateInPlay(EliteCatalog.All[0], new Vector2(Random.Range(-.8f, .8f), shipAt.y + 2.2f));
            if (e != null) e.AttackCooldown = 99f;
        }

        // the killer comes in from ahead
        Vector3 from = shipAt + new Vector3(.35f, 2.2f, 0f), to = shipAt + new Vector3(.1f, .38f, 0f);
        var killer = MakeKiller("rock", from);
        Label(new Vector3(0f, cam.transform.position.y + cam.orthographicSize - .4f, 0f),
              ShipId.NameOf(id) + (mega ? "  MEGA DOMINO (forced)" : "  domino chain"));

        // the popups, as world labels
        var popups = new System.Collections.Generic.List<(GameObject go, float until)>();
        float clock = 0f;
        System.Action<Vector3, int, int> onKill = (at, mult, points) =>
            popups.Add((Label(at + Vector3.up * .3f, (mult <= 1 ? "DOMINO" : "DOMINO x" + mult) + " +" + points,
                              mult >= 3 ? new Color(1f, .36f, .27f) : new Color(1f, .74f, .2f), .028f), clock + .7f));
        System.Action<Vector3> onMega = at =>
            popups.Add((Label(new Vector3(0f, cam.transform.position.y + 1.2f, 0f), "MEGA DOMINO!", new Color(1f, .18f, .53f), .06f), clock + 1.8f));
        System.Action<int, int, bool> onCombo = (points, n, m) =>
            popups.Add((Label(new Vector3(0f, cam.transform.position.y + .3f, 0f), "DEATH COMBO +" + points,
                              m ? new Color(1f, .18f, .53f) : new Color(1f, .74f, .2f), .055f), clock + 3f));
        DeathCrash.DominoKill += onKill;
        DeathCrash.MegaDominoStarted += onMega;
        DeathCrash.DeathCombo += onCombo;

        int frame = 0;
        const int Approach = 9;
        for (int i = 0; i < Approach; i++)
        {
            killer.transform.position = Vector3.Lerp(from, to, (i + 1f) / Approach);
            Shoot(cam, Path.Combine(frames, (frame++).ToString("000") + ".png"));
        }
        var collider = killer.GetComponentInChildren<Collider2D>();
        typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst).Invoke(cd, new object[] { collider });
        if (killer != null) Object.DestroyImmediate(killer);
        var crash = DeathCrash.Instance;

        float dt = 1f / Fps;
        GameObject done = null;
        float after = 0f;
        for (int i = 0; i < 480 && after < .8f; i++)
        {
            crash.Tick(dt);
            clock += dt;
            foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsSortMode.None)) fx.Tick(dt);
            for (int k = popups.Count - 1; k >= 0; k--)
            {
                if (popups[k].go == null) { popups.RemoveAt(k); continue; }
                popups[k].go.transform.position += Vector3.up * .5f * dt;
                if (clock >= popups[k].until) { Object.DestroyImmediate(popups[k].go); popups.RemoveAt(k); }
            }
            crash.SendMessage("LateUpdate");
            if (DeathCrash.PanelReady)
            {
                if (done == null)
                    done = Label(new Vector3(0f, cam.transform.position.y - .6f, 0f),
                                 "FLIGHT COMPLETE  (" + crash.FinishedAt.ToString("F2") + "s)");
                after += dt;
            }
            Shoot(cam, Path.Combine(frames, (frame++).ToString("000") + ".png"));
        }
        DeathCrash.DominoKill -= onKill;
        DeathCrash.MegaDominoStarted -= onMega;
        DeathCrash.DeathCombo -= onCombo;
        Debug.Log("[DOMINO] " + clip + ": " + crash.Targets + " targets, " + crash.DominoKills + " kills, " + crash.DeepestGeneration +
                  " deep, " + crash.Ricochets + " ricochets, mega " + crash.Mega + ", combo +" + crash.ComboTotal +
                  ", panel at " + crash.FinishedAt.ToString("F2") + "s, " + frame + " frames");
        crash.Cancel(false);
        RunScore.EndRun(RunScore.RunId);
        ShipUiSlots.ScreenOverride = null;
    }

    static GameObject MakeKiller(string kind, Vector3 at)
    {
        GameObject go;
        switch (kind)
        {
            case "rock": go = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), at, Quaternion.identity); break;
            case "alien": go = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Alien), at, Quaternion.identity); break;
            case "mine": go = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Mine), at, Quaternion.identity); break;
            default:
            {
                go = new GameObject("BossShot", typeof(SpriteRenderer));
                go.transform.position = at;
                var boss = BossCatalog.ForWorld(0);
                go.GetComponent<SpriteRenderer>().sprite = BossArt.Shot(boss, 0);
                go.GetComponent<SpriteRenderer>().sortingOrder = 20;
                go.transform.localScale = Vector3.one * .5f;
                var hit = new GameObject("BossShotHit", typeof(CircleCollider2D));
                hit.tag = BossHitbox.Tag;
                hit.transform.SetParent(go.transform, false);
                break;
            }
        }
        // Nothing moves them but this clip.
        foreach (var b in go.GetComponents<MonoBehaviour>())
            if (!(b is EnemyIdentity)) b.enabled = false;
        var flip = go.GetComponent<EnemyFlipbook>();
        if (flip != null) flip.Advance(.01f);
        return go;
    }

    static void Clip(string dir, int id, string killerKind, Vector3 shipAt)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        collisionDetection.lifeCounter = 0;
        ShipUiSlots.ScreenOverride = () => Rect.MinMaxRect(-100f, -100f, 100f, 100f);

        // edit mode: nothing has pressed yet, the scene shows its PAUSED card
        var paused = Object.FindFirstObjectByType<PausedLabel>();
        if (paused != null) paused.gameObject.SetActive(false);

        var cam = Camera.main;
        cam.orthographicSize = CameraFit.ComputeSize(cam.orthographicSize, 2.85f, 1080, 2340);
        cam.aspect = 1080f / 2340f;

        string name = "crash-" + ShipId.KeyOf(id) + "-" + killerKind;
        string frames = Path.Combine(dir, name);
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);

        // the ship, as it flies on its last heart
        var ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
        ship.transform.position = shipAt;
        var hull = ship.GetComponent<SpriteRenderer>();
        hull.sprite = ShipHullArt.Rest(id, 2);
        float scale = shopingShips.NormalizedHullScale(hull.sprite);
        ship.transform.localScale = new Vector3(scale, scale, 1f);
        hull.sortingOrder = 10;
        ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        ship.AddComponent<BoxCollider2D>().isTrigger = true;
        var cd = ship.AddComponent<collisionDetection>();
        cd.explosionAnimation = new GameObject("~PreviewExplosion");
        if (GameObject.Find("hypeText") == null) new GameObject("hypeText", typeof(RectTransform)).AddComponent<Text>();
        if (GameObject.Find("boostText") == null) new GameObject("boostText", typeof(RectTransform)).AddComponent<Text>();
        if (GameObject.Find("RocketsSound") == null) new GameObject("RocketsSound", typeof(AudioSource));
        if (GameObject.Find("AstroidExplotionSound") == null) new GameObject("AstroidExplotionSound", typeof(AudioSource));
        new GameObject("~boost").tag = "boost";
        typeof(collisionDetection).GetMethod("Start", Inst).Invoke(cd, null);
        buttonClicks.playerDied = false;
        var gun = UltimateGun.Attach(ship);
        if (gun.transform.childCount == 0) gun.SendMessage("Awake");
        var hearts = ship.AddComponent<ShipLivesIndicator>();
        typeof(ShipLivesIndicator).GetMethod("Start", Inst).Invoke(hearts, null);
        collisionDetection.lifeCounter = collisionDetection.MAXLIFE - 1;
        hearts.SendMessage("Update");
        hearts.SendMessage("LateUpdate");

        // the killer comes in from ahead (a mine from the rail beside it)
        Vector3 from = killerKind == "mine" ? new Vector3(2.3f, shipAt.y + 1.6f, 1f) : shipAt + new Vector3(.35f, 2.2f, 0f);
        Vector3 to = shipAt + (killerKind == "mine" ? new Vector3(.2f, .3f, 0f) : new Vector3(.1f, .38f, 0f));
        var killer = MakeKiller(killerKind, from);
        Label(new Vector3(0f, cam.transform.position.y + cam.orthographicSize - .4f, 0f),
              ShipId.NameOf(id) + "  killed by a " + (killerKind == "bossshot" ? "boss shot" : killerKind));

        int frame = 0;
        const int Approach = 9;
        for (int i = 0; i < Approach; i++)
        {
            killer.transform.position = Vector3.Lerp(from, to, (i + 1f) / Approach);
            Shoot(cam, Path.Combine(frames, (frame++).ToString("000") + ".png"));
        }

        var collider = killer.GetComponentInChildren<Collider2D>();
        typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst).Invoke(cd, new object[] { collider });
        if (killer != null) Object.DestroyImmediate(killer);   // Destroy() is play-mode only
        var crash = DeathCrash.Instance;

        float dt = 1f / Fps;
        GameObject done = null;
        float after = 0f;
        for (int i = 0; i < 120 && after < .7f; i++)
        {
            crash.Tick(dt);
            if (hearts != null)
            {
                hearts.SendMessage("Update");
                hearts.SendMessage("LateUpdate");
            }
            foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsSortMode.None)) fx.Tick(dt);
            crash.SendMessage("LateUpdate");   // the camera shake
            if (DeathCrash.PanelReady)
            {
                if (done == null)
                    done = Label(new Vector3(0f, cam.transform.position.y - .2f, 0f),
                                 "FLIGHT COMPLETE  (" + crash.FinishedAt.ToString("F2") + "s)");
                after += dt;
            }
            Shoot(cam, Path.Combine(frames, (frame++).ToString("000") + ".png"));
        }
        Debug.Log("[CRASH] " + name + ": " + crash.FragmentCount + " fragments, " + crash.PieceCount +
                  " pieces, killer " + crash.Killer + (crash.KillerCrashed ? " crashed" : "") +
                  ", panel at " + crash.FinishedAt.ToString("F2") + "s, " + frame + " frames");
        crash.Cancel(false);
        ShipUiSlots.ScreenOverride = null;
    }

    static GameObject Label(Vector3 at, string text, Color colour, float size)
    {
        var go = Label(at, text);
        var tm = go.GetComponent<TextMesh>();
        tm.color = colour;
        tm.characterSize = size;
        tm.anchor = TextAnchor.MiddleCenter;
        return go;
    }

    static GameObject Label(Vector3 at, string text)
    {
        var go = new GameObject("~Label");
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = tm.font.material;
        mr.sortingOrder = 200;
        tm.characterSize = .03f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.UpperCenter;
        tm.color = new Color(.95f, .95f, 1f);
        go.transform.position = new Vector3(at.x, at.y, -2f);
        return go;
    }

    static void Shoot(Camera cam, string path)
    {
        const int px = 432;
        int py = Mathf.RoundToInt(px / cam.aspect);
        var rt = new RenderTexture(px, py, 24);
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(px, py, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, px, py), 0, 0);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
    }
}
