using System.Collections.Generic;
using UnityEngine;

// The small, calm motion on Storm's ground landmarks (a backdrop must stay secondary to the bullets):
//   * the turbine raft's three FANS spin. The rotors are cut out of the landmark art into their own sprites (atlas "rotors",
//     tools: the turbine cell of "landmarks" keeps the towers and hubs) and turned about their hubs, each at its own speed
//     that swells and eases with the wind (nearest-filtered, so no blur);
//   * every thunderhead tower breathes (a 1-2 % scale swell, a slow drift) and its heart glows: a soft pulse a few seconds
//     long, and now and then one tower flickers once or twice inside (never more than ~2 per second, one tower at a time,
//     seconds apart: photosensitivity-safe, the world's real flashes are StormLightning's);
//   * the lightning INSIDE every thunderhead tower is its own layer (atlas "thunder", cut out of the tower art; the tower
//     cell of "landmarks" is the lightning-free cloud): it hangs dark and barely smouldering, then strikes (a quick flash in,
//     sometimes a re-strike, a slow fade out) and lights the tower's heart with it. Strikes come from StormLife's own
//     flickers and from the weather's sheet flashes (StormDirector.Strike); never closer than BoltMinGap seconds anywhere;
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
    // The bolts inside a tower: a faint smoulder between strikes, a strike's rise / fall, a re-strike, the heart's lift.
    public static float BoltIdle = .05f, BoltIdleSwing = .04f, BoltRise = .05f, BoltFall = .5f;
    public static float BoltRestrikeChance = .4f, BoltRestrikeAt = .22f, BoltRestrikePeak = .7f, BoltMinGap = .4f, BoltHeartLift = .03f;
    public const string BoltAtlas = "thunder";
    public static readonly Color HeartTint = new Color(.72f, .84f, 1f, 1f);
}

public sealed class StormLife
{
    sealed class Extras
    {
        public SpriteRenderer[] rotors;
        public SpriteRenderer heart;       // towers: the glowing core
        public SpriteRenderer tip;
        public SpriteRenderer bolts;       // towers: the lightning layer over the cloud
        public Sprite boltsFor;            // the tower sprite the bolts were resolved for
        public float strikeAt = -1f, strikePeak = 1f;   // clock when a strike began, -1 none
        public float lift;                 // the heart's eased-in share of a strike's light
        public bool again;                 // this strike has a re-strike         // masts: the rod tip's crackle
        public readonly float[] angle = new float[StormLifeTuning.Rotors];
        public float phase, bobPeriod, breathePeriod, heartPeriod;
        public float flickerAt = -1f;      // clock when this tower's flicker began, -1 none
    }

    readonly BackdropPool ground;
    readonly Extras[] extras;
    readonly Sprite turbine, glowSprite;
    readonly Dictionary<Sprite, Sprite> boltOf = new Dictionary<Sprite, Sprite>();
    float lastStrike = -9f;
    readonly System.Random rng;
    float clock, nextFlicker;

    public float Clock => clock;
    // Preview only (before / after clips): a static frame of the old look, fans parked, no bob, breathing or glow.
    public static bool Still;

