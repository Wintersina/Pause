using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// The boost music: the user's "Hyperspace Loop" cut from 26 s on, replacing the
// old BoostingMusic.wav under the same path and guid, and never restarted by a
// second pickup. The AudioSource loops silently from scene start; a pickup only
// raises its volume. The one rewind is a cold start after a few silent seconds.
public static class BoostMusicContinuityTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BMC] PASS  " : "[BMC] FAIL  ") + what);
        if (!ok) fails++;
    }

    const string ClipPath = "Assets/Audio/Music/BoostingMusic.mp3";
    const string Guid = "96cc59c5eca368b4180a8501f68b7acb";
    const float SourceSeconds = 207.9735f;   // the Hyperspace Loop m4a, decoded
    const float CutAt = 26f;

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        float inv = collisionDetection.invTimer;
        bool was = musicControl.boostMusicChanger;
        try { Body(); }
        finally { collisionDetection.invTimer = inv; musicControl.boostMusicChanger = was; }
        Debug.Log("[BMC] failures: " + fails);
        return fails;
    }

    static void Body()
    {
        // ---- the asset: replaced in place, old data gone ----
        Check("BoostingMusic.mp3 keeps its guid", AssetDatabase.AssetPathToGUID(ClipPath) == Guid);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        Check("clip loads", clip != null);
        if (clip != null)
        {
            float want = SourceSeconds - CutAt;
            Check("clip length is source minus 26 s (" + clip.length + " vs " + want + ")",
                  Mathf.Abs(clip.length - want) < 0.1f);
            Check("clip is stereo", clip.channels == 2);
            Check("clip is far longer than the old 40 s file (old data gone)", clip.length > 100f);
        }
        long size = new FileInfo(ClipPath).Length;
        Check("file is not the old 7,301,622 byte wav", size != 7301622L);

        // The original is kept outside the build: a tilde folder Unity ignores, no meta.
        Check("the old boost track is gone (master deleted it; no Legacy~ copy, not imported)",
              !File.Exists("Assets/Audio/Legacy~/BoostingMusic_old.wav") && AssetDatabase.AssetPathToGUID("Assets/Audio/Legacy~/BoostingMusic_old.wav") == "");

        var imp = AssetImporter.GetAtPath(ClipPath) as AudioImporter;
        Check("importer found", imp != null);
        if (imp != null)
        {
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var s = imp.GetOverrideSampleSettings(platform);
                Check(platform + " is Vorbis ~0.6, streaming",
                      s.compressionFormat == AudioCompressionFormat.Vorbis
                      && Mathf.Abs(s.quality - 0.6f) < 0.01f
                      && s.loadType == AudioClipLoadType.Streaming);
            }
        }

        // ---- scenes still point at it and loop ----
        foreach (var scene in new[] { "Assets/Scenes/gameS1.unity", "Assets/Scenes/tutorialS5.unity" })
        {
            string text = File.ReadAllText(scene);
            var m = Regex.Match(text, @"m_Name: BoostingMusic[\s\S]*?m_Resource: \{fileID: 8300000, guid: (\w+)[\s\S]*?Loop: (\d)");
            Check(scene + " BoostingMusic uses the clip and loops", m.Success && m.Groups[1].Value == Guid && m.Groups[2].Value == "1");
        }

        // ---- code paths: nothing restarts the track on a pickup ----
        string mc = File.ReadAllText("Assets/Scripts/Audio/musicControl.cs");
        string code = Regex.Replace(mc, @"//.*", "");
        Check("musicControl never Play()s or Stop()s", !Regex.IsMatch(code, @"\.(Play|Stop|PlayOneShot|UnPause|Pause)\s*\("));
        Check("musicControl rewinds in exactly one place (cold start)", Regex.Matches(code, @"boostSound\.time\s*=").Count == 1);
        string cd = Regex.Replace(File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs"), @"//.*", "");
        Check("pickups never touch the BoostingMusic source", !Regex.IsMatch(cd, @"BoostingMusic|musicControl\.\w+\.time"));

        // ---- behaviour, with the real musicControl ----
        var boostGo = new GameObject("BoostingMusic");
        var moveGo = new GameObject("MovingMusic");
        var ctlGo = new GameObject("~musicControlTest");
        try
        {
            var boost = boostGo.AddComponent<AudioSource>();
            moveGo.AddComponent<AudioSource>();
            var ctl = ctlGo.AddComponent<musicControl>();
            ctl.SendMessage("Start");
            Check("control found the boost source", ctl.boostSound == boost);

            musicControl.ColdRestarts = 0;
            // Silent for long enough, then a first boost: one cold restart.
            boost.volume = 0f;
            musicControl.boostMusicChanger = false;
            for (int i = 0; i < 10; i++) ctl.Tick(0.5f);
            Check("no restart while not boosting", musicControl.ColdRestarts == 0);
            musicControl.boostMusicChanger = true;
            collisionDetection.invTimer = 5.8f;
            ctl.Tick(0.016f);
            Check("first boost after a long silence restarts the track once", musicControl.ColdRestarts == 1);

            // The mix raises the volume; further pickups while it is audible never rewind.
            boost.volume = 0.3f;
            for (int pickup = 0; pickup < 5; pickup++)
            {
                collisionDetection.invTimer = 5.8f;
                for (int i = 0; i < 30; i++) ctl.Tick(0.016f);
            }
            Check("repeat pickups while boosting never rewind", musicControl.ColdRestarts == 1);

            // Pickup during the fade-out (volume low but not zero): carries on.
            boost.volume = 0.05f;
            collisionDetection.invTimer = 5.8f;
            for (int i = 0; i < 100; i++) ctl.Tick(0.1f);
            Check("pickup during the fade-out never rewinds", musicControl.ColdRestarts == 1);

            // Silent for just a moment between two boosts: carries on.
            boost.volume = 0f;
            ctl.Tick(1f);
            ctl.Tick(0.5f);
            Check("a short gap (1.5 s) between boosts never rewinds", musicControl.ColdRestarts == 1);
            boost.volume = 0.2f; ctl.Tick(0.016f);

            // A long silence then another boost: one more cold restart.
            boost.volume = 0f;
            musicControl.boostMusicChanger = false;
            for (int i = 0; i < 10; i++) ctl.Tick(1f);
            musicControl.boostMusicChanger = true;
            ctl.Tick(0.016f);
            Check("a boost after another long silence restarts once more", musicControl.ColdRestarts == 2);
        }
        finally
        {
            Object.DestroyImmediate(ctlGo);
            Object.DestroyImmediate(boostGo);
            Object.DestroyImmediate(moveGo);
        }
    }
}
