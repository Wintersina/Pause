using UnityEngine;

// The v3 main attacks of the cheaper hulls (ShipAttackRunner, partial): each one a verb of its own.
//
//   ArcaneEye       Paranoid   an eye opens ahead of the nose, gazes up its cone and runes MARK targets
//                              (they ride them); 0.8 s later every mark detonates and kills its target, and the
//                              blast (EyeBlast) also hits whatever stands near it at half weight
//   HeatWave        Solar Fang a thin wall of heat the width of the screen rolls up from the nose, burning
//                              everything it passes once
//   BoomerangStar   Ninja      a shuriken thrown on an ellipse that comes back to the ship, hitting on the
//                              way out and again on the way back
//   ProximityMines  Saboteur   mines launched from the nose land ahead, hold their screen position (hazards
//                              fall into them) and blow when something comes close; the rest fizzle out
//   AbductHurl      UFO        a beam lifts the nearest enemy to a hold point, then hurls it at the densest
//                              group; it kills what it hits and dies itself (bosses and elites are hit by
//                              the beam instead)
//   GaleWings       Dove       a feathered gust sweeps up and shoves everything up and outward (EnemyShove,
//                              displacement only); shoves into each other or the screen top hurt. Hostile
//                              shots (boss, elite) are not shoved but turned: each becomes the player's own
//                              fast return shot that crashes into the first enemy or boss it meets
//
// Rules of the rest of the runner hold: gameplay time (dt), pooled sprites, every hit through
// ShipAttackHits with the run's budget, a whole firing worth about one boss hit.
public class AttackV3
{
    public const int Max = 12;

    public AttackRun owner;
    public readonly AttackSprite[] fx = new AttackSprite[Max];
    public readonly ClearTarget[] tgt = new ClearTarget[Max];
    public readonly Collider2D[] col = new Collider2D[Max];
    public readonly Vector3[] pos = new Vector3[Max], from = new Vector3[Max], to = new Vector3[Max];
    public readonly float[] at = new float[Max];
    public readonly int[] phase = new int[Max];
    public readonly int[] n = new int[Max];
    public readonly System.Collections.Generic.List<GameObject> hit2 = new System.Collections.Generic.List<GameObject>(16);
    // Gale Wings' returned shots (a hostile shot the gust turned): position, velocity, drawing.
    public const int ReturnMax = 16;
    public readonly AttackSprite[] rFx = new AttackSprite[ReturnMax];
    public readonly Vector2[] rPos = new Vector2[ReturnMax], rVel = new Vector2[ReturnMax];
    public readonly float[] rAge = new float[ReturnMax];
    public int rCount;
    public AttackSprite eye;
    public int count, resolved;
    public float y0;

    // A fresh firing.
    public void Reset()
    {
        for (int i = 0; i < Max; i++)
        {
            fx[i] = null; tgt[i] = null; col[i] = null; at[i] = 0f; phase[i] = 0; n[i] = 0;
        }
        hit2.Clear();
        for (int i = 0; i < ReturnMax; i++) { rFx[i] = null; rAge[i] = 0f; }
        rCount = 0;
        eye = null;
        count = resolved = 0;
        y0 = 0f;
    }

    // The run is cut off (or ends): its drawings go, anything it was holding is let go.
    public void Release()
    {
        for (int i = 0; i < Max; i++)
        {
            if (fx[i] != null && fx[i].Active && fx[i].Run == owner) fx[i].Stop();
            if (col[i] != null) col[i].enabled = true;
            fx[i] = null; col[i] = null;
        }
        for (int i = 0; i < ReturnMax; i++)
        {
            if (rFx[i] != null && rFx[i].Active && rFx[i].Run == owner) rFx[i].Stop();
            rFx[i] = null;
        }
        rCount = 0;
        if (eye != null && eye.Active && eye.Run == owner) eye.Stop();
        eye = null;
    }

    public int LiveMines
    {
        get { int c = 0; for (int i = 0; i < Max; i++) if (phase[i] == 1 || phase[i] == 2) c++; return c; }
    }
}

public partial class ShipAttackRunner
{
    // ARCANE EYE (Paranoid)
    public const float EyeAhead = .7f, MarkDelay = .8f;
    // A detonation also hits everything within EyeBlast (+ half the neighbour's radius, like MineBlast) of the
    // marked target at EyeSplash of the weapon hit. Still-waiting marks are skipped: they take their own full hit.
    public const float EyeBlast = 1.2f, EyeSplash = .5f;
    // HEAT WAVE (Solar Fang)
    public const int WaveSegments = 5;
    // BOOMERANG STAR (Ninja)
    public const float StarHalfWidth = 1.9f, StarRadius = .35f;
    // PROXIMITY MINES (Saboteur)
    public const int MineCap = 12;
    public const float MineTrigger = .9f, MineBlast = 1.1f, MineLife = 6f, MineFlight = .4f;
    // ABDUCT & HURL (UFO)
    public const float HoldAhead = 1.5f, PullSeconds = .5f, HurlSpeed = 15f, HurlRadius = .3f, GroupRadius = 1.8f;
    // GALE WINGS (Dove)
    public const float GustSeconds = .8f, KnockDistance = 3f, KnockSeconds = .4f;
    // a hostile shot the gust turns flies back at ReturnSpeed (a body is shoved KnockDistance over KnockSeconds, ~7.5/s),
    // for at most ReturnSeconds, and crashes into the first thing it meets: ReturnWeight of a hit (the boss:
    // ReturnBossWeight, a whole one; its own hit window still limits it to one hurt per shot)
    public const float ReturnSpeed = 18f, ReturnSeconds = 2.5f, ReturnRadius = .3f, ReturnWeight = .5f, ReturnBossWeight = 1f;

