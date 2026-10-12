using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// One music track the codex MUSIC tab can list and play.
public sealed class CodexTrack
{
    public readonly string id;        // "music_menu", "music_world_ember", "music_boss_ember"
    public readonly string title;     // shown once heard
    public readonly string kind;      // "MENU THEME", "WORLD THEME", "BOSS THEME"
    public readonly string resource;  // Resources path, or null for the menu theme (taken from the home scene)
    public readonly int world;        // WorldManager index, -1 for the menu theme

    public CodexTrack(string id, string title, string kind, string resource, int world)
    {
        this.id = id; this.title = title; this.kind = kind; this.resource = resource; this.world = world;
    }
}

// The codex's music list and its unlock rule.
//
// A track unlocks the first time it plays in a real run: a world's song when
// WorldMusic starts it (WorldMusic.Apply), a boss theme when its fight starts
// (WorldMusic.BeginBoss). The menu theme is the one the player hears on the
// home screen, so it is always unlocked. Persistence and cloud sync ride on
// Codex.PrefsKey (Codex.HearTrack / IsTrackHeard); the tutorial never counts
// and developer mode shows everything (same rules as codex entries).
// Worlds the loop cannot reach yet are not listed at all.
public static class CodexMusic
{
    public const string MenuId = "music_menu";
    public const string LockedTitle = "???";
    // Where the menu theme lives: the home scene's own source (the wav is not under Resources).
    public const string MenuSourceName = "MainMenuMusic";

    // Song names, index-aligned with WorldManager.Worlds (see the comments in WorldMusic).
    static readonly string[] WorldSongs =
    {
        "Neon Solitude", "Frozen Pulse", "Jungle Night Pulse", "Anvil Arpeggio",
        "Deep Sonar Ping", "Static Storm", "Dune Circuit",
    };

    static List<CodexTrack> all;

    public static List<CodexTrack> All
    {
        get { if (all == null) all = Build(); return all; }
    }

    static List<CodexTrack> Build()
    {
        var list = new List<CodexTrack>
        {
            new CodexTrack(MenuId, "Main Menu", "MENU THEME", null, -1),
        };
        var worlds = WorldManager.Worlds;
        for (int i = 0; i < worlds.Length; i++)
        {
            string name = worlds[i].displayName;
            if (string.IsNullOrEmpty(worlds[i].musicResource)) continue;
            list.Add(new CodexTrack(WorldId(name), i < WorldSongs.Length ? WorldSongs[i] : name,
                                    name.ToUpperInvariant() + " THEME", worlds[i].musicResource, i));
        }
        for (int i = 0; i < worlds.Length; i++)
        {
            string name = worlds[i].displayName;
            list.Add(new CodexTrack(BossId(name), name + " Boss", name.ToUpperInvariant() + " BOSS",
                                    WorldMusic.BossResourcePrefix + name, i));
        }
        return list;
    }

    public static string WorldId(string displayName) { return "music_world_" + displayName.ToLowerInvariant(); }
    public static string BossId(string displayName) { return "music_boss_" + displayName.ToLowerInvariant(); }

    public static CodexTrack Find(string id)
    {
        foreach (var t in All) if (t.id == id) return t;
        return null;
    }

    // Worlds past WorldManager.LiveWorldCount are not listed (developer mode lists them).
    public static bool IsListed(CodexTrack t)
    {
        if (t == null) return false;
        if (t.world >= WorldManager.LiveWorldCount && !DeveloperUnlocks.Enabled) return false;
        return true;
    }

    public static bool IsUnlocked(CodexTrack t)
    {
        if (t == null) return false;
        return t.id == MenuId || Codex.IsTrackHeard(t.id);
    }

    public static string DisplayTitle(CodexTrack t) { return IsUnlocked(t) ? t.title : LockedTitle; }

    public static int ListedCount
    {
        get { int n = 0; foreach (var t in All) if (IsListed(t)) n++; return n; }
    }

    public static int UnlockedCount
    {
        get { int n = 0; foreach (var t in All) if (IsListed(t) && IsUnlocked(t)) n++; return n; }
    }

    // ---- gameplay hooks (WorldMusic) ----

    // The world's own song started playing.
    public static void HeardWorld(WorldTheme theme)
    {
        if (theme == null || string.IsNullOrEmpty(theme.musicResource)) return;
        Codex.HearTrack(WorldId(theme.displayName));
    }

    // The boss's own track started playing (not the pitched world-song fallback).
    public static void HeardBoss(WorldTheme theme)
    {
        if (theme == null) return;
        Codex.HearTrack(BossId(theme.displayName));
    }
}

