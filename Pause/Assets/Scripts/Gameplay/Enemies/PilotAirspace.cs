using System.Collections.Generic;
using UnityEngine;

// The pilots in play (EnemyBrain's pilot mode) and the space they hold.
//
// A pilot holds its place in the world while the board pours past, so
// instead of making it dodge every rock, hazards are routed round it:
//
//   * a pilot RESERVES its column -- its body at its station x, plus a
//     margin -- from the moment it is admitted until it has left;
//   * the spawner places no hazard whose envelope crosses a reserved column
//     (Blocks, TryFreeX), and a pilot waits above the view until the hazards
//     that were already in its column have gone by (ColumnClear);
//   * its lateral band is NOT reserved: hazards may come down beside its
//     column, and the pilot sidesteps them -- BandLimits tells it how far
//     either side of its column it may be while one passes, and its own
//     column is always a safe place to duck back to;
//   * admission is capped (TryAdmit): a pilot load by speed and world
//     (EnemyDensity.MaxPilotLoad) and a share of the lane that may be
//     reserved at once, so hazards always have room and so does the ship;
//   * a boss or portal on the way stops admissions and orders the pilots
//     out (MustClear / AdmissionClosed).
//
// The registry is a short static list (a handful of pilots); every query is
// a plain loop with no allocation. Columns stay clear of the rail hardware,
// so rail mines are never in one.
public static class PilotAirspace
{
    // ---- tunables ----
    // A column's clearance beyond band + body, each side.
    public const float ColumnMargin = .1f;
    // Columns keep inside +/- this: the rail mines' inner edge is at ~2.04.
    public const float RailClear = 2f;
    // At most this share of the lane (2 x SpawnLane.LaneHalf) is reserved
    // (one pilot is always allowed, however wide).
    public static float MaxReservedShare = .50f;
    // How far above the spawner's line a pilot waits (hazards lift to +1.5).
    public const float WaitAbove = 3f;
    // No new pilot this many seconds before a boss; pilots are ordered out
    // this many seconds before it (the boss's own board clear takes the rest).
    public static float AdmitLeadSeconds = 9f, ClearLeadSeconds = 3.5f;
    const int Tries = 10;

    static readonly List<EnemyBrain> live = new List<EnemyBrain>(16);

    // ---- stats (probe, tests) ----
    public static int Admitted, Departed, Escaped;
    public static float EngagedSecondsTotal, InViewSecondsTotal;

    public static void ResetStats() { Admitted = Departed = Escaped = 0; EngagedSecondsTotal = InViewSecondsTotal = 0f; }

    public static IReadOnlyList<EnemyBrain> Live { get { Prune(); return live; } }
    public static int Count { get { Prune(); return live.Count; } }

