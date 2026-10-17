using UnityEngine;

// The small, calm motion on Storm's ground landmarks (a backdrop must stay secondary to the bullets):
//   * the turbine raft's three FANS spin. The rotors are cut out of the landmark art into their own sprites (atlas "rotors",
//     tools: the turbine cell of "landmarks" keeps the towers and hubs) and turned about their hubs, each at its own speed
//     that swells and eases with the wind (nearest-filtered, so no blur);
//   * every thunderhead tower breathes (a 1-2 % scale swell, a slow drift) and its heart glows: a soft pulse a few seconds
//     long, and now and then one tower flickers once or twice inside (never more than ~2 per second, one tower at a time,
//     seconds apart: photosensitivity-safe, the world's real flashes are StormLightning's);
//   * lightning-rod masts crackle a faint glow at the tip;
//   * every landmark bobs a hair on the wind.
// Everything is driven by the scaled dt the director gets, so a paused game is a still frame. Pooled children, no allocation.
public static class StormLifeTuning
{
    public const int Rotors = 3;
    // The three fans of "turbine_raft_00", left to right: hub centre on the 256 cell (px from the top-left) and speed in deg/s
    // (negative = clockwise on screen). Real turbines are slow; this is a readable lazy spin.
    public static readonly Vector2[] FanHubs = { new Vector2(99.5f, 94.5f), new Vector2(150.5f, 120.5f), new Vector2(206.5f, 80.5f) };
    public static readonly float[] FanSpeeds = { -52f, 71f, -63f };
    public static float FanSwell = .22f, FanSwellHz = .11f;       // the wind: speed x (1 + swell sin)
    public const string TurbinePiece = "turbine_raft_00";

    public static float BobAmp = .022f, BobPeriodMin = 5f, BobPeriodMax = 9f, SwayAmp = .016f;
    public static float BreatheAmp = .014f, BreathePeriodMin = 6f, BreathePeriodMax = 10f;
    public static float HeartBase = .045f, HeartSwing = .05f, HeartPeriodMin = 3.4f, HeartPeriodMax = 5.6f, HeartSize = .75f;
    public static float FlickerPeak = .16f, FlickerGapMin = 3f, FlickerGapMax = 7f, FlickerSecondAt = .45f;
    public static float TipBase = .22f, TipSwing = .12f, TipSize = .16f;
    public static readonly Color HeartTint = new Color(.72f, .84f, 1f, 1f);
}

public sealed class StormLife
{
    sealed class Extras
    {
        public SpriteRenderer[] rotors;
        public SpriteRenderer heart;       // towers: the glowing core
        public SpriteRenderer tip;         // masts: the rod tip's crackle
        public readonly float[] angle = new float[StormLifeTuning.Rotors];
        public float phase, bobPeriod, breathePeriod, heartPeriod;
        public float flickerAt = -1f;      // clock when this tower's flicker began, -1 none
    }

    readonly BackdropPool ground;
    readonly Extras[] extras;
    readonly Sprite turbine, glowSprite;
    readonly System.Random rng;
    float clock, nextFlicker;

    public float Clock => clock;
    // Preview only (before / after clips): a static frame of the old look, fans parked, no bob, breathing or glow.
    public static bool Still;

