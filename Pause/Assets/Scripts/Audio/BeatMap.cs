using System;
using UnityEngine;

// THE BEAT MAP of one boss track: where the beats are in the clip, in clip time (seconds from the start of the audio,
// which is what AudioSource.time reports). Built offline by scripts/tools/boss_beatmap.py into
// Resources/BossBeatMaps.json (the numbers there are the analysis' guess: tempo and first-beat are measured, the bar
// line is assumed to be beat 0 + k * beatsPerBar and is the least reliable part).
//
// A STEADY track is bpm + firstBeat; a drifting one (Desert speeds up) carries an explicit `beats` list instead. Beat
// positions are continuous: BeatAt(t) = 3.5 is half way between the 4th and 5th beat, so subdivisions are plain
// fractions and a wave "on the off-beat" is a wave at beat + .5.
[Serializable]
public sealed class BeatMap
{
    public string name;
    public string clip;               // Resources path of the track
    public float bpm = 120f;
    public float firstBeat;           // clip time of beat 0
    public int beatsPerBar = 4;
    public int downbeatBeat;          // which beat index is a bar line (mod beatsPerBar)
    public float duration;            // the clip's length, seconds (the loop point)
    public bool steady = true;
    public float[] beats;             // explicit beat times when !steady

    public float[] energy, bass, accents;
    public MusicSection[] sections;
    public int analysisVersion;

    public MusicMoment MusicAt(double beat)
    {
        int i = Math.Max(0, (int)Math.Floor(beat));
        var m = new MusicMoment { energy = Feature(energy, i), bass = Feature(bass, i), accent = Feature(accents, i), kind = "drive" };
        if (sections != null)
            for (int n = 0; n < sections.Length && sections[n].beat <= beat; n++)
            { m.section = n; m.kind = sections[n].kind; }
        return m;
    }

    static float Feature(float[] values, int i) => values == null || values.Length == 0 ? .5f : values[Math.Min(i, values.Length - 1)];

    bool Listed => !steady && beats != null && beats.Length >= 2;

    public int BarBeats => Math.Max(1, beatsPerBar);

    // Seconds per beat at clip time t.
    public double SecondsPerBeatAt(double t)
    {
        if (!Listed) return 60.0 / bpm;
        int i = Index(t);
        return beats[i + 1] - beats[i];
    }

    // The beat position of clip time t (beat 0 = firstBeat), continuous.
    public double BeatAt(double t)
    {
        if (!Listed) return (t - firstBeat) * bpm / 60.0;
        int i = Index(t);
        double span = beats[i + 1] - beats[i];
        return i + (t - beats[i]) / span;
    }

    // The clip time of beat position b (inverse of BeatAt).
    public double TimeOfBeat(double b)
    {
        if (!Listed) return firstBeat + b * 60.0 / bpm;
        int i = (int)Math.Floor(b);
        i = Math.Max(0, Math.Min(beats.Length - 2, i));
        return beats[i] + (b - i) * (beats[i + 1] - beats[i]);
    }

    // The span [i, i + 1] a time falls in; the first / last span extend before and after the list.
    int Index(double t)
    {
        int lo = 0, hi = beats.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (beats[mid] <= t) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    // Whole bars' worth of beats in one trip round the clip: the beat counter grows by this at each loop wrap, so the
    // bar phase carries on across the loop point.
    public double BeatsPerLoop => BeatsInLoop(duration);

    public double BeatsInLoop(double loopSeconds)
    {
        // Include the negative beat position before the first beat. Rounding only the
        // end position makes some maps (Verdant) move backwards on wrapping to beat < 0.
        return Math.Ceiling((BeatAt(loopSeconds) - BeatAt(0)) / BarBeats) * BarBeats;
    }

}

[Serializable]
class BeatMapFile
{
    public BeatMap[] bosses;
}

public static class BeatMaps
{
    public const string Resource = "BossBeatMaps";
    static BeatMapFile file;

    // The map of the boss of world `displayName` ("Space", "Frost" ...), or null when there is none.
    public static BeatMap For(string displayName)
    {
        if (file == null)
        {
            var text = Resources.Load<TextAsset>(Resource);
            file = text != null ? Parse(text.text) : new BeatMapFile { bosses = new BeatMap[0] };
        }
        for (int i = 0; i < file.bosses.Length; i++)
            if (file.bosses[i].name == displayName) return file.bosses[i];
        return null;
    }

    public static int Count
    {
        get { For(""); return file.bosses.Length; }
    }

    static BeatMapFile Parse(string json)
    {
        var f = JsonUtility.FromJson<BeatMapFile>(json);
        return f != null && f.bosses != null ? f : new BeatMapFile { bosses = new BeatMap[0] };
    }
}

[Serializable]
public sealed class MusicSection
{
    public int beat;
    public string kind;
    public float energy;
}

public struct MusicMoment
{
    public float energy, bass, accent;
    public int section;
    public string kind;
}
