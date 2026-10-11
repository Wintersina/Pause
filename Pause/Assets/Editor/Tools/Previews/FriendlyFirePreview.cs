using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A rail mine's laser is already burning when an enemy flies into it: the enemy is destroyed and the
// small "FRIENDLY FIRE" word appears (the HUD's word is a play-mode UI element; in this batch preview a
// TextMesh stands in for it, fed by FriendlyFire.WordHook, same text and colour).
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod FriendlyFirePreview.Run
//   (writes $FRIENDLY_FIRE_PREVIEW_DIR/friendly-fire-preview.png, else Builds/FriendlyFirePreview)
public static class FriendlyFirePreview
{
    const int Width = 540, Height = 960;
    const float Dt = 1f / 60f;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("FRIENDLY_FIRE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/FriendlyFirePreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try { Capture(dir); }
            finally
            {
                FriendlyFire.WordHook = null;
                EnemyThreat.ForceShooting = false;
                EnemyThreat.Reset();
                EliteSystem.Clear();
                EliteSystem.PlayerOverride = null;
                SpawnSpace.ClockOverride = null;
                RailMineLaser.AngleOverride = null;
            }
        }
        EditorApplication.Exit(0);
    }

    static void Capture(string dir)
    {
        const int world = 0;   // Space
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        EliteSystem.Clear();
        EnemyThreat.Reset();
        FriendlyFire.OnSceneLoaded("gameS1");
        FriendlyFire.ResetCounters();
        FriendlyFire.ResetHostileCounters();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var backdrop = new GameObject("~Backdrop").AddComponent<WorldBackdrop>();
        backdrop.Show("Space", false);
        for (int i = 0; i < 600; i++) backdrop.Step(Dt);
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, 1f, 0f);
        EliteSystem.PlayerOverride = pilot;
        HazardRuntime.Ensure().ClearAll();
        var rt = new RenderTexture(Width, Height, 24);
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;

        // the stand-in word
        TextMesh word = null;
        FriendlyFire.WordHook = (text, at) =>
        {
            var go = new GameObject("~FriendlyFireWord");
            word = go.AddComponent<TextMesh>();
            word.text = text;
            word.fontSize = 48;
            word.characterSize = .06f;
            word.anchor = TextAnchor.MiddleCenter;
            word.color = DeathCombo.FlashColour;
            go.transform.position = new Vector3(at.x, at.y + .6f, -1f);
        };

        EnemyThreat.ForceShooting = true;
        RailMineLaser.AngleOverride = 0f;
        float clock = 100f;
        SpawnSpace.ClockOverride = clock;
        var def = EnemyRoster.One(world, EnemyRole.Mine);
        float x = enmiesOnBoard.WorldRailX(false);
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, 1f, 0f);
        rail.AddComponent<RailLaneScroller>();
        var mineGo = EnemyFactory.Create(def, new Vector3(x, 1f, 0f), Quaternion.identity);
        var brain = mineGo.GetComponent<EnemyBrain>();
        var mount = mineGo.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        brain.TargetOverride = pilot;
        var fb = mineGo.GetComponent<EnemyFlipbook>();

        GameObject fighter = null;
        var shots = new Texture2D[3];
        int taken = 0;
        float sinceBeam = -1f, sinceKill = -1f;
        bool beamSeen = false;
        for (int i = 0; i < 60 * 14 && taken < 3; i++)
        {
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            brain.Step(Dt);
            mount.SendMessage("LateUpdate");
            if (fb != null) fb.Advance(Dt);
            EliteSystem.Step(Dt);
            HazardRuntime.Ensure().Step(Dt);
            var l = brain.Laser;
            if (l != null) l.Place();
            bool beam = l != null && l.State == RailMineLaser.Phase.Beam;
            if (beam && !beamSeen)
            {
                beamSeen = true;
                sinceBeam = 0f;
                // the beam is already burning: the enemy only now flies in from above
                fighter = EnemyFactory.Create(EnemyRoster.One(world, EnemyRole.Fighter), new Vector3(-1.1f, l.Y + 1.15f, 0f), Quaternion.identity);
                ClearTarget.Ensure(fighter);
                var fbrain = fighter.GetComponent<EnemyBrain>();
                if (fbrain != null) fbrain.enabled = false;
                shots[taken++] = Snap(cam, rt, tex);   // 1: beam lit, the enemy still above it
            }
            if (sinceBeam >= 0f && fighter != null && fighter.activeInHierarchy && fighter.TryGetComponent(out ClearTarget ct) && ct.enabled)
            {
                sinceBeam += Dt;
                fighter.transform.position += Vector3.down * 5f * Dt;
                if (taken == 1 && fighter.transform.position.y < l.Y + .5f) shots[taken++] = Snap(cam, rt, tex);   // 2: closing on the beam
            }
            else if (sinceBeam >= 0f && taken == 2)
            {
                sinceKill += Dt;
                if (sinceKill < 0f) sinceKill = 0f;
                if (sinceKill >= .12f) shots[taken++] = Snap(cam, rt, tex);   // 3: destroyed, the word up
            }
        }
        Debug.Log("[FF-PREVIEW] frames " + taken + ", friendly-fire kills " + FriendlyFire.HostileKills + ", words " + FriendlyFire.Popups);
        if (taken < 3) { Debug.LogError("[FF-PREVIEW] FAIL did not capture the sequence"); return; }
        var sheet = new Texture2D(Width * 3, Height, TextureFormat.RGB24, false);
        for (int k = 0; k < 3; k++) sheet.SetPixels(k * Width, 0, Width, Height, shots[k].GetPixels());
        File.WriteAllBytes(Path.Combine(dir, "friendly-fire-preview.png"), sheet.EncodeToPNG());
    }

    static Texture2D Snap(Camera cam, RenderTexture rt, Texture2D tex)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = old;
        var copy = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        copy.SetPixels(tex.GetPixels());
        copy.Apply();
        return copy;
    }
}
