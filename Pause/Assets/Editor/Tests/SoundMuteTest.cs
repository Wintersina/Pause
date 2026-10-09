using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The player-facing SOUND setting: the Options row (SoundOptions, clicked
// through its real onClick) and the central switch (SoundSettings ->
// AudioListener.volume). Default on; muting silences everything including
// sounds started afterwards; the choice survives a restart and scene loads;
// un-muting restores; gameplay never waits on audio.
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
            AudioListener.volume = listener;
            EnemyDeathAudio.Simulate = simulate;
        }
        Debug.Log("[SND] failures: " + fails);
        return fails;
    }

    static GameObject Obj(string n) { return SceneUtil.FindAny(n); }
    static string Label() { var t = Obj(SoundOptions.RowName).GetComponentInChildren<Text>(true); return t.text; }
    static void Click() { Obj(SoundOptions.RowName).GetComponent<Button>().onClick.Invoke(); }

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
        EnemyDeathAudio.PlayKey(Key, EnemyRole.Rock, true);
        BossWarningAudio.Play(BossWarningBeat.Announce, 0);
    }

    static void Restart()
    {
        AudioListener.volume = 1f;   // a fresh process
        typeof(SoundSettings).GetMethod("Init", Stat).Invoke(null, null);
    }

    static void Body()
    {
        PlayerPrefs.DeleteKey(SoundSettings.Key);
        AudioListener.volume = 1f;

        // default
        Check("default: sound is on", !SoundSettings.Muted);
        Open(false);
        Check("row exists", Obj(SoundOptions.RowName) != null);
        Check("default row reads SOUND: ON (" + Label() + ")", Label() == "SOUND: ON");

        // geometry: finger-sized, clear of the other rows
        var rt = (RectTransform)Obj(SoundOptions.RowName).transform;
        Check("hit area >= 88 tall + padding", rt.sizeDelta.y >= 88f);
        foreach (var other in new[] { "PlayerStartWorld", "PlayerStartWorldLocked", "pullUpLeaderBoard", "Tutorial", "BackButton", "DeveloperToggle", "DeveloperStartWorld", "DeveloperBossRush" })
        {
            var go = Obj(other);
            if (go == null) continue;
            var r = (RectTransform)go.transform;
            bool overlap = Mathf.Abs(r.anchoredPosition.y - rt.anchoredPosition.y) < (r.sizeDelta.y + rt.sizeDelta.y) * .5f;
            Check("no overlap with " + other, !overlap);
        }

        // mute
        Click();
        Check("click: muted + saved", SoundSettings.Muted && PlayerPrefs.GetInt(SoundSettings.Key, 0) == 1);
        Check("click: row reads SOUND: OFF (" + Label() + ")", Label() == "SOUND: OFF");
        Check("click: AudioListener.volume == 0", AudioListener.volume == 0f);

        // sounds started afterwards stay silent
        PlayEverything();
        var late = new GameObject("late").AddComponent<AudioSource>();
        late.volume = 1f;
        Check("sounds started while muted: listener still 0", AudioListener.volume == 0f);
        Object.DestroyImmediate(late.gameObject);

        // something resets the listener: re-applied on scene load / focus
        AudioListener.volume = 1f;
        typeof(SoundSettings).GetMethod("OnSceneLoaded", Stat).Invoke(null, new object[] { UnityEngine.SceneManagement.SceneManager.GetActiveScene(), UnityEngine.SceneManagement.LoadSceneMode.Single });
        Check("scene load re-applies the mute", AudioListener.volume == 0f);
        AudioListener.volume = 1f;
        typeof(SoundSettings).GetMethod("OnFocus", Stat).Invoke(null, new object[] { true });
        Check("focus change re-applies the mute", AudioListener.volume == 0f);

        // persistence: restart + reopened Options
        Restart();
        Check("restart: muted before the first sound", AudioListener.volume == 0f && SoundSettings.Muted);
        Open(false);
        Check("restart: row still SOUND: OFF", Label() == "SOUND: OFF");

        // game timing: anything that consumed audio state is unaffected; the
        // music sources are not paused or stopped by muting (only the listener).
        var music = new GameObject("music").AddComponent<AudioSource>();
        bool playingBefore = music.isPlaying;
        SoundSettings.Apply();
        Check("muting does not pause/stop sources", music.isPlaying == playingBefore);
        Object.DestroyImmediate(music.gameObject);

        // unmute
        Click();
        Check("unmute: not muted, key cleared", !SoundSettings.Muted && !PlayerPrefs.HasKey(SoundSettings.Key));
        Check("unmute: AudioListener.volume == 1", AudioListener.volume == 1f);
        Check("unmute: row reads SOUND: ON", Label() == "SOUND: ON");
        Restart();
        Check("restart unmuted: sound on", AudioListener.volume == 1f);

        // developer builds: the row sits on top of the developer stack
        if (DeveloperUnlocks.Available)
        {
            Open(true);
            var r2 = (RectTransform)Obj(SoundOptions.RowName).transform;
            Check("developer: row above the developer rows", r2.anchoredPosition.y > 47f + 44f);
        }

        // cloud snapshot: untouched by the preference (device-local)
        PlayerPrefs.SetInt(SoundSettings.Key, 1);
        var snap = ProgressSnapshot.Capture(0);
        snap.Apply();
        Check("cloud apply leaves the device's mute alone", SoundSettings.Muted);
    }
}
