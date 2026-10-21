using System;
using UnityEngine;

// THE ELITE SCRIPT: one authored fight timeline per elite, in its def json ("script": { ... }).
//
//   Entrance   a signature 1-2 s arrival (EliteEntrance: arc | overshoot | slam | swirl | dash) on joining the play layer.
//   Phase 1    full hearts: a FIXED loop of steps that repeats.
//   Phase 2    after the first lost hearts (all but the last heart; an armoured 5-heart elite: 3 left): a different loop.
//   Last       the last heart: the desperation loop, and -- when the script says so -- one scripted exit-and-return.
//   Exit       the engagement clock runs out: a clear warning tell (EliteShip.ExitWarning), then the escape boost.
//
// A STEP is one beat of a phase's loop:
//   wait     seconds to breathe / position before the beat (x script tempo x phase gap, / EliteTuning.AttackRate)
//   move     a token handed to the brain when the beat starts (EliteBrain.OnScriptMove): "L" "R" "perch:2" ... -- the brain
//            decides what a token means; unknown tokens are ignored, so a brain that has no tokens just flies as ever
//   attack   "" the plain attack of the def; "-" no attack (a movement beat); otherwise the NAME OF A VARIANT
// A VARIANT is a named set of overrides on the def's own numbers (shotCount, shotSpread, tellSeconds, hazardCount ...: the keys
// in EliteTune). It is applied for that one attack and reverted after it. The same attack class plays differently per beat, so
// scripts reuse the existing attacks and brains instead of writing new ones. (Keys read once at bind time -- an array sized by
// hazardCount, say -- must not be varied: size the def for the biggest variant.)
//
// NO RANDOMNESS: what used to be a coin flip (which edge, lane, perch, side) is EliteScriptRunner.Cycle, a counter that walks
// 0,1,2.. so the sequence is the same every fight. The only randomness left is a small aim offset an attack adds itself.
//
// Everything has a default: an elite with no "script" block flies a one-step loop at its def's attackGap, as before.

[Serializable]
public class EliteSet
{
    public string key;
    public float value;
}

[Serializable]
public class EliteVariant
{
    public string name;
    public EliteSet[] set;
}

[Serializable]
public class EliteStep
{
    public float wait = 2f;     // seconds before the beat (scaled)
    public string move = "";    // brain token
    public string attack = "";  // "" plain | "-" none | a variant name
}

[Serializable]
public class ElitePhaseDef
{
    public float speed = 1f;        // multiplies the brain's speed scale in this phase
    public float gap = 1f;          // multiplies every step wait in this phase
    public EliteSet[] set;          // def overrides for the whole phase
    public EliteStep[] loop;        // the fixed sequence (empty: the default one-step loop)
}

[Serializable]
public class EliteEntranceDef
{
    public string kind = "arc";     // arc | overshoot | slam | swirl | dash
    public float seconds = 1.4f;    // 1 .. 2
    public float amp = 1.6f;        // how far the signature swings (u)
}

[Serializable]
public class EliteScriptDef
{
    public EliteEntranceDef entrance = new EliteEntranceDef();
    public float tempo = 1f;            // this elite's own rhythm: every step wait x tempo (> 1 slower and punchier, < 1 quick jabs)
    public int phase2Hearts;            // hearts at (and below) which phase 2 begins; 0: all but the last (armoured 5: from 3)
    public ElitePhaseDef phase1, phase2, last;
    public EliteVariant[] variants;
    public float exitTell = 1.4f;       // seconds of warning before the escape boost
    public string lastExit = "";        // "" none | left | right | top: leave by that side at the last heart and come back (scripted, once)
    public float lastExitSeconds = 1.6f;
    public string note = "";            // free text for the designer (ignored)
}

// The def numbers a script may vary (a switch, no reflection: IL2CPP-safe), in a fixed order.
public static class EliteTune
{
    public static readonly string[] Keys =
    {
        "speed", "accel", "tellSeconds", "actionSeconds", "dashSpeed", "shotCount", "shotSpeed", "shotSpread", "shotSize", "shotInterval",
        "blinkDistance", "lobSeconds", "lobSpacing", "lobAhead", "poolSeconds", "hazardSize", "hazardSeconds", "hazardCount", "hazardSpeed",
        "hazardArmour", "skidSpeed", "circleRadius", "keepDistance", "laneOffset", "topMargin", "followDistance", "aimCommit", "shotBounces",
    };