    static readonly int[] OneShot = ShipFxArt.BurstTicks;

    public static bool IsV3(ShipAttack a)
    {
        return a == ShipAttack.ArcaneEye || a == ShipAttack.HeatWave || a == ShipAttack.BoomerangStar ||
               a == ShipAttack.ProximityMines || a == ShipAttack.AbductHurl || a == ShipAttack.GaleWings;
    }

    // Mines still armed or flying across every slot (the cap).
    public int LiveMines
    {
        get
        {
            int c = 0;
            for (int i = 0; i < runs.Length; i++) if (runs[i] != null && runs[i].v3 != null) c += runs[i].v3.LiveMines;
            return c;
        }
    }

    // ------------------------------------------------------------ start

    void StartV3(AttackRun run, Vector3 nose)
    {
        var l = run.loadout;
        if (run.v3 == null) run.v3 = new AttackV3();
        var v = run.v3;
        v.Reset();
        v.owner = run;
        Rect view = ShipTargets.View();
        switch (l.attack)
        {
            case ShipAttack.ArcaneEye:
                v.eye = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose + Vector3.up * EyeAhead,
                    Vector3.one * .9f, 0f, ShipFxArt.LoopTicks, true, 0f, transform,
                    Vector3.up * (noseOffset + EyeAhead), 69).Owned(run);
                break;

            case ShipAttack.HeatWave:
                v.y0 = nose.y;
                break;

            case ShipAttack.BoomerangStar:
                {
                    int stars = Mathf.Clamp(l.shots, 1, 2);
                    for (int j = 0; j < stars; j++)
                        v.fx[j] = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose, Vector3.one * 1.1f, 0f,
                            ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 71).Owned(run);
                    v.count = stars;
                    for (int j = 0; j < stars; j++) v.pos[j] = nose;
                }
                break;

