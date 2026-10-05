# Enemy behaviours

Design for giving every roster enemy (`EnemyRoster.cs`, 46 entries over four worlds) its own behaviour,
written after reading the roster, viewing every strip under `Art/Resources/Enemies` and the rail-mine
atlas, and reading the spawner, the movers, the flipbook, the chaser, the elites and their shots.

Branch `feature/enemy-intelligence`. This document is committed before the implementation so it can be
reviewed; the "Progress / next steps" section at the bottom is updated with every commit.

## What exists today (before this branch)

| Role | Mover | What it did |
|---|---|---|
| Rock | `moveEnimes` + `AsteroidSpin` | Scroll + a 1 u/s ping-pong weave on the right half of the lane (or a fixed x on the left), tumble or sway. Every rock in every world the same. |
| Alien | `moveEnimes` | Same weave; lines of 1-4. |
| Big, Fighter | `moveItemEnmInStrightLine` | Straight down with the board. The tell cells played on a random timer and meant nothing. |
| Mine | `RailMineMount` on an invisible rail lane | Sat still on its rail; armed (cells 4-5) when the ship was near. |
| Chaser | `ChaserEnemy` | Climbs from below, hunts 3.5 s, then orbits. Same in every world. |

No roster enemy fired anything. Only elites (`EliteShots`) and bosses (`BossProjectiles`) shoot.

Rules that already hold and must keep holding:

* **Pause.** The board runs only while `!playerDied && (TouchInput.IsPressed || pauseCounter <= 0)`;
  otherwise `Time.timeScale` is 0. Every mover gates on that rule and elite shots are stepped only on
  running frames (`EliteDirector.Update` -> `EliteSystem.Step`).
* **No stacking.** `SpawnSpace` places every spawn so its declared sweep (`IMovementFootprint`) never
  meets another's; `SpawnLane` keeps a ship-width gap in every row; chasers self-steer (`ResolveSteer`).
* **Other systems drive the movers by hand**: the death domino (`DeathCrashDomino.StepWorld`), the stun /
  black hole / magnet (`HazardMovers.Disable`), the headless spawn simulation (`SpawnSpaceTest`).
* **Hostile shots** are `IHostileShot`s in `HostileShots` (shot vs shot, player weapons shoot them down),
  wear `HostileGlow`, and are never the player's red (`HostileGlow.IsPlayerRed`).

## Architecture

**Data, not classes.** One `EnemyBehaviour` record per roster key in a single table
(`Scripts/Gameplay/Enemies/EnemyBehaviours.cs`, reached as `EnemyDef.Behaviour`). A record is a choice of
primitives and their numbers. One component, `EnemyBrain`, runs any record.

**The existing mover stays the scroll authority.** The brain adds a *bounded offset in board space* on top
of it (and, for a mine, an offset along its rail through `RailMineMount`). So the death domino, the stun
and the old tests keep working without knowing about brains, and a stunned enemy's brain stops with its
mover.

**Envelope.** Each record declares the box its offset can ever reach: `bandX` either side, `rise` up the
board, `dive` down it. The mover's `SweptBounds` is widened by that box, and the spawner places a new
enemy with the same box, so `SpawnSpace` keeps whole envelopes apart at spawn. Enemies therefore never
stack and never need per-frame neighbour checks; the lateral band is also clamped inside the rails
(`SpawnLane.LaneHalf`). Chasers stay self-steering, as before.

**Movement primitives** (composable: one lateral, one vertical):

| Lateral | | Vertical | |
|---|---|---|---|
| `None` | holds its column | `None` | rides the board |
| `Drift` | constant slow speed, bounces at the band edges | `Bob` | slow sine heave |
| `Glide` | one slanted straight line, no bounce | `Pulse` | kicks up the board, sinks back (jelly, puff) |
| `Sway` | sine weave | `Brake` | eases up the board on entering the view (a hover), gives it back on a dive |
| `Orbit` | a small circle (with the vertical) | `Sink` | creeps down the board (heavy) |
| `Track` | moves toward the pilot's x at a capped speed | `Patrol` | mine: slides up and down its rail |
| `March` | discrete sideways hops, reversing at the edges | `Creep` | mine: slides down its rail and stops |

