# Continuous boss music director

Mapped boss fights now use `BossMusicDirector`. The song schedules the encounter continuously; the old sequence of fixed-duration patterns, cooldowns, and repeated tells no longer runs while a mapped boss song is active.

## Song analysis

All seven entries in `BossBeatMaps.json` now include per-beat normalized energy, bass strength, and onset accents. Eight-beat analysis windows classify likely breakdowns, builds, driving sections, peaks, and drops; adjacent equal classifications merge into sections. These are measured heuristics, not hand-verified musical form. Beat-by-beat energy still controls intensity within those sections.

The existing calibrated beat grids are preserved, including Desert's drifting beat list. A source SHA-256 identifies the exact audio used for each arrangement. The assumed downbeat phase in the original maps remains unchanged and should be checked by ear when refining a song.

```sh
# Refresh arrangement features without replacing the calibrated beat grids:
python scripts/tools/boss_beatmap.py --arrangement-only
# Synthetic known-drop fixture and all seven source hashes/indexing:
python scripts/tests/boss-arrangement-test.py
```

These scripts use the analyzer's ffmpeg/librosa/numpy/scipy environment.

## Musical voices

> Superseded in part by [bullet waves](boss-bullet-waves.md): the pulse and signature voices below are now gap-guaranteed waves spawned on the arrangement's rhythm. The forecast accent voice (bombs, mines, fan) is unchanged, except the Tempest Engine's lattice accent, which is a heavy three-row wave.

- **Pulse:** continuous aimed fire on whole beats; builds and energetic sections add half-beats. Quiet passages retain a lighter pulse.
- **Signature:** each boss's own shape joins according to energy and bass. Space spirals, Frost gapped rings, Verdant homing seeds, Ember flame fans, Tide moving curtains, Storm rain, Desert closing pincers.
- **Accent:** forecast events are prepared two beats ahead while the other voices continue. This includes Ember bombs, Desert mine placements, Storm lattice rows, and fan accents. Looking ahead into the arrangement can prepare a drop accent during the preceding quiet passage.
- **Phrase variation:** direction, ring rotation, and lattice drift change across phrases or section transitions, without stopping the pulse. Per-phrase counters reset so pincers do not close indefinitely.

There is one initial musical warning. After it, the director never ends a firing sequence because an old `duration`, `interval`, `tell`, or `cooldown` expired. Geometry is reused through `BossPatternRunner.EmitMusical`; its timer-based `Step` is not called by the director. The old rotation remains available for missing audio, disabled beat sync, and explicit developer attack previews.

All firing decisions occur on the song's subdivision grid. These govern emission; bullet travel, homing, and physical bomb/mine fuses remain gameplay simulation. Fuses are chosen from beat lengths when prepared, and freeze with gameplay. This does not claim that every later collision or explosion lands on a musical beat.

## Signature specials (`BossSpecials`)

The waves are the whole fight's texture, but a boss's signature attacks live in its rotation table's non-pattern slots (the `Legacy` slots of `BossPatternTables`: the Archon's pod lasers, the Leviathan's glare beams, the Bloom Queen's vine lash and trunk toss, the Drake's jaw flamethrower and eruption columns, the Kraken's beak jet and surf wave, the Tempest Engine's coil beams, the Dune-Eater's lens beam). Before `BossSpecials` those only ran in the timed fallback, never in a mapped fight. Now, during the musical fight:

- A special lands **on a bar line**. Its tell starts `tellSeconds` before the bar line, shortened by however late the frame saw that moment (an audio-buffer step, a long frame), so the hazard ignites or the laser burns on the downbeat itself; later volleys are a whole number of beats apart (`volleyGap` rounded to beats). The executors are the ones `BossActor` always had (`Tell -> Beams / Hazards`); the boss shows the attack's own tell drawings with its parts charging.
- The director's waves **stop `HoldBeats` (6) before the landing** (`BossMusicDirector.HoldFromBeat`: no wave leaves, none is told, no accent is planned into it) and resume on the next rhythm sixteenth once the special is over. A wave is in flight for 2–6 beats (the Dune-Eater's slow tilted rows are the longest), and a wave that would still be in the air when the special lands is not fired at all (`BossWaveVoice.MaxTravelBeats`, from `BossMusicDirector.ArriveBeforeBeat`), so none reaches the ship's row while the special is live. The gap chain restarts from the ship's position.
- Cadence: one every `BossSpecials.Bars[world]` bars (8 for every boss), counted from the director's start; the rotation cycles the specials the fight's progress has unlocked (`minPhase`, `BossCatalog.UnlockedAttacks`). A boss whose specials are all still locked (Space's lasers come in the last third) fires its first on the first bar line after the unlock. With a special taking 3–4.5 s plus the hold, 8 bars (12.8 s at 150 bpm) leaves the waves about two thirds of the fight; 4 bars would leave them under a third.
- Pauses: a tell start the game slept through (the song runs on) is never launched late — the special moves to the next bar line with room for its whole tell (`Skipped`). A special already in its tell plays out in game time with everything else on screen (its hazards froze with the ship), so after a pause that one lands late for the song rather than being cancelled (a pause is the player's own power; cancelling the boss's attack with it would be an exploit).
- `BossSpecials.Enabled = false` is the waves-only fight (previews, tests); `ForcedAttack >= 0` and a missing song keep the timed rotation.

`BossSpecialsTest` proves the scheduling on a manual clock and on the real actor with the real hazard pools (every boss: the special goes live within a frame of a bar line, no wave arrives after the landing or leaves while it is live, the drawings are the special's), the pause rules, and measures the dodge bot (DodgeBot through the real `BossEncounter` on the real song clock) with the waves alone, specials every 8 bars and every 4 — the `[SPECIALS] <boss> dodge bot` lines of the log are the numbers behind `Bars`.

## Difficulty ladder (`BossDifficulty`)

The song's intensity (`energy * .75 + bass * .15 + progress * .1`) used to be the whole difficulty, so the ladder ran on the songs' arrangements: the Dune-Eater's quiet intro held the final boss at one wave a bar for its first ~28 s, while the Bloom Queen's song opens hot. `BossDifficulty` (one table, `Scripts/Bosses/Patterns/BossDifficulty.cs`) now shapes it per boss, in game seconds since the fight began:

```
level(t)  = rampSeconds <= 0 ? 1 : lerp(startIntensity, 1, smoothstep(t / rampSeconds))
intensity = min(level, max(raw, intensityFloor * level))
```

| Boss | Ramp | Start level | Floor | Wave speed | Beat sync from start |
| --- | ---: | ---: | ---: | ---: | --- |
| Void Archon | song (~40 s) | song (.17) | none | x1.00 | no (2-beat warning) |
| Hoarfrost Leviathan | 20 s | .45 | .30 | x1.00 | no |
| Bloom Queen | 14 s | .55 | .40 | x1.02 | no |
| Cinder Drake | 9 s | .65 | .50 | x1.04 | no |
| Iron Kraken | 5 s | .80 | .58 | x1.06 | no |
| Tempest Engine | none | 1 | .66 | x1.08 | yes |
| Dune-Eater | none | 1 | .72 | x1.10 | yes |

- Boss 1 keeps the song alone (`songRamp`): its fight is exactly what it was.
- The floor is the attack-rate knob: >= .25 two waves a bar, >= .45 three, >= .62 a wave on every beat. Every floor stays under .75, where a wave stops showing its gap telegraph.
- With `beatSyncFromStart` the director's first wave leaves on the first beat that leaves its one-beat tell, not after two beats of calm; with the floor over .62 every beat is in the rhythm, so the first wave and every one after it are on the beat.
- The attack rotation is untouched: every boss keeps its phase structure (phase 1 its first attacks, the specials as their `minPhase` unlocks them; loops add their own head start, `LoopRules`, as before). The ladder works only through the waves' intensity, speed and start.
- The ramp level moves only on bar lines (`BossMusicDirector.RampClock`), so the rhythm a wave is told on is the rhythm it fires on.
- Bullet counts and hearts are not knobs: denser means more waves on the beat inside the director's pool budget, and every boss keeps 10 hearts.

`BossDifficultyTest` enforces the ladder (each knob monotonic, boss 1 the identity, the last two without a ramp), measures each boss's real song from the fight's first frame old against new (the `[DIFFICULTY] TABLE` log lines), the dodgeability of the first 30 s (flood fill, gap widths) and the projectile budget of a whole fight.

## Pauses and load

The song keeps advancing during a pause. The director skips elapsed subdivisions and cancels expired accent plans on resume, without replaying missed bullets. Musical warning markers expire with unscaled audio time, including during pauses. No firing occurs while gameplay is frozen.

A live projectile budget reserves space for the pulse and future bursts. Under pressure, it suppresses secondary layers before filling the bounded pool; it does not expand the pool or silently overwrite existing shots. At an exhausted pool, even the pulse must defer until room is available. This is especially relevant to prolonged extreme slow motion.

## Validation

`BossMusicDirectorTest` simulates every entire song plus a loop, varying 30/60 FPS frames, moving the player across the field, inserting repeated two-second pauses, and applying five-second 0.06× slow-motion segments. It checks continuous firing, subdivision timing, no pause backlog, bounded populations, and changing phrases. Additional fixtures check quiet/build/drop density, drop look-ahead, the real actor's continuous path, and warning expiry during pauses.

Measured full-song peaks with that stress scenario:

| Boss | Peak live shots | Dropped shots |
| --- | ---: | ---: |
| Space | 68 | 0 |
| Frost | 88 | 0 |
| Verdant | 100 | 0 |
| Ember | 112 | 0 |
| Tide | 112 | 0 |
| Storm | 104 | 0 |
| Desert | 107 | 0 |

The pool holds 144. The longest measured firing gap during active play was 0.467 seconds, approximately one beat plus frame/loop timing. Scripted initial warnings and gameplay pauses are excluded from this gap measurement.

Run the combined checks:

```sh
scripts/unity-batch.sh -executeMethod AllTests.RunSuites \
  -suites BossPatternTest,BeatSyncTest,BossMusicDirectorTest,SpaceBossMusicTest,BossEncounterTest
```

The old `BossLadder.Measure` describes the fallback timer rotation, not this director. Passing these simulations proves timing and bounded population in the measured scenarios, not human dodgeability or perfect musical interpretation. Listening and gameplay review should refine section labels, trajectories, and density.