    // Destroyed pilots drop out (OnDisable does it in play; the editor's
    // headless runs destroy objects without it).
    static void Prune()
    {
        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i] == null || live[i].Stage == EnemyBrain.PilotStage.Gone) live.RemoveAt(i);
    }

    internal static void Register(EnemyBrain pilot)
    {
        if (pilot != null && !live.Contains(pilot)) live.Add(pilot);
    }

    internal static void Unregister(EnemyBrain pilot) { live.Remove(pilot); }

    // It flew off (its script ended; not killed).
    internal static void NoteDeparture(EnemyBrain pilot)
    {
        Departed++;
        Escaped++;
        EngagedSecondsTotal += pilot.EngagedSeconds;
        InViewSecondsTotal += pilot.InViewSeconds;
    }

    public static void Clear() { live.Clear(); }

    // ---- sizes ----

    public static float ColumnHalf(EnemyDef def, EnemyBehaviour b)
    {
        return SpawnSpace.BodyHalf(def).x + ColumnMargin;
    }

    // How far either side of its column a pilot may be right now: its band,
    // cut back on a side while a hazard is passing (or about to pass) there.
    // `lookUp` / `lookDown`: how far above / below the pilot counts as
    // "about to". Its column is never crossed by a hazard, so every hazard is
    // wholly on one side and lo <= 0 <= hi always holds.
    public static void BandLimits(EnemyBrain pilot, Vector2 at, Vector2 half, float band, float lookUp, float lookDown,
                                  out float lo, out float hi)
    {
        lo = -band;
        hi = band;
        float x = pilot.Anchor.x;
        float yLow = at.y - half.y - lookDown, yHigh = at.y + half.y + lookUp;
        var enemies = SpawnSpace.Live(SpawnLayer.Enemy);
        for (int i = 0; i < enemies.Count; i++)
        {
            var f = enemies[i];
            if (f == null) continue;
            var plan = f.Plan;
            if (plan != null && plan.SelfSteering) continue;
            Rect e = f.Envelope();
            if (e.yMax < yLow || e.yMin > yHigh) continue;
            float clear = half.x + SpawnSpace.Margin * 2f + .04f;
            if (e.center.x >= x) hi = Mathf.Min(hi, e.xMin - x - clear);
            else lo = Mathf.Max(lo, e.xMax - x + clear);
        }
        if (hi < 0f) hi = 0f;
        if (lo > 0f) lo = 0f;
    }

    // How much of the pilot budget an enemy takes.
    public static float Weight(EnemyDef def)
    {
        if (def == null) return 1f;
        switch (def.role)
        {
            case EnemyRole.Alien: return .5f;
            case EnemyRole.Big: return 1.5f;
            case EnemyRole.Fighter: return def.tier <= 1 ? .5f : def.tier >= 4 ? 1.5f : 1f;
            default: return 1f;
        }
    }

    public static float Load
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < live.Count; i++) if (live[i] != null) sum += Weight(live[i].Def);
            return sum;
        }
    }

    public static float ReservedWidth
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < live.Count; i++) if (live[i] != null) sum += 2f * live[i].ColumnHalf;
            return sum;
        }
    }

    // ---- columns ----

    // Does [xMin, xMax] cross a reserved column?
    public static bool Blocks(float xMin, float xMax)
    {
        Prune();
        for (int i = 0; i < live.Count; i++)
        {
            var p = live[i];
            if (p == null) continue;
            float x = p.Anchor.x, h = p.ColumnHalf;
            if (xMin < x + h && xMax > x - h) return true;
        }
        return false;
    }

    static readonly float[] gapLo = new float[10], gapHi = new float[10];

    // A centre x for something reaching `reachHalf` either side of it, inside
    // +/-maxX, in a stretch of lane no pilot has reserved: picked at random
    // among the gaps that are wide enough, weighted by their room. False when
    // no gap is (the caller waits for a pilot to leave).
    public static bool TryFreeX(float reachHalf, float maxX, out float x)
    {
        Prune();
        x = 0f;
        float lo = -maxX - reachHalf, hi = maxX + reachHalf;
        if (live.Count == 0) { x = Random.Range(-maxX, maxX); return true; }
        // walk the lane left to right past each column in turn (a handful: no sort needed)
        int gaps = 0;
        float cursor = lo, room = 0f;
        for (int guard = 0; guard < live.Count + 1 && gaps < gapLo.Length; guard++)
        {
            // the next column starting at or after the cursor
            float nextLo = hi, nextHi = hi;
            for (int i = 0; i < live.Count; i++)
            {
                float cLo = live[i].Anchor.x - live[i].ColumnHalf, cHi = live[i].Anchor.x + live[i].ColumnHalf;
                if (cHi <= cursor) continue;
                if (cLo < nextLo) { nextLo = cLo; nextHi = cHi; }
            }
            float gapEnd = Mathf.Min(nextLo, hi);
            float usable = gapEnd - cursor - 2f * reachHalf;
            if (usable >= 0f)
            {
                gapLo[gaps] = cursor + reachHalf;
                gapHi[gaps] = gapEnd - reachHalf;
                room += usable + .01f;
                gaps++;
            }
            if (nextLo >= hi) break;
            cursor = Mathf.Max(cursor, nextHi);
        }
        if (gaps == 0) return false;
        float pick = Random.value * room;
        for (int g = 0; g < gaps; g++)
        {
            float w = gapHi[g] - gapLo[g] + .01f;
            if (pick <= w || g == gaps - 1) { x = Random.Range(gapLo[g], gapHi[g]); return true; }
            pick -= w;
        }
        return false;
    }

    // Is the pilot's column free of board-locked hazards from just below
    // `downTo` up to where it waits? (They scroll away; it only has to wait.)
    public static bool ColumnClear(EnemyBrain pilot, float downTo)
    {
        float x = pilot.Anchor.x, h = pilot.ColumnHalf;
        float yLow = downTo - 1.5f, yHigh = pilot.transform.position.y;
        var enemies = SpawnSpace.Live(SpawnLayer.Enemy);
        for (int i = 0; i < enemies.Count; i++)
        {
            var f = enemies[i];
            if (f == null) continue;
            var plan = f.Plan;
            if (plan != null && plan.SelfSteering) continue;   // pilots, chasers, elites keep clear themselves
            Rect e = f.Envelope();
            if (e.xMin >= x + h || e.xMax <= x - h) continue;
            if (e.yMax > yLow && e.yMin < yHigh) return false;
        }
        return true;
    }

    // ---- boss / portal ----

    // A boss or a portal is here or seconds away: pilots clear out.
    public static bool MustClear
    {
        get
        {
            if (BossEncounter.Running) return true;
            var wm = WorldManager.Instance;
            if (wm == null) return false;
            if (wm.PortalIsOpen || wm.Route == WorldManager.FinalRoute.Choosing) return true;
            return wm.Route != WorldManager.FinalRoute.KeepFlying && wm.DistanceLeft > 0f && wm.SecondsLeftInWorld < ClearLeadSeconds;
        }
    }

    public static bool AdmissionClosed
    {
        get
        {
            if (MustClear) return true;
            var wm = WorldManager.Instance;
            return wm != null && wm.Route != WorldManager.FinalRoute.KeepFlying && wm.DistanceLeft > 0f &&
                   wm.SecondsLeftInWorld < AdmitLeadSeconds;
        }
    }

    // ---- admission ----

    // Room for this pilot now? Gives the station x of a free column near
    // `preferredX` (NaN: anywhere). False: not now (the spawner skips it).
    public static bool TryAdmit(EnemyDef def, EnemyBehaviour b, float preferredX, out float x)
    {
        x = 0f;
        if (def == null || b == null || AdmissionClosed) return false;
        Prune();
        float hud = EnemyDensity.Hud;
        if (Load + Weight(def) > EnemyDensity.MaxPilotLoad(hud, def.world) + 1e-4f) return false;
        float half = ColumnHalf(def, b);
        if (live.Count > 0 && ReservedWidth + 2f * half > MaxReservedShare * 2f * SpawnLane.LaneHalf) return false;
        float limit = Mathf.Max(0f, RailClear - half);
        for (int t = 0; t < Tries; t++)
        {
            float c = t == 0 && !float.IsNaN(preferredX) ? Mathf.Clamp(preferredX, -limit, limit) : Random.Range(-limit, limit);
            if (Blocks(c - half, c + half)) continue;
            x = c;
            Admitted++;
            return true;
        }
        return false;
    }
}
