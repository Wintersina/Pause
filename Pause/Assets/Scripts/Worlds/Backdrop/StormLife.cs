using UnityEngine;

// The small, calm motion on Storm's ground landmarks (a backdrop must stay secondary to the bullets):
//   * the turbine raft's three FANS spin. The rotors are cut out of the landmark art into their own sprites (atlas "rotors",
//     tools: the turbine cell of "landmarks" keeps the towers and hubs) and turned about their hubs, each at its own speed
//     that swells and eases with the wind (nearest-filtered, so no blur);
//   * the relay dish turns on its mast;
//   * every landmark bobs a hair on the wind.
// (2026-11-28: the thunderhead towers -- their breathing, heart glow, flickers and their own lightning layer, atlas "thunder" -- and
// the lightning-rod spires' tip crackle were RETIRED with those landmarks: the lightning CELL and RODS props carry that now.)
// Everything is driven by the scaled dt the director gets, so a paused game is a still frame. Pooled children, no allocation.
public static class StormLifeTuning
{
    public const int Rotors = 3;
    // The three fans of "turbine_raft_00", left to right: hub centre on the 256 cell (px from the top-left) and speed in deg/s
    // (negative = clockwise on screen). Real turbines are slow; this is a readable lazy spin.
    // 2026-10 restyle: the raft is repainted with BARE hubs and the fans are three painted three-blade rotors; hubs measured on the new raft,
    // FanScales = each fan's size relative to its 119 px rotor cell. A steady, clearly visible spin (about 0.3 turns a second, wind-swelled);
    // StormLifeTest keeps every speed inside 15..140 deg/s.
    public static readonly Vector2[] FanHubs = { new Vector2(148.3f, 49.8f), new Vector2(80.7f, 96.8f), new Vector2(205.6f, 125.6f) };
    public static readonly float[] FanScales = { .589f, .442f, .442f };
    public static readonly float[] FanSpeeds = { -100f, 112f, -106f };
    // The relay dish turns on its mast: the painted dish ("dish_00" in the rotors atlas, pivot = the cell centre) sits on the mast's bare
    // mount and turns about the vertical axis (a steady DishSpeed deg/s: its width follows |cos|, mirrored past the edge-on moment).
    public const string DishPiece = "relay_dish_00";
    public static readonly Vector2 DishMount = new Vector2(123.1f, 9.6f);      // on the 256 cell, px from the top-left
    public static float DishScale = .7f, DishSpeed = 72f, DishMinWidth = .16f;
    public static float FanSwell = .22f, FanSwellHz = .11f;       // the wind: speed x (1 + swell sin)
    public const string TurbinePiece = "turbine_raft_00";

    public static float BobAmp = .022f, BobPeriodMin = 5f, BobPeriodMax = 9f, SwayAmp = .016f;
}

public sealed class StormLife
{
    sealed class Extras
    {
        public SpriteRenderer[] rotors;
        public SpriteRenderer dish;        // the relay dish piece's turning dish
        public float dishAngle;
        public readonly float[] angle = new float[StormLifeTuning.Rotors];
        public float phase, bobPeriod;
    }

    readonly BackdropPool ground;
    readonly Extras[] extras;
    readonly Sprite turbine, relay, dishArt;
    readonly System.Random rng;
    float clock;

    public float Clock => clock;
    // Preview only (before / after clips): a static frame of the old look, fans parked, no bob.
    public static bool Still;

