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

- **Pulse:** continuous aimed fire on whole beats; builds and energetic sections add half-beats. Quiet passages retain a lighter pulse.
- **Signature:** each boss's own shape joins according to energy and bass. Space spirals, Frost gapped rings, Verdant homing seeds, Ember flame fans, Tide moving curtains, Storm rain, Desert closing pincers.
- **Accent:** forecast events are prepared two beats ahead while the other voices continue. This includes Ember bombs, Desert mine placements, Storm lattice rows, and fan accents. Looking ahead into the arrangement can prepare a drop accent during the preceding quiet passage.
- **Phrase variation:** direction, ring rotation, and lattice drift change across phrases or section transitions, without stopping the pulse. Per-phrase counters reset so pincers do not close indefinitely.

There is one initial musical warning. After it, the director never ends a firing sequence because an old `duration`, `interval`, `tell`, or `cooldown` expired. Geometry is reused through `BossPatternRunner.EmitMusical`; its timer-based `Step` is not called by the director. The old rotation remains available for missing audio, disabled beat sync, and explicit developer attack previews.

All firing decisions occur on the song's subdivision grid. These govern emission; bullet travel, homing, and physical bomb/mine fuses remain gameplay simulation. Fuses are chosen from beat lengths when prepared, and freeze with gameplay. This does not claim that every later collision or explosion lands on a musical beat.

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
