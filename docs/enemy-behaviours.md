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
* [ ] Merge `integrate/oct04-batch` (the enemy strip re-cell) and re-run `EnemyRosterTest`

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
| A rail mine's blast | `FriendlyFire.Detonate` | yes |
| Boss shots and lasers | `FriendlyFire.Hit` | yes, but no elite is in play during a boss |
| The death domino | `DeathCrashDomino` | yes (a player death; unchanged) |
| **Roster enemies' shots** (the new behaviour system) | `EliteShot.Step` skips `rosterShot` | **no** |
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
* **Scroll (decision).** Mines ride the board's scroll like every hazard: gameplay position has to agree with
  rocks, pickups and `SpawnSpace`. The rail ART was what disagreed: it advanced one texture tile per unit of
  speed, a tile being 5.2 u (Frost, Verdant) or 6.8 u (Space, Ember), against the board's 30 u. The rail art
  now falls at the board's rate (`moveBackGround.RailTilesPerSecond`), so a clamped mine does not slide along
  its rail's art. This is a visible change to the rails at speed (about five times faster);
  `moveBackGround.RailRidesBoard = false` restores the old rate.
* **Firing.** Shots leave 0.5 u inside the mine, on the lane's side, inside the rail edge, and cross the lane;
  from the new mount that is 1.94 u. The tell is the mine's own waking -> charging loop plus the charge
  light; both draw over the rail art (mine sorting order 12).
* **Blast.** `FriendlyFire.MineBlastRadius` 1.05 u from 2.436 reaches in to 1.39 u: its own fifth of the lane.
* **Art.** All 16 cells are whole. Space and Frost carry none of the player's red. **Art gap:** the Ember
  mine's lava is orange-red (42% of the dormant cell is inside `HostileGlow`'s red band) and the Verdant
  mine's thorns touch it (14%).

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

