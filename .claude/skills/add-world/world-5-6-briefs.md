# Starter briefs: World 5 *Tide* (ocean) and World 6 *Storm* (gas giant)

**These are PROPOSALS for the user to refine.** They are built from the user's words ("World 5 an
OCEAN planet, World 6 a STORM GAS GIANT, one at a time, slowly") plus what the code and tests allow.
Everything marked **OPEN** needs his answer before Phase 0 closes. Names are working titles.
Build World 5 to the release gate before touching World 6.

Order after both exist: `Space -> Frost -> Verdant -> Ember -> Tide -> Storm -> (lift-off) -> Space, loop + 1`.

## Constraints both worlds inherit (from the code, see SKILL.md section 2 / 6)

Existing palette families and the hues that are already spoken for:

| Owner | Hue (deg) | Note |
| --- | --- | --- |
| Space | indigo ~240-250, magenta ~330 | north-star nebula look |
| Frost | cyan/white ~180-200 | |
| Verdant | lime ~75-90, olive | |
| Ember | orange ~20-40, black | |
| Pickups | blue shield atom cyan **178**, green repair **82**, violet capacitor **259**, star dust amber **37**, red pause atom ~**7** | `HostileShotPalette` header |
| Hostile shots (every world) | pink **312-326** | `HostileShotPalette.HueMin/Max` |
| Player red | 345..15 | never on anything hostile |

A world accent that fills big areas at a pickup's hue makes that pickup harder to read; one at the
shot hue makes shots harder to read. Small lamps are fine; a whole backdrop is not.

---

## World 5 -- TIDE (the ocean planet)

**One line.** A drowned industrial world: a planet-wide ocean with rusted oil rigs, barnacled
walkways and sunken cities still glowing under the water, lit by bioluminescence, under storm cloud.

### Palette (decide: OPEN)

The user's words: deep teal/black water, bioluminescent magenta + cyan accents, rusted rigs.
Collision check: deep teal sits on **Frost's cyan** and the **blue shield atom (178)**; bioluminescent
**magenta** is the **hostile-shot pink**; rust orange sits near **Ember's orange**.

| Option | Base | Accents | Rust | Verdict |
| --- | --- | --- | --- | --- |
| **A (recommended)** | abyssal blue-green black (hue 165-175, value .05-.2), mid-water teal-green .4 | bioluminescent **mint-green 150-160** and **blue-violet 215-230**; pink only as small warning lamps (<1% of a tile) so shots keep popping against the complementary teal | low-saturation rust brown (hue 15-28, sat <= .45), never the red band | distinct from Frost's cyan and Verdant's lime; pink shots contrast strongly with teal |
| B | deep ocean blue 205-220 | cyan + magenta as the user said | as above | closest to the user's words but fights the shield atom, the shots and Frost; needs the readability sweep before approval |

Enemy light (`EnemyPalette.Theme.light`, option A): bioluminescent mint **#7CF2C0** (hue ~158);
boss flash: the same mint; boss heart colour: mint-white; warning accent: mint. All hues >= 22 from red.

### Hook (OPEN, scope decision)

"Slow tidal currents pushing the ship sideways and whirlpools". The ship is flown by finger and a
wormhole blink; a force on the ship is a **new gameplay system** touching `ShipReach`, the rails clamp,
boss fairness and many tests. Options, cheapest first:

1. **Cosmetic**: the backdrop `flow` strip and wisps drift diagonally; whirlpools are painted features
   (a `fires`-slot atlas) whose rotation is an ambient loop. No gameplay effect. *Ship in the first pass.*
2. **Hazard-only current**: roster behaviours get a lateral bias (a `Drift`/`Glide` with the world's current
   direction that changes every ~20 s, telegraphed by the flow art); the ship is untouched.
3. **Ship current**: a gentle sideways force with a telegraphed direction change and a whirlpool hazard
   that tugs the ship (new `WorldCurrent` system + tests: lane clamp, boss phases, tutorial unaffected).
   Only on the user's explicit OK, after World 5 ships.

### Planet (approach, planetfall, lift-off)

From space: a blue-green marble almost fully ocean with **a ring of anchored rig-platforms (an orbital
tether ring of oil-rig cities)**, white spiral storm bands, glowing sunken-city clusters on the night
side, thin mint rim. Limb: horizon of dark water under layered storm cloud, rig silhouettes, lit
cities beneath the surface. Cloud decks: heavy grey-teal storm tops with mint-lit crests (dark variant:
slate-violet-black). Entry plasma: white-hot core, mint-white and blue edge flames, **wide** (not a
ribbon). Breakthrough: spray, foam rings and white-water shards instead of ash. Streaks: rain-fine
white-teal speed lines.

