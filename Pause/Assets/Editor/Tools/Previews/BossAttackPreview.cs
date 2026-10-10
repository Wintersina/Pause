using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders every boss attack, frame by frame, for review: the boss drifting
// at the top, its tell gathering at the part about to fire, the shots
// leaving that part, ricochets sparking off the rails, lasers growing out
// of their part and sweeping. One PNG per frame (15 fps of world time) plus
// a CSV of what was happening on each, for bossatk_sheets.py to turn into a
// contact sheet and a GIF per boss.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossAttackPreview.Run
//   (writes to $BOSSATK_PREVIEW_DIR, else Builds/BossAttackPreview)
public static class BossAttackPreview
{
    const int Width = 360, Height = 780;          // a 9:19.5 phone
    const float Dt = 1f / 30f;
    const float Seconds = 4.2f;                   // from the tell's start

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("BOSSATK_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/BossAttackPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                string only = null, onlyAtk = null;
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "-only") only = args[i + 1].ToLower();
                    if (args[i] == "-attack") onlyAtk = args[i + 1];
                }
                for (int w = 0; w < BossCatalog.All.Length; w++)
                {
                    if (only != null && BossCatalog.All[w].artKey.ToLower() != only) continue;
                    for (int a = 0; a < BossCatalog.All[w].attacks.Length; a++)
                        if (onlyAtk == null || onlyAtk == a.ToString()) Attack(dir, w, a);
                }
            }
            finally { BossEncounter.ResetRun(); }
        }
        EditorApplication.Exit(0);
    }

    static Camera Scene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.05f, .05f, .1f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);

        // the two walls, as in gameS1 (world-fixed quads; inner faces at +/-2.435)
        var white = Texture2D.whiteTexture;
        var sprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(.5f, .5f), white.width);
        foreach (float side in new[] { -1f, 1f })
        {
            var wall = new GameObject(side < 0 ? "leftPipe" : "rightPipe");
            var sr = wall.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(.16f, .17f, .26f);
            sr.sortingOrder = -10;
            wall.transform.position = new Vector3(side * 3.15f, 0f, 1f);
            wall.transform.localScale = new Vector3(1.43f, 30f, 1f);
            var lip = new GameObject("lip");
            lip.transform.SetParent(wall.transform, false);
            var lr = lip.AddComponent<SpriteRenderer>();
            lr.sprite = sprite;
            lr.color = new Color(.45f, .48f, .7f);
            lr.sortingOrder = -9;
            lip.transform.localPosition = new Vector3(-side * .5f + side * .012f, 0f, 0f);
            lip.transform.localScale = new Vector3(.025f, 1f, 1f);
        }
        // where the ship flies (a marker only)
        var ship = new GameObject("ship");
        var s = ship.AddComponent<SpriteRenderer>();
        s.sprite = sprite;
        s.color = new Color(.9f, .95f, 1f, .5f);
        s.sortingOrder = -8;
        ship.transform.position = new Vector3(0f, -3.5f, 0f);
        ship.transform.localScale = new Vector3(.45f, .45f, 1f);
        ship.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
        return cam;
    }

    static void Attack(string dir, int world, int index)
    {
        var cam = Scene(world);
        var boss = BossCatalog.ForWorld(world);
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        e.Actor.ForcedAttack = index;
        for (int i = 0; i < 400 && e.Actor.AttacksStarted == 0; i++) e.Step(Dt, 1f);

        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        string stem = Path.Combine(dir, "bossatk-" + boss.artKey.ToLower() + "-" + index);
        var csv = new System.Text.StringBuilder("frame,t,state,shots,beams,live,bounces,sparks\n");
        int frames = Mathf.RoundToInt(Seconds / Dt);
        for (int f = 0; f <= frames; f++)
        {
            if (f > 0) e.Step(Dt, 1f);
            if (e.State != BossEncounter.Phase.Fight) break;
            if (f % 2 != 0) continue;
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            png.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(stem + "-" + (f / 2).ToString("000") + ".png", png.EncodeToPNG());
            int bounces = 0, live = 0;
            foreach (var p in e.Pool.Shots) if (p != null && p.Active) bounces += p.Bounces;
            foreach (var b in e.Pool.Beams) if (b != null && b.Live) live++;
            string state = e.Actor.Telegraphing ? "tell" : e.Actor.CurrentAttack != null ? "fire" : "rest";
            csv.Append(f / 2).Append(',').Append((f * Dt).ToString("F2")).Append(',').Append(state).Append(',')
               .Append(e.Pool.ActiveShots).Append(',').Append(e.Pool.ActiveBeams).Append(',').Append(live).Append(',')
               .Append(bounces).Append(',').Append(e.Pool.SparksPlayed).Append('\n');
        }
        File.WriteAllText(stem + ".csv", csv.ToString());
        File.WriteAllText(stem + ".txt", boss.name + " -- " + boss.attacks[index].name + " (" +
                          string.Join("+", boss.attacks[index].emitters) + ", " + Rail(boss.attacks[index]) + ")");
        cam.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
        Debug.Log("[BOSSATK-PREVIEW] " + stem);
    }

    static string Rail(BossAttack a)
    {
        if (a.kind == BossAttackKind.Beam) return "laser, stops at the rails";
        switch (a.rail)
        {
            case BossRailMode.Bounce: return "ricochets x" + a.bounces;
            case BossRailMode.Pass: return "passes the rails";
            default: return "splashes on the rails";
        }
    }
}