**Attack primitives** (at most one per enemy):

| Attack | What happens |
|---|---|
| `None` | never fires; `EnemyBrain.ShotsFired` stays 0 (tested) |
| `Lunge` | the body dashes: sideways toward the pilot's x inside its band and/or down its `dive`. No projectile. |
| `Shot` | `count` projectiles in a fan of `spread` degrees around the aim |
| `Ring` | `count` projectiles all the way round |
| `Cross` | mine: fires across the lane from its rail; the shot rides the board so it stays level with the mine's row |
| `Lob` | one glob lobbed onto the pilot's position at tell time; lands as a pool for a few seconds |

**The attack state machine** is `Idle -> Windup -> Release -> Recover -> Idle`, advanced only by the dt
the brain is stepped with (never `Time.time`), so it cannot run or fire while the world is frozen.

* **Windup** is the telegraph: the flipbook is *driven* to tell cell 4 and held there (a mine loops
  4-5, waking -> charging, as it always has when arming), and a charge light in the world's shot colour
  grows at the muzzle. Never shorter than `EnemyBrain.TellFloorSeconds` (0.45 s).
* **Aim is locked when the windup starts** and limited to a cone (`aimCone`) around straight down. There
  is no lead. Moving after the tell begins always dodges an aimed shot: readable, not an aimbot.
* **Release** shows tell cell 5 (the strips draw the discharge there) and fires.
* An enemy only starts a windup while it is inside the view, above the pilot by at least
  `MinFireAbove` and further than `MinFireDistance` away. Shots are never spawned from off screen.
* Enemies that do not attack keep the cosmetic tells they had (rock glints, the near-pilot chomp).

**Shots reuse the elite pool** (`EliteSystem.Shots`): pooled, glow-wrapped, registered with
`HostileShots`, absorbed by a shield, erased by a blink, shot down by player weapons, cleared by the death
domino, frozen by a pause, all through the existing paths. Differences for a roster enemy's shot (a small
addition to `EliteShot`): it never hurts other hazards (no friendly fire: a line of aliens must not shoot
each other), and it can ride the board. Shot speed is *relative to the board*, so a shot always leaves
its shooter the same way whatever the scroll speed.

Per-world shot colours (all pass `HostileGlow.IsPlayerRed == false`): Space magenta, Frost cyan, Verdant
lime, Ember amber (not the sodium orange: its hue sits on the edge of the red band).

**Threat budget** (`EnemyThreat`): at most `MaxEnemyShots` roster shots alive, at least `VolleyGap`
between two enemies' volleys, and the spawner counts live shots as fractional threats (see Density).

**60 fps.** No per-frame allocation, `GetComponent` or `Find`: the brain caches its mover, flipbook and
renderer at `Init`; the pilot's transform is looked up at most twice a second (`EliteSystem.Player`);
shots are pooled; there are no neighbour scans at all.

## The table

Tell = how cells 4-5 are used. "cosmetic" = the tell is flavour only (no attack follows).
Shot speeds are relative to the board, in world units per second.

### Space (shots: magenta)

