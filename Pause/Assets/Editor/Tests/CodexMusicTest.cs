using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The Codex MUSIC tab: which tracks exist, when one unlocks (heard in a real
// run, persisted with the codex's own PrefsKey, never counted in the entry
// totals), locked tracks reading "???", play / stop / now-playing, and that a
// playing track pauses the menu music and gives it back on stop, on leaving
// the tab and on closing the codex. Playback runs in CodexMusicPlayer.Simulate
// (no audio device); the ducked source is a real AudioSource on a stand-in
// "MainMenuMusic" object.
public static class CodexMusicTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CMU] PASS  " : "[CMU] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly Rect[] Safes =
    {
        new Rect(-400f, -640f, 800f, 1280f),
        new Rect(-400f, -1067f, 800f, 2133f),
        new Rect(-1138f, -640f, 2276f, 1280f),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        PlayerPrefs.DeleteKey(SoundSettings.Key);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        Codex.Reload();
        bool simulate = CodexMusicPlayer.Simulate;
        var gain = CodexMusicPlayer.Gain;
        CodexMusicPlayer.Simulate = true;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var es = new GameObject("TestEventSystem", typeof(EventSystem));
        CodexPanel panel = null;
        try
        {
            Catalogue();
            Unlocking();
            panel = CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
            panel.ApplyLayout(Safes[0]);
            Tab(panel);
            Playback(panel);
            Leaving(panel);
            Theme(panel);
            Volume(panel);
            Layouts(panel);
        }
        finally
        {
            CodexMusicPlayer.Stop();
            CodexMusicPlayer.Simulate = simulate;
            CodexMusicPlayer.Gain = gain;
            if (panel != null) Object.DestroyImmediate(panel.gameObject);
            Object.DestroyImmediate(es);
            DeveloperUnlocks.SetEnabled(false);
        }
        Debug.Log("[CMU] failures: " + fails);
        return fails;
    }

    static CodexTrack Ember { get { return CodexMusic.Find(CodexMusic.WorldId("Ember")); } }
    static CodexTrack Frost { get { return CodexMusic.Find(CodexMusic.WorldId("Frost")); } }

    static void Catalogue()
    {
        var all = CodexMusic.All;
        var ids = new System.Collections.Generic.HashSet<string>();
        bool unique = true, clips = true;
        foreach (var t in all)
        {
            unique &= ids.Add(t.id);
            if (t.resource != null) clips &= Resources.Load<AudioClip>(t.resource) != null;
        }
        int worlds = WorldManager.Worlds.Length, songs = 0;
        foreach (var w in WorldManager.Worlds) if (!string.IsNullOrEmpty(w.musicResource)) songs++;
        Check("the menu theme comes first, then the Credits and Codex songs, a theme per world with a song and a boss track per world", all[0].id == CodexMusic.MenuId && all[1].id == CodexMusic.CreditsId && all.Count == 3 + songs + worlds);
        Check("ids are unique and every world / boss resource loads", unique && clips);
        Check("the menu theme and the codex song are always unlocked, nothing else is on a fresh profile",
              CodexMusic.IsUnlocked(all[0]) && CodexMusic.IsUnlocked(CodexMusic.Find(CodexMusic.ThemeId)) && CodexMusic.UnlockedCount == 2);
        Check("the Credits song is listed as Boom Boom, locked until Credits plays it",
              CodexMusic.Find(CodexMusic.CreditsId).title == "Boom Boom" && !CodexMusic.IsUnlocked(CodexMusic.Find(CodexMusic.CreditsId)));
        Check("a locked track reads ???", CodexMusic.DisplayTitle(Ember) == "???");
        Check("worlds the loop cannot reach are not listed", CodexMusic.ListedCount == 3 + 2 * WorldManager.LiveWorldCount - (WorldManager.Worlds[WorldManager.LiveWorldCount - 1].musicResource == "" ? 1 : 0));
    }

    static void Unlocking()
    {
        int total = Codex.Total, found = Codex.DiscoveredCount;
        Check("playing a locked track is refused", !CodexMusicPlayer.Play(Ember.id) && !CodexMusicPlayer.IsPlaying);

        CodexMusic.HeardWorld(WorldManager.Worlds[3]);   // Ember
        Check("hearing a world's song unlocks its track", CodexMusic.IsUnlocked(Ember) && CodexMusic.DisplayTitle(Ember) == "Anvil Arpeggio");
        Check("... and only that track", !CodexMusic.IsUnlocked(Frost) && CodexMusic.UnlockedCount == 3);
        Check("... stored in the codex's own pref", PlayerPrefs.GetString(Codex.PrefsKey).Contains(Ember.id));
        Codex.Reload();
        Check("... and survives a reload", CodexMusic.IsUnlocked(Ember));
        Check("tracks never count in the entry totals", Codex.Total == total && Codex.DiscoveredCount == found);

        var boss = CodexMusic.Find(CodexMusic.BossId("Ember"));
        Check("a boss theme is locked until its fight plays it", !CodexMusic.IsUnlocked(boss));
        CodexMusic.HeardBoss(WorldManager.Worlds[3]);
        Check("hearing the boss track unlocks it", CodexMusic.IsUnlocked(boss));
        string before = PlayerPrefs.GetString(Codex.PrefsKey);
        CodexMusic.HeardWorld(WorldManager.Worlds[3]);
        Check("hearing it again changes nothing", PlayerPrefs.GetString(Codex.PrefsKey) == before);

        // a cloud restore / reset reaches it through the same key
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.OnExternalChange();
        Check("a reset codex locks them again", !CodexMusic.IsUnlocked(Ember));

        DeveloperUnlocks.SetEnabled(true);
        Check("developer mode shows every track without writing", CodexMusic.IsUnlocked(Frost) && PlayerPrefs.GetString(Codex.PrefsKey) == "");
        Codex.HearTrack(Frost.id);
        Check("... and records nothing", PlayerPrefs.GetString(Codex.PrefsKey) == "");
        DeveloperUnlocks.SetEnabled(false);
        Check("turning it off brings the real set back", !CodexMusic.IsUnlocked(Frost));
        CodexMusic.HeardWorld(WorldManager.Worlds[3]);
        CodexMusic.HeardBoss(WorldManager.Worlds[3]);
    }

    static void Tab(CodexPanel panel)
    {
        panel.ShowTab(CodexPanel.MusicTab);
        panel.SkipAnimations();
        var v = panel.Music;
        Check("the MUSIC tab opens its view and hides the grid", panel.MusicOpen && v.Active && !panel.AchievementsOpen);
        Check("one row per listed track", v.VisibleRows == CodexMusic.ListedCount);
        int ei = v.RowIndexOf(Ember.id), fi = v.RowIndexOf(Frost.id);
        Check("an unlocked row shows its song and PLAY", v.RowTitle(ei).text == "Anvil Arpeggio" && v.RowChip(ei).text == "PLAY" && v.RowButton(ei).interactable);
        Check("a locked row shows ??? and cannot be tapped", v.RowTitle(fi).text == "???" && !v.RowButton(fi).interactable &&
              v.RowSub(fi).text == CodexMusicView.LockedHint);
        Check("the header counts heard tracks", panel.Counter.text.StartsWith(CodexMusic.UnlockedCount + " / " + CodexMusic.ListedCount));
        Check("STOP is disabled while nothing plays", !v.StopButton.interactable);
        Check("the strip invites a tap", v.StripLine.text == CodexMusicView.IdleLine);
    }

    static void Playback(CodexPanel panel)
    {
        var v = panel.Music;
        var menuGo = new GameObject(CodexMusic.MenuSourceName, typeof(AudioSource));
        var menu = menuGo.GetComponent<AudioSource>();
        int ei = v.RowIndexOf(Ember.id), fi = v.RowIndexOf(Frost.id);

        v.RowButton(fi).onClick.Invoke();
        Check("tapping a locked row does nothing", !CodexMusicPlayer.IsPlaying);

        v.RowButton(ei).onClick.Invoke();
        Check("tapping an unlocked row plays it", CodexMusicPlayer.NowPlaying == Ember.id);
        Check("the menu music is paused for it", CodexMusicPlayer.DuckedSource == menu);
        Check("the row reads NOW PLAYING with a STOP chip and a red edge",
              v.RowSub(ei).text == "NOW PLAYING" && v.RowChip(ei).text == "STOP" && v.RowEdge(ei).color == CodexUi.Select);
        Check("the strip names it and STOP is live", v.StripCount.text == "NOW PLAYING" && v.StripLine.text.Contains("Anvil Arpeggio") && v.StopButton.interactable);

        v.RowButton(ei).onClick.Invoke();
        Check("tapping it again stops it and gives the menu music back", !CodexMusicPlayer.IsPlaying && !CodexMusicPlayer.IsDucking);
        Check("the row goes back to PLAY", v.RowChip(ei).text == "PLAY" && v.RowSub(ei).text == Ember.kind);

        v.RowButton(ei).onClick.Invoke();
        v.StopButton.onClick.Invoke();
        Check("the strip's STOP button stops it", !CodexMusicPlayer.IsPlaying && !CodexMusicPlayer.IsDucking && !v.StopButton.interactable);

        // another track while one plays replaces it with a single duck
        CodexMusic.HeardBoss(WorldManager.Worlds[3]);
        panel.Refresh();
        int bi = v.RowIndexOf(CodexMusic.BossId("Ember"));
        v.RowButton(ei).onClick.Invoke();
        v.RowButton(bi).onClick.Invoke();
        Check("starting another track replaces the first, still one duck", CodexMusicPlayer.NowPlaying == CodexMusic.BossId("Ember") &&
              CodexMusicPlayer.DuckedSource == menu && v.RowChip(ei).text == "PLAY" && v.RowChip(bi).text == "STOP");
        CodexMusicPlayer.Stop();
        Object.DestroyImmediate(menuGo);
    }

    static void Leaving(CodexPanel panel)
    {
        var v = panel.Music;
        var menuGo = new GameObject(CodexMusic.MenuSourceName, typeof(AudioSource));
        int ei = v.RowIndexOf(Ember.id);

        v.RowButton(ei).onClick.Invoke();
        panel.ShowTab(0);
        panel.SkipAnimations();
        Check("leaving the tab stops the track and restores the menu music", !panel.MusicOpen && !CodexMusicPlayer.IsPlaying && !CodexMusicPlayer.IsDucking);

        panel.ShowTab(CodexPanel.MusicTab);
        panel.SkipAnimations();
        v.RowButton(v.RowIndexOf(Ember.id)).onClick.Invoke();
        Check("playing again after coming back", CodexMusicPlayer.IsPlaying && CodexMusicPlayer.IsDucking);
        panel.Close();
        Check("closing the codex stops the track and restores the menu music", !CodexMusicPlayer.IsPlaying && !CodexMusicPlayer.IsDucking);
        panel.SkipAnimations();

        CodexPanel.Open(CodexUi.FindFont());
        panel.SkipAnimations();
        Check("the codex reopens on the entries, not the music", !panel.MusicOpen && panel.IsOpen);

        // back from the tab closes the codex (the track goes with it)
        panel.ShowTab(CodexPanel.MusicTab);
        panel.SkipAnimations();
        v.RowButton(v.RowIndexOf(Ember.id)).onClick.Invoke();
        panel.Back();
        panel.SkipAnimations();
        Check("Back from the tab closes the codex and ends playback", !panel.IsOpen && !CodexMusicPlayer.IsPlaying && !CodexMusicPlayer.IsDucking);
        CodexPanel.Open(CodexUi.FindFont());
        panel.SkipAnimations();
        Object.DestroyImmediate(menuGo);
    }

    // The codex's own song: plays while the codex is open (menu theme paused), a preview takes over
    // and gives it back, closing hands the menu theme back.
    static void Theme(CodexPanel panel)
    {
        var menuGo = new GameObject(CodexMusic.MenuSourceName, typeof(AudioSource));
        var menu = menuGo.GetComponent<AudioSource>();
        CodexTheme.Testing = true;
        CodexTheme.Play = src => { };
        try
        {
            panel.Close();
            panel.SkipAnimations();
            Check("closed codex: no codex song", !CodexTheme.IsOpen && CodexTheme.Source == null);
            CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
            var song = CodexTheme.Source;
            Check("opening the codex starts its looping song and pauses the menu theme",
                  CodexTheme.IsOpen && song != null && song.loop && song.clip == Resources.Load<AudioClip>(CodexMusic.ThemeResource)
                  && CodexTheme.PausedMenu == menu);

            panel.ShowTab(CodexPanel.MusicTab);
            panel.SkipAnimations();
            var v = panel.Music;
            Check("the codex song is listed, always unlocked",
                  v.RowIndexOf(CodexMusic.ThemeId) >= 0 && v.RowTitle(v.RowIndexOf(CodexMusic.ThemeId)).text == "Night Calm");
            v.RowButton(v.RowIndexOf(Ember.id)).onClick.Invoke();
            Check("a preview suspends the codex song", CodexTheme.IsSuspended && CodexTheme.IsOpen && CodexTheme.PausedMenu == menu);
            v.StopButton.onClick.Invoke();
            Check("stopping the preview brings the codex song back, not the menu theme",
                  !CodexTheme.IsSuspended && CodexTheme.IsOpen && CodexTheme.Source == song && CodexTheme.PausedMenu == menu);
            v.RowButton(v.RowIndexOf(Ember.id)).onClick.Invoke();
            panel.Close();
            Check("closing the codex ends the song (preview too) and releases the menu theme",
                  !CodexTheme.IsOpen && !CodexTheme.IsSuspended && CodexTheme.PausedMenu == null && !CodexMusicPlayer.IsPlaying);
            panel.SkipAnimations();
            CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
        }
        finally
        {
            CodexTheme.Reset();
            Object.DestroyImmediate(menuGo);
        }
    }

    static void Volume(CodexPanel panel)
    {
        // the default gain is the music bus (SoundSettings): the MUSIC toggle times the music slider
        SoundSettings.SetMusicOn(true);
        SoundSettings.SetMusicVolume(.5f);
        Check("the default gain follows the music slider (SoundSettings.MusicGain)", Mathf.Abs(CodexMusicPlayer.Gain() - .5f) < .001f);
        SoundSettings.SetMusicOn(false);
        Check("the default gain is 0 with MUSIC off", CodexMusicPlayer.Gain() == 0f);
        SoundSettings.SetMusicOn(true);
        SoundSettings.SetMusicVolume(1f);

        float level = 1f;
        CodexMusicPlayer.Gain = () => level;
        Check("full music gain plays at the base volume", Mathf.Abs(CodexMusicPlayer.CurrentVolume - CodexMusicPlayer.BaseVolume) < .001f);
        level = 0f;
        Check("a music gain of 0 (Music off) silences it", CodexMusicPlayer.CurrentVolume == 0f);
        level = .5f;
        Check("half gain halves it", Mathf.Abs(CodexMusicPlayer.CurrentVolume - CodexMusicPlayer.BaseVolume * .5f) < .001f);
    }

    static void Layouts(CodexPanel panel)
    {
        panel.ShowTab(CodexPanel.MusicTab);
        panel.SkipAnimations();
        var v = panel.Music;
        foreach (var safe in Safes)
        {
            panel.ApplyLayout(safe);
            Canvas.ForceUpdateCanvases();
            bool inside = true;
            for (int i = 0; i < v.VisibleRows; i++)
            {
                var rt = v.RowRect(i);
                inside &= rt.sizeDelta.x <= v.ListRect.width && rt.sizeDelta.y >= 96f;
            }
            bool padded = v.VisibleRows > 0;
            for (int i = 0; i < v.VisibleRows; i++)
            {
                var rt = v.RowRect(i);
                float hw = rt.sizeDelta.x * .5f, hh = rt.sizeDelta.y * .5f;
                foreach (var t in new[] { v.RowTitle(i), v.RowSub(i) })
                {
                    var r = Local(t.rectTransform);
                    padded &= r.xMin >= -hw + CodexMusicView.InsetX - .01f
                           && r.yMax <= hh - CodexMusicView.InsetY + .01f
                           && r.yMin >= -hh + CodexMusicView.InsetY - .01f;
                }
                padded &= Local(v.RowTitle(i).rectTransform).xMax <= hw - CodexMusicView.InsetX + .01f;
                padded &= rt.sizeDelta.y >= 96f;
            }
            Check("row text keeps >= " + CodexMusicView.PadX + " units off the card border horizontally and >= " + CodexMusicView.PadY + " vertically at " + safe.width + "x" + safe.height, padded);
            {
                float hw = v.StripRectLocal.width * .5f, hh = v.StripRectLocal.height * .5f;
                var c = Local(v.StripCount.rectTransform); var ln = Local(v.StripLine.rectTransform);
                Check("strip text keeps its inset off the card border",
                    c.xMin >= -hw + CodexMusicView.InsetX - .01f && ln.xMin >= -hw + CodexMusicView.InsetX - .01f
                    && c.yMax <= hh - CodexMusicView.InsetY + .01f && ln.yMin >= -hh + CodexMusicView.InsetY - .01f);
            }
            Check("rows fit the list width and are >= 96 units tall at " + safe.width + "x" + safe.height, inside);
            Check("the strip sits above the list", v.StripRectLocal.yMin >= v.ListRect.yMax);
            Check("the content is tall enough for every row", v.ContentHeight >= v.VisibleRows * CodexMusicView.RowHeight);
        }
        panel.ApplyLayout(Safes[0]);
    }

    // A child laid out with CodexUi.Place (centre anchors) as a rect in its parent's space.
    static Rect Local(RectTransform rt)
    {
        return new Rect(rt.anchoredPosition - rt.sizeDelta * .5f, rt.sizeDelta);
    }
}