    public StormLife(BackdropPool ground, BackdropAtlas rotorArt, Sprite turbine, Sprite glowSprite, int seed)
    {
        this.ground = ground;
        this.turbine = turbine;
        this.glowSprite = glowSprite;
        rng = new System.Random(seed);
        nextFlicker = StormLifeTuning.FlickerGapMin;
        extras = new Extras[ground.items.Count];
        Sprite[] rs = rotorArt != null ? rotorArt.Frames("rotor") : new Sprite[0];
        const float ppu = BackdropAtlas.PixelsPerUnit;
        for (int i = 0; i < extras.Length; i++)
        {
            var p = ground.items[i];
            var e = extras[i] = new Extras();
            e.phase = (float)rng.NextDouble() * 6.2832f;
            e.bobPeriod = Mathf.Lerp(StormLifeTuning.BobPeriodMin, StormLifeTuning.BobPeriodMax, (float)rng.NextDouble());
            e.breathePeriod = Mathf.Lerp(StormLifeTuning.BreathePeriodMin, StormLifeTuning.BreathePeriodMax, (float)rng.NextDouble());
            e.heartPeriod = Mathf.Lerp(StormLifeTuning.HeartPeriodMin, StormLifeTuning.HeartPeriodMax, (float)rng.NextDouble());
            int order = p.sr.sortingOrder + 1;
            if (rs.Length >= StormLifeTuning.Rotors)
            {
                e.rotors = new SpriteRenderer[StormLifeTuning.Rotors];
                for (int k = 0; k < e.rotors.Length; k++)
                {
                    var go = new GameObject("rotor" + k);
                    go.transform.SetParent(p.body, false);
                    var hub = StormLifeTuning.FanHubs[k];
                    go.transform.localPosition = new Vector3((hub.x - 128f) / ppu, (128f - hub.y) / ppu, 0f);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = rs[k];
                    sr.sortingOrder = order;
                    sr.enabled = false;
                    e.rotors[k] = sr;
                }
            }
            if (glowSprite != null)
            {
                e.heart = MakeGlow(p, "heart", order, StormLifeTuning.HeartSize, new Vector3(0f, .05f, 0f));
                e.tip = MakeGlow(p, "tip", order, StormLifeTuning.TipSize, new Vector3(0f, (128f - StormTuning.RodTipY) / ppu, 0f));
            }
        }
    }

