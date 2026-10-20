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
* **No stacking at spawn.** `SpawnSpace` places every spawn so its declared sweep (`IMovementFootprint`) never
  meets another's; `SpawnLane` keeps a ship-width gap in every row. After that, free movers (pilots, chasers) keep off
  each other only softly (`SpawnSpace.SoftDrift`, "Soft avoidance" below); what still meets is friendly fire.
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
| `Cross` | fires across the lane from its rail; the shot rides the board so it stays level with the mine's row (no longer used by the mines) |
| `Laser` | rail mine: a laser across the whole lane, rail face to rail face, from the mine's row at a random angle (within +/-35 deg) each shot (`RailMineLaser`; see "Rail-mine laser" below) |
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
addition to `EliteShot`): it hurts other hazards like any hostile shot (FRIENDLY FIRE: a line of aliens can shoot each other,
unpaid, through `FriendlyFire.HostileHit`; never its own shooter, and a fresh formation is spared for the first
`RosterShotGraceSeconds` = 1 s each enemy has been on the board), and it can ride the board. Shot speed is *relative to the board*, so a shot always leaves
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

### Space (shots: magenta-pink, the elite shot colour)

| Key | Name | What it is (art) | Movement | Shoots | Tell | Counter |
|---|---|---|---|---|---|---|
| `space_rock_crater` | Beacon Rock | floating regolith chunk, blinking survey beacon | upright; slow heave (`Bob`) and a lazy sway | no | beacon flares (cosmetic) | steady and predictable: go round |
| `space_rock_cluster` | Cluster Rock | three strapped boulders, magenta seam | heavy slow tumble; slow `Drift` bouncing in a narrow band | no | seam flares (cosmetic) | wide: give it room |
| `space_rock_dark` | Coal Rock | dark slanted wedge, one buried eye | quick tumble; one slanted `Glide`, never turns | no | eye blinks (cosmetic) | read the slant early, it never changes |
| `space_mine` | Rail Mine | clamp + sphere on the rail | `Patrol`: slides up and down its rail; freezes while armed | **yes**: two lasers across the lane at random angles (`Laser`), fired from its hold row beside the ship (see "Rail-mine ride") | arming loop 0.9 s, aim line its last 0.7 s | be off the line when it blinks |
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
| `frost_mine` | Geode Mine | crystal sphere on the rail | `Creep`s down its rail and stops | **yes**: two lasers across the lane at random angles (`Laser`), from its hold row ("Rail-mine ride") | charging loop 0.9 s, aim line its last 0.7 s | be off the aim line when the crystals grow |
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
| `verdant_mine` | Burr Mine | vine-wrapped burr on the rail | `Patrol`: swings up and down its rail | **yes**: two lasers across the lane at random angles (`Laser`), from its hold row ("Rail-mine ride") | husk splits, charging loop 1.0 s, aim line its last 0.7 s | its aim line is the danger: leave it |
| `verdant_big` | Bloom Maw | five-petal carnivorous bud | holds its column | **yes**: lobs a resin glob onto where the pilot is; it lands as a sticky pool | petals snap open 0.9 s; a ring marks the landing spot | leave the marked spot; the pool dries in ~2.5 s |
| `verdant_fighter_1` | Gnat | small buzzing bug | fast jittery `Sway` + `Bob` | no | buzz when near (cosmetic) | small and erratic: keep a body's width |
| `verdant_fighter_2` | Wasp | striped abdomen, stinger | `Track`, `Brake` | no (`Lunge`: a deep fast dive at the pilot's x) | wings blur, then the dive | deepest dive in the roster: sidestep late |
| `verdant_fighter_3` | Mantis | scythe arms | `Brake` hover, still ("praying") | no (`Lunge`: a sideways slash right across its band) | arms raise, then the slash | be above or below its row, not beside it |
| `verdant_fighter_4` | Hornet Queen | four wings, scythes, stinger | `Brake` hover, `Track` | **yes**: three stingers in a tight aimed fan, up to three volleys | abdomen glows 0.8 s | a tight fan: one clear step sideways |
| `verdant_chaser` | Dragonsting | four-winged dragonfly | chaser: weaving pursuit, longer and slower | no | lunge loop while hunting | its weave makes it overshoot |
| `verdant_alien` | Snap Sprout | winged flytrap | `March`: sideways hops in step, reversing at the edges | no | jaws snap when near (cosmetic) | the line shifts one hop at a time |

### Ember (shots: amber)

| Key | Name | What it is (art) | Movement | Shoots | Tell | Counter |
|---|---|---|---|---|---|---|
| `ember_rock_magma` | Magma Rock | basalt with pulsing cracks | slow tumble; slow `Drift`, tiny `Bob` ("breathing") | no | cracks flare (cosmetic) | slow |
| `ember_rock_cinder` | Cinder Chunk | blocky cinder, furnace split | steady slow spin; `Sink`s down the board (heavy) | no | split glows (cosmetic) | arrives a little sooner than it looks |
| `ember_rock_obsidian` | Obsidian Shard | tall glass blade | barely turns; fast slanted `Glide` | no | vein flashes (cosmetic) | it slices one way only |
| `ember_rock_islet` | Lava Islet | floating slab dripping lava | upright; wide slow `Sway`, `Bob` | no | seams flare (cosmetic) | wide but slow |
| `ember_mine` | Crucible Mine | magma pot on the rail | stays put | **yes**: boils over, two lasers across the lane at random angles (`Laser`), from its hold row ("Rail-mine ride") | charging loop 1.1 s, aim line its last 0.7 s | it burns twice, each at its own angle: be off the line when it blinks |
| `ember_big` | Magma Skull | horned skull, furnace jaw | holds its column | **yes**: jaw drops, two slag blobs angled out below it | grille brightens 1.0 s | slag lingers under it: don't follow it down |
| `ember_fighter_1` | Cinder | charred arrowhead | diagonal `Drift` | no (`Lunge`: straight dash down) | vents flare, then the dash | the dash never tracks |
| `ember_fighter_2` | Scorch | forked claw with a mortar | `Track`s to line up over the pilot | **yes**: quick bolts straight down, up to three | vents go white 0.5 s | it must be over you to hit: keep moving |
| `ember_fighter_3` | Brand | flame-finned claw | fast strafing `Drift` across a wide band | **yes**: one aimed bolt at where the pilot is | fins flare 0.5 s | "brands anything that doesn't move": move |
| `ember_fighter_4` | Pyre | horned heavy, core like a sun | `Brake` hover, slow `Track` | **yes**: a full ring of eight bolts | core swells 1.1 s | back off; the ring opens with distance |
| `ember_chaser` | Cinder Fang | salamander jaws | chaser: short hard chase, then a wide burnt-out wander | no | jaw gapes while hunting | outlast the short chase |
| `ember_alien` | Ember Imp | flame in a basalt mask | flickers: quick small `Pulse` + `Sway` | no | flares up when near (cosmetic) | small quick moves, tight envelope |

### Tide (shots: mint, then the world's pink band like every hostile shot)

Behind the release switch (`WorldManager.TideEnabled`); the generic shots stand in until the themed jets / waves / strikes
(`world-attacks-design.md`) are switched on. Budgets are Ember's, row for row (`roster:tide_*` pins in `AttackBudgetTest`).

| Key | Name | What it is (art) | Movement | Shoots | Tell | Counter |
|---|---|---|---|---|---|---|
| `tide_rock_brain` | Brain Coral | maze-fold coral boulder, barnacles | slow tumble, wide lazy `Drift`, tiny `Bob` | no | mint cracks (cosmetic) | wide and slow |
| `tide_rock_staghorn` | Staghorn Spire | branched reef blade | barely turns; one fast slanted `Glide` | no | polyps glow (cosmetic) | reads early |
| `tide_rock_urchin` | Spine Urchin | iron-spined ball, mint core | spins as it `Sink`s | no | core flickers (cosmetic) | arrives sooner than it looks |
| `tide_rock_islet` | Kelp Islet | floating reef slab under kelp | upright; wide slow `Sway`, `Bob` | no | drips (cosmetic) | wide but slow |
| `tide_mine` | Limpet Mine | horned sea-mine on a brass clamp (second atlas) | `Creep`s down its rail | **yes**: two lasers across the lane at random angles | eye flares 1.0 s (waking -> charging) | be off the line when it blinks |
| `tide_big` | Nautilus Bulwark | spiral shell hulk, mint maw | holds its column | **yes**: shell opens, a fan of three pearls | maw lights 1.0 s | stand between the shots |
| `tide_fighter_1` | Remora | suckerfish drone | diagonal `Drift` | no (`Lunge`: straight dash) | eye flares, then the dash | the dash never tracks |
| `tide_fighter_2` | Needlefish | beaked skiff | `Track`s to line up | **yes**: quick bolts straight down, up to four | beak glows 0.5 s | keep moving |
| `tide_fighter_3` | Lantern Angler | anglerfish gunship | slow `Drift`, `Brake` hover | **yes**: one aimed bolt | the lure flares 0.8 s | move after the lamp lights |
| `tide_fighter_4` | Hammerhead | twin-cannon heavy | `Brake` hover, slow `Track` | **yes**: a full ring of eight | gills swell 1.2 s | back off; the ring opens with distance |
| `tide_chaser` | Wire Eel | mechanical eel | chaser, `ChaserStyle.Slither`: surges in sinusoidal lunges on an S-curve (speed x(1 +/- .85) at .9 Hz, a quarter-cycle side sway) | no | S-curve while hunting | slide sideways; it overshoots on each lunge |
| `tide_alien` | Glow Jelly | brass-collared jellyfish | `Pulse` + slow `Sway` | no | tendrils lash (cosmetic) | pass on the sink |

17 of the 46 shoot (one of them only some of the time); 29 never do. Six more attack with their body
(`Lunge`). Bosses are untouched. Elites are untouched apart from the small `EliteShot` addition above.

### Storm (shots: storm-white / arc blue tell, then the world's pink band like every hostile shot)

Behind the release switch (`WorldManager.StormEnabled`). The shots are arcs and ion bolts (the procedural skins in the pink
cue family; Storm has no attack atlas yet, so `ShotSkins` clamps it to the procedural set), Thunderhead calls lightning
down three lanes (`AttackStrike`, the Thunder style, ramp = Tide's white water: hue 205-212, sat <= .4). Gust-wave `Blast`
rings are not used by the roster (only the generic shots and the one strike). Budgets are Tide's, row for row
(`roster:storm_*` pins in `AttackBudgetTest`). The Cyclone Drone adds `ChaserStyle.Spiral`.

| Key | Name | What it is (art) | Movement | Shoots | Tell | Counter |
|---|---|---|---|---|---|---|
| `storm_rock_hail` | Hailstone | pale cratered ice ball | slow tumble, wide lazy `Drift`, tiny `Bob` | no | crack glows (cosmetic) | wide and slow |
| `storm_rock_ore` | Ore Shard | slanted crystal blade | barely turns; one fast slanted `Glide` | no | edge glows (cosmetic) | reads early |
| `storm_rock_slag` | Anvil Chunk | dark lightning-veined iron | spins as it `Sink`s | no | veins flicker (cosmetic) | arrives sooner than it looks |
| `storm_rock_islet` | Cloud Islet | floating platform + antenna | upright; wide slow `Sway`, `Bob` | no | n/a | wide but slow |
| `storm_mine` | Rod Mine | lightning-rod sea-mine (third atlas; Tide's until Codex delivers it) | `Creep`s down its rail | **yes**: two lasers across the lane | eye flares 1.0 s | be off the line when it blinks |
| `storm_big` | Cloud Whale | armoured cloud whale | holds its column, tiny `Bob` | **yes**: gills open, a fan of three arc bolts | gills light 1.0 s | stand between the shots |
| `storm_fighter_1` | Stormray Pup | small manta drone | diagonal `Drift` | no (`Lunge`: straight dash) | core flares, then the dash | the dash never tracks |
| `storm_fighter_2` | Stormray | hooked-wing manta gunship | `Track`s | **yes**: one aimed arc bolt (30 deg cone), up to three volleys | nose coil glows 0.55 s | keep moving |
| `storm_fighter_3` | Thunderhead | cannon-ring gunship | `Drift`, `Brake` hover | **yes**: lightning down three lanes (`Strike`) | charge flare + lane glyphs 0.9 s | leave the lanes when the flare builds |
| `storm_fighter_4` | Tempest Anvil | twin-coil-tower heavy | `Brake` hover, slow `Track` | **yes**: a ring of eight (chain lightning) | core swells 1.2 s | back off; the ring opens with distance |
| `storm_chaser` | Cyclone Drone | four-blade rotor drone | chaser, `ChaserStyle.Spiral`: a corkscrew about the line to the ship (1.5 u/s, .9 Hz) | no | corkscrew while hunting | slide sideways; it overshoots |
| `storm_alien` | Static Mote | caged ball lightning | `Pulse` + quick `Sway` | **yes** (35% of them): one slow ball of static | core flares 0.6 s | pass on the sink |

## Presence: hazards and pilots (follow-up)

User: "Some enemies should not rush past the player like idle objects like hazards. All hazards rushing
past the player makes sense, they are not too bright, but some enemy ships need to be smarter and do
more custom behavior."

The first pass kept every enemy on the scroll (the brain only added a bounded offset), so at HUD 30+ a
ship crossed the view in about a second and rarely finished its tell. Now every roster enemy has a
**presence**:

* **Hazard** (all 14 rocks, the 4 rail mines): rides the board and rushes past, exactly as in the table
  above. Nothing changes for them.
* **Pilot** (16 fighters, 4 heavies, 4 chasers, 4 aliens): piloted or alive. It flies under its own power
  in world space. The scroll moves the world behind it; it does not decide how long the pilot is on
  screen.

### How a pilot flies

`EnemyBrain` gets a pilot mode. The legacy mover component stays on the object (stun, the death domino
and old tests still find it) but is switched to `station`: it no longer scrolls. The brain owns an
anchor in world space and runs a script:

1. **Wait** above the view, column already reserved, until no hazard is still coming down that column.
2. **Enter** from the top (`Drop`: straight in; `Swoop`: overshoots its station and rises back). Aliens
   `Descend` instead: no station, a slow march down the screen at their own speed.
3. **Engage** on station (a depth below the top of the view) for at most `engageSeconds`. The existing
   primitives (Sway, Track, Orbit, Drift, March, Bob, Pulse) run around the anchor, and the attack state
   machine runs as before, now with time to finish. A lunge from a station is a dive and recover.
4. **Exit**, in character: `Climb` (retreats up and out), `Peel` (climbs out on a sideways arc), or `Run`
   (a telegraphed attack run straight down its column, past the pilot and out the bottom).

The top and bottom are the only legal edges: the sides are the rails.

**Airspace, and a sidestep.** A pilot reserves its column (its body at its station x plus a margin) in
`PilotAirspace` for as long as it lives. The spawner places no hazard whose pattern crosses a reserved
column (it picks from the free stretches of lane, `TryFreeX`, and where they are narrow the hazard keeps
a narrower lateral band, `EnemyBrain.BandScale`), and a pilot waits above the view, at most 4 s, until
hazards already in its column have gone by. Its lateral band is not reserved: hazards do come down
beside its column, and the pilot sidesteps back toward its column while one passes (`BandLimits`), then
uses its whole band again. Two pilots side by side share the lane between them, half each. The band's cut-back eases in and out (soft avoidance, below) instead of snapping. (The old safety net, every
pilot move through `SpawnSpace.ResolveSteer`, is only used with `SpawnSpace.Soft` off.) The long-run test (24 two-minute
runs, every frame) shows no two board-locked bodies overlapping; pilots and chasers may meet and break on each other. All of this is self-contained in the
roster brain files; if the elites' hazard sensor lands, `PilotAirspace.BandLimits` / `ColumnClear` and
that sensor answer the same question ("what is coming down beside or onto me") and could become one
query.

**Shots.** A pilot holds its place in the world, so its shots fly in world space (they no longer ride the
board) at 1.25x the table's speed (2.5 to 4.75 u/s). The telegraph rules are unchanged: tell never under
0.45 s, aim locked at the tell inside its cone, windups only on station, in view, 1.6 u above and 1.8 u
from the pilot, at most 12 roster shots alive, 0.4 s between two enemies' windups.

**Budget.** `EnemyDensity.MaxPilotLoad` caps the pilot load by speed and world: 3 at low speed falling to
2 at HUD 35 in Space and Frost, 3.5 falling to 2.5 in Verdant and Ember (a scout or an alien weighs 0.5,
a heavy or a tier-4 fighter 1.5, the rest 1). At most half the lane may be reserved
(`PilotAirspace.MaxReservedShare`), so hazards always have room. Chasers are capped separately
(`MaxChasers`: 2, 1 at speed). The spawner's threat ceiling counts every body in view each frame, and a
pilot wherever it is, so a pilot that stays 8 s is counted for 8 s. Scouts (tier 1) fly in pairs when
there is room.

**Bounded.** Every engagement has an upper bound (`engageSeconds`, 2.5 s for a scout to 9 s for a heavy),
and a pilot leaves early once its volleys are spent. Chasers leave after `lingerSeconds` of orbiting
instead of staying for ever.

**Boss and portal.** No pilot is admitted in the last seconds before a boss or while a portal is open,
and live pilots are ordered to `Climb` out. The boss's existing board clear (every `ClearTarget` hazard)
takes whatever is left, pilots included, as it always has.

**External displacement (a shove).** `EnemyBrain.Base` is the one public anchor. A hazard's brain only adds
its offset's change each frame, so writing its `transform.position` moves its `Base` and the shove stays.
A pilot notices it is not where it last put itself (`Displaced`) and flies back to its line at
`ShoveReturnSpeed` (3 u/s), never a snap; its station (`Base`) does not move unless `Base` is set.

**Escape.** A pilot that leaves pays nothing and does not touch the kill chain, the same as any enemy
that scrolled off the bottom before.

**Tutorial.** The tutorial scene does not use this spawner; nothing changes there.

### Scripts

| Key | Presence | Entry | Engage | Exit |
|---|---|---|---|---|
| all `*_rock_*`, all `*_mine` | hazard | rides the scroll | (table above) | scrolls off |
| `space_big` Bastion | pilot | slow Drop to 1.5 u | anchors its column, twin bolts x3, up to 9 s | slow Climb |
| `space_fighter_1` Needle | pilot | Swoop to 2.2 u | one weaving beat, 2.5 s | Run: straight dash down |
| `space_fighter_2` Steel Claw | pilot | Drop to 2.6 u | shadows the pilot, one pounce and recover, 5 s | Run at the pilot's column |
| `space_fighter_3` Twin Claw | pilot | Drop to 2.0 u | wide sweep, splayed pairs x3, 7 s | Peel |
| `space_fighter_4` Warden | pilot | Drop to 1.6 u | holds range, tracks, aimed shells x4, 9 s | Climb |
| `space_chaser` Steel Hound | pilot | from below (as before) | pursues, then orbits 5 s | Climb |
| `space_alien` Bile Mite | pilot | Descend 1.1 u/s in line | wiggles; spitters spit twice | hops out the bottom |
| `frost_big` Glacier Golem | pilot | slow Drop to 1.6 u | sways, shard fans x3, 9 s | slow Climb |
| `frost_fighter_1` Flake | pilot | Swoop to 2.4 u | snowflake drift, 3 s | Run at the pilot's column |
| `frost_fighter_2` Icicle | pilot | Drop to 2.4 u | tracks, aimed bolts x3, 5.5 s | Peel |
| `frost_fighter_3` Frost Kite | pilot | Drop to 2.6 u | loops, shard pairs x3, 7 s | Run |
| `frost_fighter_4` Hailstorm | pilot | Drop to 1.5 u | holds, hail x3, 9 s | Climb |
| `frost_chaser` Frost Lancer | pilot | from below | aim-and-dash pursuit, orbits 4 s | Climb |
| `frost_alien` Cryo Jelly | pilot | Descend 0.9 u/s | pulses | drifts out the bottom |
| `verdant_big` Bloom Maw | pilot | slow Drop to 1.4 u | resin lobs x3, 9 s | slow Climb |
| `verdant_fighter_1` Gnat | pilot | Swoop to 2.6 u | jitters, 3 s | Run |
| `verdant_fighter_2` Wasp | pilot | Drop to 2.4 u | tracks, one deep dive and recover, 5 s | Run (the deepest, fastest) |
| `verdant_fighter_3` Mantis | pilot | Drop to 3.0 u | hovers still, slashes x3, 6.5 s | Peel |
| `verdant_fighter_4` Hornet Queen | pilot | Drop to 1.6 u | tracks, stinger fans x4, 9 s | Climb |
| `verdant_chaser` Dragonsting | pilot | from below | weaving pursuit, orbits 6 s | Climb |
| `verdant_alien` Snap Sprout | pilot | Descend 1.0 u/s | hops sideways in step on leaf wings | hops out the bottom |
| `ember_big` Magma Skull | pilot | slow Drop to 1.5 u | slag pairs x3, 9 s | slow Climb |
| `ember_fighter_1` Cinder | pilot | Swoop to 2.4 u | diagonal drift, 2.5 s | Run |
| `ember_fighter_2` Scorch | pilot | Drop to 2.2 u | lines up over the pilot, bolts x4, 5.5 s | Peel |
| `ember_fighter_3` Brand | pilot | Drop to 2.4 u | strafes, aimed bolts x3, 7 s | Run |
| `ember_fighter_4` Pyre | pilot | Drop to 1.5 u | rings x3, 9 s | Climb |
| `ember_chaser` Cinder Fang | pilot | from below | short hard chase, wide orbit 7 s | Climb |
| `ember_alien` Ember Imp | pilot | Descend 1.3 u/s | flickers | out the bottom |

Tiers read as smarter by what they do with the time: tier 1 swoops, makes one pass and runs; tier 2
shadows the pilot and commits once; tier 3 holds a pattern and fires several volleys; tier 4 holds range
at the top, repositions and uses its whole attack. ("Retreats when hurt" needs hit points; roster
enemies die in one hit, so tier 4 retreats on the clock instead.)

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
| `EnemyDensity` | `RateAtLowSpeed` / `RateAtHighSpeed` between `LowHud` 5 and `HighHud` 35 | x0.90 -> x0.55, then x`HighSpeedSpawnCut` 0.75 at the high end (x0.4125 at HUD 35) | every spawn timer's rate, falling with speed; the cut also reaches asteroid hazards, rails and mines through the shared `Roll` |
| `EnemyDensity` | `ThreatsAtLowSpeed` / `ThreatsAtHighSpeed` | 11 -> 10 | ceiling on bodies in (or 2.5 u above) the view plus weighted shots; a spawn past it is skipped |
| `EnemyThreat` | `ShotWeight` | 0.5 | a live hostile projectile counts as half a body in that ceiling |
| `EnemyThreat` | `MaxEnemyShots`, `VolleyGap` | 12, 0.4 s | roster shots alive at once; gap between two enemies' windups |
| `enmiesOnBoard.SpawnPhase` | `heavyInterval` | 4.5-6.5 s early, 4-6 s in Chaos | heavies have their own timer (they shared the rocks' 0.5-3.4 s) |
| every behaviour | its envelope | band / up / down | each enemy reserves its whole pattern, so fewer fit a stretch of board |

**After** (same probe, same moments; "threats" = enemies in view + half a threat per live hostile shot):

| HUD speed | spawns/s before -> after | in view before -> after | shots in flight | threats after | cut |
|---|---|---|---|---|---|
| 5 | 1.00 -> 0.68 | 6.3 -> 4.7 | 1.30 | 5.3 | 16% |
| 10 | 2.03 -> 1.36 | 6.6 -> 4.6 | 0.66 | 4.9 | 25% |
| 20 | 6.08 -> 3.65 | 10.1 -> 6.1 | 0.50 | 6.4 | 37% |
| 30 | 14.08 -> 6.39 | 15.2 -> 7.0 | 0.30 | 7.1 | 53% |
| 35 | 21.17 -> 7.64 | 19.6 -> 7.1 | 0.12 | 7.1 | 64% |
| 40 | 21.26 -> 8.39 | 17.3 -> 6.8 | 0.04 | 6.8 | 61% |
| 46 | 21.17 -> 8.93 | 15.1 -> 6.3 | 0.01 | 6.3 | 58% |
| whole 120 s run | 7.44 -> 3.57 | 10.0 -> 5.3 | 0.53 | 5.6 | **44%** |

(HUD 40 and 46 are above the new caps of most worlds; they are kept in the table as the same moments
the before column was taken at.) `EnemyDensityTest` holds these as ranges and reprints the table.

Worth knowing: shots in flight fall away as speed rises. At HUD 30 and above the board crosses the view
in about a second, so a shooter rarely finishes its tell while it is still above the pilot. At speed the
danger is the bodies; the shooting matters most in the first two thirds of a level. If shooters should
stay active at speed, raise the `Brake` rises (the hover) for the hovering shooters.

### With pilots (follow-up): the table re-measured

Same probe, same moments. Pilots stay, so the columns that matter are bodies in view per frame and how
long a pilot is there. "Earlier" is the first pass's after table above.

| HUD | threats: original | earlier | now | cut vs original | pilots in view | shots in flight | a departing pilot was in view |
|---|---|---|---|---|---|---|---|
| 5 | 6.3 | 5.3 | 4.5 | 28% | 0.50 | 0.82 | 10.2 s |
| 10 | 6.6 | 4.9 | 5.2 | 21% | 0.58 | 0.91 | 10.2 s |
| 20 | 10.1 | 6.4 | 5.6 | 45% | 1.15 | 0.49 | 7.1 s |
| 30 | 15.2 | 7.1 | 6.7 | 56% | 1.44 | 0.57 | 8.7 s |
| 35 | 19.6 | 7.1 | 7.1 | 64% | 1.78 | 0.80 | 8.7 s |
| 40 | 17.3 | 6.8 | 6.7 | 62% | 1.80 | 0.49 | 7.9 s |
| 46 | 15.1 | 6.3 | 6.3 | 58% | 1.74 | 0.64 | 9.1 s |
| whole run | 10.0 | 5.6 | 5.3 | 47% | 1.09 | 0.63 | 8.5 s |

Shots in flight at HUD 30 and above went from 0.01-0.30 to 0.5-0.8: shooters now shoot at speed. At HUD
5 and 10 the pilots in view are mostly heavies and alien lines (fighters arrive with the third phase),
which is why the stay reads 10 s there. Flown one at a time (`EnemyBehaviourTest`), a pilot is in view
4.8 s (a scout) to about 12.5 s (a heavy), the same at HUD 5 as at HUD 40 (worst difference under
0.25 s).

Tunables changed for this: `EnemyDensity.RateAtLowSpeed` 0.90 -> 1.0, the new `PilotLoadAtLowSpeed` /
`PilotLoadAtHighSpeed` / `ChasersAtLowSpeed` / `ChasersAtHighSpeed`, `PilotAirspace.MaxReservedShare`
0.5, and the per-pilot script numbers in `EnemyBehaviours`.

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
| Loop arrival speed | 4 / 8 / 12 | removed: every world starts at the ship's START SPD | n/a |
| Top Speed leaderboard (`docs/leaderboards.md`) | best-ever value | unchanged code | see note |
| `speed_speedster` (was `achievement_speedster`) | never triggered by code | unchanged | not keyed to a speed in code |
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
* [x] Suites run in the editor (`AllTests.RunAll`): 87 suites, 85 pass (18,696 checks pass); the 5 failing checks are in
      `WorldBackdropTest` (3 Verdant palette checks) and `HostileProjectileTest` (the Frost wrapper
      contrast check, and a boss-shot rim check), none of them in code this branch touches
* [x] After table measured, `EnemyDensity` retuned once (high-speed rate 0.42 -> 0.55)
* [x] Follow-up: hazards and pilots (presence). Implemented, table re-measured, suites run
      (`AllTests.RunAll`: 87 suites, 85 pass; the 4 failing checks are the known Verdant palette three
      and the Frost wrapper contrast one)
* [x] `integrate/oct05-enemy-intelligence` merged in (the enemy strip re-cell); `EnemyRosterTest` passes
* [ ] Play it. Nothing here has been played: every number is from headless simulation
* [ ] Merge `integrate/oct04-batch` (the enemy strip re-cell) and re-run `EnemyRosterTest`

## Soft avoidance: enemies no longer steer hard round each other

Bug: "the jittery effect is back; it is because enemies try to avoid colliding with each other." Measured
(`EnemyAvoidanceTest`, dense boards of pilots, chasers and weaving rocks at 60 and 30 fps in three worlds; per body, the
sideways velocity changing sign by a visible step, the worst second of the run, and the RMS second difference of x):

| | OLD (`SpawnSpace.Soft = false`) | SOFT |
|---|---|---|
| two chasers hunting one ship (nothing else on the board) | 60 flips/s, 9 of 12 bodies shaking for 19.7 s, accel rms 79 u/s^2 | 14 flips/s (a chaser's own spiral near the ship), 1 body, 0.8 s, accel rms 11 |
| one chaser alone | 2 flips/s | 2 flips/s (untouched) |
| dense board (3-5 pilots, 8 rocks, 2 chasers) | 9 of 168 bodies shaking, 10.7 s, worst 50 flips/s, accel rms 31 | 1 of 157, 1.0 s, worst 18, accel rms 9 |
| elites (alone and in the crowd) | 2-5 flips/s | unchanged: their steering was never the shake |

Causes, found by tracing single frames (the old chaser pair is a pure two-frame flip-flop, x alternating 1.603 / 1.670):
`SpawnSpace.ResolveSteer` was a ladder of hard tests re-run every frame (the wish, the wish sideways, a step left, a step
right, carried, held), each an on / off envelope check, so two bodies close together picked a different rung every other
frame (`YieldBand` hysteresis was not enough: a neighbour carried out of the way and back changes the answer every frame);
`ChaserEnemy`'s early sidestep (`ThreatAhead`) switched on and off at the edge of its probe so a chaser poked at a rock;
`PilotAirspace.BandLimits` snapped a pilot's band shut and open as a hazard crossed its look window, and a pilot's
distance to the lane clamp fought the avoidance.

What replaced them (`SpawnSpace.SoftDrift` / `SoftOffset` / `Steer`, `SpawnSpace.Soft` on by default):

* one continuous rule: a body within `SoftReach` (0.25 u) of touching another is pushed sideways, smoothstep from nothing at
  the edge to `SoftSpeed` (1.2 u/s) at touching and `SoftDeepSpeed` (2.4 u/s) well inside; the sign comes from the sideways
  offset plus a tiny stable id bias (no coin toss at dead centre);
* low-passed (`SoftTau` 0.3 s) and rate-capped (`SoftAccel` 5 u/s^2), so no decision can flip faster than that; the outward
  push fades over `SoftEdgeFade` before the lane edge instead of hitting the lane clamp;
* a pilot turns the drift into a damped offset from its line (`SoftMaxOffset` 1 u, `SoftReturn` 0.4 u/s home); a chaser
  adds it to its move (`ChaserEnemy.SoftCap` 2x the speeds, it has more steering to give);
* the pilot's sidestep band closes at `BandCloseRate` 4 u/s and opens at `BandOpenRate` 1 u/s (asymmetric: chatter at the
  edge of the look window only ratchets it shut);
* a body that is FLYING AWAY (a pilot exiting -- Climb, Peel, Run -- or diving a lunge, a leaving chaser) ignores all of
  it: nothing holds it back, so it flies into whatever is in its way;
* nothing forbids a step any more: a body that meets another is a crash, and a crash is friendly fire
  (`FriendlyFire.StepCrashes`, unchanged: both break, a blast, "FRIENDLY FIRE" word with its gap or the boss every-kill
  rule, no score / dust / codex; elites and the boss are untouched -- an elite is never one of the two, the boss is `Immune`).

Stacking: SpawnSpace still places spawns apart; a pair stacked by accident is parted by the deep push and, mostly, by the
crash (the long runs: no pair stayed overlapped more than 0.8 s with the crash step running). Cost: one loop over the live
enemies per steerer per frame (<= ~40 bodies), no allocation (`EnemyAvoidanceTest` meters it). The elites'
`EliteEvasion` is a separate system and is unchanged: it plans smooth, acceleration-limited paths and measured 2-5 flips/s.
The home-screen traffic (`TitleScreenTraffic`) has its own code and does not use `SpawnSpace`.

## Elite evasion

Branch `feature/elite-evasion` (from `integrate/oct05-enemy-intelligence`). The complaint: elites lift off and are
killed at once by the board, without the pilot doing anything. Everything below is from headless simulation and
tests; none of it has been played.

### What could hurt an elite, and why they died

| Hazard | Path | Hurts an elite? |
|---|---|---|
| Any hazard body: rock, alien, fighter, heavy, mine, chaser, a lunging body | `EliteShip.Collide` -> `CrashInto` (overlap of hull x 0.85 + radius x 0.8) | yes, a heart (rocks spare an `armored` elite and a ploughing ram) |
| A side rail | `EliteShip.Collide` | yes |
| Another elite's body | `CrashInto` | yes, both |
| An elite's shot, its own after 0.35 s, a landed resin pool | `EliteShot.Step` | yes |
| Themed attack hazards (blast, strike, jet, wave, lash, log), rail-mine lasers | `AttackHazard`, `RailMineLaser` -> `FriendlyFire.HostileHit` | yes, a heart |
| A rail mine's blast | `FriendlyFire.Detonate` | yes |
| Boss shots and lasers | `FriendlyFire.HostileHit` / `HostileBeam` | yes, but no elite is in play during a boss. Boss rule: the boss is never hurt by its own attacks and takes only a sliver (`BossFriendlyFireWeight`, once a second) from an ordinary enemy's shot or laser; its shots and lasers kill minions |
| The death domino | `DeathCrashDomino` | yes (a player death; unchanged) |
| Roster enemies' shots | `EliteShot.Step` -> `FriendlyFire.HostileHit` (never the shooter) | yes, a heart |
| Parked or lifting off | no collider, `TakeHit` refuses | no |

So the new roster shots are not the problem: the bodies are. Before this branch an elite

* joined the play layer wherever the director had decided **when it parked**, 4-7 s earlier, with no look at
  what was there (`EliteDirector.JoinPoint`, `EliteShip.StepLiftOff` -> `EnterPlay`);
* only reacted to bodies, never to shots or pools, with a push-away reflex that looked 0.35-0.5 s ahead
  (`EliteShip.Avoid`, `EliteDef.lookAhead`): at HUD 20 the board moves 6 u/s, so that is a rock two or three
  units away, against a ship that needs about half a second to move one unit aside;
* shared a goal with the next elite of its kind (two interceptors both stalk the same spot) and overshot into
  the rails after a hard dodge.

`EliteSurvivalProbe` (the real spawner, every enemy on its real mover and brain, a pilot who never touches the
elites; 24 flights per elite: HUD 10 / 20 / 30 / 40, six seeds) measured, **before**:

| Elite | Died to the board in 15 s | within 5 s of joining | mean s to death | Killed by |
|---|---|---|---|---|
| Ash Wraith | 19 / 24 | 10 | 5.9 | rock 12, rail 2, fighter 2, alien 2, heavy 1 |
| Brass Vulture | 22 / 24 | 16 | 4.4 | rock 12, rail 4, heavy 4, alien 2 |
| Cauterizer | 24 / 24 | 22 | 2.1 | rock 17, rail 5, alien 1, heavy 1 |
| Coalrunner | 24 / 24 | 20 | 3.3 | rock 18, heavy 4, alien 2 |
| Kilnback | 20 / 24 | 10 | 4.8 | heavy 7, rail 5, alien 4, own shot 2, fighter 2 |
| Sunstoke | 23 / 24 | 18 | 4.4 | rock 12, rail 6, fighter 4, mine 1 |
| Rimebreaker | 22 / 24 | 14 | 4.4 | rock 11, own shot 4, alien 3, heavy 2, rail 1, fighter 1 |
| Resin Warden | 20 / 24 | 18 | 2.6 | rock 13, alien 4, heavy 2, fighter 1 |
| **All** | **174 / 192 (91%)** | **128 (67%)** | **3.9** | rock 95, rail 23, heavy 21, alien 18, fighter 10, own shot 6, mine 1 |

In the director's groups of three: 204 / 216 (94%) died, 153 (71%) within 5 s, and 31 of them on another
elite's hull, 8 on another elite's shot.

### Design

Everything is in `Scripts/Gameplay/Elites/EliteEvasion.cs` (the sensor, the planner, every tunable) and
`EliteShip` (`Navigate`, `HoldTell`, `MayCommit`, `BreakOff`, the lift-off).

* **Sense**, once a step for all elites (`EliteEvasion.Sense`, from `EliteSystem.Step`): one flat list of
  threats (position, velocity, radius, when it is live) from registries that already exist. Bodies come from
  `ClearTarget.Live` at the velocity they were *measured* at since the last step, so a weave, a brake, a lunge
  and a chaser all read true; a roster enemy's **telegraphed lunge** adds the stretch of board it is about to
  cross (`EnemyBrain.LungeAhead`); elite shots come from the shot pool, with the **marked spot** of a glob still
  in the air and landed pools; an elite's own shots count once they could hit it. Roster shots are left out.
  No physics query, no `GetComponent` or `Find` per frame (what a body is, is looked up once and kept on its
  `ClearTarget`), no allocation, at most 192 threats.
* **Plan**, per elite every `ReactionSeconds` (its reaction delay), on staggered frames. The brain still says
  where it wants to be. If the way there is clear the elite flies exactly that: its own pattern, untouched.
  If not, about eighty spots round the ship (a fine comb sideways, each a little higher and lower) are flown on
  paper: the swing of its velocity at its real acceleration, the run to the spot, then holding there, against
  every threat's straight-line path in closed form, against the rails, against the other elites and the spots
  they have chosen. A hit inside the commit window costs most; a hit read further off only makes a lane less
  attractive; then a tight squeeze; then distance from the brain's wish. So it sidesteps, climbs, drops back
  or holds, and returns to its pattern the moment that is clean.
* **Fly**: the ship steers to the chosen spot with its own arrive / accelerate / turn limits, a little sharper
  while evading and capped. It swerves; it does not teleport. Braking is never weaker than its evasive thrust
  and it never approaches a spot faster than it can stop, so it no longer overshoots into a rail.
* **Attacks keep their shape.** Measured points and patterns are untouched. A wind-up that holds its ground
  jinks up to `JinkReach` out of the way of something coming and gives the attack up (`BreakOff`, a short
  cooldown) only if that is not enough. A dash is checked before it starts and again as the wind-up ends: not
  down a blocked line and not into a rail. The line is only checked as far as the pilot and a little past, so
  **what is behind the pilot still catches a dash**, and anything that enters the line after it launches does
  too: once launched a dash is committed, as before. A blink only lands where nothing is about to arrive.
* **Line of fire**: an elite holds fire while another elite is in the line its shots would take
  (`EliteAttack.FriendlyInLine`), and a roster enemy does not start a lunge through an elite
  (`EnemyBrain.EliteInLungePath`). Both only delay the attack.
* **Lift-off**: the join point is chosen when it lifts off, not when it parked: clear of traffic and at least
  `MinJoinDistance` from where the pilot is *now*. It waits on its pad (lights still blinking) while the air it
  would join is busy, slides its join point as it rises, and hovers just under the play layer until the spot
  is clear. Every wait is capped.
* **The spawn shadow**: the spawner drops nothing new into the column an elite is flying
  (`EliteShip.SpawnShadow`, read by `SpawnSpace.Fits`). The board only guarantees one ship-wide gap per row,
  wherever it falls; an elite is wider than the pilot's hitbox and far slower sideways than a finger, so on a
  fast board it is walled in however well it reads it. The table below shows the planner without it.

**Friendly fire is not switched off and nothing is invulnerable.** A body, a rail, another elite and an elite's
shot hurt exactly as before; elites are only better at not being there. The lift-off was already out of reach
(no collider, for everyone, the pilot included) and still is; the hover is more of the same state, capped at
`LiftHoldMax`. No protection was added against anything, and the frame an elite joins the play it can be shot
(tested).

**Dodging the pilot's shots: no.** `PlayerShotAwareness` is 0, so the pilot's projectiles are not in the threat
picture at all and an elite never moves out of their way (it still moves for its own reasons). Raising it
towards 1 makes them count with that weight; that is wired (`AttackProjectile.Flying`) but **untested and
flagged**: it would make elites slippery.

### Tunables (all in `EliteEvasion`)

| Tunable | Value | Meaning |
|---|---|---|
| `Enabled` | true | off: elites fly exactly as before this branch |
| `LookAheadSeconds`, `LookAheadUnskilled` | 1.15 s, x0.7 | how far ahead it reads; a def with `avoidance` 0 reads 70% of it |
| `CommitShare` | 0.45 | the share of the look-ahead inside which a hit must be dodged now |
| `ReactionSeconds`, `ReactionUnskilled` | 0.14 s, x1.5 | seconds between two reads of the board (reaction delay) |
| `MaxEvadeAccel`, `EvadeAccelScale` | 13 u/s^2, x1.8 | acceleration while evading: x its own, capped |
| `EvadeSpeedScale`, `EvadeBoardShare`, `MaxEvadeSpeed` | x1.45, 0.45, 5.5 u/s | speed while evading: x its cruise or 45% of the scroll, capped |
| `EvadeTurnScale` | x2.5 | turn rate while evading |
| `SafetyMargin` | 0.16 u | the gap it likes to keep; tighter only when nothing roomier exists |
| `EliteSpacing`, `PilotSpacing` | 0.45 u, 0.9 u | room between two elites' chosen spots; it never sidesteps into the pilot |
| `PredictionSlack`, `WeaveSlack`, `MaxWeavePace` | 0.25 u/s, 0.7, 1.2 u/s | extra room for a body per second ahead, more for one that weaves |
| `SenseAboveView` | 4 u | how far above the view it sees hazards coming |
| `LiftClearSeconds` | 0.8 s | lift-off clearance: the join spot must be clear this long after joining |
| `LiftDelayMax`, `LiftHoldMax`, `LiftSlideSpeed` | 2 s, 1.5 s, 3.5 u/s | longest wait on the pad, longest hover, how fast the join point slides |
| `SpawnShadowSeconds`, `SpawnShadowPad`, `SpawnShadowLead` | 1.2 s, 0.12 u, 1 u | the shadow's length (x scroll), width beyond the hull, reach toward its goal; 0 s = off |
| `BreakOffCooldown`, `JinkReach`, `HoldFireSeconds` | 0.6 s, 1 u, 0.3 s | after an abandoned wind-up; how far it may jink while charging; how long a held attack waits |
| `BlinkClearSeconds` | 0.4 s | a blink lands only where nothing arrives this soon |
| `ReflexWeight` | 0.15 | the old push-away reflex, kept as a last resort |
| `PlayerShotAwareness` | 0 | 0-1: how much the pilot's shots count as threats (see above) |

### After

Same probe, same seeds (`EliteSurvivalProbe.Run` prints before, after and the ablation in one go):

| Elite | Died to the board in 15 s: before -> after | within 5 s of joining | Attacks per survivor (15 s) | Killed by (after) |
|---|---|---|---|---|
| Ash Wraith | 19 -> 1 of 24 | 10 -> 0 | 4.0 -> 3.9 | alien 1 |
| Brass Vulture | 22 -> 9 | 16 -> 0 | 3.0 -> 2.5 | rock 4, alien 3, heavy 2 |
| Cauterizer | 24 -> 3 | 22 -> 3 | (none survived) -> 2.7 | rock 2, fighter 1 |
| Coalrunner | 24 -> 7 | 20 -> 1 | (none survived) -> 2.5 | rock 2, alien 2, heavy 2, fighter 1 |
| Kilnback | 20 -> 0 | 10 -> 0 | 2.8 -> 2.5 | - |
| Sunstoke | 23 -> 13 | 18 -> 1 | 3.0 -> 3.0 | rock 8, rail 3, heavy 1, alien 1 |
| Rimebreaker | 22 -> 2 | 14 -> 2 | 2.5 -> 2.9 | alien 1, rock 1 |
| Resin Warden | 20 -> 1 | 18 -> 1 | 2.8 -> 2.8 | rock 1 |
| **All, solo** | **174 -> 36 of 192 (91% -> 19%)** | **128 -> 8 (67% -> 4%)** | 3.1 -> 2.9 | rock 18, alien 8, heavy 5, rail 3, fighter 2 |
| **All, groups of three** | **204 -> 26 of 216 (94% -> 12%)** | **153 -> 9 (71% -> 4%)** | 3.1 -> 2.1 | rock 13, alien 5, rail 2, heavy 2, fighter 2, mine 1, **elite on elite 1** (was 31 + 8 shots) |

Mean time from joining to a board death, for those that still die: 3.9 s -> 8.6 s solo, 3.6 s -> 8.4 s in groups.
By speed, solo, deaths within 5 s of joining: HUD 10 31% -> 0%, HUD 20 67% -> 13%, HUD 30 83% -> 0%, HUD 40
85% -> 4%. (These are the numbers of the last run on the final code; an earlier run of the same seeds one small
change before read 16% / 5% solo and 7% / 4% in groups, so take a few points either way as noise.)

**The pilot still kills them.** With a stand-in pilot that slides under the elite and fires straight up
(`hunted`: 96 flights), the pilot killed 75 before and 90 after (the rest died to the board first), in 1.3 s
before and 1.4 s after on average.

**What the spawn shadow is worth.** The same solo run with `SpawnShadowSeconds = 0` (planner, lift-off and the
rest all on): 123 of 192 died (64%), 63 within 5 s (33%); 8% at HUD 10, 71% at HUD 20, 83-94% at HUD 30-40.
So reading the board alone halves the early deaths and all but ends the rail, own-shot and elite-on-elite deaths,
and the shadow does the rest on a fast board. Its cost: with one elite out the board spawns as much as before
(4.1 -> 4.2 bodies/s); with three out it spawns about a fifth less (4.05 -> 3.33 bodies/s).

Caveats:

* The stand-in pilot has no body, so a dash that would have ended on the ship flies on through it into whatever
  is behind. Sunstoke and Brass Vulture, the two that dash at the pilot, have the most deaths left; some of
  that is this artefact (not separated out).
* The probe's view is the editor's (half-height 5 u). A phone's view is taller, which gives elites more warning.
* Groups attack less often than before (1.9 against 3.1 per survivor in 15 s). Before, few group members lived
  long enough to count; now they also hold fire and hold dashes for each other. Worth watching in play.

### Tests

`EliteEvasionTest` (in `AllTests`): sidesteps a shot on a collision course (and is hit with the evasion off);
gets off a glob's marked spot; does not lift off into an occupied column; clear air changes nothing about a
lift-off; never leaves the rails or the view and never hits a rail in a 20 s storm; two elites after the same spot
do not collide (and do with the evasion off); a holding wind-up jinks or breaks off; holds fire for a friendly
elite; a roster enemy does not lunge through an elite; the spawn shadow; nothing advances while paused; zero
allocations and bounded cost with 40 shots alive; every elite dies to the player a heart a hit, from the frame it
joins; a body that holds a station in the world is measured as standing still and flown round; an outside
velocity kick (the shield's shockwave) is ridden out, not cancelled in a frame; and the survival probe's before /
after as assertions (slow, skipped by `RunFast`). `EliteTest`'s "a very
fast board still catches it" became "what arrives inside its reaction time still catches it".

The allocation check uses the profiler's `GC.Alloc` recorder with a positive control (a deliberate allocation, alone
and inside the same loop, must be reported). `GC.GetAllocatedBytesForCurrentThread` reads 0 for everything under
this Unity Mono; `EliteTest`'s two older allocation checks still use it and prove nothing until they are switched.

### Progress

* [x] Study, hazard table, baseline probe (`EliteSurvivalProbe`)
* [x] Threat sensor, planner, steering limits, rails
* [x] Safe lift-off (pad wait, join point at lift-off, slide, hover)
* [x] Attack interplay (jink / break off, dash line, line of fire, safe blinks), roster lunges
* [x] Spawn shadow
* [x] Tunables in one place, tests, after table
* [ ] Play it on a phone: the feel of the swerve (`EvadeAccelScale`, `ReactionSeconds`), whether the hover reads,
      whether the spawn shadow is noticeable as a clear lane, whether groups now attack too rarely
* [ ] Decide on `PlayerShotAwareness` (0 today)
* [ ] Re-measure on the branch that has the roster "pilots" (bodies that hold a station in the world, world-space
      shots, `PilotAirspace`). The sensor measures every body's velocity instead of assuming the scroll, so a
      hovering pilot is read as standing still (tested here with a body that does not move), but nothing here was
      run against real pilots: the before / after table, the spawn shadow against `PilotAirspace`'s reserved
      columns (both reserve columns; an elite and a pilot may want the same one), and whether the pilots' shots
      should hurt elites (roster shots do not today, so elites ignore them)
* [ ] An exact predictor for weaving bodies (ask `EnemyBrain` where its pattern will be instead of extrapolating a
      straight line) would let elites thread alien lines; today they give weavers extra room instead

## Space elites

The four Space elites (`SpaceElites.cs`) are mini-boss fights, not followers: none of them closes on the pilot; each keeps its distance
and throws hazards with its own engagement pattern, every attack is telegraphed and its aim locks at the tell. They launch from the
Space backdrop's docks (stations, planets, big asteroids): in the last parked second rings in the elite's colour close in on the door
(`EliteShip.DockWarn`) while the engine lights blink. Brains marked *roams* fly paths that leave the view (`EliteBrain.Roams`): no goal
clamp, no planned evasions, no rail crash, and untouchable (no collider, not a target) while outside the board. A pause jump on any elite
deals `EliteShip.PauseDamage` (2) hearts; a shielded ram still takes them all.

| Elite (hearts) | Brain | Attack |
|---|---|---|
| Rift Lancer (3, hurtHide) | `sniper` *(roams)*: enters from a side, perches on that flank `Standoff` (4.6) over the pilot at the rail, takes 1-2 shots, leaves by its own side, waits `AwaySeconds`, re-enters from the OPPOSITE side. Within `MinRange` (3.3) of the pilot it leaves instead | `rift_snipe`: tell 0.85 s planted, a blinking sight line tracks then locks (blinks faster), then `shotCount` fast bolts down it. Every third run the first shot also sends a small pink rift mine gliding out along the line (`lobSeconds`) to the locked spot: it rides the board on a fuse ring (`hazardSeconds`) and pops into a short cross of `hazardCount` bolts; one player shot defuses it. The mine spot is ringed from the first frame of the tell |
| Orbit Reaver (3, hurtHide) | `swooper` *(roams)*: straight strafing passes on a line `Offset` (3.0) to the side of the pilot, entered beyond one side of the view and left beyond the other, then a loop round out of sight; next pass from the side it left by, new slope. Lost heart (and hid): `LostBoost` (+30%) faster per heart | `swoop_volley`: a marker ring tracks the pilot's spot for `aimCommit` of the tell, then locks (blinks faster); the claws take turns firing `shotCount` shards at that spot from wherever the pass carries it, so they cross on it. One volley a pass |
| Eventide Bastion (5, armoured) | `fortress`: `topMargin` under the top, a slow sine `laneOffset` either side of the middle | `ward_barrage`: casts alternate. Curtain: rings on the row's spots from the tell, one gap beside the pilot toward the middle, narrowing by `GapStep` (0.1) per heart lost down to `MinGap` (1.1); at <= 2 hearts a second row `RowDelay` (1.0 s, longer than a crossing needs) later with its gap on the other side of the pilot. Mine drop: the spot is ringed in the tell, a shield mine (`hazardArmour` shots to defuse) glides out of a pod, drifts down with the board and pops into a ring of `hazardCount` bolts (two mines at <= 3 hearts) |
| Singularity Hauler (5, armoured) | `wrecker`: parks `topMargin` under the top, a crawl either side of the middle | `tow_sling`: casts alternate. Tow: a sight line drops from the top of the view to the claws; a Space rock (`space_rock_cluster` x `hazardSize`) drifts in from above into the claws (`hazardSeconds`); a dotted lane is locked from the claws to the pilot (0.9 s), then the rock is flung down it at `hazardSpeed`. A real hazard (hurts on touch, shot apart by weapons, glances off the rails twice). Sling: a ringed gravity well ahead of the pilot, `shotCount` shots curve in and cross; the well holds 2 s and pulls the pilot at most `EliteWell.Cap` (0.32 u) off the finger, fading near the ring |

## Tide elites

`Pause/Assets/Scripts/Gameplay/Elites/TideElites.cs`, tests in `TideEliteTest`. Five mini-boss fights, none of which follows the pilot; every attack is
telegraphed with the shared thin drawing (`EliteMarks` rings, `EliteTelegraph` lanes, the strike glyphs; Tide's cue colours, no pink) and locked at the tell;
the hazards are Tide's own (foam, sea mines, nets, thunder, pearls). A cast that lays things down lobs each onto a ringed spot that rides the board
(`TideSpotAttack`). Non-armoured ones have 3 hearts (Riptide and Thunderfin `hurtHide`); Bathyscaphe and Pearl Warden are armoured (5). Drawn long side
about 1.55 u (1.6 u for the heavy brains `deep` / `trawl` / `oyster`; before: 1.67-2.11 u), `cellWorldSize` and `hullRadius` scaled together.

| Elite | brain / attack | dock | pattern |
| --- | --- | --- | --- |
| Riptide | `breaker` / `foam_wake` | trenchhatch | roams: rides a gentle wave line across the top third, in one side / out the other; rings on the water behind it, then foam banks are lobbed onto them (touch hurts, a shot pops one); hurt: two wakes a run |
| Bathyscaphe | `deep` / `mine_chain` | rigdeck | roams: surfaces at a column at the top, rings a chain of sea mines down to the pilot's column and lays them one by one (each bursts into a cross of bolts); dives off the top, surfaces elsewhere; +1 mine per two hearts lost |
| Trawler | `trawl` / `drag_net` | reefdock | roams: works one flank against a rail; drags a ringed row of nets in from it across the pilot's row (a gap of >= 1.5 u always left at the far end); leaves by its own side, returns on the opposite flank |
| Thunderfin | `tempest` / `thunder_sweep` | ventstack | loiters on a small ellipse high over the board; glyphs under every column at once, then the columns strike one after another, sweeping the way it flies |
| Pearl Warden | `oyster` / `pearl_spiral` | wreckbay | one of three stations at the top; two lanes mark an arc, a spiral of slow pearls sweeps it (gap left where the pilot stood at the lock); hops to another station after each cast, sweeping the other way |

Tide art wiring (same PR): hull `_lights` strips and a six-cell damaged burn loop per elite (`<key>_damaged` + `<key>_lights_damaged` companions, cells 10..15,
`EliteCells.damagedLoop`, the framework of Space/Ember); four launch-strip sites (floating carrier pad, lighthouse silo, submarine pen, wreck hull bay: docks
atlas via `Art/Worlds/Tide/backdrop_v3~/src~/build_launch_atlas.py`, `TideTuning.LaunchCellAt`, `TideDirector.BringHome`) with homes: Riptide carrierpad/trenchhatch,
Bathyscaphe subpen/rigdeck, Trawler wreckhull/reefdock, Thunderfin lighthouse/ventstack, Pearl Warden wreckhull/wreckbay. Codex's launch_v2 strips (closed, lamp,
opening x2, open, closing, mist in the opening bay) are baked whole; while a strip is in its open cells an existing ambient loop (`steam_vent` / `smoke_b`) plays over the
bay as an overlay (above the site, below the elite, faded in/out). Tests: `TideDockTest`.

The first-generation Tide ids (`surfer` / `surf_dash`, `abyss` / `depth_charge`, `netter` / `net_cast`, `stormray` / `thunder_strike`, `pearler` /
`pearl_curtain`) stay registered; Storm and Desert still use the base brains / attacks. Elite size floors are 1.5 x the flown hull (1.7 x heavy).

## Storm elites

`Pause/Assets/Scripts/Gameplay/Elites/StormElites.cs`, tests in `StormEliteTest`. Five mini-boss fights, none of which follows the pilot; every attack is
telegraphed with the shared thin drawing (`EliteMarks` rings, `EliteTelegraph` lines, the strike glyphs; Storm's cue colours, no pink, no green) and fixed at
the tell. The hazards are Storm's own (static fields, flares, lightning, tether cables, hail). Lightning is the existing `AttackStrike` column; the columns of
one cast go live at least `StormFlight.MinStrikeGap` (0.42 s) apart, so a fight stays under the weather's own limit (below 3 pulses a second, `StormTuning`),
and there is no full-screen flash. A cast that lays things down lobs each onto a ringed spot that rides the board (`StormSpotAttack`). Non-armoured ones have
3 hearts (Rod Dancer and Skyhook Harrier `hurtHide`: hurt, they flee off the screen and return); Refinery Tender and Static Bastion are armoured (5). Drawn long
side about 1.55 u (1.6 u for the heavy ones: Anvil Cloud, Refinery Tender, Static Bastion; before: 1.71-2.09 u), `cellWorldSize` and `hullRadius` scaled together.
Homes are the Storm backdrop's existing launch sites (`StormBackdrop.SiteKinds`).

| Elite | brain / attack | home | pattern |
| --- | --- | --- | --- |
| Anvil Cloud | `thunderhead` / `static_field` | hangar | drifts along the top in a slow pendulum, still while it casts; rings four static fields in a diamond ahead of the pilot, lobs a charged cloud onto each (pools that ride the board); a fifth at the middle once two hearts are lost |
| Refinery Tender | `airship` / `flare_drop` | rigbay | roams: crosses the whole top, leaves by the far side and returns from the other; rings a staircase of three fused flares down its heading, each bursts into an X of bolts (a shot defuses one); a second cast per pass at <= 3 hearts |
| Rod Dancer | `dancer` / `rod_strikes` | towerbay | weaves a figure of eight across the top; glyphs under every column for the whole tell, then the columns strike outside in, the pilot's own lane last; one more column at its last heart |
| Skyhook Harrier | `hooker` / `cable_rake` | padring | roams: swoops across the top on a dipping arc, off one side and back from the other; holds to hook in while four cables are drawn as thin lines along its heading, then drops them one after another; a second rake at its last heart |
| Static Bastion | `citadel` / `hail_wall` | hatch | crawls along the top; the gap of a hail wall is drawn as two thin lines, the gap sits a step to the middle side of where the pilot stood; hail falls in two staggered waves (three at <= 2 hearts), the gap sliding a hand with each wave |

Storm art wiring (same PR): hull `_lights` strips and a six-cell damaged burn loop per elite (`<key>_damaged` + `<key>_lights_damaged` companions, cells 10..15,
`EliteCells.damagedLoop`, the framework of Space/Ember/Tide); four launch-strip sites (floating airship dock, lightning-rod silo, skyhook cradle, refinery bay: docks
atlas via `Art/Worlds/Storm/backdrop_v3~/src~/build_launch_atlas.py`, `StormTuning.LaunchCellAt`, `StormDirector.BringHome`, pad point at pixel (384,430) of each 768 cell)
with homes: Anvil Cloud airshipdock/hangar, Refinery Tender refinerybay/rigbay, Rod Dancer rodsilo/towerbay, Skyhook Harrier skyhookcradle/padring, Static Bastion
airshipdock/hatch (the old kinds are the fallbacks). Codex's launch_v2 strips (closed, lamp, opening x2, open, closing, vapour in the opening bay) are baked whole; the
launch has no flash, flare, halo or lightning. While a strip is in its open cells Storm's existing weather wisps (`cloud_wisp`, played as a flipbook at 5 fps) play over the bay
as an overlay at alpha 0.6 (above the site, below the elite, faded in over the opening and out through the closing; no new art). Tests: `StormDockTest`.

Storm and Desert's base brains and attacks (`interceptor` / `lance_dash`, `siege` / `siege_cannon`, ...) stay registered; Desert still flies them.

## Frost elites

Branch `feat/elite-frost` (stacked on the Space elite PRs #82-#84). Like the Space four (above) the five Frost elites
(`Scripts/Gameplay/Elites/FrostElites.cs`) are mini-boss fights, not followers: none of them follows the pilot, each keeps its
distance and throws HAZARDS in Frost's own material (ice shards, slabs, icicles, frost mines, a cold fog; Frost cue colours, no
pink), every attack is telegraphed and its aim LOCKS at the tell, and each launches from its OWN dock among Frost's ground sites.
Hearts: the armoured two (Glacier Tender, Whiteout Sentinel) 5, the others 3; the three non-armoured ones `hurtHide` (a lost heart
sends them off the board for `hideSeconds` 3.5 and they come back by another edge, `EliteBrain.OnReturn`). The framework is the
Space one: `Roams` brains (the Rimebreaker and the Siren: no goal clamp, no planned evasions, untouchable outside the board),
`EliteFight` thinning, the 45 s engagement timer (escaping costs half the score), `EliteShip.PauseDamage` 2. Everything below is
from headless simulation, tests and capture footage (`EliteCapture.CaptureFrost`); none of it has been played on a phone.

| Elite (hearts) | Dock (`launchFrom`) | Brain | Attack |
|---|---|---|---|
| Rimebreaker (3, hurtHide) | `hangar` | `icebreaker` *(roams)*: icebreaker RUNS. Waits off the top, appears at the top (`ReadyPoint`) on a lane at least `MinLaneGap` (1.6) from the last, then runs; after the run (off the bottom) it waits `PauseSeconds` (1.8) and comes back on another lane. After a hit (and the hide) the runs come in PAIRS: the second `PairGap` (0.5) after the first, on the other side of the board. It never prowls: between runs it is at the top or off the view | `ice_run`: tell 1.0 s planted; from the first frame the lane is drawn down the whole board as an ice crack (the dotted lane outline plus a zigzag crack: `EliteTelegraph`), locked where it stands. Then it ploughs straight down it at `dashSpeed` (7) through the whole board and out of the bottom: rocks break (`Ploughs`, no heart lost), frost shards fly off the prow at the launch and off the first 3 rocks (`shotBounces` 1: they glance off a rail once); an enemy or a mine in the lane is a crash (a heart): lure it. Prow first (`turnsToFace`) |
| Floe Harrower (3, hurtHide) | `crawlerbay` | `barge`: holds `topMargin` (1.5) under the top on the side of the board AWAY from the pilot, a slow sine (0.3 u) round `laneOffset` (1.5), always `LaneKeep` (1.2) clear of the pilot's lane. It crosses to the other side (high, x1.7, not attacking) only when the pilot takes its side by more than `FlipBand` (0.3). Never comes down | `floe_barge`: casts alternate cast, skid, skid, cast (every other at its last heart). Cast = `floe_cast` as before (`shotCount` 4 slabs in a staggered row with one gap beside the pilot toward the middle, then a lance down the gap). Skid: tell 0.7 s; the slab's whole path is drawn from the first frame of the tell (out to the far rail, then mirrored down to the bottom, a ring on the bounce) and locked; then one big slab (`hazardArmour` 3 shots) leaves a chute at `skidSpeed` (3.2, +12% per heart lost) straight across the screen and glances off ONE rail (`EliteShot.Skid`) |
| Cryo Siren (3, hurtHide) | `rigbay` | `hopper` *(roams)*: perches on one of 4 fixed columns `keepDistance` (4.2) above the pilot (inside its reach), takes ONE bloom and HOPS: off a side or the top, `AwaySeconds` (0.8) out of sight while a trail of 4 blinking rings is drawn into the next perch (never the same twice running, never under the pilot's nose), then back in by a side or the top and settle. It also hops when the pilot comes within `MinRange` (3.3) or after `PerchMax` (5 s). It never closes | `frost_bloom`: tell 0.6 s; a slow cryo orb on a blinking fuse ring bursts after `hazardSeconds` (0.8) into a ring of `hazardCount` (8) shards; shot first it pops. Every third attack a beam sweep: the sight paints the arc (16 deg either side of the pilot), locks on its start, then a stream of fast bolts across it. At its last heart (below `DoubleBelow` 2) a bloom is TWO orbs, 11 deg either side of the aim |
| Glacier Tender (5, armoured) | `hatch` | `carrier`: hangs `topMargin` (1.3) under the top, sliding a slow sine round the middle (it does not follow the pilot); the pilot within `keepDistance` (1.9) and it scurries sideways at x1.7 | `tender_rig`: `drone_deploy` (2-3 tethered ice drones, `hazardCount` 3 at most, shield link while two live) + FROST MINES: every `MineEvery` (4.2 s, -0.45 per heart lost, at least 2.4) one drone picks the pilot's lane, a ring marks the spot on the board for 0.9 s (locked), then a small mine (`MineScale` 0.5) slides out of the drone to it, rides the board on a fuse ring and pops into 5 short shards; one player shot defuses it (3 mines at most). When the shield is down (fewer than two drones) every other attack is the FOG instead: tell 0.8 s planted, a cold cone straight down from the belly pods (`AttackJet`, the themed frost jet: 3.6 u long, 1.15 u half-wide at the far end) outlined from the first frame, then a 0.55 s burn that hurts on touch |
| Whiteout Sentinel (5, armoured) | `padring` | `patrol`: straight legs ACROSS THE TOP, edge to edge, `topMargin` (1.4) under the top, a beat's rest at each end, steady pace (60% of 1.8 u/s), never dodging (`Steadfast`), prow turned on the pilot. Never toward the pilot | `icicle_barrage`: plated = `armour_shatter` (3 ice plates soak hits and spray shards along a blinking cone). Stripped: NO charge. Tell 1.0 s planted: `Lanes` (4, +1 at 2 hearts lost, +1 at 4) lanes among `Lanes + 1` columns spread between the rails are marked with ice glyphs (3 down each lane) from the first frame, locked; the one bare column is the SAFE LANE, beside the pilot's toward the middle. Then the icicles fall out of the sky over their lanes one after another (a sweep from one end, `Gap` 0.42 s -0.05 per heart lost, at least 0.18; fall speed 5.4 +0.35 per heart), the lane's glyphs going out as its icicle drops |

Framework hooks added in the Frost rework: `EliteAttack.FireFrom` (a shot out of a point that is not a muzzle: a drone, the sky),
`EliteShot.Skid` (a slab straight across the screen, one rail bounce) and `EliteShot.Shrink` (a small mine), `EliteMarks` with
other sprites than the ring (`EliteFxArt.Icicle`), `EliteTelegraph.Show(..., world)` (the outline in the elite's own world colour),
`EliteDef.skidSpeed`, `FrostDirector.BringHome`. Hooks from the first Frost batch (`Passive`, `Absorbs`, `OnDeath`, `ShowsDamaged`,
`Shoots`, `Steadfast`, the `slab` / `orb` shots, `ClearTarget.Mother`) stay. The old `breaker` / `herder` / `kiter` / `tender` /
`ironclad` brains and `ice_ram` are gone (only Frost used them); `floe_cast`, `frost_bloom`, `drone_deploy` and `armour_shatter` stay as
the building blocks of the new ones.

### Docks

Each Frost elite has its own kind of site (above). The ground is fixed, so Frost cannot drift a dock in as Space does, but
`FrostDirector.BringHome(launchFrom)` (`LandingSites.BringHome`, asked `EliteDirector.HomeLeadSeconds` before a launch) asks the site spawner
for one of the elite's kinds at the top of the view when none is in view or on its way (`SpawnSite`); it then scrolls in at the ground's
own pace (slow: the director waits up to `HomeWaitSeconds`, and past that falls back to any free site). Whether a dock arrives in
time at real scroll speeds is unmeasured.

### Tests

`FrostEliteTest` (in `AllTests`): the roster (hearts 5 / 3, hurtHide, one dock each, no pink, strips and cell maps, muzzles / nozzles on
solid hull pixels); the director launching each from its own site kind and any free one otherwise; each one's life cycle off a pad, attack, and
death taking its slabs / orbs / drones / mines with it; the Rimebreaker's runs (the crack drawn, lane locked, ploughing straight at
`dashSpeed`, a rock broken without a heart lost, out of the bottom untouchable, back in from the top on another lane after the pause,
pairs once hurt on opposite sides); the Harrower on the far side of the pilot never in its lane, the cast kept (row, gap, lance), the
skid (path drawn and locked, speed, ONE bounce where the telegraph said, off the bottom, armour, breaking with the Harrower) and the
cast / skid / skid / cast order; the Siren's perches (new each time, off the view and back, the blink trail, never closer than its
range, hopping when crowded), one orb then two at its last heart, the beam; the Tender hanging at the top, not chasing, the mine (the
pilot's lane, ringed 0.9 s, locked, small, shootable, a ring of shards if left), the fog (shield down only, outlined first, planted,
harmless until it ignites, a short burn), everything gone with it; the Sentinel's patrol (straight legs across the top, never
toward the pilot, never dodging, a pilot parked under it changes nothing), plates, no charge, the barrage (glyphs from the first frame,
one icicle per marked lane in sequence, none in the safe lane, exactly one bare lane whatever the pilot's x with room to fly through
it, escalation per heart lost); the backdrop's `BringHome`; zero allocations (profiler meter).
Capture: `EliteCapture.CaptureFrost` + `scripts/elite-gifs.sh <frames> <out>` (a GIF and an MP4 per elite from its dock).

### Progress

* [x] Brains, attacks, defs (hearts, hurtHide, docks), tests, capture
* [ ] Codex hull lights and dock art for Frost (a later step on this branch; nothing blocks on it)
* [ ] Play it on a phone: how long a Rimebreaker pair feels, the skid's speed, the Siren's trail length, Tender mine / fog
      frequency, the barrage's lane count and the dock arriving in time at real scroll speeds

## Verdant elites

Branch `feat/elite-verdant` (stacked on `feat/elite-frost`, PR #85). Verdant had five elites that circled or tracked the pilot
(Resin Warden, Timber Hauler, Thornlash, Sporebloom, Leafblade; Codex's strips, `Art/Enemies/Elite/Verdant/new/manifest_new_elites.md`).
Like Space's and Frost's they are now mini-boss fights (`Scripts/Gameplay/Elites/VerdantElites.cs`, the rework section): none follows
the pilot, each keeps its distance and throws HAZARDS in Verdant's own material (trunks, resin, spores, vines, leaf blades; Verdant cue
colours, no pink, the shot cores are no longer pink either), every attack is telegraphed from the first frame of its tell
(`EliteTelegraph` / `AttackLash`'s own preview) and its aim LOCKS there, and each launches from its OWN pad. Hearts: the armoured Hauler
5, the other four 3 and `hurtHide` (a lost heart sends them off the board for `hideSeconds` 3.5 and they come back by another edge,
`EliteBrain.OnReturn`). Framework: `Roams` brains (Leafblade, Sporebloom, Thornlash: no goal clamp, untouchable outside the board),
`EliteFight` thinning, the 45 s engagement timer (escaping costs half the score), `EliteShip.PauseDamage` 2. Everything below is from
headless simulation and tests; none of it has been played on a phone. The new ids are `raptor` / `leaf_swoop`, `canopy` /
`canopy_mortar`, `floater` / `spore_drift`, `anchor` / `thorn_sweep`, `lumber` / `raft_roll`; the old `logger` / `thorn` / `drifter` /
`diver` / `warden` brains and `log_roll` / `vine_lash` / `spore_burst` / `leaf_dive` / `resin_mortar` stay registered as the cores the
new ones subclass (or, for `warden` / `resin_mortar`, unused: only the Warden ever used them).

| Elite (hearts) | Pad (`launchFrom`) | Brain | Attack |
|---|---|---|---|
| Leafblade (3, hurtHide) | `roothangar` | `raptor` *(roams)*: SWOOPING PASSES. It waits off the view, comes in by one of three entries (left edge, right edge, top; never the last pass's) and takes a start point just inside it (`ReadyPoint`, a hull partly off the board), tells, slices along the lane and out the far side, then waits `PauseSeconds` (1.5, -0.3 per heart lost, at least 0.7) and re-enters elsewhere. Never circles, never stops on the board between passes | `leaf_swoop`: tell 1.1 s held on the edge; from the first frame a diagonal lane (hull + 0.22 wide; 15-60 deg off the horizontal from a side, 15-50 deg off the vertical from the top) is drawn toward the pilot's spot, out to the far edge of the view, with the row of each crescent across it (where the lane crosses the pilot's height; a second `RowGap` 1.5 lower once hurt), locked. Then it slices along it at `dashSpeed` (7), committed, no steering; as the blade crosses a row a leaf crescent (`shotMotion` slash, 4.5 u/s) leaves level along the row toward the side with the most floor. After a hit the passes carry two crescents |
| Resin Warden (3, hurtHide) | `hatch` | `canopy`: holds `topMargin` (1.6) under the top on one of 4 perches (columns -0.82 .. 0.82 of the board) and HOPS to another after every mortar or `PerchMax` (4.5 s): a short visible leap (x2.6 speed, an arc `HopArc` 0.7 high, 0.45 s or more), preferring a perch at least 1 u from the pilot's column. Never comes down | `canopy_mortar`: tell 0.7 s planted; a ring on every spot of the wall (lobAhead 1.8 in front of the pilot, locked) is drawn from the first frame. The wall: resin globs `lobSpacing` (0.8) apart from both sides of ONE GAP out to both rails (pools overlap: no hole over 0.5 u), the gap beside the pilot toward the middle. Gap `GapBase` 1.8 u clear, -0.35 per heart lost (never under `GapMin` 1.1; the ship is .56 wide). Then the globs are lobbed out of the pods and land as sticky pools that ride the board and linger `poolSeconds` (4.5) + 35 % of it more for every heart lost |
| Sporebloom (3, hurtHide) | `podpad` | `floater` *(roams)*: floats at one of 3 stations high on the board (a slow sway), takes 2 bursts (or 10 s), then SINKS off its nearer side drifting down into the canopy, is out of play 1.3 s and rises out of the other side at a different station (at least 1.4 u from the last) | `spore_drift`: `spore_burst` as before (2 pods, burst rings and flight lines drawn first; six spores per pod) and, at `CloudHearts` (2) hearts or fewer, a lingering SPORE CLOUD: a lane `CloudDiam` (1.3) wide, 1.7 u beside the pilot toward the middle, outlined with the rest at the first frame (locked); three cloud pools (`EliteShot.Cloud`: a plain big pool, no spores of its own) are lobbed onto it and linger `CloudSeconds` (3) |
| Thornlash (3, hurtHide) | `towerbay` | `anchor` *(roams)*: latches onto one rail from just outside it (a hair of hull on the board), `Standoff` (3.4) above the pilot at arrival and then held; 1 + (hearts lost) sweeps from there, an attack gap (2.6) apart; then it SWINGS across the top (x2.2, not attacking) to the other rail and latches there | `thorn_sweep` (= `vine_lash`): tell 1.0 s planted; the whole swept area is drawn from the first frame and the sweep is aimed once, from the root nearer the pilot, keeping `AttackLash`'s own fair corridor (at least `MinCorridor` 1.4 u of open floor beyond the start line) |
| Timber Hauler (5, armoured) | `riverbay` | `lumber`: a lumber barge sliding a slow sine along the top (`topMargin` 1.6 under it), its course nothing to do with the pilot; attacks from anywhere over the board with the pilot well below | `raft_roll` (= `log_roll`): 2 trunks onto ringed spots, each rolling down a drawn diagonal; at 2 hearts or fewer every other cast is a RAFT: two trunks lobbed together onto the two spots either side of one gap (`RaftClear` 1.5 u of free floor between their roll lanes, beside the pilot toward the middle, both rolling the same way: `EliteShot.RollToward`), spots, lanes and gap drawn from the first frame, locked |

Framework hooks added: `EliteShot.Linger` (a pool stays longer), `EliteShot.Cloud` (a big plain pool), `EliteShot.RollToward` (a log's roll
direction), `VerdantDirector.BringHome` (as Frost's: the site spawner is asked for one of the elite's pad kinds at the top of the
view when none is in view or on its way; the pad then scrolls in at the ground's pace, so whether it arrives in time at real scroll
speeds is unmeasured), `RaptorBrain` ... `LumberBrain`, `LumberBrain` joins `EliteDef.HeavyBrains`.

### Tests

`VerdantEliteTest` (in `AllTests`): the roster (hearts 5 / 3, hurtHide, one pad each, no pink in shots / cores / hearts, the four new
strips, muzzles / nozzles on the hull, texture memory), the director's pads and deck, the codex, a minimum distance outside committed
attacks for all five over 40 s, the Leafblade's passes (lane and rows drawn at the first frame, locked, diagonal bounds, speed, out the
far side untouchable, another entry each time, one crescent, two once hurt), the Warden's perches and hops, its wall at 3 / 2 / 1 hearts
(one gap, the rest sealed, narrowing, pools lingering longer, dry in the end) and the gap with the pilot anywhere, the Sporebloom's
sink / under / rise cycle at a new station, its cloud lane outlined first and its pools, Thornlash's anchors alternating with a sweep
more per heart lost and the corridor open, the Hauler's raft (gap, same direction, side by side) alternating with rows, the backdrop's
`BringHome` for each pad, zero allocations (profiler meter). `EliteTest`'s Warden test now drives `canopy` / `canopy_mortar`;
`AttackBudgetScenarios` knows `leaf_swoop` (a ram) and draws the swoop's lane and rows and the wall's rings for the dodge bot.
Capture: `EliteCapture.CaptureVerdant` + `scripts/elite-gifs.sh <frames> <out>` (a GIF and an MP4 per elite from its pad).

### Progress

* [x] Brains, attacks, defs (hearts, hurtHide, pads), `BringHome`, tests, capture
* [ ] Codex hull lights and pad art for Verdant (a later step on this branch; nothing blocks on it)
* [ ] Authored death clips; shot art for the log / pod / crescent / vine (`verdant_attack_*.png` slots)
* [ ] Play it on a phone: pass pacing, the wall's gap width, the spore cloud lane, the raft, the pad arriving in time

## Desert elites

`Pause/Assets/Scripts/Gameplay/Elites/DesertElites.cs`, tests in `DesertEliteTest`. Six mini-boss fights, none of which follows the pilot; every attack is
telegraphed with the shared thin drawing (`EliteMarks` rings, `EliteTelegraph` lines; Desert's pale bone-gold cue colours, no pink, no green) and fixed at the
tell. The hazards are Desert's own (churned sand, quicksand, glass shards, sun-lens beams, boulders, dust). Nothing flashes the screen: a lens beam is a thin
drawn line and then a stream of thin bolts down it. A cast that lays things down lobs each onto a ringed spot that rides the board (`DesertSpotAttack`).
Non-armoured ones have 3 hearts (Dune Stalker, Lens Matriarch and Scorpion Skiff `hurtHide`: hurt, they flee off the screen and return); Caravan Hauler and
Mirror Bastion are armoured (5). Drawn long side about 1.55 u (1.6 u for the heavy ones: Caravan Hauler, Glass Cannon, Mirror Bastion; before: 1.80-2.25 u),
`cellWorldSize` and `hullRadius` scaled together. Homes: the four new launch-strip sites first (below), the old Desert sites as fallbacks (`launchFrom` lists both).

| Elite | brain / attack | home | pattern |
| --- | --- | --- | --- |
| Dune Stalker | `burrower` / `worm_churn` | canyondock (wormburrow) | roams: a rippling pass across the top, off one side and back from the other; flying on through the wind-up it rings a trail of four sand bursts leading toward the pilot, lobbed onto the rings nearest first so the churn travels like a worm surfacing (pools that ride the board); a second cast per pass at its last heart |
| Caravan Hauler | `caravan` / `quicksand_row` | crawlerhangar (caravanbay) | plods along the top in legs with a rest at each end and throws only while resting; holds, rings a row of five slots with the one beside the pilot's (toward the middle) left open, lobs a quicksand canister onto each; a second row with the gap mirrored once two hearts are lost |
| Lens Matriarch | `glint` / `glass_fan` | mirrorsilo (glasspad) | roams: a zig-zag pass across the top; halts, draws six thin rays (eight at its last heart) in a fan centred on the pilot's line with the central gap left open, then sends a glass shard down every ray |
| Glass Cannon | `lenspost` / `lens_beams` | mirrorsilo (buriedsilo) | perches on one of three columns along the top and glides to another after every cast; holds, draws four thin beam lines either side of the pilot's own (six at its last heart), then runs a stream of bolts down each, outside in |
| Scorpion Skiff | `flanker` / `boulder_roll` | buriedbunker (ruindock) | holds a flank high on the board and scuttles across to the other after every throw; holds, draws a boulder's path (out to a ringed bounce on the far rail and on down), then skids a boulder (`Kind.Slab`, soaks `hazardArmour` hits) down it; a second, higher one once it has lost a heart |
| Mirror Bastion | `bulwark` / `dust_wall` | buriedbunker (ruindock) | crawls along the top; holds, draws the edges of each wave's gap across the board (the first not at the pilot's height, each later one a hand further), then sweeps two (three at <= 3 hearts) walls of dust sideways from the rail far from the pilot |

### Docks, lights and burn loops (phase 2)

Tests in `DesertDockTest`. Art by Codex (masters in `Art/Enemies/Elite/Desert/new_docks_lights~/`, `manifest.md`), installed byte for byte.

- **Hull lights**: each elite's `<key>_lights` strip is an additive overlay over the flight cells (9 cells, aligned with the hull).
- **Damaged burn loops**: `<key>_damaged` (6 cells, every engine's flames weak and ragged on the damaged hull) and `<key>_lights_damaged` continue the strips as cells 9-14
  (`cells.damagedLoop`, shared with the other worlds); hurt, the ship plays them (`EliteShip.DamagedLoops`: no procedural plume over them).
- **Four new launch sites** in the Desert docks atlas (`Backdrop3/docks.png`, built by `build_launch_atlas.py`, all six cells of each `<site>_launch_v2.png`
  shrunk 3x): buried bunker (`buriedbunker`, `LandingKind.DesertBuriedBunker`), crawler hangar (`crawlerhangar`), mirror-array silo (`mirrorsilo`) and canyon
  dock (`canyondock`). Pad points are the manifest's pad pixels (`DesertTuning.SitePads`). `DesertDirector.BringHome` asks the spawner for one when an elite
  is due and none is in view. The old five sites stay as fallbacks.
- **Launch**: the site stays closed through the first 40 % of the tell (the ship's one small blinking launch light is the tell), the doors open through
  the rest and the lift-off, hold open until the elite is clear, then close for 0.6 s (`DesertTuning.LaunchCellAt`). No flash, flare or halo.
- **Dust**: while the strip is in its open cells Desert's own ambient `smoke_b` flipbook (`DesertAmbientCatalog`) plays over the bay at alpha 0.6, above the
  site and below the elite, fading in over the opening and out through the closing.

Desert was the last world on the base brains and attacks (`interceptor` / `lance_dash`, `hauler` / `slag_drop`, `skirmisher` / `blink_shards`, `siege` /
`siege_cannon`, `striker` / `claw_dive`, `bastion` / `ward_curtain`); they stay registered.

## Rails and view

Branch `fix/rails-vetting` (from `integrate/oct05-full-master`). The roster vetted against the reinforced rails
and the wider, taller camera (`CameraFit.GameplayHalfWidth` 3.72, approved by the user as permanent). Headless
tests and editor renders only; nothing here has been played.

### Authoritative values

| What | Value | Where it comes from | Who uses it |
|---|---|---|---|
| View half-width | 3.72 u on phones | `CameraFit.GameplayHalfWidth` | camera |
| View height | 13.2 u at 1080x1920, 17.4 u at 1080x2520 (10 u as authored) | `CameraFit.ViewTop` / `ViewBottom` | pilots, threat ceiling, spawner, probes |
| Drawn rail, inner edge | 2.606 u in all four worlds | `WorldPainter.VisibleRailEdges` | `BossRails.InnerEdge`, mine mount |
| Rail edge for gameplay | 2.606 u from the moment rails are painted | `BossRails.InnerEdge` (`WorldPainter.Apply` now calls `Measure`) | elites (`EliteSystem.RailEdge`), elite and roster shots, boss shots, death crash |
| Rail mine centre | 2.436 u | `enmiesOnBoard.WorldRailX` = `RailMineArt.MountX(inner)` = inner + `ClampBite` 0.16 - `ClampReach` 0.33 | spawner |
| Enemy lane | 2.35 u (centres stay within 2.35 minus half a body) | `SpawnLane.LaneHalf` | `EnemyBrain`, `PilotAirspace`, `SpawnSpace`, `ShieldShockwave`, `ChaserEnemy` |
| Ship's reach | 2.4 u (centre) | `movePlayer` clamp, `BossConfig.LaneHalfWidth` | player, bosses |

### Rail mines

* **Mount.** A mine's pivot is its body's centre; its clamp reaches 0.33 u toward the wall (measured in every
  atlas cell). At the old 2.35 u the clamp's face ended at 2.68 u, on the rail's lamp glow but short of its
  solid body, so the mine read as hanging beside the rail. It is now derived from the drawn rail: the clamp's
  face sits 0.16 u inside the rail's inner edge, which puts the centre at 2.436 u, 0.086 u further out, the
  same on both walls and at both phone shapes. With no rail art (a bare wall, headless tests) it stays 2.35.
* **Still a hazard.** Its 0.62 u box spans 2.13 to 2.75 u; the ship's centre reaches 2.4 u, so a ship at its
  clamp is inside the box. Chosen on purpose: a real hazard at the edge of the lane, not decoration.
* **Motion.** Position on the rail line = rail + spacing + `Slide` (the brain: Patrol, Creep) + `Shove` (the
  shockwave). x is always the rail's. Tested through slides, shoves both ways and a scrolling rail.
* **Lanes outlive their mines (fix: "sometimes rail mines disappear when you get close to them").** The spawner
  mounts each mine on the nearest live `RailMineLane` on its side (one lane per side while it lives), usually one
  spawned seconds earlier and already far down the board, so the spacing can be 10 u or more. The lane removed itself
  at y -12 whatever rode it, and a mine whose lane is gone destroys itself (`RailMineMount`, no detached mines): a
  mine mounted 10 u above an old lane vanished, without a blast, as it came down past y -2 -- the ship's rows (on
  a 21:9 phone the view reaches y -8.7, so anything mounted more than ~3.3 u above its lane could vanish on screen).
  Since `14fb831a` (Oct 4). Now `RailLaneScroller` keeps its riders (`Board`, `Riders`) and goes past `EndY` only
  when none is left; the mines leave by the Destroyer strip under the view like every hazard. `RailsVettingTest`
  "a lane lives while a mine rides it", both rails. Checked and intended, not changed: a pause jump landing within
  0.95 u erases a mine (a paid teleport kill with its burst, `TeleportFx.Erase`); a shielded or unshielded ram
  blows it up (`RamKill`); another mine's beam, a boss / elite shot, a crashing free mover or a neighbour's blast
  kills it with its blast (hostile fire: rail mines are listed targets, `FriendlyFire`); each of those shows an
  explosion.
* **Scroll (decision).** Mines ride the board's scroll like every hazard: gameplay position has to agree with
  rocks, pickups and `SpawnSpace`. The rail ART was what disagreed: it advanced one texture tile per unit of
  speed, a tile being 5.2 u (Frost, Verdant) or 6.8 u (Space, Ember), against the board's 30 u. The rail art
  now falls at the board's rate (`moveBackGround.RailTilesPerSecond`), so a clamped mine does not slide along
  its rail's art. This is a visible change to the rails at speed (about five times faster);
  `moveBackGround.RailRidesBoard = false` restores the old rate.
* **Firing.** Every mine fires a laser (`EnemyAttack.Laser`, `RailMineLaser`): see "Rail-mine laser" below.
  The tell is the mine's own waking -> charging loop plus the charge light (0.5 u inside the mine, on the
  lane's side), then the aim line; all draw over the rail art (mine sorting order 12, beam 11 under it).
* **Blast.** `FriendlyFire.MineBlastRadius` 1.05 u from 2.436 reaches in to 1.39 u: its own fifth of the lane.
* **Art.** All 16 cells are whole. Space and Frost carry none of the player's red. **Art gap:** the Ember
  mine's lava is orange-red (42% of the dormant cell is inside `HostileGlow`'s red band) and the Verdant
  mine's thorns touch it (14%).

### Rail-mine ride

"The rail mines at high speed fall past too fast: they should ride up the rails to keep up with the player, fire
2 lasers, then fall behind." The board scrolls at `speed` x 30 u/s (10.5 u/s at the 0.35 cap), so a mine that just
came down with it crossed the ship's rows in a blink. An armed laser mine (`RailMineMount.StepRide`, run by the
brain's `Step`, so it freezes with the world) now has three stages:

1. **Falling**: comes down with the board like any hazard. It does not start a volley yet (`AttackReady`; a
   board that is not rolling never holds a ride back: tests, previews).
2. **Holding**: when its row reaches the hold row (`ship.y + HoldAboveShip`) it RIDES UP the rail: `Ride`
   adds the board's step back every frame (counter-scroll, capped at `RideSpeedCap`), then eases onto the ship's
   row (`RideEase`), so it sits still against the screen, always within `HoldBand` of the hold row, whatever the
   speed. Its slide (Patrol / Creep), the shockwave `Shove` and the laser's geometry are unchanged and stack on top.
   It fires `RailMineLaser.ShotsPerRide` (2) lasers, `ShotGapSeconds` (1 s) between one volley's end and the next
   aim: the existing tell, aim line, 0.4 s beam, random angle each. Never longer than `MaxHoldSeconds`.
3. **Released**: both lasers fired and cooled (`EnemyBrain.RideFinished`): `Ride` unwinds with `FallAccel`
   extra pull and the board carries it away through the Destroyer strip.

   **Aim lock (fix: "once the laser starts going, the mine should stop following the player but still ride the
   rails").** Until its first laser begins the hold row is the ship's row. From the first windup (the aim tell:
   the aim line the player reads is fixed then too, its angle drawn once per shot and never ship-dependent) to the
   end of the last beam, `RailMineMount.AimLocked` freezes the row at `LockedRow`, the ship's row as that tell
   began (`TargetRow`). The mine keeps riding: `Ride` still counter-scrolls the board every frame and eases onto
   the frozen row (no stop, no snap), the free-room rule (`Slip`) is unchanged, and the beam is re-laid on the
   mine's muzzle each frame so it stays attached while the mine moves. The ship sweeping across the lane during
   the tell and beams changes neither the mine's row nor its angle. The lock clears on release; the mine then
   falls away as before. Frozen at the windup rather than the beam because the aim line is shown then: a mine
   that kept following during the tell would slide the line out from under the player. Hit boxes, damage, tell,
   aim and beam timings are unchanged. Same code path for all four worlds' mines.

A mine that is already below the band when it first reaches the ship's rows (a late spawn) skips the ride.
The mine is still clamped to its rail and its lane lives while it rides (`RailLaneScroller.Riders`); nothing about
hostile fire, shields, rams, pause jumps or spawn-in protection changes. Tunables are the consts at the top of
`RailMineMount`, plus `RailMineLaser.ShotsPerRide` / `ShotGapSeconds` (every world's roster entry uses them).
`RailMinePacingTest` checks it at low, mid and maximum speed on both rails in all four worlds, including the aim lock (a ship swinging across the lane during the beams moves neither row nor angle; the along-rail ride still advances).

### Rail-mine laser

"Have the rail mines shoot a laser." Every world's rail mine now fires a straight beam across the lane instead
of a projectile (Frost's shard, Verdant's three thorns, Ember's slag; the Space mine, which never attacked, now
fires too). `Scripts/Gameplay/Enemies/RailMineLaser.cs`, driven by the mine's `EnemyBrain`.

| Phase | Length | What shows | Harmful |
|---|---|---|---|
| Windup | the mine's tell, unchanged (Space 0.9, Frost 0.9, Verdant 1.0, Ember 1.1 s) | waking -> charging loop, the charge light | no |
| Ember's flame-thrower (art only) | Ember's mine (world 3 only; Tide keeps the plain beam) draws its laser as a flame: a pilot flame at the muzzle grows through the windup like a gas flare building, a dashed gas-jet aim line, a 6-frame white-yellow / orange / red-orange flickering beam with a pink hostile rim and ember specks, a heat haze, a ragged muzzle burst and a rail scorch (`MineFlameArt`). The red-orange edge (hue 19) is the one standing exception to "no red but the player's": at least 22 deg from the player's red (hue 355), measured in `RailMineLaserTest`. Timings, angle stream, hitbox and span are untouched. Codex slot: `ember_attack_mineflame.png` (`world-attacks-art.md` 3.7b). | n/a | no |
| Aim line | the windup's last `AimSeconds` 0.7 s | a thin (0.12 u) blinking line on the exact line the beam will burn, at its angle, from the mine's core to the far rail (the boss laser's telegraph cell, blinking on 3-tick steps) | no |
| Beam | `BeamSeconds` 0.4 s | the beam, 0.4 u drawn (the world boss's beam cells, flickering on twos), a muzzle flash at the mine, a spark on the far rail | **yes** |
| Cool | `CoolSeconds` 0.2 s | the beam flickers and thins out | no |

* **Geometry.** From the mine's muzzle -- its glowing core (`RailMineArt.CoreOffset`, 34-39 atlas px = 0.09-0.10 u
  toward the lane from the body's centre, measured on the waking / charging cells; `RailMineLaser.MuzzleOf`) --
  every frame (it rides the board with the mine; laid after `RailMineMount` places the mine, execution order 10),
  across the lane at this shot's angle to the opposite rail's face: `BossRails.DrawnInnerEdge` (the measured rails,
  RailInset included), so it reaches exactly the far rail at 9:16, 9:19.5, 9:21 and 3:4 (tested in every world);
  `Length` = (face - muzzle x) / cos(angle). Blocked by nothing but the rails.
* **Fix: "the rail laser is slightly off from the head of the rails when it shoots upwards at an angle".** The
  random-angle change pivoted the beam on its own rail's face, behind the mine and 0.26 u outboard of its core: at
  35 deg the beam crossed the core 0.18 u off (about 35 screen px on a 1080 px phone) and left the sphere near its
  rim, with the muzzle flash floating above it; and the square-cut far end stopped short of the opposite rail face on
  one side (a 0.115 u wedge at 35 deg) and poked into the rail on the other. Now everything pivots on the core, and
  both ends of the drawn beam and aim line are cut along the rails' vertical (a sheared quad: `Span` splits the
  shear into rotate-scale-rotate), so the far end lies flush on the face at every angle. The muzzle flash sits
  `FlashAlong` 0.19 u along the beam from the core (the sphere's rim). `RailMineLaserTest` "the muzzle" measures the
  core on the art and the drawn quads' corners: 40 cases (4 worlds, both rails, -35..35 deg) and +/-35 deg at
  every phone shape.
* **Angle.** "Make the lasers from the rail mines be shot at different angles too, randomly." Each shot draws its
  angle when the windup starts (`RailMineLaser.Arm` -> `NextAngle`), so the aim line already shows the exact line:
  uniform within +/-`MaxAngleDeg` 35 deg off horizontal (positive rises toward the far rail; left and right rails
  mirror), never within `MinAngleChangeDeg` 8 deg of the previous shot. Its own xorshift stream, seeded from
  `UnityEngine.Random`'s state without drawing from it (as `HazardSize`), so every other seeded stream is
  unchanged; `RailMineLaser.Seed` / `AngleOverride` for tests and previews. At 35 deg the beam rises or falls
  ~3.65 u across the lane and is 0.34 u tall where it crosses a column. The sprites, the hitbox (a BoxCollider2D
  on the rotated beam), `Touches` (an oriented-rect test), the burn (`Burn`: distance to the segment) and a blink
  (`LandsOn`) all use the one rotated segment `From` (the core) -> `To` (the far face).
* **Look.** The world boss's own laser art (`BossArt` telegraph and beam cells, `BossAttackFx` flash and spark):
  the hard-edged magenta beam with a white core in Space, Frost and Ember, Verdant's lime. No player red.
* **Damage.** A trigger box tagged `Enimey` (`RailMineLaser.HitboxName`), `HitThickness` 0.28 u across the beam,
  enabled only in the Beam phase: collisionDetection's normal hostile hit -- a heart; under a shield it is absorbed
  and the beam ends (`EliteShip.ShieldRam` -> `RailMineLaser.EraseHitbox`); a blink erases it only when the hull
  lands ON it (`EliteShip.TeleportStrike` -> `RailMineLaser.BlinkStrike`, not anywhere in the jump's 0.95 u blast
  circle); a heart lost does NOT end it (the hull never destroys the hitbox: the beam burns on and the heart's
  i-frames carry the ship through). Only the fatal hit ends it. It is a projectile to the death crash and not a
  hazard body to `RamKill`.
* **Fix: "the lasers randomly disappear when the ship gets close".** Two things erased the whole lane-wide beam
  from near the ship, on both rails alike: (1) a pause jump's `TeleportFx.Strike` overlap circle (0.95 u) caught
  the long thin hitbox from any landing within ~1.1 u of its row, so a jump landing beside the beam wiped it;
  (2) an unshielded touch destroyed the hitbox (`collisionDetection`'s ram path), ending the beam on the spot.
  Tested on both rails (`RailMineLaserTest`, "the ship coming close"). Not shootable by player weapons (like the boss lasers).
* **Friendly fire.** All hostile fire hurts what it crosses. The beam's targets are picked in one place,
  `RailMineLaser.Burn()`: with `RailMineLaser.HurtsOtherEnemies` (true) every `ClearTarget` hazard whose body the
  live beam's rect touches (`FriendlyFire.CanHit`: not the boss, not shot hitboxes) takes `FriendlyFire.Hit` -- a
  kill or an elite's heart -- at most once per pulse; never its own mine. The mine's death blast is unchanged.
* **Budget.** One volley = one beam; it reserves one shot in `EnemyThreat` during the windup and counts as a live
  shot while it burns (`RailMineLasers.LiveBeams` in `EnemyThreat.LiveShots`), so `MaxEnemyShots`, `VolleyGap`,
  the tells, cooldowns and volley counts (Space 2, Frost 2, Verdant 1, Ember 2) and the spawn rate are as they were.
* **Dodge room.** The ship's centre must leave a band of the beam's height where it crosses (0.28 u level, 0.34 u
  at 35 deg) + its hitbox height (1.35x ship: about 0.66 u) = ~0.94-1.0 u, i.e. at most ~0.5 u up or down from the
  line, with the 0.7 s aim line (plus the rest of the tell) to do it in -- under 8% of the smallest (13.2 u) view. The mine holds still from its windup until the beam
  is out.
* **Pause, 60 fps, tutorial.** Timers advance only through the brain's `Step` (running frames), so a frozen world
  freezes the beam; pooled (`RailMineLasers`, at most 6), sprites looked up once per fire, no per-frame
  allocation (tested). Roster enemies never shoot in the tutorial, so no laser there.

### The strip between the lane and the rail

Enemy bodies stop at 2.35 u, the ship's centre at 2.4 u (its hull to about 2.69 u), the drawn rail starts at
2.606 u. Only the backdrop shows in that strip; rail mines (2.13 to 2.77 u), shots (they break at 2.606 u) and
the ship's own hull enter it. **Recommendation:** leave the lane at 2.35 u and the ship at 2.4 u. Widening the
enemy lane to the rail would add 0.25 u a side that the ship's centre cannot reach, so hazards there could
only be grazed; widening the ship's reach is a player-feel change that needs the user. The strip reads as the
rail's hardware zone now that the mines sit in it.

### The rail quads' colliders

The padded quads carry a 3D `MeshCollider` and their bounds reach in to about 1.87 u. Nothing touches them:
the walls have no 2D collider or body, and no script makes a 3D physics query or receives a 3D collision
(`RailsVettingTest` scans for it). Elites' rail crashes, shots and the death crash use `BossRails.InnerEdge`.

### The taller view

Everything a pilot flies by is multiplied by `EnemyBrain.ViewScale` (view height / 10): station depth, entry,
exit, run and descend speeds, dives, the windup clearances and its shots' speed. Measured with a Warden in a
10 / 13.2 / 17.4 u view: station 16% of the way down the screen in all three, 10.1 s in view in all three, and
its shell reaches the ship's row in 1.40 / 1.43 / 1.45 s (4.3 / 5.6 / 7.4 u/s). Lateral numbers are not scaled.
Chasers' pursuit speeds are not scaled (they chase the ship, wherever it is).

The threat ceiling grows with the board the view shows (`EnemyDensity.ViewScale`): 10-11 bodies in a 10 u
view, about 16-17.5 at 17.4 u.

Threat table re-measured in the real phone views. The original spawner was never measured there; all its
enemies rode the board, so "original" is the recorded number scaled by view height.

| HUD | 13.2 u view: original / now / cut | 17.4 u view: original / now / cut | pilots in view | shots in flight |
|---|---|---|---|---|
| 5 | 8.3 / 5.6 / 33% | 10.9 / 7.2 / 34% | 0.5-0.6 | 0.78 |
| 10 | 8.7 / 6.5 / 25% | 11.5 / 8.3 / 28% | 0.6 | 0.84-0.92 |
| 20 | 13.3 / 6.8 / 49% | 17.5 / 8.9 / 49% | 1.2-1.3 | 0.39-0.49 |
| 30 | 20.1 / 9.2 / 54% | 26.4 / 11.8 / 55% | 1.8-2.0 | 0.52-0.71 |
| 35 | 26.0 / 9.5 / 63% | 34.1 / 11.9 / 65% | 1.7-1.8 | 0.38-0.57 |
| 40 | 22.9 / 9.0 / 61% | 30.1 / 12.0 / 60% | 1.8-1.9 | 0.44-0.74 |
| 46 | 20.0 / 8.2 / 59% | 26.3 / 10.9 / 58% | 1.9-2.0 | 0.59-0.61 |
| whole run | 13.2 / 6.8 / 49% | 17.3 / 8.3 / 52% | 1.1-1.2 | 0.59-0.72 |

No retune was needed: the cut holds at 49-52% over a run in both views (47% in the authored view).

### Calm arrival

Codex's 8 s spawn-free window ran once per scene load (a flag on the component), though its comment said
"a new world begins with". It now belongs to each world arrival: a portal or a loop starts it again, unless
the ship arrives at SPEED 10+. Nothing spawns in it (hazards, pilots, chasers; the old world's deferred queue
is dropped), and when it ends the slots come in staggered over ten seconds (`StaggerAfterCalm`), not as a
burst. Elites cannot lift off before 20 s in a world, so they are outside it already.

### Elites with pilots

* The spawn shadow (`ISpawnShadow`) is read only by `SpawnSpace.Fits`, which pilots do not go through, so an
  elite cannot block a pilot's admission and there is no deadlock. Pilot admission now prefers a column no
  elite is hovering over and takes it anyway on its last tries (`PilotAirspace.EliteOver`, read-only).
* `EliteSurvivalProbe` on this branch (real pilots, world-space shots, art repairs merged), 192 solo flights:
  60% died to the board with evasion off, 8% with it on (rail deaths 20 -> 2); groups 85% -> 6%; a hunting
  pilot still kills 94 of 96. The probe runs in the authored 10 u view.
* No elite file was edited.

## Progress / next steps (rails)

* [x] Rail-mine audit, mount on the drawn rail, rail art on the board's scroll
* [x] Rail edge right from scene start; colliders checked; lane/rail strip documented
* [x] Pilots and threat ceiling follow the view; table re-measured at both phone shapes
* [x] Calm arrival per world, staggered
* [x] `RailsVettingTest`; `AllTests.RunAll` 92 suites, 90 pass (Frost wrapper contrast and Verdant mid value: known)
* [ ] On a device: rail scroll at speed, mines on the rails, pilots' distance from the ship, calm arrivals

## Rails roll (follow-up)

Branch `fix/rails-roll`. "Make sure rails and rail mines roll nicely": the scroll vetted frame by frame, in
rendered 60 fps sequences and tests. Not seen on a device.

* **What was wrong in motion.** The rail art is point-filtered and drawn smaller than it was painted: one
  screen pixel covers 2.14 texels (Space, Ember) or 2.79 (Frost, Verdant) at 145.2 px per unit, never a whole
  number. Scrolled by a fraction of a pixel, point sampling picks different texels each frame, so thin lines
  and lamp edges crawl. Measured on rendered frames (mean difference between a frame and the previous one
  moved down by the step): 8 to 13 of 255, at the old slow rate just as at the board's rate. The rail art
  and a mine also integrated the scroll separately, with nothing holding them together.
* **What it does now (`BoardRoll`).** One distance, advanced once a frame, read by both walls and every rail
  lane. The rail art's offset is computed from the total (never accumulated: bounded, both walls identical,
  exact after an hour) and drawn on whole screen pixels; a mine is lifted by the same sub-pixel remainder so
  it is drawn on the same pixels. Measured: frame-to-frame difference 0.00 to 0.02 of 255 in every world,
  the tile seam included; a mine stays within 0.05 px of the art under it through 600 frames of ramping
  speed, a speed boost, a slide and a shove.
* **Speeds.** At 60 fps the rails move 3.6 / 7.3 / 14.5 / 21.8 / 27.6-31.9 px a frame at HUD 5 / 10 / 20 / 30
  / 38-44. A tile is 753 to 982 px tall and its lamps are not evenly spaced (no repeat inside a tile
  correlates above 0.25), so the largest step is about 4% of the only period there is: no wagon-wheel
  strobing. At the caps the rails are a fast streak, the same speed as the rocks beside them.
* **Choice.** Board rate on whole pixels. The alternatives stay one tunable away: `BoardRoll.RailRate` below
  1 is a slower, readable rail that mines then slide along; `BoardRoll.PixelSnap = false` is the unsnapped
  roll; `BoardRoll.LegacyTileRate` is Codex's original rate. A speed blur in the shader and a carriage
  sprite for mines were considered and not built: the first changes approved art's look, the second needs
  new art.
* **Seam.** The 3% crossfade blends the tile's last rows over its first, and those do not match (mean
  difference 42 to 69 of 255), so there is a soft double image about 25 px tall once per tile. It rides the
  art rigidly now (no pop, no gap), but it is there: the rail textures need to be made to tile.
* **Pause, boss, death.** Nothing rolls on a frame where time is frozen (the roll is speed x dt, and resumes
  from the same number). The rail art now also rolls during the ultimate's slow motion, when the lanes do;
  it used to stand still there while the mines moved.


## Hazard sizes

Branch `feature/hazard-size-variety`. User: "the hazards should come varying sizes, like rocks and stuff floating
around." Headless simulation, tests and editor renders only; nothing here has been played.

**What varies.** The 14 rocks only. Rail mines (clamped to the drawn rail with a measured clamp reach) and every
pilot (fighters, heavies, aliens, chasers: measured shot origins and identities) stay one size; `EnemyFactory`
ignores a size for anything but a rock. Backdrop rocks and planets (non-interactive, `Worlds/Backdrop`), pickups
and atoms, and the title-screen traffic are not hazards and are untouched. No other floating gameplay hazard
exists (no `Astr`-tagged prefab or scene object is left; `extraEnemyPrefabs` is empty in gameS1).

**Data.** Each rock's behaviour record carries three tier sizes (`EnemyBehaviour.Sizes(small, typical, large)`,
multiples of the roster size). `HazardSize.Draw` takes one number from the sizes' own xorshift stream, seeded off `Random.state` in the spawner's `Start` without consuming it (so the spawner's own draws, and every seeded probe, are exactly as before sizes; a seed still gives the same sizes): 28% small,
55% typical, 17% large (`SmallShare`, `LargeShare`), then +/-5% inside the tier (`Jitter`). The tiers are chosen
so each rock's mean area (sum of share x size^2) is within 3% of 1: the board holds as much rock as it was tuned
with. A spawn that has to wait (the deferred queue) keeps its draw, so waiting never trades a big rock for a small
one (without that the realised mix fell to 37% small / 11% large and 0.92 mean area at loop density; with it, 30% / 54% / 16% and 0.99).

| Rocks | small / typical / large | range |
|---|---|---|
| `space_rock_crater`, `space_rock_dark`, `verdant_rock_pod`, `verdant_rock_vine` | 0.78 / 0.98 / 1.32 | 0.74-1.39 |
| `space_rock_cluster`, `ember_rock_islet` (chunky) | 0.80 / 0.92 / 1.48 | 0.76-1.55 |
| `frost_rock_chunk`, `verdant_rock_spore`, `ember_rock_magma`, `ember_rock_cinder` | 0.80 / 0.96 / 1.40 | 0.76-1.47 |
| `frost_rock_rime`, `verdant_rock_knot` | 0.76 / 1.00 / 1.30 | 0.72-1.37 |
| `frost_rock_shard`, `ember_rock_obsidian` (slim) | 0.76 / 1.02 / 1.24 | 0.72-1.30 |

**Art.** A rock strip is bilinear, no mipmaps, 192 px a cell; at nominal size a phone draws it at 95.8 px (2.0
texels per screen pixel). The range runs from 2.78 (0.72x) to 1.29 (1.55x) texels per pixel: never magnified,
the small end still legible in the renders. `FrameWorldSize` and the roster sizes are unchanged; the size is a
per-instance transform scale on top.

**What scales with size:** transform (sprite, trigger collider, split fragments, death-domino pieces);
`EnemyIdentity.Scale`; the footprint and placement envelope (`SpawnSpace.BodyHalf(def, size)`); the lane extents,
row-gap guard and lane limit (`SpawnLane.HalfExtents / Fits / MaxX`); the brain's lane clamp and envelope
(`EnemyBrain.Envelope(..., reach)`, `EnemyBrainPlan.reach`), and so the pilots' sidestep, column routing and the
elites' spawn shadow, which read those sweeps; the hit radius (`ClearTarget.Radius` = collider half x size: elite
evasion sensor, mine blasts, crashes, the domino, weapons, secret powers); the shockwave's radial shove
(`ShoveScale` = size^-1, 0.6-1.4; the column push is the same for all); the blast size (`Blast`: below 0.85x
small, above 1.3x large, so big rocks split more often); the kill value (`RockPoints`: 5 x size rounded, 4 to 7;
4.98 on average over the draws, ScoreRules.Rock 5). Rocks still die in one hit.

**Weight.** Derived from size, no new tables: `Reach` = size^-0.5 (0.8-1.15) on the band, rise, sink and the
floating tilt (capped 15 degrees); `Pace` = size^-0.5 (0.8-1.2) on drift / glide / sink speeds, sway / bob /
pulse / orbit cycles and the tumble. A 1.55x cluster drifts in a 0.44 u band at 0.26 u/s, a 0.76x one in 0.63 u
at 0.37 u/s.

**Threat table** (`HazardSizeTest`, EnemyDensityProbe's stepping, 1080x2520, 4 seeds; the same seeds with sizes
off; sizes have their own stream, so the spawner's is identical):

| HUD | threats off -> on | rock area in view off -> on |
|---|---|---|
| 5 | 7.22 -> 7.27 (+0.7%) | 4.66 -> 5.00 |
| 10 | 8.23 -> 8.28 (+0.6%) | 6.10 -> 5.79 |
| 20 | 8.95 -> 8.87 (-0.9%) | 6.76 -> 6.83 |
| 30 | 11.71 -> 11.89 (+1.5%) | 9.08 -> 9.04 |
| 35 | 11.86 -> 12.33 (+3.9%) | 9.52 -> 9.84 |
| whole run | 8.37 -> 8.55 (+2.1%) | 6.18 -> 6.36 |

Threat accounting still counts bodies; the area-neutral draw keeps it where it was tuned, so the threat ceiling,
the portal-pressure escalation and its body cap are unchanged code (OpenPortalTest).

**Tunables** (`HazardSize`): `Enabled`, `SmallShare`, `LargeShare`, `Jitter`, `ReachExponent` / `MinReach` /
`MaxReach`, `PaceExponent` / `MinPace` / `MaxPace`, `MaxTilt`, `ShoveExponent` / `MinShove` / `MaxShove`,
`SmallBlastBelow`, `LargeBlastAbove`; per rock, `.Sizes(...)` in `EnemyBehaviours`.

**Pooling, pause, 60 fps.** Roster enemies are not pooled; `HazardSize.Apply` still sets everything absolutely
from the def, so applying a size again resets a body (tested). Sizes are fixed at spawn; every motion runs through
the existing paused-frame gates. No per-frame allocation (profiler GC.Alloc with a positive control); the
three-argument `Mathf.Max` in `SpawnSpace.BodyHalf` (a params array, one allocation per spawn try) became two calls.

**Evidence.** `HazardSizeTest`; `HazardSizeRender.Run` writes per world a spawner-flown field, the same with lane
and collider guides, and a smallest / typical / largest sheet per rock at 1080x2520.

## Ship reach and boss height

> **Superseded at the top (branch `feature/player-zone-to-scoreboard`).** The ship's ceiling is no longer 70% of the
> view: its top edge now sits `ShipReach.TopGap` (0.03 u) under the HUD band's bottom (`PlayField.Frame.bandBottom`:
> score read-out + home/replay icons, measured per screen and safe area), i.e. 82-89% of the view (old: 70%): +2.2 to
> +3.2 u (+14 to +20% of the view) on phones, +1.2 to +1.4 u on tablets. Unshielded, a boss still holds the ship under
> its muzzles; a shielded ship (blue atom / Cloak with more than 1 s left) may fly all the way up and ram it
> (`BossRam`: one hit of 0.3, 1.2 s of shield, 1 s cooldown, body stays; an unshielded touch still costs a heart).
> Scrolling hazards are not born over a ship parked up there with under 0.5 s to see them (`TopGuard`). Atoms still
> float to the old 70% (`PickupTopShare`). The table below describes the old 70% design.

Branch `fix/ship-reach-and-boss-height`. Verified by simulation, tests (`ShipReachTest`) and editor renders
(`ShipReachRender`); not played on a device.

**Touch.** Absolute, not relative: every frame a finger is down the ship's centre is put 1 u above the finger
(`ShipReach.FingerOffset`, the thumb pad never covers the hull), clamped to the reach; the first touch after a lift
is a teleport there. The reach used to be the constant y -4.15 .. 4.5. In the original 2016 design (a 10 u view,
the ship 1.5 u above the finger) the ship reached 15% .. 95% of the view; on phones the old constant gave 19% .. 84%
(16:9) and 26% .. 76% (21:9): the bottom fifth to quarter was out of reach.

**Chosen (all tunables in `ShipReach`, `PlayField`, `BossConfig`, `EnemyBrain`).** Retuned on branch
`tune/ship-ceiling-70` (the user: "closer to 70% and 65% for bosses"); the first version stopped at 60% / 53-58%.

| | Rule | 1080x1920 | 1080x2520 | iPhone 15 |
|---|---|---|---|---|
| Ship floor | safe-area bottom + hull and flame (`HullBelow` 0.82) + `BottomMargin` 0.15 | -5.64 (7%) | -7.71 (6%) | -6.45 (10%) |
| Ship ceiling | `TopShare` 0.70 of the view (never into the HUD band) | 2.65 (70%) | 3.47 (70%) | 3.23 (70%) |
| HUD 35, top edge to the hull at the ceiling | | 0.35 s (60%: 0.48, old: 0.17) | 0.47 s (0.63, 0.37) | 0.43 s (0.59, 0.31) |
| Boss rest | cell top at the top of its sway `BossTopMargin` 0.2 under the HUD band (read-out + icons) | 3.38 (76%) | 4.71 (77%) | 3.78 (73%) |
| Boss shot speed | x `ShotScale` = gap boss -> ship row (18% of the view) / authored 6.35 | x1.20 | x1.62 | x1.41 |
| Ship ceiling in a boss fight | `FightCeilingShare` 0.65 of the view, or lower: boss rest - (its lowest muzzle / body + its sway + gap + hull) | 60-64% | 65% | 60-64% |

The fight ceiling per boss (Space / Frost / Verdant / Ember), share of the view; gap = `ShipGap` 0.3 x ShotScale, at
least the muzzle charge glow's radius (0.375):

| Shape | gap | Space | Frost | Verdant | Ember | was (60% design) |
|---|---|---|---|---|---|---|
| 1080x1920 (16:9), iPhone SE | 0.38 | 1.44 (61%) | 1.32 (60%) | 1.45 (61%) | 1.88 (64%) | 53% |
| 1080x2400 + gesture bar (20:9) | 0.45 | 2.26 (64%) | 2.14 (63%) | 2.28 (64%) | 2.48 (65%) | 56% |
| 1080x2520 (21:9, the user's) | 0.48 | 2.60 (65%) | 2.54 (65%) | 2.60 (65%) | 2.60 (65%) | 58% |
| 1080x2640 (22:9) | 0.52 | 2.73 (65%) | 2.73 (65%) | 2.73 (65%) | 2.73 (65%) | 59% |
| iPhone 15 | 0.42 | 1.80 (61%) | 1.68 (60%) | 1.81 (61%) | 2.24 (64%) | 54% |
| Foldable inner 1812x2176 (boss at 62%) | 0.38 | -0.73 (43%) | -0.86 (41%) | -0.72 (43%) | -0.30 (47%) | 38% |
| iPad 9 (boss at 67%) | 0.38 | -0.26 (47%) | -0.38 (46%) | -0.25 (48%) | 0.18 (52%) | 41% |

65% is out of reach on 16:9 and the iPhone for three bosses: the boss rests at 73-76% and its lowest muzzle hangs
1.0-1.2 u (7-9% of the view) under its centre (Frost jaw 1.19 + sway 0.2, Space chin 1.14 + 0.12, Verdant stinger
0.99 + 0.26; Ember's furnace only 0.65 + 0.18), so at 65% the hull would be beside the muzzle. The ceiling there is
the highest with the gap kept: 60-61% (64% for Ember). The muzzles are the generated table's (every part, every
drawing); the body hitbox (0.55 u under the centre) is above every lowest muzzle. The boss's drawn fringe (Frost's
icicles, Verdant's tendril rings, Space's pod flames) reaches 1.25-1.64 u under its centre, so at the ceiling the
hull can touch that fringe by a few pixels; it is art, not a hitbox.

Why 70%: the user asked for it. The HUD band covers the top 9% - 13%, hazards appear at the top edge (0.35 - 0.49 s
at HUD 35 to the hull, down from 0.48 - 0.66 s), and pilots hold stations 14% - 30% below the top of the view (a Swoop
dips 9% further): at 70% the ship reaches as high as the deepest station. Sideways reach is unchanged (+/-2.4). The
range is a pure function of the camera and the safe area, so it follows a fold or a gesture bar the frame it changes.
`ShipReach.FitToView = false` / `BossConfig.FitToView = false` restore the old constants.

**Pilots hold higher while the ship is close under them** (`EnemyBrain.HoldY`, new with 70%). A pilot's windup needs
the ship 1.6 x view below and 1.8 x view away (16% / 18% of the view); with the ship at 70% every station deeper than
12% would deny it, and the deepest stations (2.6 - 3.0 u, 26-30%) would put the pilot's body on the hull. So while the
ship is within 1.9 x view (`MinFireDistance` + `HoldMargin` 0.1) under its station, a pilot backs up to keep that
clearance, no higher than 1.1 x view under the top (`HoldTopDepth`) nor into the HUD band, and returns to its station
as the ship drops (at 0.6 x its entry speed; never mid-attack). Entries (Drop, Swoop dip) aim at that held station;
the Swoop guard (`ShipReach.EntryFloor`) still applies. Windups keep their full clearance rules.

Test flights (every pilot, two lanes, three phones, ship parked at the ceiling): 120 flights, 161 windups at 70%
(138 with the first version at 60%; 234 for the hold rule at 60%), 0 started inside the clearance, 0 frames of a body
on the hull; every pilot that winds up at 60% still does at 70% (per pilot about two thirds as often as with the hold
rule at 60%). With the ship at the ceiling a pilot can hold directly over it and still wind up on 16:9, 20:9, 21:9,
22:9 and the iPhone SE (held up to 89% of the view); on the iPhone 15 (band at 87%), the foldable and the iPad the
band leaves less room, so only a pilot off to the side of the ship winds up. Chasers from below reach a ship at the
floor no sooner than in the authored view (unchanged). The portal's station (50%), pickup atoms (their soft ceiling
follows the reach), the tutorial's hovering atoms and the ship's start (y -2, 30-39%) are inside the reach on every
shape; elites join 2.6 u from a ship at either end. While a boss is up the ship's ceiling follows the boss down as it
warps in (the resting ceiling moved by the boss's distance from rest; 3.47 -> 2.60 on 21:9, never faster than the
boss moves) and is back once it is gone.

**At the fight ceiling** the first shot reaches the hull's top sooner than at the 60% design's ceiling (an aimed
chin cannon 0.10-0.13 s instead of 0.27-0.28 s, a fan 0.28-0.38 s instead of 0.55-0.68 s, beams 0.05-0.08 s as
before, Frost hail 1.18-1.30 s), always after the attack's 0.6-1.0 s tell and never from inside the hull. Two fans
are a wall that close (Space core burst, Ember fire breath); the nearest line with a way through is 4-13% of the view lower, a
drop of 0.11-0.25 s at 7 u/s within their 0.80 s / 0.70 s tells (to the 30% line: 0.58-0.87 s; 16:9 Ember fire
breath was 0.44 s at 53%). Every other pattern leaves a way through at the ceiling.

**With UiScale's floor (the minimum UI scale).** The band all of this measures is the one the device lays out: on
a phone small in dp the quick actions grow to 44 pt / 48 dp and the score read-out stacks under them
(`HudStyler.StackedReadout`), a taller band. With each device's real density none of the phones above stacks
(1080x1920 at 420 dpi and the iPhone SE keep the read-out beside the actions, band from 92%), so the numbers above
stand. A device that reports no density takes UiScale's fallback (every phone 320 dp wide) and stacks: band from
76-78%, boss at 62-66%, fight ceiling 46-50% (16:9), 54-57% (21:9), 50-53% (iPhone 15); the boss stays clear of
the band and the hull keeps its gap under every muzzle. A pilot's hold is capped by the band but never under the
shallowest authored station (`EnemyBrain.HoldBandFloorDepth` 1.4, 14% under the top), so with a stacked band it
still holds above the hull. `ShipReachTest` drives each device's real density (`FitDevice.ReportedDpi`) and checks
the unreported case explicitly.

**Boss attacks** keep their time to the ship's row within 12% of the authored view on 16:9, 20:9, 21:9, 22:9 and
iPhone 15 (before: up to 52% slower on tall phones); straight shots scale as a whole (aim and angles kept), lobs
stretch vertically (columns kept), beams grow faster by the same factor. Fans and angled beams keep their angles,
so on a tall screen they spread wider by the ship's row and meet the rails sooner: slightly easier, not retuned.

## Death combo

One rule set for the domino, in play and in the death crash. Every number lives in
`Scripts/Gameplay/Weapons/DeathCombo.cs`; tests: `DeathComboTest` (rates, chain, caps, elites, pause),
`DeathDominoTest` (the crash's chain, rolls forced on).

| Constant | Value | Meaning |
|---|---|---|
| `TriggerChance` | 40% | a hazard the pilot destroys (weapon, ultimate, secret power, shielded ram, blink; an elite brought down by weapon / blink / shielded ram) sets off a combo at the wreck |
| `ChainChance` | 40% | each other hazard within `BlastRadius` of a combo death is hit (rolled per hazard) |
| `BlastRadius` | 1.4 u | plus half the target's radius |
| `HopDelay` | 0.1 s | one link per hop, gameplay time: a wave, not a mass delete |
| `MaxDepth` | 6 | hops from the pilot's kill; `MaxPending` 32 queued links |
| `DeathHitChance` | 40% | death crash: the ship's wreckage breaks an enemy it touches (rolled once per enemy; a miss glances off) |
| `DeathChainChance` | 40% | death crash: a chain kill's own pieces are live; otherwise harmless debris |

Rules:
* A combo **kill** is a combo death of its own (its own burst and rolls). Chain victims never roll
  `TriggerChance` again. A kill pays like any kill (score with the kill chain, dust, codex, achievements),
  once; a rail mine still bursts on its own friendly-fire rules.
* An **elite** loses exactly one heart per combo hit (`EliteDamage.Combo`, respects its grace, never one-shot);
  only its death chains on. The **boss** (body, shots, beams), elite shots, pickups and the **player** are
  never touched.
* Off in the tutorial and during the death crash (which clears the queue); frozen while the world is paused.
* MEGA DOMINO keeps its own rule (`DeathCrash.MegaChance`, 12%: the whole screen).

Before this rule the death crash killed everything its wreckage touched and every kill threw more killing
pieces: a death with 6 hazards on screen chained 83% of the time (93% with 10, ~4 kills each). Measured with
the rule (random static boards): 27% / 53% / 62% of deaths with 3 / 6 / 10 hazards. In play, chain kills per
combo average 0.15 / 0.42 / 0.78 / 1.9 with 3 / 6 / 10 / 16 hazards on screen (about 6 / 17 / 31 / 75 extra
kills per 100 pilot kills); the worst seen was 12 kills, 6 hops deep (the cap).

## Boss hearts

Branch `feature/boss-hearts`. The user: "The bosses should have 5 spinning hearts around them", and "make the
enemy elite hearts more clear: give them an outline glow". Tested by `BossHeartsTest`; renders from
`BossHeartsPreview`.

**Health.** A boss has `BossConfig.HitPoints` = 12, always equal to `Hearts` (full-weight ship-attack hits: a rail slug, a fireball, one
homing shot of the volley = 1; a gatling shell 1/14, a beam or cone tick 0.2, ...), worn as `BossConfig.Hearts` = 12
hearts (`BaseHearts` 5 + `ExtraHearts` 7; it was 9 = 5 + 4, then the user asked for 12), each an
equal share (`HeartWeight` = 12/12 = 1 hit: one full hit, one heart) of the pool: a heart goes the moment its share is spent, the twelfth
exactly with the last hit point. Twelve full hits destroy it (it was 7 hits with 12 hearts, five hits and 9 hearts, three and 5 before; `HitPoints` is now defined as `Hearts`, so they cannot diverge). Every boss in every world has the same numbers (`BossConfig`; the catalogue holds neither).
**The escape clock** is `BossConfig.EscapeSeconds` = 120 s (`FightSeconds`): an untouched boss stays two minutes,
then retreats (it was 36 s). The rules round it are unchanged: hits count only in the Fight phase (nothing while it
warps in or out), the clock is never shaved, DeathCombo never chains a boss, rams and shockwaves pass through it.
The attack phases stay at the thirds of the clock / of the hearts (40 s each); the damaged-hull looks
(`BossArt.DamageStage`, 4 stages) spread across the 12 hearts (1-3 lost stage 1, 4-6 stage 2, 7-9 stage 3, 10+ stage 4).
**Hit window (no per-firing cap).** Nothing caps a firing, a barrage, a beam or a ram against a boss (`BossTarget` is
`IShipAttackTargetUncapped`; elites keep the one-budget-per-firing rule). The only limiter is
`BossConfig.HitInvulnerableSeconds` = 0.25 s of invulnerability after a lost hit point (or after a discrete bump, a ram),
`BossEncounter.TakePlayerHit`. It runs in game time (the fight's scaled dt), so a hit-stop, a pause or a frozen screen holds
it instead of spending it. A hit the window blocks deals nothing but still sparks (`BossTarget.Strike`), with no sound,
shake, hit-stop or heart effect. Light shares of a firing (weight under `HitBankMaxWeight` .5: gatling shell, beam tick,
one eye of three) are banked inside the window, held just short of a whole hit point, so fractional weapons keep their
full value; heavier hits inside the window are dropped. Homing barrages (seeker eyes, Paragon lances, missiles) no longer
"claim" the boss away from the next shot, so every shot chases it. Rams have no cooldown of their own any more: a landed ram
(`RamWeight` .3) costs the shield and opens the window, a contact the window blocks costs nothing.
Supply: a fight guarantees `BossFreeShotAtoms.Count` = 2 free-shot atoms (~30% and ~65% of the fight), each a charged shot
(`ChargedBudget` 1.5 now only matters to capped targets; against a boss it is an ordinary firing), plus one each of capacitor (twice), shield and heal at fixed shares (`RollPlan`).
Other damage: the ultimate (30-60 s countdown, cut by pickups; one firing is about 1-1.5 hits) and rams (`BossRam.RamWeight` .3,
about four per blue atom). Estimate for 12 hits: free shots 3 + ultimate 2-3 firings (~2.5-3.5) = ~6, so about 6 more
hits must come from rams (~20 rams = 5 shield atoms) in 120 s: see the PR notes on the balance (not retuned).
`BossFreeShotAtomsTest` / `BossEscapeClockTest` assert the numbers.

**Phases.** The attack escalation (one attack, two, then all three faster) goes by
`BossEncounter.PhaseProgress01`: the clock's progress or the share of hearts lost, whichever is further on, at the
same thirds. The first third of the hearts lost (12-9 left) is phase 1, the middle third phase 2, the last third phase 3 (or later, by the clock).

**The ring** (`BossHearts`, on the shared `HeartOrbit` engine with the player's lives and the elites' hearts): one
flat ellipse in the screen plane, twelve hearts evenly spaced (HeartOrbit's ceiling), a revolution every 5 s, no warp or flourish. Its radii
are the ellipse round each boss's idle silhouette (`BossHearts.Body`, measured from the atlases and re-measured by
the test) plus a 0.06 gap and half the drawn heart: Space 1.93 x 1.84, Frost 2.09 x 1.95, Verdant 1.87 x 1.82,
Ember 1.97 x 1.70. At rest it clears the body and stays inside the rails (|x| <= 2.09 against 2.43) and under the
HUD band on 9:16, 9:19.5, 9:21 and 3:4. Where it would cross a rail (the boss swayed to a side: its own body reaches
past the rail there) or the band, it flattens against that side, so a heart may pass over the body's edge but never
under a rail or the HUD. Hearts are 0.29 (the player's 0.22 x 1.3), the white elite heart in the boss's own colour
(Space violet, Frost ice, Verdant bile, Ember amber; never the player's red), sorting order 40: over the boss,
its charges and shots, under the HUD. No colliders. They pop in when the fight starts (on real time, so even with the
world frozen), turn on world time (pause freezes them), and a lost heart darts to the hit, flashes and crumbles like
an elite's while the rest close up evenly in about 0.3 s. Retreating, they go at once; destroyed, the last one's
crumble plays over the blasts and they go with the wreck. Pooled; nothing allocates a frame.

**Outline** (`HeartOutline`, elites' and bosses' hearts, their dart and their shards): a thin two-tone trace built
once from the heart sprite's opaque pixels (a chamfer distance, the boss-shot rim technique): a light amber-white
core line 2.5 texels wide right at the silhouette, then a 2-texel dark keyline whose outer half fades out -- 0.07 of
the heart's cell in all, never a round halo; constant scale, only its alpha pulses (0.78..1). Elite hearts are drawn
10% larger (0.198, still under the player's 0.22). Over all four worlds every outlined heart has outline pixels
3:1 clear of what is behind it.


## Elite lock-on and size tiers

Full analysis: `docs/ship-scale-and-lockon-analysis.md`.

* An elite perceives the pilot exactly (`EliteShip.Seen`) but its **brain steers to `Follow`**, `Seen` through a low-pass of `followLag` seconds, so it trails the pilot's twitches (gunship 0.45, stalkers 0.35, skirmishers 0.25 ...).
* An **aimed attack** (`EliteAttack.Aimed`) reads `EliteShip.AimSeen`: live for the first `aimCommit` (0.5) of the tell, frozen after it. The shot / row / well / dive goes where the pilot was at the commit, so the second half of the tell is the player's window (>= 0.3 s: aimed tells are at least `EliteDef.MinAimedTell` = 0.6 s).
* The hull turns to face the pilot at most `faceTurnMax` (300) deg/s.
* Sizes: enemy roles Rock / Alien / Fighter / Chaser / Big are 1.2 x their legacy size (`EnemyRoster.TierScale`; mines and bosses unchanged); elites' drawn silhouette is >= 1.9 x the flown hull (2.1 x for armoured classes). Tests: `ShipTierTest`, `EliteLockOnTest`.


## Ember elites

`Pause/Assets/Scripts/Gameplay/Elites/EmberElites.cs`, tests in `EmberEliteTest`. Six mini-boss fights, none of which follows the pilot; every
attack is telegraphed with the shared thin drawing (`ShowSight`, `EliteTelegraph`, `EliteMarks`) and locked at the tell; hazards are Ember's own
(heat needles, coal, scrap, ember clouds, slag, lava, firebombs). Non-armoured ones (3 hearts) `hurtHide`; Kilnback and Cauterizer are armoured (5).

| Elite | brain / attack | dock | pattern |
| --- | --- | --- | --- |
| Sunstoke | `stoker` / `lance_pass` | foundryhangar | waits off-screen; thin sight line locks a lane; rams the whole board and out the far edge (ploughs rocks), needles back along the lane; next pass from another edge; hurt: two passes |
| Coalrunner | `strafer` / `coal_strafe` | magmabay | strafing runs across the top third, in one side / out the other; bolt lanes drawn and locked; <= 2 hearts: bouncing coal clumps |
| Brass Vulture | `scavenger` / `scrap_dive` | slagpad | circles off-screen; thin ring, dives through it flinging scrap, back off-screen; +1 offset dive per heart lost |
| Ash Wraith | `ashling` / `ember_veil` | hatch | blinks between 4 perches at range; ringed ember cloud left behind, then a shard fan |
| Kilnback | `kiln` / `kiln_dump` | furnacebay | holds the top; slag dump down drawn lanes (more per heart lost) alternating with a lava spill strip; always a gap |
| Cauterizer | `furnace` / `fire_mortar` | furnacebay, foundryhangar | sight-line shell (two lanes with a safe gap at <= 2 hearts) alternating with ringed firebombs |

Storm and Desert still use the base brains / attacks. `EmberDirector.BringHome` asks the site spawner for a site of the elite's kind (as Frost's).

Ember art wiring (same PR): hull `_lights` strips and a six-cell damaged burn loop per elite (`<key>_damaged` + `<key>_lights_damaged` companions, cells 8..13,
`EliteCells.damagedLoop`, the same framework as Space's), four launch-strip sites (crucible silo, lava-moat bunker, brass gantry cradle, kiln mouth: docks
atlas via `build_launch_atlas.py`, `EmberTuning.LaunchCellAt`) with homes: Sunstoke kiln/foundryhangar, Coalrunner crucible/magmabay, Brass Vulture
brassgantry/slagpad, Ash Wraith kiln/hatch, Kilnback crucible/furnacebay, Cauterizer bunker/furnacebay. The launch strips are Codex's launch_v2 (closed, lamp, opening x2, open, closing; the first delivery's flat black panels were replaced) baked whole into the docks atlas. Tests: `EmberDockTest`.


## Fighting zone, rails, boss clear, escape boost, cadence (`EliteFightZoneTest`)

**Fighting zone** (`Scripts/Gameplay/FightZone.cs`). Nothing hostile perches, hovers, stands off, patrols or aims from the HUD's top
band (the "red zone"). The edge is read from the live HUD layout, not guessed: `PlayField.Live.bandBottom` (the lowest edge of the
score read-out / home-replay band, in world units, from `TopBand` and `HudStyler`; the same edge the player's reach stops at,
`ShipReach.TopFor`). `FightZone.Top(halfHeight)` = that edge - the body's drawn half-height - `Margin` (0.1 u), so the whole hull is
under the band and below the player's own top. With no band laid out (editor, batch) the band is `NoBandShare` (17%) of the view.
* Elites: **every goal goes through `EliteShip.ClampGoal`** (the clamp lives there, plus `EliteShip.ZoneTop` / `ZoneY` / `InZone`), so
  every brain inherits it: perches, standoffs, orbit points, hop targets, `topMargin` stations, rejoin (`BeginReentry`) and join
  (`EliteDirector.JoinPoint`) points. A roaming brain (`Roams`: its path leaves the view) holds its perches, ready points, stations and bands at the zone top through `ship.ZoneY(y)` / `FightZone.Top(FightZone.HalfOf(def))` (Ember, Verdant, Tide, Desert, Storm, Space and Frost roamers all do); its legs off the view stay free. A brain that settles on a point must clamp the POINT, not rely on a later clamp, or it never settles. **New brains (Ember #89, Verdant
  #90, Tide #101, Storm #104, Desert #107) get it by returning goals as usual; a roamer's hold point must use `ship.ZoneY`.** An
  elite above the zone top (a descent) does not start an attack until it is in (`InZone`, `FightZone.AttackSlack`). The Frost sky
  icicles (`icicle_barrage`) and the Singularity Hauler's towed rock are hazards that ENTER from above the view and fall through; they
  are not spawned by anything holding there. Mortar / barrage rows are clamped to `ship.ZoneTop`.
* Roster pilots (where the HUD band is laid out, i.e. on a device; the batch boards have none, so the `AttackBudget*` suites do not see this clamp: a pilot such as the Tide Hammerhead, station 1.5 u deep, sits lower on a phone than in those suites): the hold row is clamped by `EnemyBrain.ZoneHoldY` (drawing top and the pattern's `rise` under the band), except that it never drops onto a ship parked at the top of its reach (the player's reach touches the band's edge: the pilot then stands off above the hull, as `HostileReach` always required). Chasers,
  fighters, mines and rocks ride the scroll or chase the pilot, who cannot be above the band.

**Under the rails.** The rails (`leftPipe` / `rightPipe`) sort at `EliteShip.RailOrder` (0). The elite layer is `PlayOrder` -3 (was 5):
hull, plumes (-4), lights / glows (-2), hearts (-1) all sort UNDER the rails, over the backdrop, docks and weather (<= -7). A hull
that exits a side or the hurt-hide loop passes beneath them. Shots (30) and the attacks' ring marks keep drawing over; rocks (2) and
roster enemies (3) stay lane-bound and are unchanged.

**A boss clears the elites.** The moment a boss encounter starts (`BossEncounter.Running`, i.e. its warning), `EliteDirector.DismissAll`
destroys every elite: the ones on the board or lifting off through the normal death (`EliteDeath`: explosion, debris), parked and
hidden ones quietly (`EliteShip.DismissForBoss`). No score, no dust, no codex entry, no `ESCAPED` loss. Spawns stay blocked while the
boss runs (`Blocked` -> "boss"). Their shots and area hazards already in the air expire on their own; the landing shockwave sweeps the rest.

**Escape boost** (`EliteShip.StepRetreat`, all elites). When the engagement timer runs out the elite spools its engines for
`EscapeSpoolSeconds` (0.4 s: hull nearly still, `thrust` ramping so the plume / glow / damaged burn loop grow), then boosts at
`EscapeBoostScale` (3x) its cruise speed, accelerating at `EscapeAccelScale` (6x), `thrust` `EscapeThrust` (1.7: a long trail), straight
out of the top or the nearest side (a side where it is clearly nearer). It flies over the rails (no rail crash) and may cross the zone
top. It no longer flies home to its dock. Costs the pilot half the kill's score as before.

**Attack cadence** (`EliteTuning.AttackRate`, 1.7). Every gap between two attacks (the first wait, the cooldown after an attack and
after a hurt-hide, the Rimebreaker's pause between runs) is divided by it; wind-ups, telegraphs and hazards are unchanged.

A hard backstop: `EliteShip.KeepInView` holds every non-escaping elite at the zone top, so a dash or shove cannot carry it up into the band.
Suites that pin the elites' exact timing (`EliteTest`, `EliteFoundationTest`, `EliteEvasionTest`, `SpaceEliteTest`, `FrostEliteTest`, `TideEliteTest`) run at attack rate 1 (`AllTests.TimingPinned`).