### Backdrop (four sets; v4 = night side) -- flown low over the sea

| Set | Look | `flow` | Pieces that fit |
| --- | --- | --- | --- |
| v1 Open sea | dark swell, whitecap lines, kelp rafts, oil sheen | foam streaks drifting | rigs, oil platforms, tankers |
| v2 Rig field | a dense archipelago of rusted platforms joined by walkways and pipe bridges, flare stacks | slow wake lines | refinery rigs, pipe racks, cranes |
| v3 Drowned city | the surface shallow over a **sunken industrial city** glowing under the water: lit streets, towers breaking the surface | caustic ripples | towers, bridges, wrecks |
| v4 **Night** | black water, bioluminescent blooms and rig lights, storm lightning far off | glowing plankton drift | lit rigs, buoys, jellies' glow |

Ground classes for `ground_masks.py`: `W` open water (most of every tile: **this is the opposite of
Ember** -- landmarks pin to `B` built/`O` shallows/reef; `PieceRule` mapping: rigs/towers = Built or
Shallow-Open, vessels = Water, bridges = Span over Water, reef = Open), `B` platforms/walkways/city,
`O` sandbars/reef flats, `C` kelp/reef canopy.

Pieces (landmarks 16): oil rig x3, refinery rig x2, crane gantry, tanker, trawler barge x2, lighthouse
tower, sunken skyscraper cluster x2, pump platform, wind-turbine rig, mooring buoy cluster, bridge.
Pipes: barnacled pipe runs, manifolds, undersea pipe bridges, leaks (**bubble streams** as the
leak emitter), flare lines. Features: whirlpools, oil slicks, reef heads, wreck hulls, bubbling vents,
plankton blooms. Weather: storm-cloud banks (ceiling continues the planetfall deck), rain curtains,
spray mist, wind gusts, cloud palls. Sites (elite launch): **trench hatch, rig bay, reef dock, vent
stack, wreck hangar** (new `LandingKind`s, uniquely named: `TideTrenchHatch, TideRigDeck, TideReefDock,
TideVentStack, TideWreckBay`, with `KindOf` strings `tidetrenchhatch` ...).
Loops: flare flames, steam/stack smoke, rain, bubble streams, whirlpool spin, surf foam pulse, rig
beacons (mint + small pink), window lights, searchlights.
Rails (725x2170): barnacled, salt-streaked steel, kelp stains, lamp cores mint (not pink).

### The 12 roster units (proposal; behaviours use existing primitives)

| Key | Name | Design | Movement / attack / tell | Counter |
| --- | --- | --- | --- | --- |
| `tide_rock_brain` | Brain Coral | rounded coral boulder with barnacle pits | slow tumble, `Drift` | wide, slow |
| `tide_rock_staghorn` | Staghorn Spire | branching reef blade | `Glide` one slanted slice, little spin | reads early |
| `tide_rock_urchin` | Spine Urchin | spiked ball with a glowing core | `Sink` with spin | arrives sooner than it looks |
| `tide_rock_islet` | Kelp Islet (floating) | a floating reef slab under a kelp cap, tide pool, dripping | upright `Sway` + `Bob` | wide and slow |
| `tide_mine` | Limpet Mine | horned sea-mine on a barnacled clamp, mint core (atlas row) | `Laser`, 2 shots, rides the rail | be off the aim line |
| `tide_big` | Nautilus Bulwark | mollusk-armoured hull, shell opens on a glowing maw | holds a column; shell opens 1 s then a spread of pearl shots (`Shot` x3 fan) | stand between the shots |
| `tide_fighter_1` | Remora | small suckerfish drone | diagonal `Drift` then a straight `Lunge` | the dash never tracks |
| `tide_fighter_2` | Needlefish | long-beaked skiff | `Track` to line up, quick bolts straight down (x3) | keep moving |
| `tide_fighter_3` | Lantern Angler | anglerfish gunship, lure lamp | `Brake` hover; lure flares 0.8 s then one aimed bolt | move after the lure lights |
| `tide_fighter_4` | Hammerhead | armoured twin-cannon heavy | `Brake` + slow `Track`; full ring of 8 | back off, the ring opens with distance |
| `tide_chaser` | Wire Eel | mechanical eel | OPEN: needs a **new `ChaserStyle` (Slither: sinusoidal lunges)** or shares `Weaver` (duplicate; relax `EnemyBehaviourTest`) | slide sideways |
| `tide_alien` | Glow Jelly | jellyfish drone, trailing tendrils | `Pulse` + `Sway` in lines; tendrils lash when near (cosmetic) | pass on the sink |

