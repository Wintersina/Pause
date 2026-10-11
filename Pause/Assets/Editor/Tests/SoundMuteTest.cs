using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The player-facing audio settings: the Options rows (SoundOptions: EFFECTS and
// MUSIC, each a toggle plus a volume slider, driven through their real
// onClick / onValueChanged) and the central bus (SoundSettings: effects =
// AudioListener.volume, music sources scaled by the music gain). Default on and
// full; toggles and sliders persist across a restart and scene loads; the old
// single SOUND mute migrates to both toggles off; the in-run music (musicControl)
// follows the music setting and no longer switches track on a boost; gameplay
// never waits on audio.
public static class SoundMuteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SND] PASS  " : "[SND] FAIL  ") + what);
        if (!ok) fails++;
    }

    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float listener = AudioListener.volume;
        bool simulate = EnemyDeathAudio.Simulate;
        try { Body(); }
        finally
        {
            ClearPrefs();
            AudioListener.volume = listener;
            EnemyDeathAudio.Simulate = simulate;
            typeof(SoundSettings).GetMethod("Init", Stat).Invoke(null, null);
        }
        Debug.Log("[SND] failures: " + fails);
        return fails;
    }

    static void ClearPrefs()
    {
        foreach (var k in new[] { SoundSettings.Key, SoundSettings.SfxOnKey, SoundSettings.MusicOnKey,
                                  SoundSettings.SfxVolumeKey, SoundSettings.MusicVolumeKey })
            PlayerPrefs.DeleteKey(k);
    }

    static GameObject Obj(string n) { return SceneUtil.FindAny(n); }
    static string Label(string row) { return Obj(row).GetComponentInChildren<Text>(true).text; }
    static void Click(string row) { Obj(row).GetComponent<Button>().onClick.Invoke(); }
    static Slider Sl(string n) { return Obj(n).GetComponent<Slider>(); }

    static void Open(bool developer)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
        DeveloperUnlocks.SetEnabled(developer);
        if (DeveloperUnlocks.Available)
        {
            var dev = new GameObject("~DeveloperOptions").AddComponent<DeveloperOptions>();
            dev.SendMessage("Start");
        }
        var sw = new GameObject("~StartWorldOptions").AddComponent<StartWorldOptions>();
        sw.SendMessage("Start");
        var o = new GameObject("~SoundOptions").AddComponent<SoundOptions>();
        o.SendMessage("Start");
    }

    // Plays through the game's real sound entry points.
    const string Key = "ember_mine";
    static void PlayEverything()
    {
        EnemyDeathAudio.Simulate = true;
        EnemyDeathAudio.ResetVoices();
        EnemyDeathAudio.PlayAuthored(Key, .8f);
        EnemyDeathAudio.PlayKey(Key, EnemyRole.Rock);
        BossWarningAudio.Play(BossWarningBeat.Announce, 0);
    }

    static void Restart()
    {
        AudioListener.volume = 1f;   // a fresh process
        typeof(SoundSettings).GetMethod("Init", Stat).Invoke(null, null);
    }

    static bool Near(float a, float b) { return Mathf.Abs(a - b) < 1e-4f; }

    static void Body()
    {
        ClearPrefs();
        AudioListener.volume = 1f;

        // default
        Check("default: effects and music on, both full", SoundSettings.SfxOn && SoundSettings.MusicOn &&
              Near(SoundSettings.SfxVolume, 1f) && Near(SoundSettings.MusicVolume, 1f) && !SoundSettings.Muted);
        Open(false);
        Check("rows exist", Obj(SoundOptions.RowName) != null && Obj(SoundOptions.MusicRowName) != null &&
              Sl(SoundOptions.EffectsSliderName) != null && Sl(SoundOptions.MusicSliderName) != null);
        Check("default rows read EFFECTS / MUSIC over ON (" + Label(SoundOptions.RowName).Replace("\n", "|") + " / " + Label(SoundOptions.MusicRowName).Replace("\n", "|") + ")",
              Label(SoundOptions.RowName) == "EFFECTS\nON" && Label(SoundOptions.MusicRowName) == "MUSIC\nON");
        Check("default sliders at full", Near(Sl(SoundOptions.EffectsSliderName).value, 1f) && Near(Sl(SoundOptions.MusicSliderName).value, 1f));

        // geometry: finger-sized, clear of the other rows and of each other
        var names = new List<string> { "PlayerStartWorld", "PlayerStartWorldLocked", "pullUpLeaderBoard", "Tutorial", "BackButton",
            "DeveloperToggle", "DeveloperStartWorld", "DeveloperBossRush" };
        foreach (var mine in new[] { SoundOptions.RowName, SoundOptions.MusicRowName, SoundOptions.EffectsSliderName, SoundOptions.MusicSliderName })
        {
            var rt = (RectTransform)Obj(mine).transform;
            Check(mine + ": hit area >= 88 tall", rt.sizeDelta.y >= 88f);
                        foreach (var other in names)
            {
                var go = Obj(other);
                if (go == null) continue;
                var r = (RectTransform)go.transform;
                bool overlap = Mathf.Abs(r.anchoredPosition.y - rt.anchoredPosition.y) < (r.sizeDelta.y + rt.sizeDelta.y) * .5f;
                Check(mine + ": no overlap with " + other, !overlap);
            }
        }
        var tg = (RectTransform)Obj(SoundOptions.RowName).transform;
        var sl = (RectTransform)Obj(SoundOptions.EffectsSliderName).transform;
        var tm = (RectTransform)Obj(SoundOptions.MusicRowName).transform;
        var sm = (RectTransform)Obj(SoundOptions.MusicSliderName).transform;
        var four = new[] { tg, sl, tm, sm };
        bool sideBySide = true;
        for (int i = 0; i < 4; i++)
        {
            if (!Near(four[i].anchoredPosition.y, tg.anchoredPosition.y)) sideBySide = false;
            if (i > 0 && four[i - 1].anchoredPosition.x + four[i - 1].sizeDelta.x * .5f > four[i].anchoredPosition.x - four[i].sizeDelta.x * .5f) sideBySide = false;
        }
        Check("effects toggle, effects slider, music toggle, music slider: one row, left to right, none overlapping", sideBySide);
        Check("the row stays inside +-270 units (curved-edge safe areas)", tg.anchoredPosition.x - tg.sizeDelta.x * .5f >= -270f && sm.anchoredPosition.x + sm.sizeDelta.x * .5f <= 270f);

        // a music source on the bus
        var music = new GameObject("StoreMusic").AddComponent<AudioSource>();
        music.volume = .8f;
        SoundSettings.RegisterMusic(music);
        Check("music source opts out of the listener and starts at its authored volume", music.ignoreListenerVolume && Near(music.volume, .8f));

        // effects off
        Click(SoundOptions.RowName);
        Check("effects click: off + saved", !SoundSettings.SfxOn && PlayerPrefs.GetInt(SoundSettings.SfxOnKey, 1) == 0);
        Check("effects click: row reads EFFECTS\nOFF (" + Label(SoundOptions.RowName).Replace("\n", "|") + ")", Label(SoundOptions.RowName) == "EFFECTS\nOFF");
        Check("effects click: AudioListener.volume == 0", AudioListener.volume == 0f);
        Check("effects off leaves music on and audible", SoundSettings.MusicOn && Near(music.volume, .8f) && Label(SoundOptions.MusicRowName) == "MUSIC\nON");

        // sounds started afterwards stay silent
        PlayEverything();
        var late = new GameObject("late").AddComponent<AudioSource>();
        late.volume = 1f;
        Check("sounds started while effects are off: listener still 0", AudioListener.volume == 0f);
        Object.DestroyImmediate(late.gameObject);

        // effects slider while off turns the bus back on at that level
        Sl(SoundOptions.EffectsSliderName).value = .4f;
        Check("effects slider: volume saved, bus back on, listener at the level",
              Near(SoundSettings.SfxVolume, .4f) && SoundSettings.SfxOn && Near(AudioListener.volume, .4f) &&
              Near(PlayerPrefs.GetFloat(SoundSettings.SfxVolumeKey, 1f), .4f));
        Check("effects slider leaves the music alone", Near(music.volume, .8f));
        Check("effects row reads EFFECTS\nON again", Label(SoundOptions.RowName) == "EFFECTS\nON");

        // music slider and toggle
        Sl(SoundOptions.MusicSliderName).value = .5f;
        Check("music slider: volume saved, source scaled (.8 * .5), listener (effects) untouched",
              Near(SoundSettings.MusicVolume, .5f) && Near(music.volume, .4f) && Near(AudioListener.volume, .4f));
        bool playing = music.isPlaying;
        Click(SoundOptions.MusicRowName);
        Check("music click: off, source silent, row reads MUSIC\nOFF, still the same play state",
              !SoundSettings.MusicOn && music.volume == 0f && Label(SoundOptions.MusicRowName) == "MUSIC\nOFF" && music.isPlaying == playing);
        Check("music off leaves the effects bus alone", Near(AudioListener.volume, .4f) && SoundSettings.SfxOn);

        // something resets the listener: re-applied on scene load / focus
        AudioListener.volume = 1f;
        typeof(SoundSettings).GetMethod("OnSceneLoaded", Stat).Invoke(null, new object[] { UnityEngine.SceneManagement.SceneManager.GetActiveScene(), UnityEngine.SceneManagement.LoadSceneMode.Single });
        Check("scene load re-applies the effects level", Near(AudioListener.volume, .4f));
        AudioListener.volume = 1f;
        typeof(SoundSettings).GetMethod("OnFocus", Stat).Invoke(null, new object[] { true });
        Check("focus change re-applies the effects level", Near(AudioListener.volume, .4f));

        // persistence: restart + reopened Options
        Restart();
        Check("restart: effects at .4, music off, before the first sound", Near(AudioListener.volume, .4f) && !SoundSettings.MusicOn && Near(SoundSettings.MusicVolume, .5f));
        Open(false);
        Check("restart: rows and sliders restored",
              Label(SoundOptions.RowName) == "EFFECTS\nON" && Label(SoundOptions.MusicRowName) == "MUSIC\nOFF" &&
              Near(Sl(SoundOptions.EffectsSliderName).value, .4f) && Near(Sl(SoundOptions.MusicSliderName).value, .5f));
        if (music != null) Object.DestroyImmediate(music.gameObject);   // the scene reload already took it

        // music back on
        Click(SoundOptions.MusicRowName);
        Check("music on again", SoundSettings.MusicOn && !PlayerPrefs.HasKey(SoundSettings.MusicOnKey));

        // old single SOUND: OFF migrates to both toggles off
        ClearPrefs();
        PlayerPrefs.SetInt(SoundSettings.Key, 1);
        Restart();
        Check("legacy soundMuted=1: migrated to effects + music off, legacy key gone, listener 0",
              !SoundSettings.SfxOn && !SoundSettings.MusicOn && SoundSettings.Muted && !PlayerPrefs.HasKey(SoundSettings.Key) && AudioListener.volume == 0f);
        ClearPrefs();
        PlayerPrefs.SetInt(SoundSettings.Key, 0);
        Restart();
        Check("legacy soundMuted=0: everything stays on", SoundSettings.SfxOn && SoundSettings.MusicOn && !PlayerPrefs.HasKey(SoundSettings.Key) && AudioListener.volume == 1f);
        SoundSettings.SetMuted(true);
        Check("SetMuted(true) silences both buses; SetMuted(false) restores", SoundSettings.Muted && AudioListener.volume == 0f);
        SoundSettings.SetMuted(false);
        Check("SetMuted(false) restores both", !SoundSettings.Muted && AudioListener.volume == 1f);

        // the in-run music follows the music setting and ignores the boost
        {
            var src = new GameObject("MovingMusic").AddComponent<AudioSource>();
            src.volume = 1f;
            var ctl = new GameObject("~musicControlTest").AddComponent<musicControl>();
            ctl.SendMessage("Start");
            ctl.SendMessage("Update");
            Check("musicControl: full music setting -> full volume, listener opted out", Near(src.volume, 1f) && src.ignoreListenerVolume);
            SoundSettings.SetMusicVolume(.3f);
            ctl.SendMessage("Update");
            Check("musicControl: follows the music volume slider (.3)", Near(src.volume, .3f));
            SoundSettings.SetMusicOn(false);
            ctl.SendMessage("Update");
            Check("musicControl: music off -> silent", src.volume == 0f);
            SoundSettings.SetMusicOn(true);
            SoundSettings.SetMusicVolume(1f);
            ctl.SendMessage("Update");
            Check("musicControl: back on -> full", Near(src.volume, 1f));
            Check("no boost-music state is left on musicControl",
                  typeof(musicControl).GetField("boostMusicChanger", Stat) == null && typeof(musicControl).GetField("boostSound") == null);
            Object.DestroyImmediate(ctl.gameObject);
            Object.DestroyImmediate(src.gameObject);
        }
        foreach (var scene in new[] { "gameS1", "tutorialS5" })
            Check(scene + ": no BoostingMusic source in the scene",
                  !System.IO.File.ReadAllText("Assets/Scenes/" + scene + ".unity").Contains("m_Name: BoostingMusic"));

        // developer builds: the rows sit on top of the developer stack
        if (DeveloperUnlocks.Available)
        {
            Open(true);
            var r2 = (RectTransform)Obj(SoundOptions.RowName).transform;
            var r3 = (RectTransform)Obj(SoundOptions.MusicRowName).transform;
            Check("developer: the row above the developer rows", r2.anchoredPosition.y > 47f + 88f && Near(r3.anchoredPosition.y, r2.anchoredPosition.y));
        }

        // cloud snapshot: untouched by the preference (device-local)
        SoundSettings.SetMuted(true);
        var snap = ProgressSnapshot.Capture(0);
        snap.Apply();
        Check("cloud apply leaves the device's audio settings alone", SoundSettings.Muted);
    }
}
