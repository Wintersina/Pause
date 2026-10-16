# Boss bullet waves

Boss attacks in a mapped song are **waves with a gap**, spawned on the music's beat grid, and **every bullet leaves the boss itself**: one of its measured muzzles (`BossEmitterTable`), in the drawing on screen, on the body part that visibly does the attack. Nothing is spawned at a screen edge or above the view.

Code: `Scripts/Bosses/Patterns/BossWaves.cs`; wired in `BossMusicDirector.Step` and `BossActor`; tests: `BossWaveTest`.

## Pieces

| Piece | Job |
| --- | --- |
| `WaveRhythm.Mask(kind, intensity)` | Which sixteenths of a bar (relative to the song's downbeat) spawn a wave. Breakdown: one a bar. Calm: beats 1 and 3. Drive: 3+3+2 syncopation. Build / peak: every beat. Drop at high intensity: every beat plus off-beats. |
| `WaveMoves.For(world)` | Each boss's menu of shapes, unlocked by intensity tier (`WaveRhythm.Tier`: 0..3), cycled per phrase. |
| `BossWaveSources.For(world, boss)` | Which body part fires each shape (wall, the alternate wall side, ring, fan, accent) and the tell pose whose drawings show that part working. |
| `WaveDeploy` | The fan-out geometry: the formation line under the boss, the spray time, the nearest muzzle for a lane. |
| `WaveGaps.Choose` + `WaveFront` | The fairness core. Picks the gap centre, knowing how the wave's front meets the ship's row. |
| `BossWaveVoice.Emit` | Turns a move into bullets from the source's muzzles and telegraphs the gap lane. |

### Shapes (`WaveKind`)

* **Wall**: a row of bullets with one gap. The bullets leave the source muzzles (each from the muzzle nearest its lane), fan out to a **formation line** just under the boss (`PShot.turnAt`), and there turn and fall straight down their lanes. Below the formation it is exactly the old full-width wall. `sweep` makes the gap travel steadily (the Iron Kraken's tidal wall), reversing at the lane's end.
* **Slant**: the same row **tilted** by `drift` degrees: the bullets on one side turn down later, so the row sweeps down at an angle. `+` and `-` slants alternating are the crossing / closing waves (Dune-Eater's pincer, Tempest lightning). (It used to be a row drifting sideways in from above the view; a drifting row that starts on the boss would leave the trailing side open.)
* **Ring**: radial ring from the source muzzle with one gap, only the downward half.
* **Fan**: an arc of bullets from the source muzzle with one gap.
* `rows` makes a wave thicker (more bullets, same gap); a swept ring is the spiral.

### The deploy (fan-out)

* Formation line: `lerp(ship ceiling, lowest source muzzle, .45)`, never closer than ship radius + bullet radius + a margin to the ship's ceiling and never above the muzzles, so the spray never touches the ship. On a 21:9 phone the boss sits high under the HUD band and the ship's ceiling is just under its lowest muzzle: the line then sits right under the muzzles (a sideways spray).
* Every row falls at the **same speed** (the world's nominal wave speed at that intensity). Rows of different speeds would overtake each other and cross; the beat lock moves into the spray time instead: the spray takes between its minimum (`distance / 8 u/s`, .25..55 s) and one beat more, so the gap centre still lands on the beat.
* The boss's sway does not move a wave once it has left: the formation points are fixed world points, aimed at from wherever the muzzle is that frame.

## Where each attack comes from (the art audit)

Pose cells: Space, Ember and Tide have the 35-cell atlas (tell pose p = cells 26+3p, 27+3p, fire = 28+3p); the others tell in 5/6 (pose 0), 7/8 (pose 1), 10/11 (pose 2) and fire in 9. While a song plays, the boss shows the **tell drawings of the part about to fire** on the beat before each wave (the first two drawings of a 3-cell tell; the third is its fire drawing), its **fire drawing the frame the bullets leave** (held for a breath), and idles in between; the charge glow gathers on the muzzles of that part and the flash pops on the muzzles that fired.

### Song waves (`BossWaveSources`, what plays in the game)

| Boss | Shape | Source part (emitter ids) | Pose / cells | Projectile |
| --- | --- | --- | --- | --- |
| Void Archon | wall / slant | PodL + PodR nozzles (2, 3) | 2: 32-34 | shards |
| | ring | Core reactor (1) | 1: 29-31 | shards |
| | fan, accent fan | Chin cannon (0) | 0: 26-28 | shards |
| Hoarfrost Leviathan | wall / slant | icicle Jaw (0) | 0: 5/6, 9 | shards |
| | ring (cold snap) | icicle Jaw (0) **re-mapped** | 0: 5/6, 9 | shards |
| | accent fan | Jaw (0) | 0 | shards |
| Bloom Queen | wall / slant | acid CannonL + CannonR (7, 8) | 2: 10/11, 9 | bolts |
| | fan, accent fan | Stinger (0) | 0: 5/6, 9 | bolts |
| | ring (not in its menu) | Bulb (9) | 0 | bolts |
| Cinder Drake | wall / slant (slugs) | twin brass shoulder CannonL + CannonR (3, 4) + chest Furnace (1) | 1: 29/30, fire 31 (the cell paints both cannons flaring and three slugs out of the furnace) | shards |
| | fan (flame breath) | Jaw / maw (0), fire drawing held a beat | 0: 26/27, fire 28 (the flame cell) | shards |
| | accent bombs | Furnace (1) | 1 | bombs |
| Iron Kraken | wall / slant (tidal walls) | port arm cluster LeftA + LeftB (1, 2), then the starboard cluster RightA + RightB (3, 4), alternating | 1: 29-31 / 2: 32-34 | shards |
| | ring, fan, accent fan | hydraulic Beak (0) | 0: 26-28 | shards |
| Tempest Engine | wall / slant (rain curtains) | Tesla CoilL + CoilR (2, 3) | 2: 10/11, 9 | bolts |
| | ring, fan | Core drum (0) | 0: 5/6, 9 | bolts |
| | accent lattice (a 3-row wall) | CoilL + CoilR | 2 | bolts |
| Dune-Eater | wall / slant (sand walls, pincers) | Maw (0) | 0: 5/6, 9 | shards |
| | ring | Maw (0) | 0 | shards |
| | accent mines | Boom crane (1), thrown onto their marked spots | 1: 7/8, 9 | mines |

Flagged and re-mapped:

* **Hoarfrost Leviathan cold-snap rings** came from the blowhole Crown at the top of its head. A ring from that high sweeps down through the body and meets the ship's row so unevenly that the gap chain could not be proved (`BossWaveTest` found dead ends); they now burst from the icicle jaw (its pose 0 open-jaw drawings). The timed fallback's "cold snap" / "frozen crown" still use the Crown.
* **Cinder Drake slugs**: the new art has twin brass shoulder cannons that flare in the slug fire cell 31; the emitter table had no points on them. `CannonL` / `CannonR` were added (`measure_emitters.py`, measured on every cell) and the slug walls, the catalog's "furnace slugs" (one slug from each of furnace + both cannons per volley) and the Drake's flash slots use them.
* **Cinder Drake cell 28** (the breath): the whole-body registration was dragged by the painted flame; the brow sat ~20 reference px (30 art px) above its gem, the cannons ~30 px off their bores and the maw off the flame's root. Fixed by pixel (`OVERRIDES` in `measure_emitters.py`, asserted in `BossWaveTest`).
* **Dune-Eater closing jaws** (timed pincer) fired its two jaws from 1.5 u either side of its single Maw muzzle, i.e. from empty air; both streams now fork from the Maw.
* **Dune-Eater mines** appeared on their spots out of nowhere; they are now thrown from the Boom crane (0.45 s, `BossPatternRunner.MineThrow`) and settle on the marked spots.

### Timed rotation (no song: the fallback and developer previews)

Every projectile attack in `BossPatternTables` / `BossCatalog` fires from its own stream muzzles (curtains, rain and lattices now deploy from them too). `BossWaveTest.TimedOrigins` asserts each shot's start is one of the attack's muzzles in the drawing on screen and the drawing is the attack's own tell / fire pose. Not re-mapped (a design call, flagged): multi-stream slots that mix parts whose drawings belong to different poses, shown in the slot's single pose -- Frost "shatter" (pose 0 jaw + eye shards), "frozen crown" (pose 2 crown + jaw fan); Verdant "thorn storm" (pose 0 stinger + petal seeds / fans); Ember "slag storm" (pose 1 furnace + jaw fan); Tide "riptide" / "maelstrom" (pose 0 beak + arm cannons); Storm "thunderhead" / "cage storm" (rods / core + coils); Desert "buried mines", "dune-eater's maw" (boom / maw + lens). Every shot still leaves a real muzzle on the boss.

## The gap guarantee

For each wave the generator knows the game-clock time it reaches the ship's row (`arrival`) and where the previous wave's gap was. The ship can be anywhere inside that gap while it passes, so the new gap centre may move at most

`room(prev gap) + room(new gap) + 4 u/s * (arrival - early - prevArrival - thickness - 0.14 s)`

(`PlanSpeed` 4 u/s, a quarter below the 5 u/s the flood-fill solver uses). `early` (`WaveFront`) is how much sooner the wave meets any point of the ship's path from the old gap to the new one than it meets the gap: a ring from a muzzle off to one side reaches the row under that muzzle first; a tilted row reaches one side first. `thickness` covers the tilt across the gap and every row of a thick wave. A wave after a quiet spell is judged from the ship's own position instead. All of it is **game time**, so a pause or slow motion that squeezes two waves together shrinks the allowed shift to zero; a pause cannot trap the ship. Waves closer than 0.15 game seconds are dropped. A plain accent fan is a gap-chained fan wave too, and an accent takes its sixteenth's place (one firing, one drawing per sixteenth).

## Telegraphs

The gap lane is marked (the world's own cue colour, pooled markers) for waves below high intensity and for every fourth wave above it, lasting the wave's flight. On the beat before each wave the boss tells it (the source part's tell drawings, charge glow on its muzzles). No pink outside Space: bullet and marker art come from the boss/world as before.

## Beat lock

* A wave leaves the boss on a sixteenth (`LastScheduledBeat * 4` is an integer).
* The beat clock reads `AudioSettings.dspTime`, which moves in **audio-buffer steps** (20..45 ms on phones) -- longer than a frame at 120 Hz (8 ms). A sixteenth is therefore often first seen more than one frame late. It is still played if it is at most `BossMusicDirector.AudioStepSeconds` (85 ms) late, with its flight shortened by the lateness so it still lands on the beat. (It used to be dropped if it was more than one frame late: see Root causes.) Only the current sixteenth is ever played, so there is no backlog after a pause.
* The wave reaches the ship's row exactly `k` beats after it left (rows: by the spray time; rings / fans: by their speed).
* Bullets fly in game time, so slow motion shifts the arrival (not the spawn).

## Root causes (October 2026)

1. **Bullets from outside the scene.** `BossWaveVoice.Row` (walls, slants, the Tempest lattice accent) spawned every bullet on `host.SpawnTop` = `CameraFit.ViewTop + 0.15`, i.e. on a line just ABOVE the top of the screen, across the full width (slants even further out sideways: x up to -/+(rail + drift)). The legacy runner did the same for curtains, rain and lattices, and placed mines straight onto their spots. Only rings and fans used a muzzle, and only the first attack's single part.
2. **"The boss tells the attack on the beat, but no attack comes."** `BossMusicDirector.Step` dropped any sixteenth it first saw more than one rendered frame after the grid line (`allowedLate = realDt`), and it consumed the sixteenth while doing so. The beat clock moves in audio-buffer steps, so on a phone (and at 120 Hz, where a frame is 8 ms) most sixteenths are first seen late and were silently skipped -- the waves (and the accents) never fired, while the boss kept holding its attack pose. The headless tests never saw it because their clock advanced smoothly every frame. `BossWaveTest` now plays every boss live at 120 fps with the beat clock in 42 ms steps and requires every telegraph to be answered by bullets within two beats.

## Validation

`BossWaveTest`:

* plays every boss song in full plus a loop seam through the real `BossMusicDirector` + `PatternSim` at an uneven 30/60 fps, with the boss's **real muzzles** (its idle drawing, swaying like the live boss), in the authored 10 u view and in a tall 21:9 phone view (boss high, shots scaled); flood-fills the ship's reachable set (5 u/s, 0.04 u margin) every frame: never empty; every shot starts **at a muzzle** (0.000 u off) and inside the view; flashes only on wave parts; sixteenth grid, whole-beat landings, gap floor, pool peak, us / frame and bytes / frame;
* a stress variant with repeated pauses and 0.25x slow motion;
* **live** (a real `BossActor` + `BossProjectilePool`, every boss, 60 fps and 120 fps with a 42 ms audio clock): every tell is followed by bullets within two beats; every shot leaves a muzzle of the part that fired, in the drawing on screen; none outside the view (and the `BossActor.OffscreenSpawns` guard stays 0); none culled straight away; the fire drawing shows as the bullets leave, the source's tell before, idle between, the Drake's breath held;
* the Cinder Drake's cells (fire cells 28 / 31 / 34, cannons either side of the head in every cell, brow on its gem in cell 28, maw on the flame root, cannon bores in cells 28 / 31);
* every boss's timed projectile attacks: every shot from the attack's own muzzles in its own pose.

`BossActor` counts and pulls back (and warns once about) any shot that would start above the view or outside its sides.

This is a machine proof that a path exists and that shots start on the boss; it is not a dodgeability or feel playtest.