Explosion material: a **Water** burst (new `TargetExplosion.Kind` + a new `Explosions.png` row) or reuse
`Ice` (OPEN; the first pass can reuse). Rails/boss/elites use the same kind through `KindForWorld`.

Screams (living occupants/creatures only): Needlefish, Lantern Angler, Hammerhead pilots, Remora pilot,
Glow Jelly (wet reedy chitter), elites with pilots. Not: eel, big, mine, rocks. Death-cue identities:
hydraulic burst, pressure hiss, bubbling gurgle, barnacle-crust crack, brass/steel under water (low-passed,
muffled), no beeps, no sonar pings (a ping is a tone!).

### Elites (6)

| Key | Name | Role -> existing brain | Attack idea | Site |
| --- | --- | --- | --- | --- |
| `tide_elite_riptide_lancer` | Riptide Lancer | interceptor -> `interceptor` | `lance_dash` variant through a foam wake | trench hatch |
| `tide_elite_trawler_maw` | Trawler Maw | hauler -> `hauler` | drops sinking **nets** (reuse `slag_drop` with a net shot; new shot kind only if needed) | rig bay |
| `tide_elite_abyss_lamp` | Abyss Lamp | kiter -> `skirmisher` | lure beam + `blink_shards` | reef dock |
| `tide_elite_brine_siege` | Brine Siege | artillery -> `siege` | depth-charge lobs (`siege_cannon` piercing shell, ringed spots) | vent stack |
| `tide_elite_manta_dive` | Manta | striker -> `striker` | wing dives (`claw_dive`) | wreck hangar |
| `tide_elite_pearl_bastion` | Pearl Bastion | shield platform -> `bastion` | `ward_curtain` of slow pearls with one gap | rig bay |

### Boss: IRON KRAKEN -- "DEEP-SEA DREADNOUGHT"

A kraken-shaped machine: riveted brass-and-steel mantle with a lamp eye, a beak, four hydraulic
tentacle arms ending in cannons. Three firing parts (seed `measure_emitters.py`): **Beak**, **Lamp**
(eye), **Tentacles** (L/R pair). Attacks (3, unlocked in thirds): (1) *beak spit*: `Fan` of 7 shots
(`Bolt`, `Bounce` once off the rails), tell0 beak opens; (2) *ink barrage*: `Lob` rain on 4 chosen
columns with lane telegraphs, tell1 the mantle vents; (3) *tentacle lasers*: `Beam` from both arms
sweeping inward with a safe middle, tell2 arms glow (hold .8 s, cooldown 1.3). Flash: mint; heart:
mint-white. Damage: stages crack the mantle, snap a tentacle, flood the breaches (water jets +
sparks), late stages drag a hanging arm; smoke = steam. Death: the mantle bursts, the lamp shatters, a
water-white shockwave ring.

### Difficulty and numbers (propose; validate with the pace tests)

`speedRampPerSecond .00385`, `enemyRampScale 1.50`, pilot load 4.0 (low speed) / 3.0 (high). Music: `""`
(the user supplies it).

### OPEN questions for the user

