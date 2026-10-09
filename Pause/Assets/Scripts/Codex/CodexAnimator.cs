using UnityEngine;
using UnityEngine.UI;

// What a codex entry's art does while it sits on a card or in the detail
// view: its idle loop as held steps (one sprite + hold per step), optional
// attack "tells" for the detail view, and gentle whole-sprite motion (a spin,
// a sway, a slow pan). Built once per entry by CodexAnimations from the same
// loaders the game draws with, then shared by every animator showing it.
public enum CodexAnimKind { Static, Log, Enemy, Mine, Boss, Atom, Ship, World, Portal }

public sealed class CodexAnimation
{
    // How the art sits in its square box.
    public enum FitMode
    {
        Stretch,   // fill the box, aspect kept (the old static look)
        Union,     // every frame placed by its own bounds inside the union of all frames, so pivots never jump
        Cover,     // fill the box completely, overflow clipped (the worlds' round mask)
    }

    public CodexAnimKind kind;
    public FitMode fit = FitMode.Union;

    // Idle loop: idle[i] shows for idleHold[i] seconds.
    public Sprite[] idle;
    public float[] idleHold;
    // Drawn beneath the loop and never changed (the green atom's untouched
    // original; its loop is light overlays on top). Null: the loop is the art.
    public Sprite under;

    // Detail view only: attack wind-ups, one variant per tell in turn, each
    // played tellRepeats times, every tellGap seconds.
    public Sprite[][] tells;
    public float[][] tellHolds;
    public int tellRepeats = 1;
    public Vector2 tellGap = new Vector2(3f, 6f);

    // Whole-sprite motion.
    public float spinDegreesPerSecond;
    public float swayDegrees, swayPeriod;
    public float driftPeriod;          // Cover: seconds per slow up-and-down pan
    public float overscan = 1f;        // Cover: extra size so a square texture can still pan

    public Bounds union;               // sprite units, every drawn sprite
    public int distinctIdle;           // different drawings in the idle loop
    bool hadArt;

    public float IdleLoopSeconds
    {
        get
        {
            float t = 0f;
            if (idleHold != null) for (int i = 0; i < idleHold.Length; i++) t += idleHold[i];
            return t;
        }
    }

    public bool HasArt { get { return idle != null && idle.Length > 0 && idle[0] != null; } }

    // Anything to tick at all: more than one drawing, or motion.
    public bool Animates
    {
        get
        {
            if (!HasArt) return false;
            return distinctIdle > 1 || spinDegreesPerSecond != 0f || (swayDegrees > 0f && swayPeriod > 0f) ||
                   driftPeriod > 0f;
        }
    }

    public bool HasTell { get { return tells != null && tells.Length > 0; } }

    // A Sprite.Create()d frame dies with an editor scene swap: re-resolve.
    public bool Stale
    {
        get
        {
            if (!hadArt) return false;
            if (idle == null || idle.Length == 0 || idle[0] == null) return true;
            return hadUnder && under == null;
        }
    }
    bool hadUnder;

    // One drawing, no motion: shown exactly as before the codex animated.
    public static CodexAnimation Static(CodexAnimKind kind, Sprite sprite)
    {
        var a = new CodexAnimation { kind = kind, fit = FitMode.Stretch };
        a.idle = new[] { sprite };
        a.idleHold = new[] { 1f };
        a.Finish();
        return a;
    }

    // A held-step loop. Null drawings are dropped; null if nothing is left.
    public static CodexAnimation Loop(CodexAnimKind kind, Sprite[] steps, float[] holds)
    {
        if (steps == null || holds == null) return null;
        int n = 0;
        for (int i = 0; i < steps.Length && i < holds.Length; i++) if (steps[i] != null) n++;
        if (n == 0) return null;
        var a = new CodexAnimation { kind = kind };
        a.idle = new Sprite[n];
        a.idleHold = new float[n];
        n = 0;
        for (int i = 0; i < steps.Length && i < holds.Length; i++)
        {
            if (steps[i] == null) continue;
            a.idle[n] = steps[i];
            a.idleHold[n] = Mathf.Max(holds[i], 1f / 60f);
            n++;
        }
        return a;
    }