// Plays codex tracks and gets the menu music out of the way.
//
// While a track plays, the home scene's menu music source is paused; leaving
// (Stop, the codex closing, a scene change) un-pauses it where it left off.
// Only a source this class paused is ever resumed, so a muted or absent menu
// source is left alone.
//
// Volume: the track plays at BaseVolume x Gain(). Today the only sound switch
// is SoundSettings.Muted (AudioListener.volume 0 silences this source like
// every other). INTEGRATION POINT for the Music/Effects options branch
// (audio-settings): set `CodexMusicPlayer.Gain = () => SoundSettings.MusicGain;`
// and call SoundSettings.MarkMusic(CodexMusicPlayer.Source) in Created so the
// effects gain no longer applies to it; Tick() re-reads Gain every frame, so a
// slider moved while a track plays takes effect at once.
public static class CodexMusicPlayer
{
    public const float BaseVolume = .8f;

    // Tests: track the state without touching real audio devices.
    public static bool Simulate;
    // Music gain 0..1 (see the integration note above).
    public static Func<float> Gain = () => 1f;
    // Raised whenever the playing track changes or ends, so the open view repaints.
    public static event Action Changed;
    // Raised when a real AudioSource is created (hook for SoundSettings.MarkMusic).
    public static event Action<AudioSource> Created;

    static AudioSource source;
    static AudioSource ducked;
    static string nowPlaying;
    static bool hooked;

    public static string NowPlaying { get { return nowPlaying; } }
    public static bool IsPlaying { get { return nowPlaying != null; } }
    public static AudioSource Source { get { return source; } }
    public static AudioSource DuckedSource { get { return ducked; } }
    public static bool IsDucking { get { return ducked != null; } }
    public static float CurrentVolume { get { return source != null ? source.volume : BaseVolume * Mathf.Clamp01(Gain()); } }

    // True when the track can be started right now (unlocked and its clip is available).
    public static bool CanPlay(CodexTrack t)
    {
        return t != null && CodexMusic.IsUnlocked(t) && (Simulate || Resolve(t) != null);
    }

    static AudioClip Resolve(CodexTrack t)
    {
        if (t.resource != null) return Resources.Load<AudioClip>(t.resource);
        var go = GameObject.Find(CodexMusic.MenuSourceName);
        var menu = go != null ? go.GetComponent<AudioSource>() : null;
        return menu != null ? menu.clip : null;
    }

    // Starts the track (replacing any other). Returns false for a locked track or a missing clip.
    public static bool Play(string id)
    {
        var t = CodexMusic.Find(id);
        if (!CanPlay(t)) return false;
        var clip = Simulate ? null : Resolve(t);

        Hook();
        Duck();
        nowPlaying = id;
        if (!Simulate)
        {
            if (source == null)
            {
                var go = new GameObject("~CodexMusic");
                UnityEngine.Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                var created = Created;
                if (created != null) created(source);
            }
            source.Stop();
            source.clip = clip;
            source.loop = false;
            source.volume = BaseVolume * Mathf.Clamp01(Gain());
            source.time = 0f;
            source.Play();
        }
        Raise();
        return true;
    }

    // Stops the track and gives the menu music back. Safe to call any time.
    public static void Stop()
    {
        bool was = nowPlaying != null;
        nowPlaying = null;
        if (source != null) source.Stop();
        Unduck();
        if (was) Raise();
    }

    // Per frame while the MUSIC tab is open: follows the music gain, notices a track that ran out.
    public static void Tick()
    {
        if (nowPlaying == null) return;
        if (source != null)
        {
            source.volume = BaseVolume * Mathf.Clamp01(Gain());
            if (!Simulate && !source.isPlaying) Stop();
        }
    }

    static void Duck()
    {
        if (ducked != null) return;
        var go = GameObject.Find(CodexMusic.MenuSourceName);
        var menu = go != null ? go.GetComponent<AudioSource>() : null;
        if (menu == null) return;
        if (!Simulate && !menu.isPlaying) return;   // nothing to pause; never un-pause a source we did not pause
        menu.Pause();
        ducked = menu;
    }

    static void Unduck()
    {
        if (ducked == null) return;
        if (!Simulate) ducked.UnPause();
        ducked = null;
    }

    static void Hook()
    {
        if (hooked) return;
        hooked = true;
        SceneManager.sceneLoaded += (s, m) => { if (nowPlaying != null || ducked != null) Stop(); };
    }

    static void Raise()
    {
        var h = Changed;
        if (h != null) h();
    }
}
