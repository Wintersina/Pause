using System;
using UnityEngine;
using UnityEngine.UI;

// The HapticGate gate-opening animation (approved brand animation from
// HapticGate-brand/final: the steel gate rattles, jolts open in three steps
// with smoke, steam and sparks, then the red "HapticGate" wordmark rises and
// flickers on under it). In-game cut: transparent ground, 1080 square layout
// downscaled to 512 px, every 2nd source frame (15 fps), 69 frames = 4.6 s;
// the last frame is the open gate + wordmark (the source's final hold).
//
//   Art/Resources/HapticGate/GateOpen/gate_open_00..62.png   512x512 RGBA sprites
//   Timeline frames 63..68 repeat frame 62 (the source PNGs are byte-identical).
//
// Shared by every screen that shows the animated logo (Credits now; the boot
// splash can use the same player). Renderer-agnostic: drives a uGUI Image or a
// SpriteRenderer. Self-driven by Update by default; an owner with its own
// clock (a preview's fake clock, a splash state machine) sets selfDriven =
// false and calls Step(dt).
public static class GateOpenFrames
{
    public const string Folder = "HapticGate/GateOpen";
    public const string Prefix = "gate_open_";
    public const int Count = 69;
    public const int LastUniqueFrame = 62;
    public const float Fps = 15f;
    public const float Duration = Count / Fps;   // 4.6 s, then hold the last frame

    static Sprite[] cached;

    // All frames in order, or null when the art is missing (callers fall back to a static logo).
    public static Sprite[] Load()
    {
        if (cached != null && cached.Length > 0 && cached[0] != null) return cached;
        var frames = new Sprite[Count];
        for (int i = 0; i < Count; i++)
        {
            frames[i] = i > LastUniqueFrame ? frames[LastUniqueFrame] :
                Resources.Load<Sprite>(Folder + "/" + Prefix + i.ToString("00"));
            if (frames[i] == null) return cached = null;
        }
        return cached = frames;
    }

    // Frame index at `t` seconds since the start (clamped: holds the last frame).
    public static int FrameAt(float t, int count = Count, float fps = Fps)
    {
        if (count <= 0) return 0;
        return Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, t) * fps + 1e-4f), 0, count - 1);
    }
}

[DisallowMultipleComponent]
public class GateOpenPlayer : MonoBehaviour
{
    public Image image;                    // either target (or both)
    public SpriteRenderer spriteRenderer;
    public float fps = GateOpenFrames.Fps;
    public float delay;                    // seconds on frame 0 before the gate starts moving
    public bool playOnEnable = true;
    public bool selfDriven = true;         // false: the owner calls Step(dt)
    public bool unscaledTime = true;       // menus/splash run regardless of Time.timeScale

    Sprite[] frames;
    bool playing, completed;

    public float Time { get; private set; }
    public int Frame { get; private set; }
    public bool Playing { get { return playing; } }
    public bool Finished { get { return completed; } }
    public float Duration { get { return delay + (frames != null ? frames.Length : GateOpenFrames.Count) / fps; } }
    public bool HasArt { get { return Frames() != null; } }
    public event Action Completed;          // once per Play, when the last frame is reached

    // Adds a player to a uGUI Image (same GameObject). Returns null when the frames are missing.
    public static GateOpenPlayer Attach(Image target)
    {
        if (target == null || GateOpenFrames.Load() == null) return null;
        var p = target.GetComponent<GateOpenPlayer>() ?? target.gameObject.AddComponent<GateOpenPlayer>();
        p.image = target;
        p.Show(0);
        return p;
    }

    public static GateOpenPlayer Attach(SpriteRenderer target)
    {
        if (target == null || GateOpenFrames.Load() == null) return null;
        var p = target.GetComponent<GateOpenPlayer>() ?? target.gameObject.AddComponent<GateOpenPlayer>();
        p.spriteRenderer = target;
        p.Show(0);
        return p;
    }

    Sprite[] Frames() { return frames ?? (frames = GateOpenFrames.Load()); }

    void OnEnable() { if (playOnEnable) Play(); }

    // From the first frame.
    public void Play()
    {
        Time = 0f;
        playing = true;
        completed = false;
        Show(0);
    }

    public void Stop() { playing = false; }

    // Jump straight to the end (e.g. a skip): shows the final frame.
    public void ShowFinal() { Seek(Duration); }

    public void Seek(float t)
    {
        Time = Mathf.Max(0f, t);
        Apply();
    }

    public void Step(float dt)
    {
        if (!playing) return;
        Time += Mathf.Max(0f, dt);
        Apply();
    }

    void Update()
    {
        if (selfDriven && playing) Step(unscaledTime ? UnityEngine.Time.unscaledDeltaTime : UnityEngine.Time.deltaTime);
    }

    void Apply()
    {
        var f = Frames();
        if (f == null) return;
        Show(GateOpenFrames.FrameAt(Time - delay, f.Length, fps));
        if (!completed && Frame == f.Length - 1)
        {
            completed = true;
            if (Completed != null) Completed();
        }
    }

    void Show(int index)
    {
        var f = Frames();
        if (f == null) return;
        Frame = Mathf.Clamp(index, 0, f.Length - 1);
        if (image != null) image.sprite = f[Frame];
        if (spriteRenderer != null) spriteRenderer.sprite = f[Frame];
    }
}
