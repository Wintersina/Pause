using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Atoms next to the hostile shots, over each world's backdrop, at a 1080 px
// wide phone's own pixel density (CameraFit.GameplayHalfWidth: 7.44 u across
// 1080 px). One PNG per world:
//
//   row 1  the pickups: blue shield, red pause, capacitor, green heal, bright
//          star dust, star dust
//   row 2  the world's roster enemy shot in every kind (bolt, shard, slag,
//          shell, glob in the air, a landed pool)
//   row 3  the world's elites' shots
//   row 4  the world's boss shots (bolt, shard)
//   below  a mixed field: atoms and shots scattered together, as in a run
//
// plus <world>-zoom.png (rows 1-4 x2, nearest) for reading the drawings.
//
//   scripts/unity-batch.sh -projectPath <abs>/Pause -executeMethod AtomClarityPreview.Run
//   (writes to $ATOM_CLARITY_DIR, else Builds/AtomClarity)
public static class AtomClarityPreview
{
    public const int Width = 1080, Height = 1240;
    public const float PixelsPerUnit = Width / (2f * CameraFit.GameplayHalfWidth);
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("ATOM_CLARITY_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/AtomClarity";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                for (int w = 0; w < Worlds.Length; w++) World(dir, w);
            }
            finally
            {
                BossEncounter.ResetRun();
                EliteSystem.Clear();
            }
        }
        EditorApplication.Exit(0);
    }

    public static readonly string[] AtomPrefabs = { "prefabs/atom3a", "prefabs/pauseAtom", "prefabs/cooldownAtom", null, "prefabs/LargeStar_1", "prefabs/smStar_1" };

    // A pickup as the main game spawns it (spawnGoodStuff.Place), at `at`;
    // index into AtomPrefabs (3: the green heal atom, built in code).
    public static GameObject SpawnPickup(int index, Vector3 at)
    {
        GameObject go;
        if (AtomPrefabs[index] == null) go = HealAtom.Spawn(at);
        else
        {
            var prefab = Resources.Load<GameObject>(AtomPrefabs[index]);
            go = Object.Instantiate(prefab, at, Quaternion.identity);
            go.name = prefab.name + "(Clone)";
            PickupArt.ApplyInGameScale(go, prefab);
            if (index <= 2) AtomSpin.AddTo(go);
        }
        foreach (var book in go.GetComponentsInChildren<PickupFlipbook>()) book.Advance(.001f);
        PickupGlow.Dress(go);
        return go;
    }

    // The camera of the review: a phone's pixel density, centred on the lane.
    public static Camera PhoneCamera(int width, int height, float centreY)
    {
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = width / (float)height;
        cam.orthographicSize = height * .5f / PixelsPerUnit;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, centreY, -10f);
        return cam;
    }

    public static WorldBackdrop Backdrop(int world)
    {
        var bgGo = new GameObject("~Backdrop");
        var backdrop = bgGo.AddComponent<WorldBackdrop>();
        backdrop.Show(Worlds[world], false);
        for (int i = 0; i < 600; i++) backdrop.Step(1f / 60f);
        return backdrop;
    }

    // The world's ordinary enemy shot style (the first roster shooter there).
    public static EnemyBehaviour RosterShooter(int world)
    {
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (def.world == world && b != null && (b.attack == EnemyAttack.Shot || b.attack == EnemyAttack.Ring ||
                                                    b.attack == EnemyAttack.Cross || b.attack == EnemyAttack.Lob)) return b;
        }
        return null;
    }

    static void World(string dir, int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        EliteSystem.Clear();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        float top = 3.9f;
        var cam = PhoneCamera(Width, Height, top - Height * .5f / PixelsPerUnit);
        Backdrop(world);
        HazardRuntime.Ensure();

        // row 1: the pickups
        for (int i = 0; i < AtomPrefabs.Length; i++) SpawnPickup(i, new Vector3(-2.5f + i, 3.3f, 0f));

        // row 2: the roster shot in every kind
        var roster = RosterShooter(world);
        var shots = EliteSystem.Shots;
        if (roster != null)
        {
            var kinds = new[] { EliteShots.Kind.Bolt, EliteShots.Kind.Shard, EliteShots.Kind.Slag, EliteShots.Kind.Shell, EliteShots.Kind.Glob };
            for (int i = 0; i < kinds.Length; i++)
            {
                var s = shots.Fire(null, roster.ShotStyle, kinds[i], new Vector2(-2.5f + i, 2.3f), Vector2.down * .01f);
                if (s != null) s.AsRosterShot(null, 0f);
            }
            var pool = shots.Fire(null, roster.ShotStyle, EliteShots.Kind.Glob, new Vector2(2.5f, 2.3f), Vector2.down * .01f);
            if (pool != null)
            {
                pool.Lob(new Vector2(2.5f, 2.3f), .1f);
                for (int i = 0; i < 6 && pool.Airborne; i++) pool.Step(1f / 30f);
            }
        }

        // row 3: the world's elites' shots
        var elites = new List<EliteDef>();
        EliteCatalog.ForWorld(world, elites);
        for (int i = 0; i < elites.Count && i < 6; i++)
            shots.Fire(null, elites[i], EliteShots.KindOf(elites[i].shotKind), new Vector2(-2.5f + i, 1.3f), Vector2.down * .01f);

        // row 4: the boss's shots
        var bossPool = new BossProjectilePool(40, 1);
        var boss = BossCatalog.ForWorld(world);
        bossPool.Fire(boss, BossShotStyle.Bolt, new Vector3(-1.5f, .3f, 0f), Vector2.down * .01f);
        bossPool.Fire(boss, BossShotStyle.Shard, new Vector3(-.5f, .3f, 0f), Vector2.down * .01f);
        bossPool.Fire(boss, BossShotStyle.Bolt, new Vector3(.5f, .3f, 0f), new Vector2(.7f, -.7f));
        bossPool.Fire(boss, BossShotStyle.Shard, new Vector3(1.5f, .3f, 0f), new Vector2(-.7f, -.7f));

        // below: a mixed field
        var rng = new System.Random(7 + world);
        for (int i = 0; i < 18; i++)
        {
            var at = new Vector2(-2.4f + (float)rng.NextDouble() * 4.8f, -.5f - (float)rng.NextDouble() * 3.9f);
            if (i % 3 == 0) SpawnPickup(rng.Next(0, 4), at);
            else if (i % 3 == 1 && roster != null)
            {
                var s = shots.Fire(null, roster.ShotStyle, roster.shotKind, at, new Vector2((float)rng.NextDouble() - .5f, -1f));
                if (s != null) s.AsRosterShot(null, 0f);
            }
            else if (elites.Count > 0)
            {
                var d = elites[rng.Next(elites.Count)];
                shots.Fire(null, d, EliteShots.KindOf(d.shotKind), at, new Vector2((float)rng.NextDouble() - .5f, -1f));
            }
        }
        shots.Step(.001f);
        bossPool.Step(.001f);

        string stem = Path.Combine(dir, Worlds[world].ToLower());
        var png = Shoot(cam, Width, Height);
        File.WriteAllBytes(stem + ".png", png.EncodeToPNG());
        // rows 1-4 doubled for reading the drawings
        int rows = Mathf.RoundToInt(4.1f * PixelsPerUnit);
        const int cropX = 140, cropW = 800;
        var zoom = new Texture2D(cropW * 2, rows * 2, TextureFormat.RGB24, false);
        var src = png.GetPixels(cropX, Height - rows, cropW, rows);
        var dst = new Color[cropW * 2 * rows * 2];
        for (int y = 0; y < rows * 2; y++)
            for (int x = 0; x < cropW * 2; x++) dst[y * cropW * 2 + x] = src[(y / 2) * cropW + x / 2];
        zoom.SetPixels(dst);
        zoom.Apply();
        File.WriteAllBytes(stem + "-zoom.png", zoom.EncodeToPNG());
        Object.DestroyImmediate(zoom);
        Object.DestroyImmediate(png);
        bossPool.Dispose();
        Debug.Log("[ATOM-CLARITY] " + stem + ".png");
    }

    public static Texture2D Shoot(Camera cam, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(w, h, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        return png;
    }
}