| Key | Name | What it is (art) | Movement | Shoots | Tell | Counter |
|---|---|---|---|---|---|---|
| `space_rock_crater` | Beacon Rock | floating regolith chunk, blinking survey beacon | upright; slow heave (`Bob`) and a lazy sway | no | beacon flares (cosmetic) | steady and predictable: go round |
| `space_rock_cluster` | Cluster Rock | three strapped boulders, magenta seam | heavy slow tumble; slow `Drift` bouncing in a narrow band | no | seam flares (cosmetic) | wide: give it room |
| `space_rock_dark` | Coal Rock | dark slanted wedge, one buried eye | quick tumble; one slanted `Glide`, never turns | no | eye blinks (cosmetic) | read the slant early, it never changes |
| `space_mine` | Rail Mine | clamp + sphere on the rail | `Patrol`: slides up and down its rail; freezes while armed | no | arming loop when the ship is near (as before) | hug the middle lanes; time the slide |
| `space_big` | Bastion | octagonal pod, twin cannon prongs, reactor eye | holds its column; short `Brake` hover | **yes**: twin parallel bolts straight down | eye flares for 0.8 s, then fires | leave the column under it when the eye flares |
| `space_fighter_1` | Needle | slim dart, one ram prong | fast narrow `Sway` | no (`Lunge`: straight dash down) | thrusters flare, then the dash | it only goes straight: step off its line |
| `space_fighter_2` | Steel Claw | twin pinching claws | `Track`s the pilot slowly, `Brake`s | no (`Lunge`: pounces sideways-and-down at the pilot's x) | claws pinch, then snap | sidestep once the claws pinch; it commits |
| `space_fighter_3` | Twin Claw | wide saw-armed crab | wide slow `Sway` ("flies wider than it looks") | **yes**: two splayed bolts | flare, then fires | stand in the gap between the two bolts |
| `space_fighter_4` | Warden | heavy hull, big side cannon | `Brake` hover, slow `Track` | **yes**: one heavy shell, aimed (locked at tell) | cannon charges 0.9 s | move after the charge starts; the shell flies where you were |
| `space_chaser` | Steel Hound | interceptor with jaws | chaser: steady pursuit, then orbits (as before) | no | lunge loop while hunting | keep sliding sideways |
| `space_alien` | Bile Mite | crowned bug, one slit eye | lockstep wiggle (`Sway`) in lines | **some** (about 2 in 5 are spitters): one slow bile shard straight down, once | eye glows, then spits | the gap in the line; spit only goes straight down |

### Frost (shots: cyan)

| Key | Name | What it is (art) | Movement | Shoots | Tell | Counter |
|---|---|---|---|---|---|---|
| `frost_rock_shard` | Ice Shard | crystal splinters round a core | tumble; light, skittish `Drift` bouncing across a wide band | no | core flashes (cosmetic) | watch the bounce |
| `frost_rock_chunk` | Frozen Chunk | floating bedrock, snow cap, icicles | upright; heavy slow heave (`Bob`), no sideways | no | crystal glints (cosmetic) | it stays in its column |
| `frost_rock_rime` | Rime Star | six-point ice star | even snowflake spin; slow `Orbit` circle | no | glint (cosmetic) | the circle is small and regular |
| `frost_mine` | Geode Mine | crystal sphere on the rail | `Creep`s down its rail and stops | **yes**: an ice shard across the lane (`Cross`) | charging loop 0.9 s | be above or below its row when the crystals grow |
| `frost_big` | Glacier Golem | iceberg hulk, visor, ice maw | holds; slow small `Sway` | **yes**: a fan of three frost shards downward | maw cracks open 0.9 s | get outside the fan or tight between two shards |
| `frost_fighter_1` | Flake | orb drone with an icicle lance | snowflake `Sway` drift | no (`Lunge`: lance dash at the pilot's x) | lance swirls, then the dash | it drifts until it decides: move when it does |
| `frost_fighter_2` | Icicle | four blades, long lance | `Track` | **yes**: one aimed lance bolt | blades pinch 0.55 s | sidestep after the pinch |
| `frost_fighter_3` | Frost Kite | six-blade star, fast | quick `Orbit` loops | **yes**: two shards, splayed | flare | between the pair |
| `frost_fighter_4` | Hailstorm | armoured collar, double lance | `Brake` hover | **yes**: a wide downward hail of five slow shards | big flare 1.0 s | the shards are slow: pick a gap |
| `frost_chaser` | Frost Lancer | needle of ice | chaser: stops, aims, dashes in a straight line, repeats | no | fins fold on the dash | step off the line when it stops to aim |
| `frost_alien` | Cryo Jelly | crystal-dome jelly, icicle tentacles | `Pulse`: kicks up, sinks back, slight sway | no | lashes when the ship is near (cosmetic) | the pulse is regular: pass on the sink |

### Verdant (shots: lime)

| Key | Name | What it is (art) | Movement | Shoots | Tell | Counter |
|---|---|---|---|---|---|---|
| `verdant_rock_pod` | Thorn Pod | armoured seed pod, glowing seam | slow roll; small slow `Sway` | no | seam breathes (cosmetic) | nearly straight |
| `verdant_rock_spore` | Spore Rock | floating mossy rock, spore vents | upright; each puff lifts it (`Pulse`), slow `Drift` | no | vents puff (cosmetic) | it hops up, never down |
| `verdant_rock_knot` | Bramble Knot | spinning thorn ball | fast spin; rolls across a wide band (`Drift`) | no | bud glows (cosmetic) | it crosses lanes: don't sit beside it |
| `verdant_rock_vine` | Vine Rock | floating earth, dangling vines | upright; swings like a pendulum (`Sway`, bigger tilt) | no | buds light, vines whip (cosmetic) | pass at the end of a swing |
| `verdant_mine` | Burr Mine | vine-wrapped burr on the rail | `Patrol`: swings up and down its rail | **yes**, once: three thorns fanned across the lane (`Cross`) | husk splits, charging loop 1.0 s | its row is the danger: leave it |
| `verdant_big` | Bloom Maw | five-petal carnivorous bud | holds its column | **yes**: lobs a resin glob onto where the pilot is; it lands as a sticky pool | petals snap open 0.9 s; a ring marks the landing spot | leave the marked spot; the pool dries in ~2.5 s |
| `verdant_fighter_1` | Gnat | small buzzing bug | fast jittery `Sway` + `Bob` | no | buzz when near (cosmetic) | small and erratic: keep a body's width |
| `verdant_fighter_2` | Wasp | striped abdomen, stinger | `Track`, `Brake` | no (`Lunge`: a deep fast dive at the pilot's x) | wings blur, then the dive | deepest dive in the roster: sidestep late |
| `verdant_fighter_3` | Mantis | scythe arms | `Brake` hover, still ("praying") | no (`Lunge`: a sideways slash right across its band) | arms raise, then the slash | be above or below its row, not beside it |
| `verdant_fighter_4` | Hornet Queen | four wings, scythes, stinger | `Brake` hover, `Track` | **yes**: three stingers in a tight aimed fan, up to three volleys | abdomen glows 0.8 s | a tight fan: one clear step sideways |
| `verdant_chaser` | Dragonsting | four-winged dragonfly | chaser: weaving pursuit, longer and slower | no | lunge loop while hunting | its weave makes it overshoot |
| `verdant_alien` | Snap Sprout | walking flytrap | `March`: sideways hops in step, reversing at the edges | no | jaws snap when near (cosmetic) | the line shifts one hop at a time |

### Ember (shots: amber)

| Key | Name | What it is (art) | Movement | Shoots | Tell | Counter |
|---|---|---|---|---|---|---|
| `ember_rock_magma` | Magma Rock | basalt with pulsing cracks | slow tumble; slow `Drift`, tiny `Bob` ("breathing") | no | cracks flare (cosmetic) | slow |
| `ember_rock_cinder` | Cinder Chunk | blocky cinder, furnace split | steady slow spin; `Sink`s down the board (heavy) | no | split glows (cosmetic) | arrives a little sooner than it looks |
| `ember_rock_obsidian` | Obsidian Shard | tall glass blade | does not tumble (point down, small tilt); fast slanted `Glide` | no | vein flashes (cosmetic) | it slices one way only |
| `ember_rock_islet` | Lava Islet | floating slab dripping lava | upright; wide slow `Sway`, `Bob` | no | seams flare (cosmetic) | wide but slow |
| `ember_mine` | Crucible Mine | magma pot on the rail | stays put | **yes**: boils over, a slag blob that sinks slowly down the lane | charging loop 1.1 s | the slag is slow and lingers: route round it |
| `ember_big` | Magma Skull | horned skull, furnace jaw | holds its column | **yes**: jaw drops, two slag blobs angled out below it | grille brightens 1.0 s | slag lingers under it: don't follow it down |
| `ember_fighter_1` | Cinder | charred arrowhead | diagonal `Drift` | no (`Lunge`: straight dash down) | vents flare, then the dash | the dash never tracks |
| `ember_fighter_2` | Scorch | forked claw with a mortar | `Track`s to line up over the pilot | **yes**: quick bolts straight down, up to three | vents go white 0.5 s | it must be over you to hit: keep moving |
| `ember_fighter_3` | Brand | flame-finned claw | fast strafing `Drift` across a wide band | **yes**: one aimed bolt at where the pilot is | fins flare 0.5 s | "brands anything that doesn't move": move |
| `ember_fighter_4` | Pyre | horned heavy, core like a sun | `Brake` hover, slow `Track` | **yes**: a full ring of eight bolts | core swells 1.1 s | back off; the ring opens with distance |
| `ember_chaser` | Cinder Fang | salamander jaws | chaser: short hard chase, then a wide burnt-out wander | no | jaw gapes while hunting | outlast the short chase |
| `ember_alien` | Ember Imp | flame in a basalt mask | flickers: quick small `Pulse` + `Sway` | no | flares up when near (cosmetic) | small quick moves, tight envelope |

17 of the 46 shoot (one of them only some of the time); 29 never do. Six more attack with their body
(`Lunge`). Bosses are untouched. Elites are untouched apart from the small `EliteShot` addition above.

## Density (deliverable 2)

Measured with `EnemyDensityProbe` (the real spawner stepped headless, every enemy on its real mover).
**Before** the change, at moments of a stock Space run (HUD speed, and the level second that run reaches
it):

| HUD speed | level second | spawns/s | on screen (mean / peak) |
|---|---|---|---|
| 5 | 15.9 | 1.00 | 6.3 / 8 |
| 10 | 31.7 | 2.03 | 6.6 / 9 |
| 20 | 63.5 | 6.08 | 10.1 / 15 |
| 30 | 95.2 | 14.08 | 15.2 / 21 |
| 35 | 111.1 | 21.17 | 19.6 / 27 |
| 40 | 119.0 | 21.26 | 17.3 / 23 |
| 46 | 119.0 | 21.17 | 15.1 / 21 |
| whole 120 s run | | 7.44 | 10.0 |

What changed (every number is a tunable):

| Where | Tunable | Value | Effect |
|---|---|---|---|
| `EnemyDensity` | `RateAtLowSpeed` / `RateAtHighSpeed` between `LowHud` 5 and `HighHud` 35 | x0.90 -> x0.42 | every spawn timer's rate, falling with speed |
| `EnemyDensity` | `ThreatsAtLowSpeed` / `ThreatsAtHighSpeed` | 11 -> 10 | ceiling on bodies in (or 2.5 u above) the view plus weighted shots; a spawn past it is skipped |
| `EnemyThreat` | `ShotWeight` | 0.5 | a live hostile projectile counts as half a body in that ceiling |
| `EnemyThreat` | `MaxEnemyShots`, `VolleyGap` | 12, 0.4 s | roster shots alive at once; gap between two enemies' windups |
| `enmiesOnBoard.SpawnPhase` | `heavyInterval` | 4.5-6.5 s early, 4-6 s in Chaos | heavies have their own timer (they shared the rocks' 0.5-3.4 s) |
| every behaviour | its envelope | band / up / down | each enemy reserves its whole pattern, so fewer fit a stretch of board |

**After: NOT YET MEASURED.** The values above are first estimates from the before table (rate x time on
screen), chosen to land near -30% at HUD 5-10, -40% at 20, -50% and more from 30 up, about -45% over a
run. The editor could not be started to run the probe (see Progress). `EnemyDensityTest` holds the
targets (under 45% cut at HUD 5 and 10, at least 45% at 30+, 38-55% over a run, at least 4 enemies in
view early) and prints the after table; retune `EnemyDensity` against it.

## Speed (deliverable 3)

**Before.** Speed (`moveBackGround.speed`; the HUD shows x100) climbed linearly at the world's
`speedRampPerSecond` to its `maxSpeed`:

| World | HUD per second | cap | a stock run meets the boss (120 s) at | HUD 35 reached at |
|---|---|---|---|---|
| Space | 0.315 | 46 | 37.8 | 111 s |
| Frost | 0.330 | 51 | 39.6 | 106 s |
| Verdant | 0.345 | 56 | 41.4 | 101 s |
| Ember | 0.365 | 62 | 43.8 | 96 s |

Loops added +2 / +4 to the cap, KEEP FLYING up to +8, never past 72. Scroll (speed x 30 u/s), every
mover, the score multiplier and the dust trickle follow speed; spawn density follows the level clock.

**After.** The early ramp is unchanged. Past a knee at HUD 30 the ramp keeps 40% of its rate, and the caps
are lower:

| World | HUD per second below / above the knee | cap | stock run meets the boss at | HUD 35 reached at | cap reached at |
|---|---|---|---|---|---|
| Space | 0.315 / 0.126 | **38** | 33.1 | 135 s (after the boss) | 159 s |
| Frost | 0.330 / 0.132 | **40** | 33.8 | 129 s | 167 s |
| Verdant | 0.345 / 0.138 | **42** | 34.6 | 123 s | 174 s |
| Ember | 0.365 / 0.146 | **44** | 35.5 | 116 s | 178 s |

So a stock run no longer passes 35 at all before Ember's last seconds; a fast-start ship or a loop
climbs through 35 at about one HUD point every 7-8 seconds and plateaus at 38-44. Reasoning: the user
reports the wall at 35; at 35 the board crosses the view in under a second, so the answer is to arrive
there later, climb slower and stop sooner, while keeping the worlds distinct (each cap 2 apart) and
leaving something above 35 for endurance.

Tunables, all in one place each:

| Tunable | Was | Now |
|---|---|---|
| `SpeedRamp.SoftKnee` | (none) | 0.30 |
| `SpeedRamp.SoftRampScale` | (none) | 0.40 |
| `WorldManager.Worlds[].maxSpeed` | .46 / .51 / .56 / .62 | .38 / .40 / .42 / .44 |
| `LoopRules.MaxSpeedPerLoop` / `MaxSpeedBonusCap` | .02 / .04 | .01 / .02 |
| `LoopRules.EndlessSpeedPerSecond` / `EndlessSpeedCap` | .0004 / .08 | .0002 / .04 |
| `LoopRules.AbsoluteMaxSpeed` | .72 | .50 |

Thresholds keyed to speed, checked one by one:

| Threshold | Was | Now | Why |
|---|---|---|---|
| `ScoreRules.SpeedTierHud` (x1.25 / x1.5 / x2 / x2.5) | 20 / 35 / 50 / 65 | 20 / 30 / 40 / 46 | 50 and 65 became unreachable; each tier is reachable where it was (x1.5 late in a stock level, x2 near a later world's cap, x2.5 only on a loop / KEEP FLYING) |
| World distance (`WorldManager.WorldDistanceFor`) | linear ramp | follows the new curve | a stock level is still exactly 120 s; no boss, portal or world moved |
| Boss fight speed (`BossConfig.FightSpeed`) | 20 | unchanged | below every cap |
| Resume slow-motion (`ResumeSlowMo.MinHudSpeed`) | 15 | unchanged | below every cap |
| Ship start speeds (`ShipStartSpeed`) | 0-30 | unchanged | the fastest (30) starts at the knee, under the lowest cap (38) |
| Loop arrival speed | 4 / 8 / 12 | unchanged | below the knee |
| Top Speed leaderboard (`docs/leaderboards.md`) | best-ever value | unchanged code | see note |
| `achievement_speedster` | never triggered by code | unchanged | not keyed to a speed in code |
| `score.topSpeed` (dust trickle, scene value 0.6) | 0.6 | unchanged | under 1% of income; left so the economy does not move |

Note for the user: scores already on the **Top Speed** board (and each device's saved best speed) were
set under the old caps, up to 62-72. Under the new caps nobody can pass 50, so old entries cannot be
beaten. That is a product decision (reset the board, or accept it), not something this branch changes.

## Shield scoring (deliverable 4)

A hostile projectile absorbed by the **blue-atom shield** pays `ScoreRules.ShieldedShot` = **5**.

* Which projectiles: every hostile shot that has a hitbox -- roster enemies' shots, elite shots (and a
  landed resin pool), boss shots. Not lasers and not a boss's body (they never paid; that stays).
* Flat: no kill-chain and no speed multiplier, and it does not start or extend a chain.
* Why 5: the same as a rock or a tier-1 fighter, the smallest kill. A boss shot used to pay 1 when
  absorbed; it now pays 5 in total like the others (its 1 is part of the 5).
* Farming guard: only the first `ScoreRules.ShieldedShotsPerShield` = **12** projectiles of each shield
  pay (60 points at most per blue atom, against 300 for a boss). A new blue atom starts the allowance
  again. Past the cap a boss shot pays its old 1, the others nothing.
* What happens on the hit: exactly what happened before. The projectile is erased, the shield shows its
  absorb flash and keeps its timer, no heart is lost.
* Only the shield: Phase Cloak, Hard Shell and post-hit invulnerability still absorb or ignore the shot
  as before and pay nothing.
* Feedback: a cyan "+5  ABSORB" popup at the hit (`RunScore.Source.Shield`, `ScoreHud.StyleFor`), on top
  of the shield's existing absorb flash. The points are counted with the kills on the death panel
  (`Breakdown.shieldedShots` holds the count).
* The death domino still clears shots unscored (the run is over; no shield).

## Art gaps

Listed as found; none of these blocks the feature.

* No roster enemy has its own projectile sprite. All use the elites' code-drawn shot sprites
  (`EliteFxArt` bolt / shard / slag / pool) tinted to the world.
* No muzzle points are authored for roster strips; shots leave from a per-enemy offset below the
  sprite centre.

## Progress / next steps

* [x] Study (roster, art, spawner, movers, elites, pause, domino)
* [x] Baseline density measured (`EnemyDensityProbe`)
* [x] This design
* [x] Primitives: `EnemyBehaviours`, `EnemyBrain`, envelope hooks in the movers, driven flipbook
* [x] Shots through the elite pool (`EnemyVolley`), threat budget (`EnemyThreat`)
* [x] All 46 behaviours wired, four chaser styles, mine rail slides
* [x] Density tunables (`EnemyDensity`, heavies' own interval)
* [x] Speed curve + thresholds
* [x] Shield scoring
* [x] Tests written: `EnemyBehaviourTest`, `EnemyDensityTest`, `DifficultyRetuneTest`; existing
      expectations updated in `SpawnSpaceTest`, `DifficultyRebalanceTest`, `LoopTest`, `ScoringTest`
* [x] Both assemblies compile clean with Unity's Roslyn (run outside the editor)
* [ ] **Run the suites in the editor.** Blocked: Unity batch mode could not get a licence on this
      machine (a stale `Unity.Licensing.Client` holds the channel; every editor launch loops on
      "connection with the Unity Licensing Client has been lost"). Nothing in this branch has been
      executed yet apart from the baseline probe.
* [ ] Measure the after table and retune `EnemyDensity` to the targets
* [ ] Fix whatever the first real test run turns up
* [ ] Play it