    SpriteRenderer MakeGlow(BackdropPiece p, string name, int order, float scale, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p.body, false);
        go.transform.localPosition = at;
        go.transform.localScale = new Vector3(scale, scale, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = glowSprite;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    // ---- reads for tests ----
    public bool IsFan(BackdropPiece p) { return turbine != null && p.sr.sprite == turbine; }
    public float[] RotorAngles(BackdropPiece p) { return extras[ground.items.IndexOf(p)].angle; }
    public bool RotorsShown(BackdropPiece p)
    {
        var e = extras[ground.items.IndexOf(p)];
        if (e.rotors == null) return false;
        for (int k = 0; k < e.rotors.Length; k++) if (!e.rotors[k].enabled) return false;
        return true;
    }
    public float HeartAlpha(BackdropPiece p) { var e = extras[ground.items.IndexOf(p)]; return e.heart != null && e.heart.enabled ? e.heart.color.a : 0f; }
    public float TipAlpha(BackdropPiece p) { var e = extras[ground.items.IndexOf(p)]; return e.tip != null && e.tip.enabled ? e.tip.color.a : 0f; }
    public static float FanSpeedAt(int rotor, float t)
    {
        return StormLifeTuning.FanSpeeds[rotor] * (1f + StormLifeTuning.FanSwell * Mathf.Sin(6.2832f * StormLifeTuning.FanSwellHz * t + rotor * 2.1f));
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    // One flicker bump: up in .07 s, down over .28 s.
    static float Bump(float t) { return t <= 0f ? 0f : t < .07f ? Smooth(t / .07f) : Mathf.Max(0f, 1f - Smooth((t - .07f) / .28f)); }

    void StepStill(BackdropPiece p, Extras e, float a)
    {
        p.body.localPosition = Vector3.zero;
        if (e.rotors != null)
            for (int r = 0; r < e.rotors.Length; r++)
            {
                e.rotors[r].enabled = turbine != null && p.sr.sprite == turbine;
                e.rotors[r].transform.localRotation = Quaternion.identity;
                e.rotors[r].color = new Color(1f, 1f, 1f, a);
            }
        if (e.heart != null) { e.heart.enabled = false; e.tip.enabled = false; }
    }

    public void Step(float dt)
    {
        if (dt <= 0f) return;
        if (!Still) clock += dt;
        // pick the next tower to flicker, one at a time, seconds apart
        if (clock >= nextFlicker)
        {
            nextFlicker = clock + Mathf.Lerp(StormLifeTuning.FlickerGapMin, StormLifeTuning.FlickerGapMax, (float)rng.NextDouble());
            int start = rng.Next(extras.Length);
            for (int j = 0; j < extras.Length; j++)
            {
                int i = (start + j) % extras.Length;
                var p = ground.items[i];
                if (p.active && p.kind == StormDirector.KindTower && p.y < 2.2f && p.y > -2.2f) { extras[i].flickerAt = clock; break; }
            }
        }
        for (int i = 0; i < extras.Length; i++)
        {
            var p = ground.items[i];
            var e = extras[i];
            if (!p.active) { e.flickerAt = -1f; continue; }
            float k = Mathf.Max(.0001f, p.root.localScale.x);
            float sgn = p.body.localScale.x < 0f ? -1f : 1f;
            float a = p.sr.color.a;
            float t = clock + e.phase;
            if (Still) { StepStill(p, e, a); continue; }

            // hover: a hair of bob and sway on the wind (body space is the piece's own, so divide by its scale)
            float bob = Mathf.Sin(6.2832f * t / e.bobPeriod) * StormLifeTuning.BobAmp / k;
            float sway = Mathf.Sin(6.2832f * t / (e.bobPeriod * 1.7f) + 1.3f) * StormLifeTuning.SwayAmp / k;
            p.body.localPosition = new Vector3(sway, bob, 0f);

            bool tower = p.kind == StormDirector.KindTower, spire = p.kind == StormDirector.KindSpire;
            float breathe = tower ? 1f + Mathf.Sin(6.2832f * t / e.breathePeriod) * StormLifeTuning.BreatheAmp : 1f;
            p.body.localScale = new Vector3(sgn * breathe, breathe, 1f);

            if (e.rotors != null)
            {
                bool fan = turbine != null && p.sr.sprite == turbine;
                for (int r = 0; r < e.rotors.Length; r++)
                {
                    var sr = e.rotors[r];
                    if (sr.enabled != fan) sr.enabled = fan;
                    if (!fan) continue;
                    e.angle[r] += FanSpeedAt(r, clock + e.phase * .3f) * dt;
                    if (e.angle[r] > 3600f || e.angle[r] < -3600f) e.angle[r] %= 360f;
                    sr.transform.localRotation = Quaternion.Euler(0f, 0f, e.angle[r]);
                    sr.color = new Color(1f, 1f, 1f, a);
                }
            }
            if (e.heart != null)
            {
                if (e.heart.enabled != tower) e.heart.enabled = tower;
                if (tower)
                {
                    float pulse = StormLifeTuning.HeartBase + StormLifeTuning.HeartSwing * (.5f + .5f * Mathf.Sin(6.2832f * t / e.heartPeriod));
                    if (e.flickerAt >= 0f)
                    {
                        float ft = clock - e.flickerAt;
                        pulse += StormLifeTuning.FlickerPeak * (Bump(ft) + .6f * Bump(ft - StormLifeTuning.FlickerSecondAt));
                        if (ft > StormLifeTuning.FlickerSecondAt + .5f) e.flickerAt = -1f;
                    }
                    var c = StormLifeTuning.HeartTint; c.a = pulse * a;
                    e.heart.color = c;
                }
                if (e.tip.enabled != spire) e.tip.enabled = spire;
                if (spire)
                {
                    float f = .5f + .25f * Mathf.Sin(5.3f * t) + .25f * Mathf.Sin(2.9f * t + 1f);   // 0.46 and 0.84 Hz: a shimmer, no strobe
                    var c = StormLifeTuning.HeartTint; c.a = (StormLifeTuning.TipBase + StormLifeTuning.TipSwing * f) * a;
                    e.tip.color = c;
                }
            }
        }
    }
}