    // Adds a tell variant (skipped if any of its drawings is missing).
    public void AddTell(Sprite[] steps, float[] holds)
    {
        if (steps == null || holds == null || steps.Length == 0 || steps.Length != holds.Length) return;
        foreach (var s in steps) if (s == null) return;
        int n = tells == null ? 0 : tells.Length;
        var t = new Sprite[n + 1][];
        var h = new float[n + 1][];
        for (int i = 0; i < n; i++) { t[i] = tells[i]; h[i] = tellHolds[i]; }
        t[n] = steps;
        h[n] = holds;
        tells = t;
        tellHolds = h;
    }

    public void SetUnder(Sprite sprite)
    {
        under = sprite;
    }

    // Call once everything is set: the union bounds and the distinct count.
    public CodexAnimation Finish()
    {
        bool any = false;
        var u = new Bounds();
        Grow(ref u, ref any, under);
        if (idle != null) foreach (var s in idle) Grow(ref u, ref any, s);
        if (tells != null) foreach (var seq in tells) foreach (var s in seq) Grow(ref u, ref any, s);
        union = any ? u : new Bounds(Vector3.zero, Vector3.one);

        distinctIdle = 0;
        if (idle != null)
            for (int i = 0; i < idle.Length; i++)
            {
                if (idle[i] == null) continue;
                bool seen = false;
                for (int j = 0; j < i; j++) seen |= idle[j] == idle[i];
                if (!seen) distinctIdle++;
            }
        hadArt = HasArt;
        hadUnder = under != null;
        return this;
    }

    static void Grow(ref Bounds u, ref bool any, Sprite s)
    {
        if (s == null) return;
        var b = s.bounds;
        b.center = new Vector3(b.center.x, b.center.y, 0f);
        b.size = new Vector3(b.size.x, b.size.y, 0f);
        if (!any) { u = b; any = true; }
        else u.Encapsulate(b);
    }
}

// Plays an entry's CodexAnimation on a codex Image: a card's art or the
// detail view's. The panel binds it when it fills the card and ticks it from
// its own Update on unscaled time, and only while the card is on screen, so
// it never runs off-screen, behind the detail view or with the panel closed.
// Nothing here allocates once bound.
[DisallowMultipleComponent]
public class CodexAnimator : MonoBehaviour
{
    // A long hitch (or the app coming back from the background) is clamped
    // so the loop resumes rather than skipping through dozens of steps.
    public const float MaxStep = .1f;

    Image image, overlay;
    RectTransform box;
    CodexAnimation anim;
    bool detail;
    int step;
    float hold, clock, untilTell;
    bool telling;
    int tellVariant, tellStep, tellLoop;
    int changes;
    Sprite shown;

    // One-shot death (detail view): the game's three-drawing strip, then a
    // short empty beat and a fade back into the idle loop.
    public enum DeathPhase { None, Strip, Gap, FadeIn }
    public static readonly float[] DeathHolds =
        { .08f, .11f, .2f };   // EnemyDeathFlipbook's Flash / Rupture / Smoke
    public const float DeathGap = .35f, DeathFadeIn = .4f;
    Sprite[] deathFrames;
    float[] stripHolds;
    CodexBurst burst;
    DeathPhase death;
    float deathClock;
    public DeathPhase Death { get { return death; } }
    public bool Dying { get { return death != DeathPhase.None; } }

    // Set by the panel each tick: is this animator being advanced?
    public bool Ticking { get; set; }
    public CodexAnimation Animation { get { return anim; } }
    public bool Animates { get { return anim != null && anim.Animates; } }
    public bool Telling { get { return telling; } }
    public int Step { get { return step; } }
    public float Clock { get { return clock; } }
    // How many times the drawing changed since the last Bind.
    public int FrameChanges { get { return changes; } }
    // The drawing the loop shows now (on the overlay for an `under` entry).
    public Sprite Shown { get { return shown; } }
    public Image Image { get { return image; } }
    public Image Overlay { get { return overlay; } }

