# Boss bullet waves

Boss attacks in a mapped song are **waves with a gap**, spawned on the music's beat grid. This replaces the single aimed "pulse" and the one-stream "signature" voice of `BossMusicDirector`; the forecast accent voice (bombs, mines, lattice, fan) is unchanged.

Code: `Scripts/Bosses/Patterns/BossWaves.cs`; wired in `BossMusicDirector.Step`; tests: `BossWaveTest`.

## Pieces

| Piece | Job |
| --- | --- |
| `WaveRhythm.Mask(kind, intensity)` | Which sixteenths of a bar (relative to the song's downbeat) spawn a wave. Breakdown: one a bar. Calm: beats 1 and 3. Drive: 3+3+2 syncopation. Build / peak: every beat. Drop at high intensity: every beat plus off-beats. |
| `WaveMoves.For(world)` | Each boss's menu of shapes, unlocked by intensity tier (`WaveRhythm.Tier`: 0..3), cycled per phrase. |
| `WaveGaps.Choose` | The fairness core. Picks the gap centre. |
| `BossWaveVoice.Emit` | Turns a move into bullets and telegraphs the gap lane. |

### Shapes (`WaveKind`)

* **Wall**: a flat row of bullets falling with one gap. `sweep` makes the gap travel steadily (the Iron Kraken's tidal wall), reversing at the lane's end.
* **Slant**: the same row drifting sideways; a `+` and a `-` slant alternating are the crossing / closing waves (Dune-Eater's pincer, Tempest lightning).
* **Ring**: radial ring from a muzzle with one gap, only the downward half (Void Archon pulsar when swept, Hoarfrost cold snap).
* **Fan**: an arc of bullets from a muzzle with one gap (Bloom Queen vine fans, Cinder Drake flame fans).
* `rows` makes a wave thicker (more bullets, same gap); a swept ring is the spiral.

### Intensity ramp

Intensity = energy, bass and fight progress (existing `BossMusicDirector.Intensity`).

* more waves per bar (rhythm mask), more shapes unlocked (tier), thicker rows and wider rings/fans at the top tier,
* waves 12 % faster at full intensity,
* gap width from `GapTarget + 1.2` ship widths down to the boss's own `BossLadder.GapTarget` (never under `WaveGaps.MinMultiple = 1.7` ship widths of clear space).

## The gap guarantee

For each wave the generator knows the game-clock time it reaches the ship's row (`arrival`) and where the previous wave's gap was. The ship can be anywhere inside that gap while it passes, so the new gap centre may move at most

`room(prev gap) + room(new gap) + 4 u/s * (arrival - prevArrival - thickness - 0.14 s)`

(`PlanSpeed` 4 u/s, a quarter below the 5 u/s the flood-fill solver uses). A wave after a quiet spell is judged from the ship's own position instead. All of it is **game time**, so a pause or slow motion that squeezes two waves together shrinks the allowed shift to zero and the later wave reuses the earlier gap (a thicker wall with the same hole); a pause cannot trap the ship. Waves closer than 0.15 game seconds are dropped.

Layered or crossing waves are not special cases: they are just consecutive waves, and the chain constraint covers them. Rings and fans aim their gap corridor at that centre and widen it by the angle that gives the required clear width at the *nearest* distance the ship can be.

## Telegraphs

The gap lane is marked (the world's own cue colour, pooled markers) for waves below high intensity and for every fourth wave above it, lasting the wave's flight so the lane blinks until the wall arrives. No pink outside Space: bullet and marker art come from the boss/world as before.

## Beat lock

* A wave leaves the boss exactly on a sixteenth (`LastScheduledBeat * 4` is an integer; test checks 1e-6).
* The wave's speed is **quantised** (`v = distance / (k * secondsPerBeat)`) so it also *reaches the ship's row on a beat* `k` beats later. This keeps the nominal speed within the rounding of one beat (a few percent).
* Bullets fly in game time, so slow motion shifts the arrival (not the spawn).

## Validation

`BossWaveTest` plays every boss song in full plus a loop seam through the real `BossMusicDirector` + `PatternSim` at an uneven 30/60 fps, flood-filling the ship's reachable set (5 u/s, 0.04 u margin) after every frame: the set must never be empty, every wave must be on a sixteenth and within one frame of the beat, every gap must be at least the fair floor, every landing a whole number of beats. A stress variant adds repeated pauses and 0.25x slow motion. Unit checks cover the gap chain generator (400 random chains), rhythm monotonicity, gap-width ramp, density ramp, and a clock regression.

This is a machine proof that a path exists; it is not a dodgeability or feel playtest (see PR notes).
