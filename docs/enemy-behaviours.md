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

### Space (shots: magenta-pink, the elite shot colour)

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
| `ember_rock_obsidian` | Obsidian Shard | tall glass blade | barely turns; fast slanted `Glide` | no | vein flashes (cosmetic) | it slices one way only |
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
uses its whole band again. Two pilots side by side share the lane between them, half each. As a safety
net every pilot move still goes through `SpawnSpace.ResolveSteer` (the chaser's rule). The long-run test
(24 two-minute runs, every frame) shows no two bodies overlapping. All of this is self-contained in the
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
| `space_alien` Bile Mite | pilot | Descend 1.1 u/s in line | wiggles; spitters spit twice | marches out the bottom |
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
| `verdant_alien` Snap Sprout | pilot | Descend 1.0 u/s | marches sideways in step | marches out the bottom |
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
| `EnemyDensity` | `RateAtLowSpeed` / `RateAtHighSpeed` between `LowHud` 5 and `HighHud` 35 | x0.90 -> x0.55 | every spawn timer's rate, falling with speed |
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
* [x] Suites run in the editor (`AllTests.RunAll`): 87 suites, 85 pass (18,696 checks pass); the 5 failing checks are in
      `WorldBackdropTest` (3 Verdant palette checks) and `HostileProjectileTest` (the Frost wrapper
      contrast check, and a boss-shot rim check), none of them in code this branch touches
* [x] After table measured, `EnemyDensity` retuned once (high-speed rate 0.42 -> 0.55)
* [x] Follow-up: hazards and pilots (presence). Implemented, table re-measured, suites run
      (`AllTests.RunAll`: 87 suites, 85 pass; the 4 failing checks are the known Verdant palette three
      and the Frost wrapper contrast one)
* [x] `integrate/oct05-enemy-intelligence` merged in (the enemy strip re-cell); `EnemyRosterTest` passes
* [ ] Play it. Nothing here has been played: every number is from headless simulation
