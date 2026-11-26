using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Capture the actual musical director (not the fallback timed attack tables).
public static class BossIdentityReview
{
    sealed class Music : IMusicBeatSource
    {
        public bool Active => true;
        public double BeatNow { get; set; }
        public double SecondsPerBeat => .4;
        public int BeatsPerBar => 4;
        public int DownbeatBeat => 0;
        public MusicMoment MusicNow => new MusicMoment { energy = .8f, bass = .8f, accent = .8f, kind = "drive" };
        public MusicMoment MusicAhead(double beats) => MusicNow;
    }
    public static void Export()
    {
        const string folder = "/private/tmp/pause-boss-identity-review";
        Directory.CreateDirectory(folder);
        using var sandbox = new TestHarness.Sandbox();
        for (int world = 0; world < BossCatalog.All.Length; world++)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BossEncounter.ResetRun();
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>(); camera.tag = "MainCamera";
            camera.orthographic = true; camera.orthographicSize = 5f; camera.aspect = .6f;
            camera.transform.position = new Vector3(0, 0, -10); camera.backgroundColor = new Color(.025f, .035f, .06f); camera.clearFlags = CameraClearFlags.SolidColor;
            buttonClicks.playerDied = false; score.pauseCounter = 0; moveBackGround.speed = .37f;
            var boss = BossCatalog.ForWorld(world); var actor = BossActor.Spawn(boss);
            var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
            var music = new Music(); actor.BeginFight(); actor.BeatOverride = music;
            var target = new RenderTexture(360, 600, 24); camera.targetTexture = target;
            var png = new Texture2D(360, 600, TextureFormat.RGB24, false);
            for (int frame = 0; frame <= 360; frame++)
            {
                music.BeatNow = frame / 60.0 / .4;
                actor.StepFight(1f / 60f, 1f / 60f, new Vector3(0f, -3.2f), .8f, pool); pool.Step(1f / 60f);
                if (frame < 120 || frame % 30 != 0) continue;
                camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
                png.ReadPixels(new Rect(0, 0, 360, 600), 0, 0); png.Apply(); RenderTexture.active = previous;
                File.WriteAllBytes(folder + "/" + boss.artKey + "-" + frame + ".png", png.EncodeToPNG());
            }
            camera.targetTexture = null; Object.DestroyImmediate(png); Object.DestroyImmediate(target); pool.Dispose(); Object.DestroyImmediate(actor.gameObject);
        }
        Debug.Log("[IDENTITY-REVIEW] exported " + folder);
        DockProgressionReview.Export();
    }
}