    public static int IndexOf(string key)
    {
        for (int i = 0; i < Keys.Length; i++) if (Keys[i] == key) return i;
        return -1;
    }

    public static float Get(EliteDef d, int i)
    {
        switch (i)
        {
            case 0: return d.speed; case 1: return d.accel; case 2: return d.tellSeconds; case 3: return d.actionSeconds;
            case 4: return d.dashSpeed; case 5: return d.shotCount; case 6: return d.shotSpeed; case 7: return d.shotSpread;
            case 8: return d.shotSize; case 9: return d.shotInterval; case 10: return d.blinkDistance; case 11: return d.lobSeconds;
            case 12: return d.lobSpacing; case 13: return d.lobAhead; case 14: return d.poolSeconds; case 15: return d.hazardSize;
            case 16: return d.hazardSeconds; case 17: return d.hazardCount; case 18: return d.hazardSpeed; case 19: return d.hazardArmour;
            case 20: return d.skidSpeed; case 21: return d.circleRadius; case 22: return d.keepDistance; case 23: return d.laneOffset;
            case 24: return d.topMargin; case 25: return d.followDistance; case 26: return d.aimCommit; default: return d.shotBounces;
        }
    }

    public static void Set(EliteDef d, int i, float v)
    {
        switch (i)
        {
            case 0: d.speed = v; break; case 1: d.accel = v; break; case 2: d.tellSeconds = v; break; case 3: d.actionSeconds = v; break;
            case 4: d.dashSpeed = v; break; case 5: d.shotCount = Mathf.RoundToInt(v); break; case 6: d.shotSpeed = v; break;
            case 7: d.shotSpread = v; break; case 8: d.shotSize = v; break; case 9: d.shotInterval = v; break; case 10: d.blinkDistance = v; break;
            case 11: d.lobSeconds = v; break; case 12: d.lobSpacing = v; break; case 13: d.lobAhead = v; break; case 14: d.poolSeconds = v; break;
            case 15: d.hazardSize = v; break; case 16: d.hazardSeconds = v; break; case 17: d.hazardCount = Mathf.RoundToInt(v); break;
            case 18: d.hazardSpeed = v; break; case 19: d.hazardArmour = Mathf.RoundToInt(v); break; case 20: d.skidSpeed = v; break;
            case 21: d.circleRadius = v; break; case 22: d.keepDistance = v; break; case 23: d.laneOffset = v; break;
            case 24: d.topMargin = v; break; case 25: d.followDistance = v; break; case 26: d.aimCommit = v; break; default: d.shotBounces = Mathf.RoundToInt(v); break;
        }
    }

    public static bool Apply(EliteDef d, EliteSet[] sets)
    {
        bool any = false;
        if (sets == null) return false;
        for (int s = 0; s < sets.Length; s++)
        {
            int i = IndexOf(sets[s].key);
            if (i < 0) { Debug.LogWarning("[EliteScript] unknown tuning key '" + sets[s].key + "' on " + d.key); continue; }
            Set(d, i, sets[s].value);
            any = true;
        }
        return any;
    }
}

// The runtime of one ship's script. Allocates at construction only.
public sealed class EliteScriptRunner
{
    public const int Phase1 = 1, Phase2 = 2, PhaseLast = 3;
    public const float PhaseBreath = 1.6f;      // seconds of quiet after a phase change before the new loop's first beat

    readonly EliteShip ship;
    readonly EliteDef def;
    readonly EliteScriptDef sd;
    readonly float[] baseVals = new float[EliteTune.Keys.Length];
    readonly float[] phaseVals = new float[EliteTune.Keys.Length];
    readonly EliteStep fallback;
    readonly int[] counters = new int[16];
    readonly string[] counterKeys = new string[16];
    int phase = Phase1;
    int idx;
    bool varied;
    EliteVariant current;