    public StormLife(BackdropPool ground, BackdropAtlas rotorArt, Sprite turbine, int seed, Sprite relayPiece = null)
    {
        relay = relayPiece;
        dishArt = rotorArt != null ? rotorArt.Get("dish_00") : null;
        this.ground = ground;
        this.turbine = turbine;
        rng = new System.Random(seed);
        extras = new Extras[ground.items.Count];
        Sprite[] rs = rotorArt != null ? rotorArt.Frames("rotor") : new Sprite[0];
        const float ppu = BackdropAtlas.PixelsPerUnit;
        for (int i = 0; i < extras.Length; i++)
        {
            var p = ground.items[i];
            var e = extras[i] = new Extras();
            e.phase = (float)rng.NextDouble() * 6.2832f;
            e.bobPeriod = Mathf.Lerp(StormLifeTuning.BobPeriodMin, StormLifeTuning.BobPeriodMax, (float)rng.NextDouble());
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
                    go.transform.localScale = new Vector3(StormLifeTuning.FanScales[k], StormLifeTuning.FanScales[k], 1f);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = rs[k];
                    sr.sortingOrder = order;
                    sr.enabled = false;
                    e.rotors[k] = sr;
                }
            }
            if (dishArt != null && relay != null)
            {
                var go = new GameObject("dish");
                go.transform.SetParent(p.body, false);
                go.transform.localPosition = new Vector3((StormLifeTuning.DishMount.x - 128f) / ppu, (128f - StormLifeTuning.DishMount.y) / ppu, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = dishArt;
                sr.sortingOrder = order;
                sr.enabled = false;
                e.dish = sr;
            }
        }
    }

    // ---- reads for tests ----
    public bool IsDish(BackdropPiece p) { return relay != null && p.sr.sprite == relay; }
    public float DishAngle(BackdropPiece p) { return extras[ground.items.IndexOf(p)].dishAngle; }
    public float DishWidth(BackdropPiece p) { var e = extras[ground.items.IndexOf(p)]; return e.dish != null && e.dish.enabled ? e.dish.transform.localScale.x : 0f; }
    public bool IsFan(BackdropPiece p) { return turbine != null && p.sr.sprite == turbine; }
    public float[] RotorAngles(BackdropPiece p) { return extras[ground.items.IndexOf(p)].angle; }
    public bool RotorsShown(BackdropPiece p)
    {
        var e = extras[ground.items.IndexOf(p)];
        if (e.rotors == null) return false;
        for (int k = 0; k < e.rotors.Length; k++) if (!e.rotors[k].enabled) return false;
        return true;
    }
    public static float FanSpeedAt(int rotor, float t)
    {
        return StormLifeTuning.FanSpeeds[rotor] * (1f + StormLifeTuning.FanSwell * Mathf.Sin(6.2832f * StormLifeTuning.FanSwellHz * t + rotor * 2.1f));
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
        if (e.dish != null)
        {
            e.dish.enabled = IsDish(p);
            e.dish.transform.localScale = new Vector3(StormLifeTuning.DishScale, StormLifeTuning.DishScale, 1f);
            e.dish.color = new Color(1f, 1f, 1f, a);
        }
    }

    public void Step(float dt)
    {
        if (dt <= 0f) return;
        if (!Still) clock += dt;
        for (int i = 0; i < extras.Length; i++)
        {
            var p = ground.items[i];
            var e = extras[i];
            if (!p.active) continue;
            float k = Mathf.Max(.0001f, p.root.localScale.x);
            float a = p.sr.color.a;
            float t = clock + e.phase;
            if (Still) { StepStill(p, e, a); continue; }

            // hover: a hair of bob and sway on the wind (body space is the piece's own, so divide by its scale)
            float bob = Mathf.Sin(6.2832f * t / e.bobPeriod) * StormLifeTuning.BobAmp / k;
            float sway = Mathf.Sin(6.2832f * t / (e.bobPeriod * 1.7f) + 1.3f) * StormLifeTuning.SwayAmp / k;
            p.body.localPosition = new Vector3(sway, bob, 0f);

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
            if (e.dish != null)
            {
                bool relayPiece = IsDish(p);
                if (e.dish.enabled != relayPiece) e.dish.enabled = relayPiece;
                if (relayPiece)
                {
                    e.dishAngle += StormLifeTuning.DishSpeed * dt;
                    if (e.dishAngle > 3600f) e.dishAngle %= 360f;
                    float c = Mathf.Cos(e.dishAngle * Mathf.Deg2Rad);
                    float w = Mathf.Sign(c == 0f ? 1f : c) * Mathf.Max(StormLifeTuning.DishMinWidth, Mathf.Abs(c));
                    e.dish.transform.localScale = new Vector3(w * StormLifeTuning.DishScale, StormLifeTuning.DishScale, 1f);
                    e.dish.color = new Color(1f, 1f, 1f, a);
                }
            }
        }
    }
}
