using UnityEngine;

// THE BOSS DIFFICULTY LADDER, one table (docs/boss-music-director.md, "Difficulty ladder"). Tune the musical fight's difficulty per boss
// HERE; BossDifficultyTest keeps it a ladder (every knob non-decreasing in difficulty from boss 1 to boss 7).
//
// The musical fight (BossMusicDirector) turns the boss song into an INTENSITY 0..1 every sixteenth:
//     raw = energy * .75 + bass * .15 + fight progress * .1
// and the intensity decides everything the waves do: how many waves a bar (WaveRhythm.Mask: 1 / 2 / 3 / 4 a bar,
// 6 in a hot drop), which wave shapes are unlocked (WaveRhythm.Tier), how tight the gap is (WaveRhythm.Squeeze, down to
// BossLadder.GapTarget), how fast the waves fall (+12 % at 1) and whether the accents (bombs, mines, lattices) come
// (> .48). Before this table the song alone set it, so the ladder ran on the songs' arrangements: the Dune-Eater's quiet
// intro left the last boss at one wave a bar for its first ~28 s, while the Bloom Queen's song opens on a drop.
//
// Each row reshapes the raw intensity in game seconds since the fight began (BossDifficulty.Shape):
//     level(t)  = rampSeconds <= 0 ? 1 : Lerp(startIntensity, 1, smoothstep(t / rampSeconds))   the RAMP-UP
//     intensity = Min(level, Max(raw, intensityFloor * level))
// so during the ramp the song is capped at `level` (a readable opening, however hot the song starts) and lifted to
// the floor's share of it; once the ramp is over the song plays in full on top of the floor. A boss with
// `songRamp` keeps the song's raw intensity untouched (boss 1: exactly today's fight).
//
//   songRamp          true: no imposed shape, the song's own arrangement is the ramp (only boss 1). Its rampSeconds and
//                     startIntensity then DESCRIBE that ramp (measured on its song) and are not applied.
//   rampSeconds       how long the ramp-up lasts (game seconds after the entrance); 0 = none, full from the first wave
//   startIntensity    the ramp's ceiling at the fight's first frame (0..1); 1 = no ramp
//   intensityFloor    the least intensity once the ramp is over -- THE ATTACK-RATE KNOB: >= .25 two waves a bar,
//                     >= .45 three, >= .62 a wave on every beat; also the gap's squeeze and the wave tier. Kept under
//                     .75 for every boss so every floor-driven wave still shows its gap telegraph (BossMusicDirector)
//   shotSpeedScale    the waves' fall speed times this (the gap chain plans with the real speed: still fair)
//   unlockHeadStart   fight progress added for the signature specials' unlocks (and the timed fallback's rotation):
//                     1/3 = the phase-2 specials from the first one. A later loop's own head start (LoopRules) is the
//                     larger of the two, not their sum.
//   beatSyncFromStart the director's first wave leaves on the first beat that leaves its one-beat tell, instead of
//                     holding two beats of warning first (BossMusicDirector.StartBeat)
//
// Bullet counts are not a knob: walls are laid at each world's pitch with one gap, so a "denser" wave is more waves on
// the beat, not more bullets in one; the director's budget (Room: the pool less a reserve) bounds the live shots.
// The boss's health is not a knob either: every boss has the same 10 hearts (BossConfig.Hearts, the user's rule).
public sealed class BossDifficultyRow
{
    public string boss;
    public bool songRamp;
    public float rampSeconds;
    public float startIntensity;
    public float intensityFloor;
    public float shotSpeedScale = 1f;
    public float unlockHeadStart;
    public bool beatSyncFromStart;
}

public static class BossDifficulty
{
    // false: every boss plays its song raw with today's start (tests comparing against the old director)
    public static bool Enabled = true;