    public static CodexAnimator On(Image art)
    {
        var a = art.GetComponent<CodexAnimator>();
        if (a == null) a = art.gameObject.AddComponent<CodexAnimator>();
        a.image = art;
        a.box = art.transform.parent as RectTransform;
        return a;
    }

    // Shows `a` from its first drawing. `locked`: drawn as the flat ink
    // silhouette (every frame, the overlay too), never in colour. `detail`:
    // the occasional attack tell plays. `phase` (seconds) starts a card part
    // way into its loop so a grid of one family doesn't move in lockstep.
    public void Bind(CodexAnimation a, bool locked, bool detail, float phase = 0f)
    {
        anim = a != null && a.HasArt ? a : null;
        death = DeathPhase.None;
        deathFrames = null;
        if (burst != null) { burst.Hide(); burst = null; }
        this.detail = detail;
        telling = false;
        tellVariant = tellStep = tellLoop = 0;
        step = 0;
        clock = 0f;
        Ticking = false;

        // Locked: the flat silhouette material (shape only, no detail).
        CodexUi.PaintArt(image, locked);
        image.rectTransform.localRotation = Quaternion.identity;
        if (anim == null)
        {
            // Nothing to play: whatever the panel put on the image stays.
            if (overlay != null) overlay.enabled = false;
            shown = image.sprite;
            changes = 0;
            Stretch(image.rectTransform);
            return;
        }

        if (anim.under != null)
        {
            EnsureOverlay();
            overlay.enabled = true;
            CodexUi.PaintArt(overlay, locked);
            overlay.rectTransform.localRotation = Quaternion.identity;
            image.sprite = anim.under;
            Place(image, anim.under);
        }
        else if (overlay != null)
        {
            overlay.enabled = false;
            overlay.sprite = null;
        }

        hold = anim.idleHold[0];
        untilTell = anim.tellGap.x;
        Show(anim.idle[0], true);
        image.enabled = image.sprite != null;

        if (phase > 0f && anim.Animates)
        {
            float loop = anim.IdleLoopSeconds;
            Run(loop > 0f ? Mathf.Repeat(phase, loop) : 0f, false);
            clock = phase;
        }
        Motion();
        changes = 0;
    }

    // Lays the drawing out again for the box's current size (after the panel
    // re-lays its cards out).
    public void Relayout()
    {
        if (anim == null) return;
        if (anim.under != null) Place(image, anim.under);
        var target = anim.under != null ? overlay : image;
        if (target != null && target.sprite != null) Place(target, target.sprite);
        Motion();
    }

    // dt seconds of unscaled time.
    public void Advance(float dt)
    {
        if (anim == null || dt <= 0f) return;
        if (dt > MaxStep) dt = MaxStep;
        if (death != DeathPhase.None) { AdvanceDeath(dt); if (death == DeathPhase.Strip || death == DeathPhase.Gap) return; }
        if (!anim.Animates) return;
        clock += dt;
        Run(dt, detail && anim.HasTell);
        Motion();
    }

    // Plays the death strip once over the idle (no-op while one is running or
    // with nothing bound). The frames share the idle's box and scale.
    public bool PlayDeath(Sprite[] frames) { return PlayDeath(frames, DeathHolds); }

    // Any strip with a hold (seconds) per drawing: an elite's own death
    // strip, a boss' six cells.
    public bool PlayDeath(Sprite[] frames, float[] holds)
    {
        if (anim == null || anim.under != null || frames == null || holds == null || frames.Length == 0 || frames.Length != holds.Length) return false;
        if (death != DeathPhase.None) return false;
        for (int i = 0; i < frames.Length; i++) if (frames[i] == null) return false;
        deathFrames = frames;
        stripHolds = holds;
        burst = null;
        death = DeathPhase.Strip;
        deathClock = 0f;
        telling = false;
        tellStep = tellLoop = 0;
        Show(frames[0], true);
        image.enabled = true;
        return true;
    }

