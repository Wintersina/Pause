using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders Paragon in play without a build: a still per moment, into
// $PAUSE_PARAGON_PREVIEW_DIR (default /private/tmp) as paragon-play-<name>.png.
//
//   scripts/unity-batch.sh -executeMethod ParagonPreview.Run
//
//   flames   the hull with its three engine flames and the timed shield up
//   missiles a volley of mini-warheads leaving the nose (ParagonMissiles)
//   barrage  the nuke (the ultimate)
//   damage   the three damage states side by side (intact / damaged / wreck)
public static class ParagonPreview
{
    const int Id = PremiumShips.Paragon;
    static Camera cam;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("PAUSE_PARAGON_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "/private/tmp";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            DeveloperUnlocks.SetEnabled(false);
            PlayerPrefs.SetString(ShipId.OwnedKey(Id), "True");
            PlayerPrefs.SetInt(ShipId.SelectedKey, Id);
            Flames(dir);
            Missiles(dir);
            Damage(dir);
        }
        EditorApplication.Exit(0);
    }

    static void Fresh()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 3.2f;
        cam.aspect = .75f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.055f, .07f, .14f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        collisionDetection.lifeCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
    }

    static GameObject Ship(Vector3 at, float scale = 1f)
    {
        var go = new GameObject("ship" + Id + "(Clone)", typeof(SpriteRenderer));
        go.transform.position = at;
        spawnShips.ApplyHull(go, Id);
        go.transform.localScale *= scale;
        return go;
    }

    static GameObject Hazard(float x, float y)
    {
        var go = new GameObject("hazard", typeof(SpriteRenderer));
        go.tag = "Enimey";
        go.transform.position = new Vector3(x, y, 0f);
        var sr = go.GetComponent<SpriteRenderer>();
        var tex = new Texture2D(8, 8);
        var px = new Color[64];
        for (int i = 0; i < 64; i++) px[i] = new Color(.55f, .3f, .8f);
        tex.SetPixels(px); tex.Apply();
        tex.filterMode = FilterMode.Point;
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f), 16f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static void Shot(string path, int w = 810, int h = 1080)
    {
        var target = new RenderTexture(w, h, 24);
        cam.targetTexture = target;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var png = new Texture2D(w, h, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(target);
    }

    static void Flames(string dir)
    {
        Fresh();
        var ship = Ship(new Vector3(0f, -1f, 0f), 3.2f);
        var boost = ship.transform.Find("Boost" + Id);
        if (boost != null) boost.gameObject.SetActive(true);
        foreach (var f in ship.GetComponentsInChildren<ShipFlameFlipbook>(true)) f.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
        var shield = ShipShield.For(ship);
        shield.Show();
        for (int i = 0; i < 40; i++) shield.Tick(1f / 60f);
        Shot(Path.Combine(dir, "paragon-play-flames.png"));
    }

    static void Missiles(string dir)
    {
        Fresh();
        PlayerPrefs.SetInt("spawnShip", Id);
        var ship = Ship(new Vector3(0f, -2.2f, 0f), 1.6f);
        var power = ship.AddComponent<ShipPowerController>();
        power.SendMessage("Awake");
        power.SendMessage("Start");
        Hazard(-1.4f, 1.6f); Hazard(-.5f, 2.3f); Hazard(.6f, 1.9f); Hazard(1.5f, 2.6f);
        var missiles = ship.GetComponent<ParagonMissiles>();
        for (int i = 0; i < 101; i++) missiles.Step(.1f);
        for (int i = 0; i < 14; i++) power.Runner.Step(1f / 60f);
        Shot(Path.Combine(dir, "paragon-play-missiles.png"));
        AttackPool.StopAll();
        power.Runner.Fire();
        for (int i = 0; i < 40; i++) power.Runner.Step(1f / 60f);
        Shot(Path.Combine(dir, "paragon-play-barrage.png"));
        AttackPool.StopAll();
    }

    static void Damage(string dir)
    {
        Fresh();
        cam.aspect = 2.2f;
        for (int st = 0; st < 3; st++)
        {
            var go = new GameObject("state" + st, typeof(SpriteRenderer));
            go.transform.position = new Vector3((st - 1) * 2.9f, 0f, 0f);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = ShipHullArt.Get(Id, 0, st, 0);
            go.transform.localScale = Vector3.one * (2.6f / sr.sprite.bounds.size.x);
        }
        Shot(Path.Combine(dir, "paragon-play-damage.png"), 1650, 750);
    }
}