            case ShipAttack.ProximityMines:
                {
                    int n = Mathf.Min(Mathf.Clamp(l.shots, 1, AttackV3.Max), MineCap - LiveMines);
                    for (int i = 0; i < n; i++)
                    {
                        float a = n <= 1 ? 0f : (i - (n - 1) * .5f) * (70f / (n - 1));
                        float dist = 2.2f + 2.8f * ((i * .618f) % 1f);
                        Vector3 land = nose + Quaternion.Euler(0f, 0f, -a) * Vector3.up * dist;
                        if (view.width < 100f)
                        {
                            land.x = Mathf.Clamp(land.x, view.xMin + .4f, view.xMax - .4f);
                            land.y = Mathf.Min(land.y, view.yMax - .5f);
                        }
                        v.phase[i] = 1;
                        v.from[i] = v.pos[i] = nose;
                        v.to[i] = land;
                        v.at[i] = 0f;
                        v.fx[i] = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose, Vector3.one * .6f, 0f,
                            ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 70).Owned(run);
                    }
                    v.count = n;
                    if (n <= 0) run.active = false;
                }
                break;

            case ShipAttack.AbductHurl:
                StartAbduct(run, nose);
                break;

            case ShipAttack.GaleWings:
                if (Application.isPlaying) ShieldShockwave.Prewarm();   // the clock that drives EnemyShove
                v.fx[0] = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose, GustScale(l, 0f, view), 0f,
                    ShipFxArt.LoopTicks, true, GustSeconds, null, Vector3.zero, 69).Owned(run);
                break;
        }
    }

    // ------------------------------------------------------------- step

    void StepV3(AttackRun run, float dt)
    {
        var v = run.v3;
        if (v == null) { run.active = false; return; }
        switch (run.loadout.attack)
        {
            case ShipAttack.ArcaneEye:       StepEye(run, v); break;
            case ShipAttack.HeatWave:        StepWave(run, v); break;
            case ShipAttack.BoomerangStar:   StepStars(run, v); break;
            case ShipAttack.ProximityMines:  StepMines(run, v, dt); break;
            case ShipAttack.AbductHurl:      StepAbduct(run, v, dt); break;
            case ShipAttack.GaleWings:       StepGale(run, v, dt); break;
        }
    }

    void Burst(AttackRun run, Vector3 at, float size, string strip = null)
    {
        var s = AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, at, Vector3.one * size, 0f,
            OneShot, false, 0f, null, Vector3.zero, 72);
        if (strip != null) s.Strip(strip);
    }

    static bool Special(GameObject go) { return go.GetComponent<IShipAttackTarget>() != null; }

    // ------------------------------------------------------- arcane eye

    void StepEye(AttackRun run, AttackV3 v)
    {
        var l = run.loadout;
        Vector3 eyePos = Nose + Vector3.up * EyeAhead;
        float gaze = Mathf.Max(.1f, l.duration);
        float w = 1f / Mathf.Max(1, l.shots);
        if (v.count < l.shots && v.count < AttackV3.Max && run.t <= gaze + .05f)
        {
            // the gaze sweeps its cone left to right; whatever it passes over is marked
            float scan = Mathf.Lerp(-l.width, l.width, Mathf.Clamp01(run.t / gaze));
            ShipTargets.Collect(targets);
            for (int i = 0; i < targets.Count && v.count < l.shots; i++)
            {
                var t = targets[i];
                if (t == null || !t.isActiveAndEnabled) continue;
                Vector2 d = t.transform.position - eyePos;
                if (d.y <= 0f || d.magnitude > l.range + t.Radius) continue;
                float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                if (Mathf.Abs(bearing) > l.width || bearing > scan) continue;
                bool marked = false;
                for (int k = 0; k < v.count; k++) marked |= v.tgt[k] == t;
                if (marked) continue;
                int s = v.count++;
                v.tgt[s] = t;
                v.at[s] = run.t;
                v.fx[s] = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, t.transform.position,
                    Vector3.one * .75f, 0f, ShipFxArt.LoopTicks, true, 0f, t.transform, Vector3.zero, 71).Owned(run).Strip("mark");
            }
        }
        int kills = 0;
        for (int i = 0; i < v.count; i++)
        {
            if (v.phase[i] != 0) continue;
            var t = v.tgt[i];
            if (t == null || !t.isActiveAndEnabled) { Resolve(v, i); continue; }   // gone already
            if (run.t < v.at[i] + MarkDelay) continue;
            Vector3 at = t.transform.position;
            Burst(run, at, EyeBlast * 2f, "mark");   // the mark burst, sized to the blast so its reach reads
            ShipTargets.Collect(targets);
            for (int k = 0; k < targets.Count; k++)
            {
                var n = targets[k];
                if (n == null || n == t || !n.isActiveAndEnabled || ShipAttackHits.AlreadyHit(n.gameObject)) continue;
                bool waiting = false;   // a mark still counting down takes its own full hit instead of a splash
                for (int m = 0; m < v.count && !waiting; m++) waiting = v.phase[m] == 0 && v.tgt[m] == n;
                if (waiting) continue;
                Vector2 d = n.transform.position - at;
                float r = EyeBlast + n.Radius * .5f;
                if (d.sqrMagnitude > r * r) continue;
                if (ShipAttackHits.Hit(n.gameObject, ship, w * EyeSplash, ref run.budget)) kills++;
            }
            if (ShipAttackHits.Hit(t.gameObject, ship, w, ref run.budget)) kills++;
            Resolve(v, i);
        }
        if (run.t >= gaze + .05f && v.resolved >= v.count)
        {
            Burst(run, eyePos, 1f);
            v.Release();
            run.active = false;
        }
    }

    static void Resolve(AttackV3 v, int i)
    {
        v.phase[i] = 3;
        v.resolved++;
        if (v.fx[i] != null && v.fx[i].Active && v.fx[i].Run == v.owner) v.fx[i].Stop();
        v.fx[i] = null;
        v.tgt[i] = null;
    }

    // -------------------------------------------------------- heat wave

    void StepWave(AttackRun run, AttackV3 v)
    {
        var l = run.loadout;
        int waves = Mathf.Clamp(l.shots, 1, 2);
        Rect view = ShipTargets.View();
        bool bounded = view.width < 100f;
        float left = bounded ? view.xMin : transform.position.x - 3f;
        float width = bounded ? view.width : 6f;
        float top = bounded ? view.yMax : v.y0 + 12f;
        float band = Mathf.Max(.2f, l.width);
        float segW = width / WaveSegments;
        float w = 1f / waves;
        int kills = 0;
        ShipTargets.Collect(targets);
        int done = 0;
        for (int k = 0; k < waves; k++)
        {
            float start = k * l.duration;
            if (run.t < start) continue;
            if (v.phase[k] == 2) { done++; continue; }
            float y = v.y0 + (run.t - start) * l.speed;
            if (v.phase[k] == 0)
            {
                v.phase[k] = 1;
                for (int j = 0; j < WaveSegments; j++)
                    v.fx[k * WaveSegments + j] = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship,
                        new Vector3(left + (j + .5f) * segW, y, 0f), new Vector3(segW * 1.3f, band * 2.4f, 1f), 0f,
                        ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 69).Owned(run);
            }
            for (int j = 0; j < WaveSegments; j++)
            {
                var s = v.fx[k * WaveSegments + j];
                if (s != null && s.Active && s.Run == run) s.transform.position = new Vector3(left + (j + .5f) * segW, y, 0f);
            }
            var list = k == 0 ? run.hit : v.hit2;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.isActiveAndEnabled || list.Contains(t.gameObject)) continue;
                Vector3 p = t.transform.position;
                if (p.y - t.Radius > y + band * .5f || p.y + t.Radius < v.y0) continue;   // not reached yet / behind the ship
                list.Add(t.gameObject);
                Burst(run, new Vector3(p.x, Mathf.Min(p.y, y), 0f), .7f);
                if (ShipAttackHits.Hit(t.gameObject, ship, w, ref run.budget)) kills++;
            }
            if (y - band > top + .3f)
            {
                v.phase[k] = 2;
                done++;
                for (int j = 0; j < WaveSegments; j++)
                {
                    var s = v.fx[k * WaveSegments + j];
                    if (s != null && s.Active && s.Run == run) s.Stop();
                    v.fx[k * WaveSegments + j] = null;
                }
            }
        }
        if (done >= waves) run.active = false;
    }

    // ---------------------------------------------------- boomerang star

    // Seconds one throw takes: the ellipse's length over the star's speed.
    public static float StarSeconds(ShipLoadout l)
    {
        float b = l.range * .5f;
        float perimeter = 2f * Mathf.PI * Mathf.Sqrt((StarHalfWidth * StarHalfWidth + b * b) * .5f);
        return perimeter / Mathf.Max(1f, l.speed);
    }

    // Where star `side` (+1 / -1) is at fraction u of its throw, relative to the nose: out along one
    // side of the ellipse to the range, back along the other to the ship.
    public static Vector3 StarOffset(ShipLoadout l, float side, float u)
    {
        float th = u * 2f * Mathf.PI, b = l.range * .5f;
        return new Vector3(side * StarHalfWidth * Mathf.Sin(th), b - b * Mathf.Cos(th), 0f);
    }

    void StepStars(AttackRun run, AttackV3 v)
    {
        var l = run.loadout;
        float u = Mathf.Clamp01(run.t / StarSeconds(l));
        var list = u < .5f ? run.hit : v.hit2;   // each target once per leg
        Vector3 nose = Nose;
        ShipTargets.Collect(targets);
        int kills = 0;
        for (int j = 0; j < v.count; j++)
        {
            Vector3 prev = v.pos[j];
            Vector3 now = nose + StarOffset(l, j == 0 ? 1f : -1f, u);
            v.pos[j] = now;
            if (v.fx[j] != null && v.fx[j].Active && v.fx[j].Run == run) v.fx[j].transform.position = now;
            HostileShots.ShootDownAlong(prev, now, StarRadius);
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.isActiveAndEnabled || list.Contains(t.gameObject)) continue;
                float along;
                if (!SegmentHits(prev, now, t.transform.position, StarRadius + t.Radius * .7f, out along)) continue;
                list.Add(t.gameObject);
                Burst(run, t.transform.position, .6f);
                if (ShipAttackHits.Hit(t.gameObject, ship, .5f, ref run.budget)) kills++;
            }
        }
        if (u >= 1f)
        {
            v.Release();
            run.active = false;
        }
    }

    // ---------------------------------------------------- proximity mines

    void StepMines(AttackRun run, AttackV3 v, float dt)
    {
        var l = run.loadout;
        Rect view = ShipTargets.View();
        float w = 1f / Mathf.Max(1, l.shots);
        ShipTargets.Collect(targets);
        int live = 0, kills = 0;
        for (int i = 0; i < v.count; i++)
        {
            if (v.phase[i] != 1 && v.phase[i] != 2) continue;
            v.at[i] += dt;
            var s = v.fx[i];
            bool drawn = s != null && s.Active && s.Run == run;
            if (v.phase[i] == 1)
            {
                // the launch: nose to its spot, eased
                float k = Mathf.Clamp01(v.at[i] / MineFlight);
                float e = 1f - (1f - k) * (1f - k);
                v.pos[i] = Vector3.Lerp(v.from[i], v.to[i], e);
                if (k >= 1f) v.phase[i] = 2;
            }
            if (drawn) s.transform.position = v.pos[i];

            bool offscreen = view.width < 100f && v.pos[i].y < view.yMin - .3f;
            if (v.at[i] >= MineLife || offscreen) { Fizzle(v, i); continue; }
            live++;
            if (v.phase[i] != 2) continue;
            bool trip = false;
            for (int k = 0; k < targets.Count && !trip; k++)
            {
                var t = targets[k];
                if (t == null || !t.isActiveAndEnabled || !ShipTargets.IsThreat(t)) continue;
                Vector2 d = t.transform.position - v.pos[i];
                float r = MineTrigger + t.Radius * .5f;
                trip = d.sqrMagnitude <= r * r;
            }
            if (!trip) continue;
            Vector3 at = v.pos[i];
            Fizzle(v, i);
            Burst(run, at, 1.8f);
            for (int k = 0; k < targets.Count; k++)
            {
                var t = targets[k];
                if (t == null || !t.isActiveAndEnabled) continue;
                Vector2 d = t.transform.position - at;
                float r = MineBlast + t.Radius * .5f;
                if (d.sqrMagnitude > r * r) continue;
                if (ShipAttackHits.Hit(t.gameObject, ship, w, ref run.budget)) kills++;
            }
            CameraKick.Kick(.02f);
        }
        if (live == 0) run.active = false;
    }

    static void Fizzle(AttackV3 v, int i)
    {
        v.phase[i] = 3;
        if (v.fx[i] != null && v.fx[i].Active && v.fx[i].Run == v.owner) v.fx[i].Stop();
        v.fx[i] = null;
    }

    // ------------------------------------------------------ abduct & hurl

    Vector3 HoldPoint(AttackV3 v, int slot, int slots)
    {
        float off = slots <= 1 ? 0f : (slot == 0 ? -.7f : .7f);
        return Nose + new Vector3(off, HoldAhead, 0f);
    }

    void StartAbduct(AttackRun run, Vector3 nose)
    {
        var l = run.loadout;
        var v = run.v3;
        int grabs = Mathf.Clamp(l.shots, 1, 2);
        ShipTargets.Collect(targets);
        for (int g = 0; g < grabs; g++)
        {
            ClearTarget best = null;
            float bestSq = l.range * l.range;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.isActiveAndEnabled || !ShipTargets.IsThreat(t) || t.IsShotHitbox) continue;
                if (RunScore.IsHostileShot(t.gameObject) || Special(t.gameObject)) continue;
                Vector3 p = t.transform.position;
                if (p.y < nose.y - .5f) continue;
                bool taken = false;
                for (int k = 0; k < v.count; k++) taken |= v.tgt[k] == t;
                float sq = ((Vector2)(p - nose)).sqrMagnitude;
                if (!taken && sq < bestSq) { bestSq = sq; best = t; }
            }
            if (best == null) break;
            int s = v.count++;
            v.tgt[s] = best;
            float radius = best.Radius;   // measured before the collider goes quiet
            if (best.TryGetComponent(out Collider2D c)) { c.enabled = false; v.col[s] = c; }
            v.phase[s] = 1;
            v.from[s] = v.pos[s] = best.transform.position;
            v.at[s] = 0f;
            v.n[s] = Mathf.Max(1, l.maxHits);
            v.fx[s] = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose, new Vector3(.4f, 1f, 1f), 0f,
                ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 69).Owned(run);
        }
        if (v.count > 0) return;
        // Nothing it can lift: a boss or an elite takes the beam as one hit instead.
        ClearTarget special = null;
        float bestD = l.range * l.range;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled || !Special(t.gameObject)) continue;
            float sq = ((Vector2)(t.transform.position - nose)).sqrMagnitude;
            if (sq < bestD) { bestD = sq; special = t; }
        }
        if (special != null)
        {
            Vector3 p = special.transform.position;
            Vector3 d = p - nose;
            AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, (nose + p) * .5f,
                new Vector3(.4f, Mathf.Max(.5f, d.magnitude), 1f), Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f,
                ShipFxArt.LoopTicks, true, .3f, null, Vector3.zero, 69);
            Burst(run, p, .9f);
            ShipAttackHits.Hit(special.gameObject, ship, 1f, ref run.budget);
        }
        run.active = false;
    }

    void StepAbduct(AttackRun run, AttackV3 v, float dt)
    {
        var l = run.loadout;
        Vector3 nose = Nose;
        Rect view = ShipTargets.View(1f);
        bool any = false;
        ShipTargets.Collect(targets);
        for (int g = 0; g < v.count; g++)
        {
            if (v.phase[g] != 1 && v.phase[g] != 2) continue;
            var body = v.tgt[g];
            if (body == null || !body.isActiveAndEnabled) { EndGrab(v, g, false); continue; }
            any = true;
            v.at[g] += dt;
            if (v.phase[g] == 1)
            {
                float k = Mathf.Clamp01(v.at[g] / PullSeconds);
                float e = k * k * (3f - 2f * k);
                Vector3 hold = HoldPoint(v, g, v.count);
                v.pos[g] = Vector3.Lerp(v.from[g], hold, e) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * .3f);
                body.transform.position = v.pos[g];
                BeamTo(run, v.fx[g], nose, v.pos[g]);
                if (k >= 1f) Hurl(run, v, g, hold);
            }
            else
            {
                Vector3 prev = v.pos[g];
                Vector3 now = prev + v.to[g] * dt;   // to[] holds the velocity while hurling
                v.pos[g] = now;
                body.transform.position = now;
                bool done = !view.Contains(now) || v.at[g] > 2.5f;
                for (int i = 0; i < targets.Count && !done; i++)
                {
                    var t = targets[i];
                    if (t == null || t == body || !t.isActiveAndEnabled || run.hit.Contains(t.gameObject)) continue;
                    if (IsHeld(v, t)) continue;
                    float along;
                    if (!SegmentHits(prev, now, t.transform.position, HurlRadius + t.Radius * .7f, out along)) continue;
                    run.hit.Add(t.gameObject);
                    bool boss = Special(t.gameObject);
                    Burst(run, t.transform.position, .9f);
                    ShipAttackHits.Hit(t.gameObject, ship, 1f, ref run.budget);
                    if (boss || --v.n[g] <= 0) done = true;
                }
                if (done) EndGrab(v, g, true);
            }
        }
        if (!any) { v.Release(); run.active = false; }
    }

    static bool IsHeld(AttackV3 v, ClearTarget t)
    {
        for (int i = 0; i < v.count; i++) if (v.tgt[i] == t && (v.phase[i] == 1 || v.phase[i] == 2)) return true;
        return false;
    }

    void BeamTo(AttackRun run, AttackSprite beam, Vector3 from, Vector3 to)
    {
        if (beam == null || !beam.Active || beam.Run != run) return;
        Vector3 d = to - from;
        beam.transform.position = (from + to) * .5f;
        beam.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
        beam.transform.localScale = new Vector3(.4f, Mathf.Max(.3f, d.magnitude), 1f);
    }

    // The grabbed body is thrown at the densest knot of the other targets (straight up if there are none).
    void Hurl(AttackRun run, AttackV3 v, int g, Vector3 from)
    {
        var l = run.loadout;
        var body = v.tgt[g];
        ClearTarget best = null;
        int bestN = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || t == body || !t.isActiveAndEnabled || IsHeld(v, t)) continue;
            Vector3 p = t.transform.position;
            if (p.y < from.y - .5f) continue;
            int near = 0;
            for (int k = 0; k < targets.Count; k++)
            {
                var o = targets[k];
                if (o == null || o == body || !o.isActiveAndEnabled) continue;
                Vector2 d = o.transform.position - p;
                if (d.sqrMagnitude <= GroupRadius * GroupRadius) near++;
            }
            float dist = ((Vector2)(p - from)).sqrMagnitude;
            if (near > bestN || (near == bestN && dist < bestD)) { bestN = near; best = t; bestD = dist; }
        }
        Vector3 dir = best != null ? (best.transform.position - from).normalized : Vector3.up;
        v.phase[g] = 2;
        v.at[g] = 0f;
        v.to[g] = dir * Mathf.Max(5f, l.speed);
        if (v.fx[g] != null && v.fx[g].Active && v.fx[g].Run == run) v.fx[g].Stop();
        v.fx[g] = null;
    }

    // A thrown body is done: it dies (paid like any kill). A body whose run was cut short is just let go.
    void EndGrab(AttackV3 v, int g, bool kill)
    {
        var body = v.tgt[g];
        if (v.fx[g] != null && v.fx[g].Active && v.fx[g].Run == v.owner) v.fx[g].Stop();
        v.fx[g] = null;
        if (v.col[g] != null) v.col[g].enabled = true;
        v.col[g] = null;
        v.phase[g] = 3;
        v.tgt[g] = null;
        if (kill && body != null)
        {
            Burst(v.owner, body.transform.position, .9f);
            float spare = 1f;
            ShipAttackHits.Hit(body.gameObject, ship, 1f, ref spare);
        }
    }

    // Held bodies are placed after every other mover has had its turn this frame.
    void LateUpdate()
    {
        for (int i = 0; i < runs.Length; i++)
        {
            var r = runs[i];
            if (r == null || !r.active || r.v3 == null || r.loadout.attack != ShipAttack.AbductHurl) continue;
            var v = r.v3;
            for (int g = 0; g < v.count; g++)
                if ((v.phase[g] == 1 || v.phase[g] == 2) && v.tgt[g] != null) v.tgt[g].transform.position = v.pos[g];
        }
    }

    // ------------------------------------------------------- gale wings

    static Vector3 GustScale(ShipLoadout l, float k, Rect view)
    {
        float w = view.width < 100f ? view.width : 6f;
        return new Vector3(w * Mathf.Lerp(.5f, 1.1f, k), 1.1f, 1f);
    }

    // The displacement a body at `p` gets from a gust blown from `origin` (before any lane / screen limit).
    public static Vector2 GaleKnock(Vector2 origin, Vector2 p, float mass)
    {
        Vector2 away = p - origin;
        away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector2.up;
        Vector2 dir = new Vector2(away.x * .9f, Mathf.Max(away.y, 0f) + .7f).normalized;
        return dir * (KnockDistance * mass);
    }

    // The velocity of a hostile shot at `p` that a gust blown from `origin` turns: straight at the boss when
    // there is one (`boss`), otherwise away from the ship, up and outward like a body's knock.
    public static Vector2 GaleReturn(Vector2 origin, Vector2 p, bool hasBoss, Vector2 boss)
    {
        Vector2 dir;
        if (hasBoss && (boss - p).sqrMagnitude > 1e-4f) dir = (boss - p).normalized;
        else dir = GaleKnock(origin, p, 1f).normalized;
        return dir * ReturnSpeed;
    }

    // Is a shot of radius `r` at `p` inside a gust whose front is at distance `front` (range `range`, half angle `width`)?
    // The same cone the bodies are tested with.
    public static bool InGust(Vector2 origin, Vector2 p, float r, float range, float front, float width)
    {
        Vector2 d = p - origin;
        float dist = d.magnitude;
        if (dist > range + r || dist - r * .5f > front) return false;
        if (dist > r && Vector2.Angle(Vector2.up, d) > width + Mathf.Asin(Mathf.Clamp01(r / dist)) * Mathf.Rad2Deg) return false;
        return true;
    }

    // The gust turns every hostile shot inside it (once each): the shot is taken out of play without a hit on
    // the ship and becomes the player's own return shot, drawn and flown by this run. Because the original is gone,
    // it can never hurt the ship afterwards.
    void TurnShots(AttackRun run, AttackV3 v, Vector2 origin, float front, bool hasBoss, Vector2 boss)
    {
        var l = run.loadout;
        var shots = HostileShots.All;
        for (int i = 0; i < shots.Count && v.rCount < AttackV3.ReturnMax; i++)
        {
            var s = shots[i];
            if (s == null || (Object)s == null || !s.ShotShovable || s.ShotMass == HostileShots.Fixed) continue;
            Vector2 p = s.ShotPosition;
            if (!InGust(origin, p, s.ShotRadius, l.range, front, l.width)) continue;
            int k = v.rCount++;
            v.rPos[k] = p;
            v.rVel[k] = GaleReturn(origin, p, hasBoss, boss);
            v.rAge[k] = 0f;
            v.rFx[k] = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, p, Vector3.one * .6f, 0f,
                ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 71).Owned(run);
            s.ShotPop(p);
        }
    }

    // The returned shots fly; the first target one meets takes the crash and the shot pops. Anything hostile in
    // the way is broken (the boss's and enemies' shots crash into each other). The boss goes through
    // ShipAttackHits like every player attack, so its normal damage rules apply.
    void StepReturns(AttackRun run, AttackV3 v, float dt, ref int kills)
    {
        Rect view = ShipTargets.View(.5f);
        for (int j = v.rCount - 1; j >= 0; j--)
        {
            Vector2 prev = v.rPos[j], now = prev + v.rVel[j] * dt;
            v.rPos[j] = now;
            v.rAge[j] += dt;
            var fx = v.rFx[j];
            if (fx != null && fx.Active && fx.Run == run) fx.transform.position = now;
            HostileShots.ShootDownAlong(prev, now, ReturnRadius);
            bool crashed = false;
            for (int i = 0; i < targets.Count && !crashed; i++)
            {
                var t = targets[i];
                if (t == null || !t.isActiveAndEnabled || t.Scenery || RunScore.IsHostileShot(t.gameObject)) continue;
                float along;
                if (!SegmentHits(prev, now, t.transform.position, ReturnRadius + t.Radius * .7f, out along)) continue;
                crashed = true;
                Burst(run, t.transform.position, .8f);
                bool boss = t.TryGetComponent(out BossTarget _) || t.TryGetComponent(out BossActor _);
                if (ShipAttackHits.Hit(t.gameObject, ship, boss ? ReturnBossWeight : ReturnWeight, ref run.budget)) kills++;
            }
            bool gone = crashed || v.rAge[j] >= ReturnSeconds || (view.width < 100f && !view.Contains(now));
            if (!gone) continue;
            if (fx != null && fx.Active && fx.Run == run) fx.Stop();
            int last = --v.rCount;
            v.rPos[j] = v.rPos[last]; v.rVel[j] = v.rVel[last]; v.rAge[j] = v.rAge[last]; v.rFx[j] = v.rFx[last];
            v.rFx[last] = null;
        }
    }

    void StepGale(AttackRun run, AttackV3 v, float dt)
    {
        var l = run.loadout;
        Rect view = ShipTargets.View();
        Vector3 origin = Nose;
        float k = Mathf.Clamp01(run.t / GustSeconds);
        float front = l.range * k;
        var gust = v.fx[0];
        if (gust != null && gust.Active && gust.Run == run)
        {
            gust.transform.position = origin + Vector3.up * (front * .85f);
            gust.transform.localScale = GustScale(l, k, view);
        }
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        int kills = 0;
        ShipTargets.Collect(targets);
        bool gusting = k < 1f || run.t < GustSeconds + .05f;
        if (gusting)
        {
            // hostile shots are turned, not shoved: they head for the boss (if any) and crash into what they meet
            bool hasBoss = false;
            Vector2 bossAt = Vector2.zero;
            for (int i = 0; i < targets.Count && !hasBoss; i++)
            {
                var bt = targets[i];
                if (bt == null || !bt.isActiveAndEnabled) continue;
                if (bt.TryGetComponent(out BossTarget _) || bt.TryGetComponent(out BossActor _)) { hasBoss = true; bossAt = bt.transform.position; }
            }
            TurnShots(run, v, origin, front, hasBoss, bossAt);
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.isActiveAndEnabled || t.Scenery || run.hit.Contains(t.gameObject)) continue;
                var go = t.gameObject;
                if (go.TryGetComponent(out BossTarget _) || go.TryGetComponent(out BossActor _) || RunScore.IsHostileShot(go)) continue;
                Vector2 p = t.transform.position, d = p - (Vector2)origin;
                float dist = d.magnitude;
                if (dist > l.range + t.Radius || dist - t.Radius * .5f > front) continue;
                if (dist > t.Radius && Vector2.Angle(Vector2.up, d) > l.width + Mathf.Asin(Mathf.Clamp01(t.Radius / dist)) * Mathf.Rad2Deg) continue;
                run.hit.Add(go);
                Shove(run, v, t, origin, top, bottom, ref kills);
            }
        }
        // shoved bodies: into each other, or into the screen top, they take a hit
        for (int j = 0; j < v.count; j++)
        {
            if (v.phase[j] != 1 && v.phase[j] != 2) continue;
            var a = v.tgt[j];
            if (a == null || !a.isActiveAndEnabled) { v.phase[j] = 3; continue; }
            Vector2 pa = a.transform.position;
            bool hitSomething = false;
            for (int i = 0; i < targets.Count && !hitSomething; i++)
            {
                var b = targets[i];
                if (b == null || b == a || !b.isActiveAndEnabled || b.Scenery || Special(b.gameObject) || RunScore.IsHostileShot(b.gameObject)) continue;
                Vector2 d = (Vector2)b.transform.position - pa;
                float r = (a.Radius + b.Radius) * .8f;
                if (d.sqrMagnitude > r * r) continue;
                hitSomething = true;
                Burst(run, (pa + (Vector2)b.transform.position) * .5f, .8f);
                if (ShipAttackHits.Hit(b.gameObject, ship, .5f, ref run.budget)) kills++;
                if (ShipAttackHits.Hit(a.gameObject, ship, .5f, ref run.budget)) kills++;
            }
            if (hitSomething) { v.phase[j] = 3; continue; }
            if (run.t >= v.at[j])
            {
                if (v.phase[j] == 2)
                {
                    Burst(run, new Vector3(pa.x, Mathf.Min(top, pa.y + a.Radius), 0f), .8f);
                    if (ShipAttackHits.Hit(a.gameObject, ship, .5f, ref run.budget)) kills++;
                }
                v.phase[j] = 3;
            }
        }
        if (v.rCount > 0) StepReturns(run, v, dt, ref kills);
        if (run.t < GustSeconds + .05f || v.rCount > 0) return;
        for (int j = 0; j < v.count; j++) if (v.phase[j] == 1 || v.phase[j] == 2) return;
        v.Release();
        run.active = false;
    }

    // One body takes the gust. A small rock is simply broken; anything else is displaced (EnemyShove, the same
    // mechanism as the shield's shockwave: a displacement fed into its position over KnockSeconds, so its mover,
    // brain and steering carry on from the new place).
    void Shove(AttackRun run, AttackV3 v, ClearTarget t, Vector3 origin, float top, float bottom, ref int kills)
    {
        var go = t.gameObject;
        bool rock = go.CompareTag("Astr");
        if (rock && EnemyIdentity.ScaleOf(go) <= .8f)
        {
            Burst(run, t.transform.position, .7f);
            if (ShipAttackHits.Hit(go, ship, .5f, ref run.budget)) kills++;
            return;
        }
        EnemyBrain brain;
        if (!go.TryGetComponent(out brain) || !brain.enabled || brain.Behaviour == null) brain = null;
        bool pilot = brain != null && brain.IsPilot;
        if (pilot && brain.Stage != EnemyBrain.PilotStage.Entering && brain.Stage != EnemyBrain.PilotStage.Engaging &&
            brain.Stage != EnemyBrain.PilotStage.Exiting) return;
        Vector2 p = t.transform.position;
        SpawnFootprint print;
        bool hasPrint = go.TryGetComponent(out print) && print.isActiveAndEnabled;
        Vector2 h = hasPrint ? print.half : new Vector2(t.Radius, t.Radius);
        float mass = rock ? HazardSize.ShoveScale(EnemyIdentity.ScaleOf(go)) : 1f;
        Vector2 move = GaleKnock(origin, p, mass);

        if (go.TryGetComponent(out EliteShip elite))
        {
            if (elite.InPlay) elite.Velocity += move / KnockSeconds * ShieldShockwave.EliteResist;   // its own steering eases it back
            return;
        }
        RailMineMount mount;
        bool topHit = false;
        if (go.TryGetComponent(out mount) && mount.rail != null)
        {
            float along = Mathf.Clamp(mount.Shove + move.y, -ShieldShockwave.MineMaxSlide, ShieldShockwave.MineMaxSlide) - mount.Shove;
            move = new Vector2(0f, along);
        }
        else
        {
            mount = null;
            float lane = SpawnLane.LaneHalf - h.x;
            float offset = brain != null && !pilot ? p.x - brain.Base.x : 0f;
            float baseX = p.x - offset;
            if (Mathf.Abs(baseX) <= lane) move.x = Mathf.Clamp(baseX + move.x, -lane, lane) - baseX;
            else if (Mathf.Abs(baseX + move.x) > Mathf.Abs(baseX)) move.x = 0f;
            // up: no further than the screen top; reaching it counts as a collision with it
            float room = top - h.y - p.y;
            if (move.y > room) { move.y = Mathf.Max(0f, room); topHit = true; }
        }
        if (move.sqrMagnitude < 1e-6f && !topHit) return;
        if (EnemyShove.Add(t.transform, mount, move, h.x, KnockSeconds, pilot) && v.count < AttackV3.Max)
        {
            int s = v.count++;
            v.tgt[s] = t;
            v.at[s] = run.t + KnockSeconds;
            v.phase[s] = topHit && mount == null ? 2 : 1;
        }
    }
}