1. Palette option A or B (and is a rust accent acceptable next to Ember's orange)?
2. Hook scope: cosmetic first, hazard-only, or ship current?
3. Name: *Tide*, or *Abyss / Brine / Maelstrom*?
4. Chaser: new `Slither` style (new code) or accept a shared style?
5. New Water explosion row or reuse Ice for the first pass?
6. Is the night side (v4) a deep-sea abyss (dark, bioluminescent) or just night over the rigs?
7. Which units scream (default list above)?

---

## World 6 -- STORM (the gas giant)

**One line.** The ship rides inside a gas giant's churning banded atmosphere: lightning in the cloud
walls, floating refineries and skyhook platforms on the cloud sea, updrafts and hail.

### Palette (decide: OPEN)

The user's words: dark churning banded gas, lightning, electric-yellow/white and steel-violet that
must not collide with Space's magenta. Collision check: **steel-violet (~259-270) is the violet
capacitor atom (259)**; **electric yellow ~50-62 is within ~20 degrees of the amber star dust (37)**;
Space's indigo is ~240-250.

| Element | Recommendation |
| --- | --- |
| Base | bruised **slate/steel blue-grey, hue 215-230**, low saturation, value .05-.25 (not violet) |
| Lightning | **white to pale yellow, hue 55-62**, mostly white-hot with a pale-yellow edge; never orange |
| Cloud bands | alternating slate, graphite and a muted teal-grey (hue ~195, low sat) -- avoid Frost's pure cyan |
| Platforms/metal | gunmetal, matte black, small sodium-white work lights (keep amber <= trace) |
| Accent | cold white-blue (hue ~205) arc glow on the coils and rods |

Enemy light: **storm-white #F2F6FF** with a pale-yellow edge #FFF3A0; boss flash: white-yellow; heart
colour: pale yellow; warning accent: electric yellow (hue ~58).

### Hook (OPEN, scope decision)

"Lightning strikes telegraphed on the lane; updrafts". Options:

1. **Cosmetic**: lightning flashes inside the cloud walls (ambient loop + a brief global brightening of
   the backdrop, capped so the lane contrast guard holds: the flash must not wash enemies out); updrafts
   modulate the `flow` strip speed. No gameplay effect. *Ship in the first pass.*
2. **Strike hazard**: a world event on a timer (never in the first 20 s, boss or portal) paints a lane
   telegraph column (reuse the boss lane-telegraph sprite/`BossShots` telegraph look) ~1.2 s before a bolt
   hits it; hits cost a heart (fairness: at most one column at a time, never the pilot's current column on
   the telegraph frame). New system + tests (`HostileReachTest`-style reach, pacing, `WorldLeakTest`).
3. **Updraft** (gameplay speed modulation) -- touches `moveBackGround.speed`/`SpeedRamp`/pace tests;
   only with an explicit OK.

### Planet

From space: a banded gas giant (slate, graphite, teal-grey, one pale storm eye), a **skyhook ring** (a
thin orbital ring with hanging cables and platforms), flickering lightning clusters on the night side.
Limb: cloud-top horizon, towering anvil clouds, lightning inside, floating platforms. Decks: dark
churning cloud tops with white-lit crests (dark variant: near-black slate). Entry plasma: wide
white-yellow-blue shroud. Breakthrough: a lightning flash ring with cloud puffs. Streaks: rain/ice
speed lines. (A gas giant has no surface: its "lift-off" climbs out through the cloud tops as the others.)

### Backdrop (four sets; v4 = night side) -- flown above the cloud sea

| Set | Look | `flow` | Pieces |
| --- | --- | --- | --- |
| v1 Banded deck | long slate bands, vortex scars, shear lines | wind streaks | floating refineries, skyhooks |
| v2 Platform city | a cloud-sea of linked floating platforms, gantries, airship docks | fast cirrus | refinery floats, pipe racks, docks |
| v3 Eye of the storm | a great calm vortex, towering walls, shafts of light | spiral drift | anvil towers, drifting wrecks |
| v4 **Night** | near-black bands lit from inside by sheet lightning, platform lights | rain streaks | lit platforms, rods |

Ground classes: `W` = open cloud sea (flat bands, where vessels/airships go), `B` platforms, `O` calm
patches/eye, `C` churning thunderhead. Pieces (16): skyhook anchors x2, refinery float x3, gas-siphon
tower, airship dock, lightning-rod mast x2, floating gantry, wind-turbine raft, relay dish, storm
beacon, anvil-cloud tower. Pipes: siphon pipelines, vapour manifolds, cable spans. Features: vortex
scars, charged plasma pockets, wreck hulks, **hail fields**. Weather: anvil banks (ceiling continuing
the planetfall deck), cirrus wisps, rain curtains, ice mist, gusts, cloud palls. Sites (elite launch):
skyhook dock, refinery pad, balloon hangar, gantry hatch, cloud bay (`LandingKind`s prefixed `Storm`).
Loops: flare/siphon stack smoke, **sheet lightning** (a measured brightest/darkest ratio >= 4 like the
beacons, and kept short and dim enough not to hide enemies), arcs on rods, rain, hail glints, beacons
(white + small yellow), window lights. Rails: wind-scoured steel with ice rime and coil lamps (cold
white-blue).

### The 12 roster units (proposal)

| Key | Name | Design | Movement / attack / tell |
| --- | --- | --- | --- |
| `storm_rock_hail` | Hailstone | pitted ice-ore ball | steady spin, `Drift` |
| `storm_rock_ore` | Ore Shard | long ice-ore crystal blade | `Glide` one slanted slice |
| `storm_rock_slag` | Anvil Chunk | dark iron-ice chunk, static glint | `Sink` + spin |
| `storm_rock_islet` | Cloud Islet (floating) | platform fragment with an antenna, vapour trails | upright `Sway` + `Bob` |
| `storm_mine` | Rod Mine | lightning-rod sphere on a clamp, coil core (atlas row) | `Laser` x2, arcs |
| `storm_big` | Cloud Whale | armoured airship-whale, ballast gills | holds a column; gills open then a fan of 3 arc shots |
| `storm_fighter_1` | Stormray Pup | small manta drone | diagonal `Drift` + straight `Lunge` |
| `storm_fighter_2` | Stormray | larger manta | `Track` + aimed bolt, wing-flap tell |
| `storm_fighter_3` | Thunderhead | gunship with a cloud-seed cannon | `Brake` hover, charge flares 0.9 s, one heavy aimed shell |
| `storm_fighter_4` | Tempest Anvil | heavy twin-coil dreadnought | `Brake` + slow `Track`, ring of 8 |
| `storm_chaser` | Cyclone Drone | spinning rotor vortex drone | OPEN: new `Spiral` chaser style or shared |
| `storm_alien` | Static Mote | ball-lightning sprite in a cage | `Pulse` + `Sway`, flares when near |

Explosion material: **Arc** (new row) or reuse `Metal`/`Ice` (OPEN). Sound identity: **no electrical
beeps** -- crackle as filtered noise bursts, thunder as low rumble, hail clatter, hydraulic gasps,
muffled; screams only for pilots/creatures.

### Elites (6)

Skyhook Harrier (interceptor), Refinery Tender (hauler, drops sinking canisters), Rod Dancer
(skirmisher; blink + arc fan), Anvil Cloud (siege; lane-telegraphed lightning shell), Vortex Reaver
(`reaver` orbit raider, `crescent_volley`), Static Bastion (`bastion`, `ward_curtain` of arcs).

### Boss: TEMPEST ENGINE -- "STORM-MAKING TURBINE"

A colossal floating turbine-engine: a central core drum between two coil towers, a lightning-rod crown
and exhaust vents. Parts: **Core**, **Coils** (L/R), **Crown**. Attacks: (1) *arc fan*: `Fan` out of the
core, bounce once; (2) *lane strike*: `Lob`/lane telegraphs on chosen columns, the crown flares first
(the boss-level version of the lane-strike hook); (3) *coil beams*: two `Beam`s sweeping, safe middle.
Damage: coils snap and spark, turbine blades tear off, vents burst steam; death: core overload with a
white-yellow flash and ring. Flash: white-yellow; heart: pale yellow.

### Difficulty and numbers (propose)

`speedRampPerSecond .00405`, `enemyRampScale 1.65`, pilot load 4.5 / 3.5.

### OPEN questions for the user

1. Violet or slate base (the violet capacitor atom is 259)? Is amber/brass allowed at all (star dust 37)?
2. Hook scope (cosmetic / strike hazard / updraft)?
3. Name: *Storm*, *Tempest*, *Jovian*?
4. Chaser style (new `Spiral` or shared)?
5. Arc explosion row or reuse?
6. Does lightning flash the whole backdrop (needs a readability cap) or only inside the cloud walls?

---

## Shared order of work for each world (summary of SKILL.md section 3)

1. Phase 0 with the user (answers to the OPEN lists).
2. Day one: Phase 9 scaffold (Claude) + Codex J1 (planet) and J2 (backdrop A) in parallel.
3. Codex J3 (backdrop B) + J4 (enemies); then J5 (loops) + J6 (boss base); then J7 (deaths) + J8 (elites);
   then J9 (boss damage) + J10 (sounds); rails in a gap.
4. Claude: Phase 10 once the planet art lands (this is the step that makes the old last world stop
   looping), 11 backdrop wiring, 12 roster, 13 boss, 14 elites, 15 audio, 16 meta.
5. Phase 17 gate. Only then start World 6, whose Phase 10 moves `autoLoop` from Tide to Storm.
