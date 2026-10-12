using System;
using System.IO;
using UnityEngine;

// The blue atom's jet-engine spool-up (BoostJet): the clip exists and loads, a
// pickup starts it, quick repeats restart one pooled source, the effects
// volume/switch silences it, and it is noise (no sustained narrow peak).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BoostJetTest.Run
public static class BoostJetTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOOSTJET] PASS  " : "[BOOSTJET] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        float savedVol = PlayerPrefs.GetFloat(SoundSettings.SfxVolumeKey, 1f);
        int savedOn = PlayerPrefs.GetInt(SoundSettings.SfxOnKey, 1);
        try { Body(); }
        finally
        {
            PlayerPrefs.SetFloat(SoundSettings.SfxVolumeKey, savedVol);
            PlayerPrefs.SetInt(SoundSettings.SfxOnKey, savedOn);
        }
        Debug.Log("[BOOSTJET] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    static void Body()
    {
        var clip = Resources.Load<AudioClip>(BoostJet.ClipPath);
        Check("clip loads from Resources", clip != null);
        if (clip == null) return;
        Check("clip is 1.0-1.8 s mono", clip.length >= 1f && clip.length <= 1.8f && clip.channels == 1);

        string wav = Path.Combine(Application.dataPath, "Audio/Resources/Audio/Boost/boost_jet.wav");
        Check("file under 1 MiB", new FileInfo(wav).Length < 1024 * 1024);

        // the pickup branch calls it
        string src = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Ship/collisionDetection.cs"));
        int atom = src.IndexOf("\"atom3a\"", StringComparison.Ordinal);
        int call = src.IndexOf("BoostJet.Play()", StringComparison.Ordinal);
        Check("blue-atom pickup calls BoostJet.Play", atom >= 0 && call > atom && call - atom < 1200);

        // plays at full volume, restarts instead of stacking
        PlayerPrefs.SetFloat(SoundSettings.SfxVolumeKey, 1f);
        PlayerPrefs.DeleteKey(SoundSettings.SfxOnKey);
        int p0 = BoostJet.Plays;
        BoostJet.Play();
        var s1 = BoostJet.Source;
        Check("pickup starts the sound", BoostJet.Plays == p0 + 1 && s1 != null && s1.clip == clip);
        Check("gain follows the effects volume", Mathf.Abs(BoostJet.LastGain - BoostJet.Volume) < 1e-4f);
        BoostJet.Play();
        Check("second pickup restarts the same source", BoostJet.Plays == p0 + 2 && BoostJet.Source == s1
            && s1.GetComponents<AudioSource>().Length == 1);

        PlayerPrefs.SetFloat(SoundSettings.SfxVolumeKey, .5f);
        BoostJet.Play();
        Check("half volume halves the gain", Mathf.Abs(BoostJet.LastGain - BoostJet.Volume * .5f) < 1e-4f);

        // sound volume 0 / effects off: nothing starts
        PlayerPrefs.SetFloat(SoundSettings.SfxVolumeKey, 0f);
        int p1 = BoostJet.Plays;
        BoostJet.Play();
        Check("sound volume 0 plays nothing", BoostJet.Plays == p1);
        PlayerPrefs.SetFloat(SoundSettings.SfxVolumeKey, 1f);
        PlayerPrefs.SetInt(SoundSettings.SfxOnKey, 0);
        BoostJet.Play();
        Check("effects off plays nothing", BoostJet.Plays == p1);

        // spectral: noise, not a tone
        var data = new float[clip.samples * clip.channels];
        Check("clip data readable", clip.GetData(data, 0) && data.Length > 40000);
        double worst = WorstPeakRatio(data);
        Debug.Log("[BOOSTJET] worst narrow-peak ratio " + worst.ToString("F1"));
        Check("no sustained pure tone (peak/local mean < 80 in every loud 93 ms frame)", worst < 80.0);
        float peak = 0f; foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
        Check("not clipping and audible", peak > .3f && peak < .99f);
    }

    // Worst (peak bin power / mean of +-64 neighbours without the peak) over loud 4096-sample frames.
    // Noise sits near 5-30; a steady sine is in the thousands.
    static double WorstPeakRatio(float[] x)
    {
        const int N = 4096, H = 2048, half = 64;
        double rms = 0; for (int i = 0; i < x.Length; i++) rms += x[i] * x[i];
        rms = Math.Sqrt(rms / x.Length);
        var re = new double[N]; var im = new double[N]; var p = new double[N / 2 + 1];
        double worst = 0;
        for (int s = 0; s + N <= x.Length; s += H)
        {
            double e = 0;
            for (int i = 0; i < N; i++) { double w = .5 - .5 * Math.Cos(2 * Math.PI * i / N); re[i] = x[s + i] * w; im[i] = 0; e += x[s + i] * x[s + i]; }
            if (Math.Sqrt(e / N) < .25 * rms) continue;
            Fft(re, im);
            int k = 3;
            for (int i = 0; i <= N / 2; i++) { p[i] = re[i] * re[i] + im[i] * im[i]; if (i >= 3 && p[i] > p[k]) k = i; }
            int lo = Math.Max(k - half, 1), hi = Math.Min(k + half, N / 2 + 1);
            double sum = 0; int cnt = 0;
            for (int i = lo; i < hi; i++) { if (Math.Abs(i - k) <= 2) continue; sum += p[i]; cnt++; }
            worst = Math.Max(worst, p[k] / (sum / cnt + 1e-30));
        }
        return worst;
    }

    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double a = -2 * Math.PI / len, wr = Math.Cos(a), wi = Math.Sin(a);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int u = i + k, v = i + k + len / 2;
                    double xr = re[v] * cr - im[v] * ci, xi = re[v] * ci + im[v] * cr;
                    re[v] = re[u] - xr; im[v] = im[u] - xi; re[u] += xr; im[u] += xi;
                    double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
    }
}