    public StormLife(BackdropPool ground, BackdropAtlas rotorArt, Sprite turbine, Sprite glowSprite, int seed, BackdropAtlas landmarkArt = null, BackdropAtlas boltArt = null)
    {
        if (landmarkArt != null && boltArt != null)
            for (int k = 0; k < StormTuning.Towers.Length; k++)
            {
                Sprite tower = landmarkArt.Get(StormTuning.Towers[k]), bolt = boltArt.Get("bolts_" + k.ToString("00"));
                if (tower != null && bolt != null) boltOf[tower] = bolt;
            }
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
            if (boltOf.Count > 0)
            {
                var go = new GameObject("bolts");
                go.transform.SetParent(p.body, false);
                e.bolts = go.AddComponent<SpriteRenderer>();
                e.bolts.sortingOrder = order;
                e.bolts.sharedMaterial = p.sr.sharedMaterial;
                e.bolts.enabled = false;
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
    public float BoltAlpha(BackdropPiece p) { var e = extras[ground.items.IndexOf(p)]; return e.bolts != null && e.bolts.enabled ? e.bolts.color.a : 0f; }
    public bool HasBolts { get { return boltOf.Count > 0; } }
    public float LastStrike { get { return lastStrike; } }
    public int Strikes { get; private set; }
    public float TipAlpha(BackdropPiece p) { var e = extras[ground.items.IndexOf(p)]; return e.tip != null && e.tip.enabled ? e.tip.color.a : 0f; }
    public static float FanSpeedAt(int rotor, float t)
    {
        return StormLifeTuning.FanSpeeds[rotor] * (1f + StormLifeTuning.FanSwell * Mathf.Sin(6.2832f * StormLifeTuning.FanSwellHz * t + rotor * 2.1f));
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    // One flicker bump: up in .07 s, down over .28 s.
    static float Bump(float t) { return t <= 0f ? 0f : t < .07f ? Smooth(t / .07f) : Mathf.Max(0f, 1f - Smooth((t - .07f) / .28f)); }

    // One strike's light: up in BoltRise, down over BoltFall (smoothstepped).
    static float Flash(float t) { return t <= 0f ? 0f : t < StormLifeTuning.BoltRise ? Smooth(t / StormLifeTuning.BoltRise) : Mathf.Max(0f, 1f - Smooth((t - StormLifeTuning.BoltRise) / StormLifeTuning.BoltFall)); }
    public static float StrikeLevel(float t, bool again)
    {
        float v = Flash(t);
        if (again) v = Mathf.Max(v, StormLifeTuning.BoltRestrikePeak * Flash(t - StormLifeTuning.BoltRestrikeAt));
        return v;
    }
    public static float StrikeLength(bool again) { return StormLifeTuning.BoltRise + StormLifeTuning.BoltFall + (again ? StormLifeTuning.BoltRestrikeAt : 0f); }

    // Strike the tower's lightning (the weather's sheet flash found this tower, or its own flicker). False: too soon after another.
    public bool Strike(BackdropPiece p, float peak)
    {
        if (clock - lastStrike < StormLifeTuning.BoltMinGap) return false;
        int i = ground.items.IndexOf(p);
        if (i < 0 || !p.active || extras[i].bolts == null) return false;
        var e = extras[i];
        e.strikeAt = clock; e.strikePeak = Mathf.Clamp01(peak);
        e.again = (float)rng.NextDouble() < StormLifeTuning.BoltRestrikeChance;
        lastStrike = clock; Strikes++;
        return true;
    }

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
        if (e.bolts != null) e.bolts.enabled = false;
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
                if (p.active && p.kind == StormDirector.KindTower && p.y < 2.2f && p.y > -2.2f) { extras[i].flickerAt = clock; Strike(p, 1f); break; }
            }
        }
        for (int i = 0; i < extras.Length; i++)
        {
            var p = ground.items[i];
            var e = extras[i];
            if (!p.active) { e.flickerAt = -1f; e.strikeAt = -1f; e.lift = 0f; if (e.bolts != null && e.bolts.enabled) e.bolts.enabled = false; continue; }
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
            float strike = 0f;
            if (e.bolts != null)
            {
                if (e.strikeAt >= 0f)
                {
                    float st = clock - e.strikeAt;
                    if (st > StrikeLength(e.again)) e.strikeAt = -1f; else strike = StrikeLevel(st, e.again) * e.strikePeak;
                }
                if (tower && e.boltsFor != p.sr.sprite)   // a new tower drawing in this slot: find its bolts once
                {
                    e.boltsFor = p.sr.sprite;
                    Sprite art;
                    e.bolts.sprite = boltOf.TryGetValue(p.sr.sprite, out art) ? art : null;
                }
                float level = StormLifeTuning.BoltIdle + StormLifeTuning.BoltIdleSwing * (.5f + .5f * Mathf.Sin(6.2832f * t / (e.heartPeriod * 1.3f))) + strike * (1f - StormLifeTuning.BoltIdle);
                bool show = tower && e.bolts.sprite != null && level * a > .004f;
                if (e.bolts.enabled != show) e.bolts.enabled = show;
                if (show)
                {
                    Color pc = p.sr.color;
                    e.bolts.color = new Color(pc.r, pc.g, pc.b, Mathf.Min(1f, level) * a);
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
                    e.lift += (strike * StormLifeTuning.BoltHeartLift - e.lift) * Mathf.Min(1f, dt * 8f);   // eased, so the heart never snaps
                    pulse += e.lift;
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
