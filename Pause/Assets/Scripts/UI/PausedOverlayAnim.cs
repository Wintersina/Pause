using UnityEngine;

// Flipbook for the "paused" overlay (moveStarsBackground picks one of the
// PauseGlow variants each time the game freezes). Each time it is shown it
// pops in -- one tick squashed, one tick stretched, then the drawing -- and
// while it stays up a hard BONE glint sweeps across the bars every so often.
// The frames are drawn, not tweened (Art/UI/Pause/src~/build_pause.py), and
// it runs on unscaled time because the world is frozen at timeScale 0.
public class PausedOverlayAnim : MonoBehaviour
{
    public const string FxRoot = "PauseGlowFx/";
    const int Squash = 0, Stretch = 1, ShineFirst = 2, ShineCount = 4;
    // Pop-in tick (squash, stretch, rest). PausedLabel pops on the same one.
    public const float Tick = 1f / 24f;
    const float ShineEvery = 1.6f;

    SpriteRenderer sr;
    Sprite rest;
    Sprite[] frames;
    float clock;
    int step;          // 0 squash, 1 stretch, 2 rest/idle
    int shine = -1;
    float shineClock;
    Sprite lastShown;

    public static PausedOverlayAnim AddTo(GameObject go)
    {
        if (go == null) return null;
        var anim = go.GetComponent<PausedOverlayAnim>();
        return anim != null ? anim : go.AddComponent<PausedOverlayAnim>();
    }

    void Awake() { sr = GetComponent<SpriteRenderer>(); }

    void OnEnable() { Restart(); }

    void Restart()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        rest = sr != null ? sr.sprite : null;
        frames = rest != null ? FramesFor(rest.name) : null;
        step = 0;
        clock = 0f;
        shine = -1;
        shineClock = 0f;
        Show(frames != null ? frames[Squash] : null);
    }

    void OnDisable()
    {
        // Hand the chosen variant back untouched for the next pick.
        if (sr != null && rest != null) sr.sprite = rest;
    }

    // pausedGlow_a -> PauseGlowFx/pausedGlow_a_0..5 (null if none drawn).
    public static Sprite[] FramesFor(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName) || !spriteName.StartsWith("pausedGlow_")) return null;
        var list = new Sprite[ShineFirst + ShineCount];
        for (int i = 0; i < list.Length; i++)
        {
            list[i] = Resources.Load<Sprite>(FxRoot + spriteName + "_" + i);
            if (list[i] == null) return null;
        }
        return list;
    }

    void Update()
    {
        if (sr == null) return;
        // Someone else swapped the sprite (a new variant picked while we were
        // already active): start over from that drawing.
        if (sr.sprite != lastShown) Restart();
        if (frames == null) return;
        float dt = Time.unscaledDeltaTime;
        if (step < 2)
        {
            clock += dt;
            if (clock < Tick) return;
            clock = 0f;
            step++;
            Show(step == 1 ? frames[Stretch] : rest);
            return;
        }

        if (shine < 0)
        {
            shineClock += dt;
            if (shineClock < ShineEvery) return;
            shineClock = 0f;
            shine = 0;
            Show(frames[ShineFirst]);
            return;
        }
        shineClock += dt;
        if (shineClock < Tick) return;
        shineClock = 0f;
        shine++;
        if (shine >= ShineCount) { shine = -1; Show(rest); }
        else Show(frames[ShineFirst + shine]);
    }

    void Show(Sprite s)
    {
        if (s != null && sr != null) sr.sprite = s;
        if (sr != null) lastShown = sr.sprite;
    }
}