    public EliteScriptRunner(EliteShip ship, EliteDef def)
    {
        this.ship = ship;
        this.def = def;
        sd = def.script ?? (def.script = new EliteScriptDef());
        for (int i = 0; i < baseVals.Length; i++) baseVals[i] = EliteTune.Get(def, i);
        fallback = new EliteStep { wait = def.attackGap * Mathf.Lerp(1.25f, .6f, Mathf.Clamp01(def.aggression)) };
        EnterPhase(Phase1, false);
    }

    public EliteScriptDef Data => sd;
    public int Phase => phase;
    public int StepIndex => idx;
    public float Tempo => Mathf.Max(.2f, sd.tempo);
    public float EntranceSeconds => Mathf.Clamp(sd.entrance != null ? sd.entrance.seconds : 1.4f, .8f, 2.4f);
    public string EntranceKind => sd.entrance != null && !string.IsNullOrEmpty(sd.entrance.kind) ? sd.entrance.kind : "arc";
    public float EntranceAmp => sd.entrance != null ? sd.entrance.amp : 1.6f;
    public float ExitTell => Mathf.Clamp(sd.exitTell, .6f, 3f);
    public string LastExit => sd.lastExit ?? "";
    public float LastExitSeconds => Mathf.Clamp(sd.lastExitSeconds, .8f, 4f);
    public float SpeedScale => Phase_().speed > 0f ? Phase_().speed : 1f;
    public bool Varied => varied;
    public string VariantName => current != null ? current.name : "";

    // Phase for `hearts` of `max`: 1 full-ish, 2 after the first loss(es), 3 the last heart.
    public static int PhaseFor(EliteScriptDef sd, int hearts, int max)
    {
        if (max > 1 && hearts <= 1) return PhaseLast;
        int p2 = sd != null && sd.phase2Hearts > 0 ? sd.phase2Hearts : (max >= 5 ? max - 2 : max - 1);
        return hearts <= p2 ? Phase2 : Phase1;
    }

    ElitePhaseDef Phase_()
    {
        ElitePhaseDef p = phase == PhaseLast ? (sd.last ?? sd.phase2 ?? sd.phase1) : phase == Phase2 ? (sd.phase2 ?? sd.phase1) : sd.phase1;
        return p ?? Empty;
    }

    static readonly ElitePhaseDef Empty = new ElitePhaseDef();

    EliteStep[] Loop()
    {
        var l = Phase_().loop;
        return l != null && l.Length > 0 ? l : null;
    }

    public EliteStep Step
    {
        get
        {
            var l = Loop();
            return l == null ? fallback : l[idx % l.Length];
        }
    }

    // The upcoming beat is a movement beat (no attack).
    public bool IsBeat => Step.attack == "-";

    // Wait of the upcoming step, in real seconds (tempo, phase gap and AttackRate applied).
    public float WaitSeconds()
    {
        var p = Phase_();
        return EliteTuning.Gap(Mathf.Max(.2f, Step.wait) * Tempo * (p.gap > 0f ? p.gap : 1f));
    }

    // Called when hearts change: returns true when the phase changed (the loop restarts, a breath first).
    public bool OnHearts(int hearts)
    {
        int p = PhaseFor(sd, hearts, def.hearts);
        if (p == phase) return false;
        EnterPhase(p, true);
        return true;
    }

    void EnterPhase(int p, bool revertFirst)
    {
        phase = p;
        idx = 0;
        // def = base + this phase's overrides
        for (int i = 0; i < baseVals.Length; i++) EliteTune.Set(def, i, baseVals[i]);
        EliteTune.Apply(def, Phase_().set);
        for (int i = 0; i < phaseVals.Length; i++) phaseVals[i] = EliteTune.Get(def, i);
        varied = false;
        current = null;
    }

    // The attack of the current step begins: its variant's overrides go on top of the phase's.
    public void BeginAttack()
    {
        Revert();
        string a = Step.attack;
        if (string.IsNullOrEmpty(a) || a == "-" || sd.variants == null) return;
        for (int i = 0; i < sd.variants.Length; i++)
        {
            if (sd.variants[i].name != a) continue;
            current = sd.variants[i];
            varied = EliteTune.Apply(def, current.set);
            return;
        }
        Debug.LogWarning("[EliteScript] " + def.key + ": no variant '" + a + "'");
    }

    // The attack is over (done or broken off): the phase's numbers again.
    public void Revert()
    {
        if (!varied) { current = null; return; }
        for (int i = 0; i < phaseVals.Length; i++) EliteTune.Set(def, i, phaseVals[i]);
        varied = false;
        current = null;
    }