    // index = world (WorldManager.Worlds): Space, Frost, Verdant, Ember, Tide, Storm, Desert
    public static readonly BossDifficultyRow[] Rows =
    {
        // 1 Void Archon: the easiest, unchanged. Its song's sparse intro is the ramp: one or two waves a bar for ~40 s
        //   (mean intensity .17 over the first 10 s), the drop ~40 s in; no floor, no imposed cap.
        new BossDifficultyRow { boss = "Void Archon", songRamp = true, rampSeconds = 40f, startIntensity = .17f },
        // 2 Hoarfrost Leviathan: a 20 s ramp from .45, then never below two waves a bar.
        new BossDifficultyRow { boss = "Hoarfrost Leviathan", rampSeconds = 20f, startIntensity = .45f, intensityFloor = .30f },
        // 3 Bloom Queen: its song opens hot (intensity ~.9: six waves a bar in its peaks from the first second): the 14 s
        //   ramp caps that opening at .55 (a wave a beat in its builds, never the doubled six) and lets the song in by
        //   14 s; the floor holds it busy in the breaks.
        new BossDifficultyRow { boss = "Bloom Queen", rampSeconds = 14f, startIntensity = .55f, intensityFloor = .40f, shotSpeedScale = 1.02f },
        // 4 Cinder Drake: 9 s ramp from .65 (a wave on every beat by ~3 s), three waves a bar at the least; bombs early.
        new BossDifficultyRow { boss = "Cinder Drake", rampSeconds = 9f, startIntensity = .65f, intensityFloor = .50f, shotSpeedScale = 1.04f, unlockHeadStart = .10f },
        // 5 Iron Kraken: a 5 s breath from .8, then three waves a bar at the least, the tighter gaps.
        new BossDifficultyRow { boss = "Iron Kraken", rampSeconds = 5f, startIntensity = .80f, intensityFloor = .58f, shotSpeedScale = 1.06f, unlockHeadStart = .20f },
        // 6 Tempest Engine: no ramp. A wave on every beat from the first one, on the beat; phase-2 specials from the start.
        new BossDifficultyRow { boss = "Tempest Engine", rampSeconds = 0f, startIntensity = 1f, intensityFloor = .66f, shotSpeedScale = 1.08f, unlockHeadStart = 1f / 3f, beatSyncFromStart = true },
        // 7 Dune-Eater (the final boss): no ramp, hard from the first beat: its quiet song can never drop it below a
        //   wave a beat, the tightest gap squeeze, the fastest waves, the lens beam in rotation from the first special.
        new BossDifficultyRow { boss = "Dune-Eater", rampSeconds = 0f, startIntensity = 1f, intensityFloor = .72f, shotSpeedScale = 1.10f, unlockHeadStart = 1f / 3f, beatSyncFromStart = true },
    };

    public static BossDifficultyRow For(int world) => Rows[Mathf.Clamp(world, 0, Rows.Length - 1)];

    // Is the row's shape applied (not the song alone)?
    public static bool Imposed(int world) => Enabled && !For(world).songRamp;

    // The ramp-up level at `fightSeconds` of game time into the fight: startIntensity rising smoothly to 1 over
    // rampSeconds (1 throughout for a boss without a ramp). For a songRamp boss it describes the song's ramp.
    public static float RampLevel(int world, float fightSeconds)
    {
        var r = For(world);
        if (r.rampSeconds <= 0f) return 1f;
        float x = Mathf.Clamp01(fightSeconds / r.rampSeconds);
        return Mathf.Lerp(r.startIntensity, 1f, x * x * (3f - 2f * x));
    }

    // The director's intensity for the song's raw intensity at `fightSeconds` (see the top of the file).
    public static float Shape(int world, float raw, float fightSeconds)
    {
        if (!Imposed(world)) return raw;
        var r = For(world);
        float level = RampLevel(world, fightSeconds);
        return Mathf.Clamp01(Mathf.Min(level, Mathf.Max(raw, r.intensityFloor * level)));
    }

    public static float ShotSpeed(int world) => Enabled ? For(world).shotSpeedScale : 1f;

    public static float HeadStart(int world) => Enabled ? For(world).unlockHeadStart : 0f;

    public static bool SyncFromStart(int world) => Enabled && For(world).beatSyncFromStart;
}