    // The composed elite death (no strip of its own): the hit drawing, then
    // the generic debris / sparks / ring (CodexBurst) over the art box.
    // `unit`: how many pixels one world unit of the art is (see UnitScale).
    public bool PlayBurst(CodexBurst b, Sprite flash)
    {
        if (anim == null || anim.under != null || b == null || flash == null) return false;
        if (death != DeathPhase.None) return false;
        burst = b;
        deathFrames = null;
        stripHolds = null;
        death = DeathPhase.Strip;
        deathClock = 0f;
        telling = false;
        tellStep = tellLoop = 0;
        Show(flash, true);
        image.enabled = true;
        b.Begin(image.rectTransform.anchoredPosition);
        return true;
    }

    // Pixels per world unit of the idle art as the box lays it out.
    public float UnitScale()
    {
        if (anim == null || box == null) return 0f;
        var u = anim.union;
        return Side() / Mathf.Max(.0001f, Mathf.Max(u.size.x, u.size.y));
    }

    public RectTransform Box { get { return box; } }

    void AdvanceDeath(float dt)
    {
        deathClock += dt;
        switch (death)
        {
            case DeathPhase.Strip:
                float t = deathClock;
                if (burst != null)
                {
                    if (t >= CodexBurst.Duration)
                    {
                        burst.Hide();
                        death = DeathPhase.Gap;
                        deathClock = 0f;
                        image.enabled = false;
                    }
                    else
                    {
                        if (t >= CodexBurst.FlashSeconds) image.enabled = false;
                        burst.Step(t);
                    }
                    break;
                }
                int f = 0;
                float end = 0f;
                for (int i = 0; i < stripHolds.Length; i++) { end += stripHolds[i]; if (t >= end && i < stripHolds.Length - 1) f = i + 1; }
                if (t >= end)
                {
                    death = DeathPhase.Gap;
                    deathClock = 0f;
                    image.enabled = false;
                }
                else if (image.sprite != deathFrames[f]) Show(deathFrames[f], true);
                break;
            case DeathPhase.Gap:
                if (deathClock >= DeathGap)
                {
                    death = DeathPhase.FadeIn;
                    deathClock = 0f;
                    step = 0;
                    hold = anim.idleHold[0];
                    untilTell = anim.tellGap.x;
                    Show(anim.idle[0], true);
                    CodexUi.SetAlpha(image, 0f);
                    image.enabled = true;
                    Motion();
                }
                break;
            case DeathPhase.FadeIn:
                float k = Mathf.Clamp01(deathClock / DeathFadeIn);
                CodexUi.SetAlpha(image, k);
                if (k >= 1f) { death = DeathPhase.None; deathFrames = null; burst = null; }
                break;
        }
    }

    void Run(float dt, bool canTell)
    {
        int idleN = anim.idle.Length;
        if (idleN <= 1 && !telling && !canTell) return;
        if (canTell && !telling) untilTell -= dt;
        hold -= dt;
        int guard = 0;
        while (hold <= 0f && guard++ < 64)
        {
            if (telling)
            {
                var seq = anim.tells[tellVariant];
                tellStep++;
                if (tellStep >= seq.Length)
                {
                    tellStep = 0;
                    tellLoop++;
                    if (tellLoop >= Mathf.Max(1, anim.tellRepeats))
                    {
                        telling = false;
                        tellVariant = (tellVariant + 1) % anim.tells.Length;
                        untilTell = Random.Range(anim.tellGap.x, anim.tellGap.y);
                        step = 0;
                        hold += anim.idleHold[0];
                        Show(anim.idle[0], false);
                        continue;
                    }
                }
                hold += anim.tellHolds[tellVariant][tellStep];
                Show(seq[tellStep], false);
                continue;
            }

            // The tell waits for the loop to come back round to its key pose.
            if (canTell && untilTell <= 0f && step == idleN - 1)
            {
                telling = true;
                tellStep = 0;
                tellLoop = 0;
                hold += anim.tellHolds[tellVariant][0];
                Show(anim.tells[tellVariant][0], false);
                continue;
            }
            step = (step + 1) % idleN;
            hold += anim.idleHold[step];
            Show(anim.idle[step], false);
        }
        if (hold <= 0f) hold = anim.idleHold[step];   // a pathological table: never spin forever
    }