    // The step is done: on to the next of the loop; its move token goes to the brain.
    public void Advance()
    {
        idx++;
        var l = Loop();
        if (l != null) idx %= l.Length; else idx = 0;
        var m = Step.move;
        if (!string.IsNullOrEmpty(m)) ship.Brain.OnScriptMove(m);
    }

    // The step that is up now has its token delivered (the very first one, at the start of the fight).
    public void Begin()
    {
        var m = Step.move;
        if (!string.IsNullOrEmpty(m)) ship.Brain.OnScriptMove(m);
    }

    // A deterministic stand-in for a coin flip: 0, 1, .. n-1, 0, 1 .. per `key` (one counter each, up to 16 keys).
    public int Cycle(string key, int n)
    {
        if (n <= 1) return 0;
        int k = 0;
        for (; k < counterKeys.Length && counterKeys[k] != null; k++)
            if (counterKeys[k] == key) return counters[k]++ % n;
        if (k >= counterKeys.Length) return 0;
        counterKeys[k] = key;
        return counters[k]++ % n;
    }

    // Where the counter for `key` would go next, without taking it.
    public int Peek(string key, int n)
    {
        if (n <= 1) return 0;
        for (int k = 0; k < counterKeys.Length && counterKeys[k] != null; k++)
            if (counterKeys[k] == key) return counters[k] % n;
        return 0;
    }

    // -1 / +1, alternating from `first`.
    public float Alternate(string key, float first = 1f) => Cycle(key, 2) == 0 ? first : -first;

    // A small aim wobble is the one random thing left: +-`amount`, from a counter-driven table (still the same every fight).
    static readonly float[] Wobble = { 0f, .6f, -.4f, .9f, -.8f, .3f, -.6f, .5f };
    public float Offset(string key, float amount) => Wobble[Cycle(key, Wobble.Length)] * amount;
}

// The signature arrival: a path from where the lift-off ended to the brain's first goal, `seconds` long.
public static class EliteEntrance
{
    // Position at k in [0, 1] of the entrance. `side` is -1 or +1 (the side the path bulges to), `amp` how far it swings.
    public static Vector2 Path(string kind, Vector2 from, Vector2 to, float k, float side, float amp)
    {
        k = Mathf.Clamp01(k);
        float e = k * k * (3f - 2f * k);
        Vector2 d = to - from;
        float len = d.magnitude;
        Vector2 dir = len > 1e-3f ? d / len : Vector2.up;
        Vector2 perp = new Vector2(-dir.y, dir.x) * side;
        switch (kind)
        {
            case "overshoot":
            {
                // rushes past the goal and settles back onto it
                float o = Mathf.Sin(k * Mathf.PI * 1.15f) * (1f - k) * amp * .9f;
                return Vector2.Lerp(from, to, 1f - (1f - k) * (1f - k)) + dir * o;
            }
            case "slam":
            {
                // up and back for the first third, then straight down onto the spot
                Vector2 top = from + Vector2.up * amp * .55f;
                if (k < .38f) return Vector2.Lerp(from, top, Mathf.Sin(k / .38f * Mathf.PI * .5f));
                float f = (k - .38f) / .62f;
                return Vector2.Lerp(top, to, f * f);
            }
            case "swirl":
            {
                // a decaying spiral round the goal
                float a = (1f - e) * Mathf.PI * 2.2f * side;
                float r = (1f - e) * amp;
                Vector2 c = Vector2.Lerp(from, to, e);
                return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * .8f;
            }
            case "dash":
            {
                // straight and fast, hard on the brakes
                float f = 1f - Mathf.Pow(1f - k, 3f);
                return Vector2.Lerp(from, to, f);
            }
            default:   // arc
                return Vector2.Lerp(from, to, e) + perp * Mathf.Sin(k * Mathf.PI) * amp;
        }
    }

    // The spark burst that signs the landing, by kind.
    public static int Sparks(string kind)
    {
        switch (kind)
        {
            case "slam": return 14;
            case "overshoot": return 9;
            case "swirl": return 7;
            case "dash": return 11;
            default: return 6;
        }
    }
}
