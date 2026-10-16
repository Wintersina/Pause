using UnityEngine;

// WORLD HAZARDS (docs/world-weather-hazards.md): the data types. A hazard is a rare weather event that
// SHOVES or PULLS (never damages). Its force is a vector field (HazardForce) given a shape and, per kind of
// target, a mix (how strongly, and whether its pull / swirl is kept, flipped or doubled).

// Swirl: a vortex. Front: a horizontal band falling down the screen. Column: a vertical stream rising from the bottom.
// Sweep: a VERTICAL band (a flare front) that crosses the screen sideways, with a hot shocked leading edge, shoving
// everything along its direction of travel (Space's solar flares). `bands` of them in a train, `bandGap` apart.
public enum HazardShape { Swirl, Front, Column, Sweep }

// What a force acts on. Bosses, beams, mines on their rails and pickups are not targets at all.
public enum HazardTarget { Player, Body, Elite, Hostile, Mine }

// One target kind's share of a hazard: `gain` scales the field's speed; pullMul / swirlMul scale the Swirl's
// inward and tangential parts for it (a tornado's bullets get a NEGATIVE pull: they are thrown outward).
[System.Serializable]
public struct TargetMix
{
    public float gain, pullMul, swirlMul;
    public TargetMix(float gain, float pullMul = 1f, float swirlMul = 1f) { this.gain = gain; this.pullMul = pullMul; this.swirlMul = swirlMul; }
}

public sealed class HazardDef
{
    public string id;
    public int world;                 // WorldManager.Worlds index
    public HazardShape shape;
    public float strength;            // u/s at the core / peak for a body with mix gain 1
    public float size;                // Swirl radius R, Front thickness T, Column width W (u)
    public float speed;               // Swirl: centre drift (u/s); Front: fall speed down the screen (u/s)
    public float warning = 2f;        // s of telegraph before it acts (>= WorldWeatherTable.MinWarning)
    public float live = 10f;          // s it acts (a Front lasts as long as its sweep takes)
    public float pull = 1f, swirl = 0f;          // Swirl: inward / tangential shares
    public Vector2 push;              // Front: push vector (x is mirrored by the side picked, y is up-screen)
    public float lift, spread;        // Column: up-screen speed share, outward share
    public float pulseHz, pulseDepth; // Column: lift pulses (depth 1 = hard bursts, < 1 = soft)
    public TargetMix player, body, elite, hostile, mine;
    public float spinDegrees;         // Swirl: the painted body also turns continuously at this rate (the tornado); 0: the art's own loop only
    public int artRow = -1, artWarnCol;   // the approved hazard sheet (wx_hazard.png): this hazard's loop row (0 / 1) and its 2 warning cells' first column in row 2; -1: no art yet (staged worlds)
    public float bossShotScale = .6f; // scales a Front / Sweep's rigid sideways push on boss shots (see WeatherWind.BossShotWind)
    public float capMul = 1f;         // multiplies the body / shot / elite speed caps for this hazard (a flare flings harder than a breeze)
    public int bands = 1;             // Sweep: bands in the train
    public float bandGap = 0f;        // Sweep: distance between trailing bands (u)
    public float shock = .35f;        // Sweep: how much stronger the leading half of a band is than the trailing half (0..1)

    public TargetMix MixOf(HazardTarget t)
    {
        switch (t)
        {
            case HazardTarget.Player: return player;
            case HazardTarget.Body: return body;
            case HazardTarget.Elite: return elite;
            case HazardTarget.Hostile: return hostile;
            default: return mine;
        }
    }
}

// A running event. Pooled in WorldHazards (fixed array); nothing allocates after prewarm.
public sealed class HazardField
{
    public enum Phase { Idle, Warning, Live, Fading }
    public HazardDef def;
    public Phase phase;
    public float t;                   // seconds since it started (warning included)
    public float liveFor;             // how long the Live phase lasts
    public float fadeT;               // seconds into the fade-out
    public float fadeLen = WorldWeatherTable.RampOut;
    public Vector2 centre;            // Swirl / Column
    public Vector2 vel;               // Swirl drift
    public float side = 1f;           // +1 / -1: the side a Front pushes toward, a Column spreads
    public int variant;               // which drawn variant of the body (the tornado's four funnels)
    public bool hazArtHeld;           // this event holds the world's hazard sheet loaded (HazardArt.Acquire)
    public float frontY;              // Front: current centre y
    public float sweepX;              // Sweep: current centre x of the LEADING band (it travels in direction `side`)
    public float viewBottom, viewTop; // the view when it began
    public float laneHalf = 3.7f;     // half the width the bounds cover

    public bool Active => phase != Phase.Idle;
    public bool Acting => phase == Phase.Live || phase == Phase.Fading;

    // 0..1: how hard it acts now (0 while only warned).
    public float Envelope
    {
        get
        {
            if (!Acting) return 0f;
            float liveT = t - def.warning;
            float inK = Mathf.Clamp01(liveT / WorldWeatherTable.RampIn);
            inK = inK * inK * (3f - 2f * inK);
            float outK = phase == Phase.Fading ? 1f - Mathf.Clamp01(fadeT / Mathf.Max(.05f, fadeLen)) : Mathf.Clamp01((liveFor - (t - def.warning)) / WorldWeatherTable.RampOut);
            return inK * outK;
        }
    }

    // Where it can act (a cheap cull before any per-target sampling).
    public Rect Bounds
    {
        get
        {
            switch (def.shape)
            {
                case HazardShape.Swirl: return new Rect(centre.x - def.size, centre.y - def.size, def.size * 2f, def.size * 2f);
                case HazardShape.Front: return new Rect(-laneHalf, frontY - def.size * .5f, laneHalf * 2f, def.size);
                case HazardShape.Sweep:
                {
                    float trail = Mathf.Max(0, def.bands - 1) * def.bandGap;
                    float lead = sweepX + side * def.size * .5f, tail = sweepX - side * (def.size * .5f + trail);
                    float x0 = Mathf.Min(lead, tail), x1 = Mathf.Max(lead, tail);
                    return new Rect(x0, viewBottom, x1 - x0, viewTop - viewBottom);
                }
                default: return new Rect(centre.x - def.size * .5f - 1.2f, viewBottom, def.size + 2.4f, viewTop - viewBottom);
            }
        }
    }
}
