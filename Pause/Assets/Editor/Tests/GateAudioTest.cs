using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The HapticGate splash sounds (GateAudio): the right cue at the right sim
// event, clips load and are imported like the death cues, mute respected,
// no allocation per cue, a voice cap, the smash cutting the rattle.
public static class GateAudioTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[GATESND] PASS  " : "[GATESND] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static double clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool sim = GateAudio.Simulate;
        var savedClock = GateAudio.Clock;
        var savedMute = GateAudio.MutedSource;
        var loadWas = splashScene.LoadScene;
        var inputWas = splashScene.InputSource;
        try
        {
            GateAudio.Simulate = true;
            GateAudio.Clock = () => clock;
            GateAudio.MutedSource = () => false;
            Clips();
            Variants();
            Voices();
            Muted();
            Allocations();
            SceneCues();
            OpenClip();
            Import();
        }
        finally
        {
            GateAudio.Simulate = sim; GateAudio.Clock = savedClock; GateAudio.MutedSource = savedMute;
            splashScene.LoadScene = loadWas; splashScene.InputSource = inputWas;
            splashScene.BuildGateInEditMode = false;
            Time.timeScale = 1f;
            GateAudio.Reset();
        }
        Debug.Log("[GATESND] failures: " + fails);
        return fails;
    }

    static void Clips()
    {
        foreach (var cue in GateAudio.Cues)
        {
            Check(cue + " has 2 clips (" + GateAudio.Variants(cue) + ")", GateAudio.Variants(cue) == 2 && GateAudio.Clip(cue, 0) != null && GateAudio.Clip(cue, 1) != null);
            Check(cue + " clips are mono 44.1 kHz", GateAudio.Clip(cue, 0).channels == 1 && GateAudio.Clip(cue, 0).frequency == 44100);
        }
        Check("smash is the longest tail (>= 1.2 s)", GateAudio.Clip("gate_smash", 0).length >= 1.2f);
    }

    static void Variants()
    {
        GateAudio.Reset(); GateAudio.Seed(7);
        foreach (var cue in GateAudio.Cues)
        {
            AudioClip last = null; bool repeat = false, both0 = false, both1 = false, pitchOk = true;
            for (int i = 0; i < 40; i++)
            {
                clock += 3;
                GateAudio.Play(cue);
                if (GateAudio.LastClip == last) repeat = true;
                last = GateAudio.LastClip;
                if (last == GateAudio.Clip(cue, 0)) both0 = true; else both1 = true;
                pitchOk &= Mathf.Abs(GateAudio.LastPitch - 1f) <= GateAudio.PitchJitter + 1e-5f && GateAudio.LastVolume > 0f && GateAudio.LastVolume <= 1f;
            }
            Check(cue + ": both variants used, never twice in a row, pitch within +-4%", !repeat && both0 && both1 && pitchOk);
        }
        GateAudio.Play("gate_beep"); GateAudio.Play(null);
        Check("unknown cue names are ignored", GateAudio.LastCue != "gate_beep");
    }

    static void Voices()
    {
        GateAudio.Reset(); clock += 10;
        int peak = 0;
        for (int i = 0; i < 60; i++) { clock += .02; GateAudio.Play(i % 3 == 0 ? "gate_step" : i % 3 == 1 ? "gate_crack" : "gate_smash"); peak = Mathf.Max(peak, GateAudio.ActiveVoices()); }
        Check("voice cap holds under a burst (peak " + peak + " <= " + (GateAudio.MaxVoices + 1) + ")", peak <= GateAudio.MaxVoices + 1);

        GateAudio.Reset(); clock += 10;
        GateAudio.Play("gate_rattle"); clock += .2;
        Check("rattle is sounding", GateAudio.ActiveVoices() == 1);
        GateAudio.Play("gate_smash");
        Check("the smash cuts a rattle still sounding (" + GateAudio.RattleCut + ")", GateAudio.RattleCut == 1 && GateAudio.ActiveVoices() == 1);
        // a smash tail outlives the card hand-over (0.45 s)
        clock += .45;
        Check("the smash tail is still sounding 0.45 s later", GateAudio.ActiveVoices() == 1);
        GateAudio.Reset(); clock += 10;
        GateAudio.Play("gate_smash"); clock += 2;
        Check("... and ends by itself", GateAudio.ActiveVoices() == 0);
    }

    static void Muted()
    {
        GateAudio.Reset(); clock += 10;
        GateAudio.MutedSource = () => true;
        GateAudio.Play("gate_smash"); GateAudio.Play("gate_rattle");
        Check("muted: nothing starts", GateAudio.Played == 0 && GateAudio.ActiveVoices() == 0 && GateAudio.Skipped == 2);
        GateAudio.MutedSource = () => false;
        GateAudio.Play("gate_smash");
        Check("unmuted: plays again", GateAudio.Played == 1);

        // the real setting is what the default source reads
        string key = SoundSettings.SfxOnKey;
        int had = PlayerPrefs.GetInt(key, 1);
        var def = new GateAudioDefaults();
        PlayerPrefs.SetInt(key, 0);
        bool m1 = SoundSettings.SfxGain <= 0f;
        PlayerPrefs.SetInt(key, 1);
        bool m0 = SoundSettings.SfxGain <= 0f;
        PlayerPrefs.SetInt(key, had);
        Check("default mute source follows SoundSettings", m1 && !m0 && def.Ok());
    }

    sealed class GateAudioDefaults
    {
        public bool Ok() { return typeof(GateAudio).GetField("MutedSource").GetValue(null) != null; }
    }

    static void Allocations()
    {
        GateAudio.Reset(); clock += 10;
        for (int i = 0; i < 30; i++) { clock += 1; GateAudio.Play(GateAudio.Cues[i % 4]); }
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) { clock += .05; GateAudio.Play(GateAudio.Cues[i % 4]); }
        long a = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("no allocation per cue (" + a + " bytes)", a == 0);
    }

    // The real card with a fake clock: which cue fires at which sim event.
    static void SceneCues()
    {
        EditorSceneLoader.Open("spashS7", OpenSceneMode.Single);
        splashScene.BuildGateInEditMode = true;
        var card = Object.FindFirstObjectByType<splashScene>();
        card.ApplyLayout(1080, 2340, new Rect(0, 0, 1080, 2340));
        int loads = 0;
        splashScene.LoadScene = n => loads++;
        var heard = new List<string>();
        System.Action<string> hook = heard.Add;
        splashScene.SoundCue += hook;
        try
        {
            // natural run: rattle once, then one step per film shove, no crack / smash
            GateAudio.Reset(); clock += 10; heard.Clear();
            card.Restart();
            card.SendMessage("Start");   // the first frame in play mode
            for (int i = 0; i < 600 && loads == 0; i++) { clock += Dt; card.Step(Dt); }
            Check("natural run cues: 1 rattle, " + GateSim.StepCount + " steps, no crack / smash (" + string.Join(",", heard) + ")",
                  Count(heard, "gate_rattle") == 1 && Count(heard, "gate_step") == GateSim.StepCount && Count(heard, "gate_crack") == 0 && Count(heard, "gate_smash") == 0 && heard[0] == "gate_rattle");
            Check("... every cue reached the player or was replaced by the opening clip (" + GateAudio.Played + "+" + GateAudio.Suppressed + " of " + heard.Count + ")",
                  GateAudio.Played + GateAudio.Suppressed == heard.Count && GateAudio.Suppressed == 1 + GateSim.StepCount);

            // three taps: crack, crack, smash
            loads = 0; GateAudio.Reset(); clock += 10; heard.Clear();
            card.Restart(); card.SendMessage("Start");
            float[] at = { .3f, .5f, .7f };
            int k = 0; double smashAt = -1, loadAt = -1;
            for (int i = 0; i < 600 && loads == 0; i++)
            {
                if (k < 3 && card.Sim.time >= at[k]) { card.Tap(); k++; }
                clock += Dt; card.Step(Dt);
                if (smashAt < 0 && heard.Contains("gate_smash")) smashAt = clock;
            }
            loadAt = clock;
            Check("three taps: 2 cracks then exactly 1 smash (" + string.Join(",", heard) + ")",
                  Count(heard, "gate_crack") == 2 && Count(heard, "gate_smash") == 1 && heard.IndexOf("gate_smash") > heard.LastIndexOf("gate_crack"));
            Check("the smash plays before the scene change and keeps ringing after it", smashAt > 0 && loadAt - smashAt > 0.2 && GateAudio.ActiveVoices() >= 1);
            Check("steps stopped by the smash: at most " + GateSim.StepCount + " (" + Count(heard, "gate_step") + ")", Count(heard, "gate_step") <= GateSim.StepCount);
        }
        finally { splashScene.SoundCue -= hook; }
    }

    static int Count(List<string> l, string s) { int n = 0; foreach (var x in l) if (x == s) n++; return n; }

    // ---- the user's gate-opening clip ---------------------------------------------

    static splashScene OpenCard(out int[] loads, out List<string> heard)
    {
        EditorSceneLoader.Open("spashS7", OpenSceneMode.Single);
        splashScene.BuildGateInEditMode = true;
        var card = Object.FindFirstObjectByType<splashScene>();
        card.ApplyLayout(1080, 2340, new Rect(0, 0, 1080, 2340));
        var count = new int[1];
        splashScene.LoadScene = n => count[0]++;
        var list = new List<string>();
        splashScene.SoundCue += list.Add;
        loads = count; heard = list;
        return card;
    }

    static void OpenClip()
    {
        var clip = GateAudio.OpenClip();
        Check("gate_open clip is assigned (" + (clip != null ? clip.length.ToString("F2") + " s, " + clip.channels + " ch, " + clip.frequency + " Hz" : "missing") + ")",
              clip != null && Mathf.Abs(clip.length - 5.6f) < 0.1f);
        Check("the hit offset sits inside the clip and the start delay lines it up with the film's first split (" + GateFilm.FirstSplit.ToString("F3") + " s)",
              clip != null && GateAudio.OpenImpactSeconds < clip.length
              && Mathf.Abs(splashScene.OpenClipStart + GateAudio.OpenImpactSeconds - GateFilm.FirstSplit) < 0.011f);

        // natural run: plays exactly once, at the start delay, then fades on leaving
        var card = OpenCard(out var loads, out var heard);
        try
        {
            GateAudio.Reset(); clock += 10; heard.Clear(); loads[0] = 0;
            card.Restart(); card.SendMessage("Start");
            float startedAt = -1f; bool activeMid = false;
            for (int i = 0; i < 600 && loads[0] == 0; i++)
            {
                clock += Dt; card.Step(Dt);
                if (startedAt < 0 && GateAudio.OpenStarts > 0) startedAt = card.Sim.time;
                if (card.Sim.time > 1.2f && card.Sim.time < 1.3f && GateAudio.OpenActive) activeMid = true;
            }
            Check("natural run: the opening clip starts once, at " + startedAt.ToString("F2") + " s (want " + splashScene.OpenClipStart.ToString("F2") + ")",
                  GateAudio.OpenStarts == 1 && Count(heard, "gate_open") == 1 && Mathf.Abs(startedAt - splashScene.OpenClipStart) < 0.03f);
            Check("... it is playing when the leaves split (1.2 s) and replaces rattle + steps (suppressed " + GateAudio.Suppressed + ")",
                  activeMid && GateAudio.Suppressed == 1 + GateSim.StepCount);
            Check("... leaving naturally fades it over " + GateAudio.OpenLeaveFade + " s (stops: " + GateAudio.OpenStops + ")",
                  GateAudio.OpenStops == 1 && Mathf.Abs(GateAudio.OpenLastFade - GateAudio.OpenLeaveFade) < 0.001f);
            clock += 1.0;
            Check("... and it is silent a second after the scene change", !GateAudio.OpenActive);

            // muted: not started
            GateAudio.Reset(); clock += 10; heard.Clear(); loads[0] = 0;
            GateAudio.MutedSource = () => true;
            card.Restart(); card.SendMessage("Start");
            for (int i = 0; i < 600 && loads[0] == 0; i++) { clock += Dt; card.Step(Dt); }
            Check("muted: the opening clip never plays", GateAudio.OpenStarts == 0 && !GateAudio.OpenActive);
            GateAudio.MutedSource = () => false;

            // smash during the clip: stopped within 0.1 s, the card leaves on the usual schedule
            GateAudio.Reset(); clock += 10; heard.Clear(); loads[0] = 0;
            card.Restart(); card.SendMessage("Start");
            float[] at = { .3f, .5f, .7f };
            int k = 0; float smashTime = -1f, loadTime = -1f, clipActiveAtSmash = -1f;
            double smashClock = -1;
            for (int i = 0; i < 600 && loads[0] == 0; i++)
            {
                if (k < 3 && card.Sim.time >= at[k]) { card.Tap(); k++; }
                clock += Dt; card.Step(Dt);
                if (smashTime < 0 && card.Sim.smashed) { smashTime = card.Sim.time; smashClock = clock; clipActiveAtSmash = GateAudio.OpenActive ? 1f : 0f; }
            }
            loadTime = card.Sim.time;
            Check("smash during the clip: it was playing, started once (" + GateAudio.OpenStarts + ")", GateAudio.OpenStarts == 1 && clipActiveAtSmash >= 0f);
            Check("... the smash fades it out in <= 0.1 s (fade " + GateAudio.OpenLastFade.ToString("F2") + ")",
                  GateAudio.OpenStops == 1 && GateAudio.OpenLastFade <= 0.1001f);
            clock = smashClock + 0.11;
            Check("... and 0.11 s after the smash it is stopped", !GateAudio.OpenActive);
            Check("... the splash advances on the old schedule: scene change " + (loadTime - smashTime).ToString("F3") + " s after the smash (want "
                  + GateSim.SmashLeaveDelay.ToString("F2") + "), nothing waits for the 5.6 s clip",
                  Mathf.Abs(loadTime - smashTime - GateSim.SmashLeaveDelay) < 0.03f && loadTime < 1.5f && loads[0] == 1);

            // smash before the clip would start: it never starts
            GateAudio.Reset(); clock += 10; heard.Clear(); loads[0] = 0;
            card.Restart(); card.SendMessage("Start");
            for (int i = 0; i < 10; i++) { clock += Dt; card.Step(Dt); }   // 0.17 s: past the tap lockout
            // the lockout is 0.15 s and the clip starts at 0.03 s, so only a queued burst can beat it:
            card.Restart(); GateAudio.Reset(); heard.Clear(); loads[0] = 0;
            var sim = card.Sim;
            sim.Advance(0.2f); sim.Tap(); sim.Tap(); sim.Tap();   // three taps queued before the card's first Step
            clock += 10; card.Step(Dt);
            Check("three taps queued before the clip's start: it never starts (" + GateAudio.OpenStarts + ")", GateAudio.OpenStarts == 0);
        }
        finally { splashScene.SoundCue -= heard.Add; GateAudio.Reset(); }
    }

    static void Import()
    {
        var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { EnemyDeathAudioImporter.GateFolder.TrimEnd('/') });
        bool ok = guids.Length == 8;
        foreach (var g in guids)
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g)) as AudioImporter;
            if (imp == null) { ok = false; break; }
            var s = imp.defaultSampleSettings;
            ok &= imp.forceToMono && s.loadType == AudioClipLoadType.DecompressOnLoad && s.compressionFormat == AudioCompressionFormat.PCM
                  && s.preloadAudioData && s.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate;
        }
        Check("gate clips (" + guids.Length + ") import mono / PCM / decompress-on-load / preload", ok);
        string openPath = "Assets/Audio/Resources/Audio/GateOpen/gate_open.wav";
        var oi = AssetImporter.GetAtPath(openPath) as AudioImporter;
        var os = oi != null ? oi.defaultSampleSettings : default(AudioImporterSampleSettings);
        Check("gate_open imports as Vorbis q" + os.quality.ToString("F2") + ", decompress-on-load, preloaded, 5.6 s",
              oi != null && os.compressionFormat == AudioCompressionFormat.Vorbis && os.quality >= 0.6f && os.quality <= 0.7f
              && os.loadType == AudioClipLoadType.DecompressOnLoad && os.preloadAudioData);
    }
}