    void Show(Sprite s, bool force)
    {
        shown = s;
        var target = anim.under != null ? overlay : image;
        if (target == null) return;
        if (!force && target.sprite == s) return;
        target.sprite = s;
        changes++;
        Place(target, s);
    }

    // The spin, sway and pan, from the clock.
    void Motion()
    {
        if (anim == null) return;
        float z = 0f;
        if (anim.spinDegreesPerSecond != 0f) z = -Mathf.Repeat(clock * anim.spinDegreesPerSecond, 360f);
        else if (anim.swayDegrees > 0f && anim.swayPeriod > 0f)
            z = Mathf.Sin(clock * 2f * Mathf.PI / anim.swayPeriod) * anim.swayDegrees;
        if (z != 0f || anim.spinDegreesPerSecond != 0f || anim.swayDegrees > 0f)
        {
            var q = Quaternion.Euler(0f, 0f, z);
            image.rectTransform.localRotation = q;
            if (overlay != null && overlay.enabled) overlay.rectTransform.localRotation = q;
        }

        if (anim.fit == CodexAnimation.FitMode.Cover && anim.driftPeriod > 0f && box != null)
        {
            var rt = image.rectTransform;
            float side = Side();
            float spare = Mathf.Max(0f, (rt.sizeDelta.y - side) * .5f);
            float y = Mathf.Sin(clock * 2f * Mathf.PI / anim.driftPeriod) * spare;
            var p = rt.anchoredPosition;
            if (!Mathf.Approximately(p.y, y)) rt.anchoredPosition = new Vector2(p.x, y);
        }
    }

    float Side()
    {
        if (box == null) return 0f;
        var size = box.rect.size;
        return Mathf.Min(size.x, size.y);
    }

    void Place(Image target, Sprite s)
    {
        var rt = target.rectTransform;
        if (anim == null || s == null || anim.fit == CodexAnimation.FitMode.Stretch || box == null)
        {
            Stretch(rt);
            return;
        }
        float side = Side();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        var b = s.bounds;
        if (anim.fit == CodexAnimation.FitMode.Cover)
        {
            float k = side / Mathf.Max(.0001f, Mathf.Min(b.size.x, b.size.y)) * Mathf.Max(1f, anim.overscan);
            SetRect(rt, new Vector2(b.size.x * k, b.size.y * k), new Vector2(0f, rt.anchoredPosition.y));
            return;
        }
        var u = anim.union;
        // A turning drawing must fit the box at every angle: its diagonal.
        bool turns = anim.spinDegreesPerSecond != 0f;
        float extent = turns ? new Vector2(u.size.x, u.size.y).magnitude : Mathf.Max(u.size.x, u.size.y);
        float scale = side / Mathf.Max(.0001f, extent);
        SetRect(rt, new Vector2(b.size.x * scale, b.size.y * scale),
                new Vector2((b.center.x - u.center.x) * scale, (b.center.y - u.center.y) * scale));
    }

    // Writes only what changed: an untouched rect dirties no layout or mesh.
    static void SetRect(RectTransform rt, Vector2 size, Vector2 pos)
    {
        if (rt.sizeDelta != size) rt.sizeDelta = size;
        if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(.5f, .5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    void EnsureOverlay()
    {
        if (overlay != null) return;
        overlay = CodexUi.NewImage("Overlay", image.transform.parent, null, Color.white);
        overlay.preserveAspect = true;
        overlay.transform.SetSiblingIndex(image.transform.GetSiblingIndex() + 1);
    }
}
