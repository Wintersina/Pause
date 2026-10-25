using UnityEngine;

// Keeps the last couple of seconds of its AudioSource's output (post volume,
// pitch and the pause dimming: exactly what the player heard). Sits on the
// music source's GameObject. Allocates nothing on the audio thread after its
// first call.
public sealed class KillCamMusicTap : MonoBehaviour
{
    public const float RingSeconds = 2f;

    float[] ring;
    int channels, rate, write, filledFrames;
    readonly object gate = new object();

    void Awake() { Prepare(AudioSettings.outputSampleRate); }

    // Tests call this directly with a fake rate.
    public void Prepare(int sampleRate)
    {
        lock (gate)
        {
            rate = Mathf.Max(8000, sampleRate);
            ring = new float[Mathf.CeilToInt(rate * RingSeconds) * 2];   // up to stereo
            channels = 0;
            write = 0;
            filledFrames = 0;
        }
    }

    // The audio thread's callback (and the tests'): `data` is interleaved.
    public void Feed(float[] data, int ch)
    {
        if (ch < 1 || ch > 2 || data == null) return;
        lock (gate)
        {
            if (ring == null) return;
            if (channels != ch) { channels = ch; write = 0; filledFrames = 0; }
            int capacity = ring.Length / ch;
            int frames = data.Length / ch;
            int w = write;
            for (int f = 0; f < frames; f++)
            {
                for (int c = 0; c < ch; c++) ring[w * ch + c] = data[f * ch + c];
                if (++w >= capacity) w = 0;
            }
            write = w;
            filledFrames = Mathf.Min(capacity, filledFrames + frames);
        }
    }

    void OnAudioFilterRead(float[] data, int ch) { Feed(data, ch); }

    // The last `seconds` heard, reversed in time (newest first), interleaved,
    // into `dest` (grown only if too small). Returns the frame count.
    public int Snapshot(float seconds, ref float[] dest, out int ch, out int sampleRate)
    {
        lock (gate)
        {
            ch = channels;
            sampleRate = rate;
            if (ring == null || ch < 1) return 0;
            int capacity = ring.Length / ch;
            int frames = Mathf.Min(filledFrames, Mathf.Max(1, Mathf.RoundToInt(seconds * rate)));
            if (frames <= 0) return 0;
            if (dest == null || dest.Length < frames * ch) dest = new float[frames * ch];
            for (int k = 0; k < frames; k++)
            {
                int src = write - 1 - k;
                if (src < 0) src += capacity;
                for (int c = 0; c < ch; c++) dest[k * ch + c] = ring[src * ch + c];
            }
            return frames;
        }
    }
}
