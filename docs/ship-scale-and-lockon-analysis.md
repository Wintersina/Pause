# Ship scale, lock-on and design-language analysis

Branch `feat/ship-scale-lockon`. Written before any gameplay edit (phase 1); the implementation notes at the end are updated as the work lands.
Generated numbers come from `docs/tools/ship_audit/` (measure.py, sheet.py, lockon.py, lang.py): re-run them after any art or size change.

Contact sheets (every ship, current vs proposed, with the flown hull beside it): `docs/ship-scale-analysis/sheet_elites.png`, `docs/ship-scale-analysis/sheet_enemies.png`.

## 0. How big things are on a phone

* The gameplay camera is width-fitted: the view is **7.44 u wide** on every phone (`CameraFit.GameplayHalfWidth` 3.72), 13.2 u (16:9) to 18.2 u tall.
* So 1 u = **145 px** on a 1080-wide phone (the 1080x2420 reference) and **97 px** on a 720-wide small phone (720x1280 / 720x1600). 540-wide budget phones: 73 px.
* The flown hull is `ReferenceHullSize` 0.58 u x `ShipScale.Main` 1.35 = **0.78 u across = 114 px (1080) / 76 px (720)**.
* Art: enemy / fodder flipbooks are 192 px cells shown 0.66-1.3 u wide, i.e. **0.33-0.55 screen px per art px at 720** (the drawings are downscaled 2-3x; a 1 px outline or a 3 px eye is below one screen pixel). Elite strips are 192 px cells shown 1.45-2.07 u: 0.76-1.04 screen px per art px at 720 (about 1:1). Bosses: 384 px cells at 3.3 u (about 1.25 screen px per art px at 1080).
* Detail rule of thumb used here: a feature (eye, weapon port, cockpit, engine glow) is readable at **>= 3 screen px across**, and a 1 px outline survives only at >= 0.75 screen px per art px.

## 1. Lock-on

### 1.1 Why it feels like "locking in"

The framework gives every elite **perfect, zero-latency knowledge of the pilot** and aims with it at the last possible instant:

1. `EliteShip.Perceive`: `seen = pilot position, this frame`. Only a pause-teleport ever makes it lose you. There is no reaction delay.
2. Brains steer to `Goal(seen)` every frame: the gunship copies your height and x (plus a lane offset), the interceptor sits `followDistance` behind your exact x, the skirmisher / striker / reaver orbit your exact centre, the lancer holds a flank of your exact x. The only softening is the speed cap.
3. Many attacks read `ship.Seen` in `BeginAction`, i.e. **after** the tell is over: `blink_shards` (fires at your position at the moment it fires, after teleporting), `resin_mortar`, `ward_curtain` / `pearl_curtain` (the row is centred exactly on you), `gravity_sling`, `floe_cast`, `frost_bloom` orb, `armour_shatter` plated shards. A tell that tracks you all the way to the shot is not a warning: whatever you do during the tell is read and answered. Only attacks that lock at the **start** of the tell and draw where they will land (all Verdant / Tide telegraphs, `claw_dive`, `crescent_volley`, `ice_ram`) are fair, and those are the model.
4. Nose: facing turns at `turnRate x 1.4` (up to 420 deg/s) towards you for the interceptor, skirmisher and lancer brains: the nose snaps onto you, which reads as a hard lock.
5. Tells are short where it matters: Ash Wraith 0.45 s, Brass Vulture 0.5 s, Coalrunner 0.5 s, Sunstoke / Cryo Siren / Whiteout Sentinel 0.6 s. With aim live to the shot the warning is 0.

There is no velocity lead anywhere (nothing predicts you), so the problem is exactness and latency, not prediction.

### 1.2 Per-elite measurement (before / after the proposal)

Columns: `follow` = how the brain uses the pilot position; `aim` = when the shot's aim is fixed; `tracked` = seconds of tell during which the aim still follows you; `warning` = seconds between the aim being fixed and the shot / dash starting (what the player can react to); severity 0-5 (higher = locks in harder).

| Elite | World | Brain / attack | follow (now) | aim now | tell s (now -> new) | tracked s (now -> new) | warning s (now -> new) | sev |
|---|---|---|---|---|---|---|---|---|
| Ash Wraith | ember | skirmisher / blink_shards | exact centre, nose snaps | LIVE until the shot | 0.45 -> 0.65 | 0.45 -> 0.33 | 0.00 -> 0.33 | 5 |
| Lens Matriarch | desert | skirmisher / blink_shards | exact centre, nose snaps | LIVE until the shot | 0.70 -> 0.70 | 0.70 -> 0.35 | 0.00 -> 0.35 | 4 |
| Rod Dancer | storm | skirmisher / blink_shards | exact centre, nose snaps | LIVE until the shot | 0.70 -> 0.70 | 0.70 -> 0.35 | 0.00 -> 0.35 | 4 |
| Dune Stalker | desert | interceptor / lance_dash | exact (zero lag), nose snaps at turnRate x1.4 | tracks then locks | 0.85 -> 0.85 | 0.42 -> 0.42 | 0.42 -> 0.42 | 3 |
| Sunstoke | ember | interceptor / lance_dash | exact (zero lag), nose snaps at turnRate x1.4 | tracks then locks | 0.60 -> 0.60 | 0.30 -> 0.30 | 0.30 -> 0.30 | 3 |
| Cryo Siren | frost | kiter / frost_bloom | exact | LIVE until the shot | 0.60 -> 0.60 | 0.60 -> 0.30 | 0.00 -> 0.30 | 3 |
| Floe Harrower | frost | herder / floe_cast | exact | LIVE until the shot | 0.80 -> 0.80 | 0.80 -> 0.40 | 0.00 -> 0.40 | 3 |
| Rift Lancer | space | lancer / rift_rail | exact flank, nose snaps | tracks then locks | 0.85 -> 0.85 | 0.51 -> 0.42 | 0.34 -> 0.42 | 3 |
| Skyhook Harrier | storm | interceptor / lance_dash | exact (zero lag), nose snaps at turnRate x1.4 | tracks then locks | 0.85 -> 0.85 | 0.42 -> 0.42 | 0.42 -> 0.42 | 3 |
| Riptide | tide | surfer / surf_dash | exact | tracks then locks | 0.90 -> 0.90 | 0.45 -> 0.45 | 0.45 -> 0.45 | 3 |
| Mirror Bastion | desert | bastion / ward_curtain | already slow | LIVE until the shot | 1.00 -> 1.00 | 1.00 -> 0.50 | 0.00 -> 0.50 | 2 |
| Scorpion Walker | desert | striker / claw_dive | exact centre | locked at tell start (drawn) | 0.75 -> 0.75 | 0.00 -> 0.00 | 0.75 -> 0.75 | 2 |
| Brass Vulture | ember | striker / claw_dive | exact centre | locked at tell start (drawn) | 0.50 -> 0.65 | 0.00 -> 0.00 | 0.50 -> 0.65 | 2 |
| Whiteout Sentinel | frost | ironclad / armour_shatter | exact aim of each leg | tracks then locks | 0.60 -> 0.60 | 0.30 -> 0.30 | 0.30 -> 0.30 | 2 |
| Eventide Bastion | space | bastion / ward_curtain | already slow | LIVE until the shot | 0.90 -> 0.90 | 0.90 -> 0.45 | 0.00 -> 0.45 | 2 |
| Singularity Hauler | space | tug / gravity_sling | already slow | LIVE until the shot | 0.80 -> 0.80 | 0.80 -> 0.40 | 0.00 -> 0.40 | 2 |
| Static Bastion | storm | bastion / ward_curtain | already slow | LIVE until the shot | 1.00 -> 1.00 | 1.00 -> 0.50 | 0.00 -> 0.50 | 2 |
| Pearl Warden | tide | pearler / pearl_curtain | already slow | LIVE until the shot | 1.00 -> 1.00 | 1.00 -> 0.50 | 0.00 -> 0.50 | 2 |
| Leafblade | verdant | diver / leaf_dive | exact | locked at tell start (drawn) | 1.10 -> 1.10 | 0.00 -> 0.00 | 1.10 -> 1.10 | 2 |
| Resin Warden | verdant | warden / resin_mortar | no follow | LIVE until the shot | 0.70 -> 0.70 | 0.70 -> 0.35 | 0.00 -> 0.35 | 2 |
| Coalrunner | ember | gunship / broadside | exact x and y (+.1), speed x1.1 | not aimed | 0.50 -> 0.50 | 0.00 -> 0.00 | 0.50 -> 0.50 | 1 |
| Glacier Tender | frost | tender / drone_deploy | exact | not aimed | 0.80 -> 0.80 | 0.00 -> 0.00 | 0.80 -> 0.80 | 1 |
| Rimebreaker | frost | breaker / ice_ram | exact centre of the sweep | locked at tell start (drawn) | 0.65 -> 0.65 | 0.00 -> 0.00 | 0.65 -> 0.65 | 1 |
| Orbit Reaver | space | reaver / crescent_volley | exact centre | locked at tell start (drawn) | 0.60 -> 0.60 | 0.00 -> 0.00 | 0.60 -> 0.60 | 1 |
| Thunderfin | tide | stormray / thunder_strike | exact | locked at tell start (drawn) | 1.40 -> 1.40 | 0.00 -> 0.00 | 1.40 -> 1.40 | 1 |
| Thornlash | verdant | thorn / vine_lash | exact | locked at tell start (drawn) | 1.00 -> 1.00 | 0.00 -> 0.00 | 1.00 -> 1.00 | 1 |
| Caravan Hauler | desert | hauler / slag_drop | already slow | not aimed | 0.80 -> 0.80 | 0.00 -> 0.00 | 0.80 -> 0.80 | 0 |
| Glass Cannon | desert | siege / siege_cannon | already slow | not aimed | 1.35 -> 1.35 | 0.00 -> 0.00 | 1.35 -> 1.35 | 0 |
| Cauterizer | ember | siege / siege_cannon | already slow | not aimed | 1.10 -> 1.10 | 0.00 -> 0.00 | 1.10 -> 1.10 | 0 |
| Kilnback | ember | hauler / slag_drop | already slow | not aimed | 0.60 -> 0.60 | 0.00 -> 0.00 | 0.60 -> 0.60 | 0 |
| Anvil Cloud | storm | siege / siege_cannon | already slow | not aimed | 1.35 -> 1.35 | 0.00 -> 0.00 | 1.35 -> 1.35 | 0 |
| Refinery Tender | storm | hauler / slag_drop | already slow | not aimed | 0.80 -> 0.80 | 0.00 -> 0.00 | 0.80 -> 0.80 | 0 |
| Bathyscaphe | tide | abyss / depth_charge | already slow | locked at tell start (drawn) | 1.10 -> 1.10 | 0.00 -> 0.00 | 1.10 -> 1.10 | 0 |
| Trawler | tide | netter / net_cast | already slow | locked at tell start (drawn) | 1.10 -> 1.10 | 0.00 -> 0.00 | 1.10 -> 1.10 | 0 |
| Sporebloom | verdant | drifter / spore_burst | already slow | locked at tell start (drawn) | 1.00 -> 1.00 | 0.00 -> 0.00 | 1.00 -> 1.00 | 0 |
| Timber Hauler | verdant | logger / log_roll | already slow | locked at tell start (drawn) | 0.90 -> 0.90 | 0.00 -> 0.00 | 0.90 -> 0.90 | 0 |

"tracked" for `L` attacks was the whole tell (and, for `blink_shards`, also the blink and 35% of the action).

### 1.3 Proposal (generic, data-driven, personality kept per elite)

Three new knobs, all in `EliteDef` with defaults, so no elite is rewritten:

1. **`followLag` (s)** - the pilot position the *brain* steers to is `seen` run through an exponential low-pass with this time constant (`EliteShip.Follow`, fed to `Brain.Goal`; snapped on spawn and teleports). An elite no longer mirrors every twitch: it converges on you in ~3 x `followLag`. Values by personality: twitchy skirmishers 0.25, stalkers and strikers 0.30-0.35, the gunship 0.45 (the worst mirror), thorn lane 0.40, slow haulers / bastions / sieges / tugs 0.25 (they already crawl, so it is a no-op there), station elites (warden) 0.20.
2. **`aimCommit` (share of the tell, default 0.5)** - the aim point the attacks use (`EliteShip.AimSeen`) is live for the first `aimCommit` of the tell, then frozen. Every `ship.Seen` read in an attack's action moves to `AimSeen`: `blink_shards`, `resin_mortar`, `ward_curtain` / `pearl_curtain`, `gravity_sling`, `floe_cast`, `frost_bloom` orb, `armour_shatter` plated shards. Lance / rift rail / armour charge already lock at 0.5-0.6; they are unchanged. After the commit the player has `tell x (1 - aimCommit)` to move.
3. **Minimum tell**: aimed attacks (`T` and `L` above) get an effective tell of at least **0.6 s**, so the post-commit warning is >= 0.3 s (human reaction + a short sidestep); Ash Wraith (0.45) and Brass Vulture (0.5) go to 0.65 in data.
4. **`faceTurnMax` (deg/s, default 300)** caps how fast the hull turns to face you, whatever `turnRate` x 1.4 says (the skirmishers, striker, interceptor and lancer are 364-420): the nose now swings round instead of snapping.

What each personality keeps: Ash Wraith / Lens Matriarch / Rod Dancer stay fast, twitchy blinkers - they blink and shoot where you *were* half a tell ago, so the trick is to move after the glow; Sunstoke / Dune Stalker / Skyhook Harrier / Riptide are still relentless stalkers with a committed, baitable ram, just slower to re-centre on a juking player; Coalrunner keeps its lane beside you but trails your height and column by ~0.45 s instead of welding to them; Warden / bastion curtains and mortars keep their gap geometry, but the row is where you were at the commit, not where you are at the shot.

Tests (gate lane): `EliteLockOnTest` asserts for every def: `followLag` in [0.15, 0.6]; effective tell x (1 - aimCommit) >= 0.3 for aimed attacks; the nose never turns faster than `faceTurnMax` (stepped); after the commit, moving the pilot 2 u does not move any `AimSeen`-driven first shot/spot by more than the pilot's drift before the commit (aim is frozen); the brain's goal lags a stepped pilot by about `followLag`.

## 2. Size audit

### 2.1 Tier system
All sizes are the drawn silhouette's longest side, in world units, from the idle cells (not the cell). "x player" is relative to the flown hull (0.78 u).

| Tier | What | Today (drawn, u) | Today x player | Proposed | Proposed x player | px @1080 / @720 (proposed) |
|---|---|---|---|---|---|---|
| T0 fodder | rocks, mites / aliens | 0.52-0.74 | 0.7-0.9 | **x1.20** -> 0.62-0.89 | 0.8-1.1 | 90-129 / 60-86 |
| T1 fighter | fighters, chasers | 0.75-0.90 | 0.95-1.15 | **x1.20** -> 0.90-1.08 | 1.15-1.4 | 131-157 / 87-104 |
| T2 heavy | the world "Big" (Bastion, Magma Skull, ...) | 1.10-1.22 | 1.4-1.6 | **x1.20** -> 1.32-1.46 | 1.7-1.9 | 192-212 / 128-141 |
| T3 elite | all elites | 0.95-1.69 | 1.2-2.2 | **floor 1.50 u** (1.65 u for armoured / slow classes) | >= 1.9 / 2.1 | >= 218 / 145 (240 / 160 heavy) |
| T4 boss | the seven bosses | 2.85-3.21 | 3.6-4.1 | unchanged | | 413-466 / 275-311 |
| rail mines | | 0.65 u | 0.83 | unchanged (rail-mounted; collider pinned) | | 94 / 63 |

Rules: (a) tiers strictly increase: fodder < fighter < heavy (1.35 x fighter) < elite floor (1.07 x heavy, 1.2 x for the armoured 1.65 u class) < boss (about 2 x elite); (b) elite floor = 1.9 x the flown hull (`ShipScale.Main` x `ReferenceHullSize`), armoured classes (hauler, bastion, siege, tug, herder, tender, ironclad, logger, abyss, netter, pearler brains) 2.1 x, so the player's own ship never reads as larger than the elite it is fighting; (c) elites that are already above the floor (Ember: Ash Wraith 1.51, Vulture 1.56, Cauterizer 1.69, Sunstoke 1.47 -> 1.5) are left alone, which keeps the originals the user already approved; (d) hit radius scales with the drawing (x0.95 of the size factor, 5% leniency), never sprite-only.

### 2.2 Elites (36)

`k` = proposed scale of `cellWorldSize` and `hullRadius` (art, muzzles, nozzles, hearts, FX all follow because they are in art px / cell fractions). Detail columns: the three largest bright accent features found in the drawing (px across on screen at 720), before -> after; a value under 3 is flagged `!`.

| Elite | World | Long side now (u) | px 1080 / 720 | x player | k | Long side new (u) | px 1080 / 720 new | cellWorldSize | hullRadius | accent features @720 now -> after | pale outline | priority |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Trawler | tide | 0.98 | 142 / 95 | 1.26 | x1.68 | 1.65 | 240 / 160 | 1.50 -> 2.52 | 0.31 -> 0.49 | 2.6! / 2.1! / 1.7! -> 4.3 / 3.5 / 2.9! | no | HIGH |
| Timber Hauler | verdant | 1.00 | 145 / 96 | 1.28 | x1.65 | 1.65 | 239 / 159 | 1.50 -> 2.47 | 0.32 -> 0.50 | 2.4! / 2.3! / 2.3! -> 4.0 / 3.7 / 3.7 | yes | HIGH |
| Refinery Tender | storm | 1.02 | 148 / 99 | 1.31 | x1.61 | 1.65 | 239 / 159 | 1.60 -> 2.58 | 0.33 -> 0.51 | 2.6! / 2.6! / 2.4! -> 4.1 / 4.1 / 3.9 | no | HIGH |
| Static Bastion | storm | 1.08 | 157 / 104 | 1.38 | x1.52 | 1.65 | 239 / 159 | 1.60 -> 2.43 | 0.34 -> 0.49 | 2.6! / 2.4! / 2.4! -> 3.9 / 3.7 / 3.7 | no | HIGH |
| Thunderfin | tide | 0.98 | 142 / 95 | 1.26 | x1.52 | 1.50 | 217 / 144 | 1.50 -> 2.28 | 0.28 -> 0.40 | 2.3! / 2.1! / 1.9! -> 3.4 / 3.2 / 2.9! | no | HIGH |
| Dune Stalker | desert | 1.09 | 158 / 105 | 1.40 | x1.37 | 1.50 | 217 / 145 | 1.50 -> 2.06 | 0.30 -> 0.39 | 1.9! / 1.5! / 1.5! -> 2.6! / 2.0! / 2.0! | yes | HIGH |
| Skyhook Harrier | storm | 1.09 | 158 / 105 | 1.40 | x1.37 | 1.50 | 217 / 145 | 1.50 -> 2.06 | 0.30 -> 0.39 | 2.7! / 2.7! / 2.3! -> 3.7 / 3.7 / 3.1 | no | HIGH |
| Bathyscaphe | tide | 0.97 | 140 / 93 | 1.24 | x1.70 | 1.65 | 239 / 159 | 1.50 -> 2.55 | 0.33 -> 0.53 | 9.3 / 5.3 / 2.7! -> 15.8 / 8.9 / 4.6 | no | HIGH |
| Leafblade | verdant | 1.11 | 161 / 107 | 1.42 | x1.35 | 1.50 | 217 / 144 | 1.50 -> 2.03 | 0.34 -> 0.44 | 2.4! / 1.9! / 1.7! -> 3.3 / 2.6! / 2.3! | yes | HIGH |
| Lens Matriarch | desert | 1.15 | 166 / 111 | 1.47 | x1.31 | 1.50 | 218 / 145 | 1.50 -> 1.97 | 0.36 -> 0.45 | 2.1! / 1.7! / 1.5! -> 2.7! / 2.2! / 1.9! | no | HIGH |
| Caravan Hauler | desert | 1.01 | 146 / 97 | 1.29 | x1.64 | 1.65 | 240 / 160 | 1.60 -> 2.62 | 0.33 -> 0.51 | 4.2 / 3.0 / 2.7! -> 6.8 / 4.9 / 4.5 | yes | HIGH |
| Scorpion Walker | desert | 1.15 | 167 / 111 | 1.47 | x1.30 | 1.50 | 217 / 145 | 1.55 -> 2.02 | 0.33 -> 0.41 | 2.2! / 2.0! / 1.5! -> 2.8! / 2.6! / 2.0! | yes | HIGH |
| Riptide | tide | 1.16 | 167 / 111 | 1.48 | x1.30 | 1.50 | 218 / 145 | 1.50 -> 1.95 | 0.30 -> 0.37 | 2.6! / 2.4! / 1.9! -> 3.3 / 3.1 / 2.5! | no | HIGH |
| Pearl Warden | tide | 0.95 | 138 / 92 | 1.22 | x1.73 | 1.65 | 239 / 159 | 1.50 -> 2.59 | 0.33 -> 0.54 | 5.1 / 4.6 / 4.6 -> 8.9 / 7.9 / 7.9 | no | HIGH |
| Thornlash | verdant | 1.08 | 156 / 104 | 1.38 | x1.39 | 1.50 | 217 / 145 | 1.50 -> 2.08 | 0.35 -> 0.46 | 1.7! / 1.5! -> 2.4! / 2.1! | yes | HIGH |
| Anvil Cloud | storm | 1.08 | 157 / 104 | 1.38 | x1.52 | 1.65 | 238 / 159 | 1.65 -> 2.51 | 0.38 -> 0.55 | 3.3 / 3.1 / 3.0! -> 4.9 / 4.7 / 4.5 | no | HIGH |
| Rod Dancer | storm | 1.15 | 166 / 111 | 1.47 | x1.31 | 1.50 | 218 / 145 | 1.50 -> 1.97 | 0.36 -> 0.45 | 3.2 / 2.8! / 2.8! -> 4.2 / 3.7 / 3.7 | no | HIGH |
| Floe Harrower | frost | 1.01 | 146 / 97 | 1.29 | x1.64 | 1.65 | 240 / 160 | 1.60 -> 2.62 | 0.38 -> 0.59 | 13.2 / 10.5 / 7.0 -> 21.7 / 17.2 / 11.6 | no | HIGH |
| Glacier Tender | frost | 1.16 | 168 / 112 | 1.48 | x1.42 | 1.65 | 239 / 159 | 1.55 -> 2.20 | 0.36 -> 0.49 | 3.8 / 3.4 / 2.9! -> 5.5 / 4.8 / 4.2 | no | med |
| Whiteout Sentinel | frost | 1.05 | 152 / 101 | 1.34 | x1.57 | 1.65 | 239 / 159 | 1.55 -> 2.43 | 0.33 -> 0.49 | 5.3 / 5.1 / 4.9 -> 8.3 / 8.0 / 7.7 | no | med |
| Glass Cannon | desert | 1.08 | 157 / 104 | 1.38 | x1.52 | 1.65 | 238 / 159 | 1.65 -> 2.51 | 0.38 -> 0.55 |  ->  | no | med |
| Mirror Bastion | desert | 1.12 | 162 / 108 | 1.43 | x1.48 | 1.65 | 239 / 159 | 1.60 -> 2.37 | 0.34 -> 0.48 | 3.5 / 3.5 / 3.0 -> 5.2 / 5.2 / 4.5 | no | med |
| Rimebreaker | frost | 1.03 | 149 / 99 | 1.32 | x1.45 | 1.50 | 217 / 144 | 1.60 -> 2.32 | 0.25 -> 0.34 | 6.4 / 6.4 / 3.8 -> 9.3 / 9.3 / 5.4 | no | med |
| Sporebloom | verdant | 1.09 | 158 / 105 | 1.40 | x1.37 | 1.50 | 217 / 145 | 1.50 -> 2.06 | 0.33 -> 0.43 | 5.4 / 4.6 / 3.7 -> 7.4 / 6.3 / 5.1 | yes | med |
| Singularity Hauler | space | 1.24 | 180 / 120 | 1.59 | x1.33 | 1.65 | 240 / 160 | 1.55 -> 2.06 | 0.32 -> 0.40 | 7.9 / 5.4 / 3.4 -> 10.6 / 7.2 / 4.5 | no | med |
| Coalrunner | ember | 1.15 | 167 / 111 | 1.47 | x1.30 | 1.50 | 217 / 145 | 1.56 -> 2.03 | 0.30 -> 0.37 | 5.3 / 5.3 / 5.1 -> 6.9 / 6.9 / 6.6 | yes | med |
| Eventide Bastion | space | 1.28 | 185 / 123 | 1.63 | x1.29 | 1.65 | 238 / 159 | 1.55 -> 2.00 | 0.34 -> 0.42 | 9.6 / 6.0 / 6.0 -> 12.4 / 7.8 / 7.7 | no | med |
| Cryo Siren | frost | 1.20 | 174 / 116 | 1.54 | x1.25 | 1.50 | 218 / 145 | 1.50 -> 1.88 | 0.26 -> 0.31 | 5.5 / 4.9 / 4.9 -> 6.8 / 6.1 / 6.1 | no | low |
| Rift Lancer | space | 1.20 | 174 / 116 | 1.54 | x1.25 | 1.50 | 218 / 145 | 1.50 -> 1.88 | 0.26 -> 0.31 | 6.0 / 6.0 / 4.0 -> 7.5 / 7.5 / 5.0 | no | low |
| Resin Warden | verdant | 1.20 | 174 / 116 | 1.54 | x1.25 | 1.50 | 218 / 145 | 1.50 -> 1.88 | 0.36 -> 0.43 | 12.2 / 11.2 / 3.2 -> 15.3 / 14.0 / 4.0 | no | low |
| Orbit Reaver | space | 1.28 | 186 / 124 | 1.64 | x1.17 | 1.50 | 217 / 145 | 1.60 -> 1.87 | 0.30 -> 0.33 | 4.7 / 4.4 / 4.0 -> 5.5 / 5.1 / 4.6 | no | low |
| Kilnback | ember | 1.45 | 210 / 140 | 1.85 | x1.14 | 1.65 | 239 / 159 | 1.77 -> 2.02 | 0.34 -> 0.37 | 6.7 / 4.8 / 4.6 -> 7.6 / 5.5 / 5.3 | no | low |
| Sunstoke | ember | 1.47 | 213 / 142 | 1.88 | x1.02 | 1.50 | 217 / 145 | 1.80 -> 1.84 | 0.36 -> 0.35 | 9.5 / 6.9 / 6.8 -> 9.7 / 7.0 / 6.9 | no | low |
| Ash Wraith | ember | 1.51 | 219 / 146 | 1.93 | x1.00 | 1.51 | 219 / 146 | 1.85 -> 1.85 | 0.29 -> 0.29 | 6.9 / 6.0 / 5.3 -> 6.9 / 6.0 / 5.3 | yes | low |
| Brass Vulture | ember | 1.56 | 226 / 150 | 1.99 | x1.00 | 1.56 | 226 / 150 | 1.92 -> 1.92 | 0.33 -> 0.33 | 6.5 / 3.5 / 3.3 -> 6.5 / 3.5 / 3.3 | no | low |
| Cauterizer | ember | 1.69 | 245 / 163 | 2.16 | x1.00 | 1.69 | 245 / 163 | 2.07 -> 2.07 | 0.43 -> 0.43 | 5.4 / 5.0 / 4.2 -> 5.4 / 5.0 / 4.2 | no | low |

Reading it: the 6 Desert, 5 Storm, 5 Tide, 5 Verdant, 5 Frost and 3 Space elites are only 1.2-1.5 x the player's ship (0.95-1.2 u, 92-111 px at 720) - the Tide set (Bathyscaphe 0.97, Pearl Warden 0.95, Trawler 0.98, Thunderfin 0.98) and Frost Floe Harrower / Rimebreaker (1.01-1.03) are the smallest and need x1.5-1.7. Their art draws only 63-67% of its cell (the cell is 1.5 u, the ship 0.95-1.0 u), which is why they look small next to Ember's 1.45-1.7 u at the same `cellWorldSize`: **the cell size is not the ship size**. The Ember originals (the ones that read well) are 82% fill. Storm / Desert / Tide / Verdant elites also have most of their accents under 3 screen px at 720 (Storm 2.4-3.3, Tide 1.7-2.6, Verdant Thornlash 1.5-1.7).

### 2.3 Enemies, fodder and heavies (75) at x1.2

Same method; `px` = longest side on screen. The enemy flipbooks are the least readable things in the game: 0.33-0.45 screen px per art px at 720 (fighters 0.45, rocks 0.33, aliens 0.34-0.49), so a 4 px eye is 1.5 px and a 1 px pale outline cannot render. x1.2 only buys 20% (to 0.40-0.54): it fixes the size tier but **not** the thin-detail problem, which needs the Codex redraw list in 2.5.

| Ship | World | Role | Long side (u) | px 1080 / 720 | x player | art->screen px @720 | x1.2 -> u | accent features @720 now -> x1.2 | pale outline |
|---|---|---|---|---|---|---|---|---|---|
| Mirage Wisp | desert | alien | 0.74 | 107 / 71 | 0.95 | 0.42 | 0.89 | 2.7! / 1.3! / 1.3! -> 3.2 / 1.6! / 1.5! | no |
| Sandcrawler | desert | heavy | 1.18 | 171 / 114 | 1.50 | 0.49 | 1.41 | 5.7 / 5.2 / 5.1 -> 6.9 / 6.2 / 6.2 | yes |
| Scarab Drone | desert | chaser | 0.90 | 130 / 86 | 1.15 | 0.53 | 1.08 | 3.4 / 3.0 / 2.8! -> 4.0 / 3.6 / 3.3 | yes |
| Sand Skimmer | desert | fighter | 0.87 | 126 / 84 | 1.12 | 0.52 | 1.05 | 6.7 / 5.6 / 4.2 -> 8.1 / 6.7 / 5.1 | no |
| Glint Hawk | desert | fighter | 0.80 | 116 / 77 | 1.02 | 0.45 | 0.96 | 4.5 / 4.2 / 3.6 -> 5.4 / 5.0 / 4.3 | no |
| Lens Gunner | desert | fighter | 0.79 | 114 / 76 | 1.01 | 0.45 | 0.94 | 4.0 / 3.8 / 2.1! -> 4.8 / 4.5 / 2.5! | no |
| Dust Devil Tank | desert | fighter | 0.82 | 119 / 79 | 1.05 | 0.45 | 0.98 | 2.8! / 2.7! / 2.6! -> 3.4 / 3.3 / 3.1 | no |
| Dune Boulder | desert | rock | 0.61 | 88 / 58 | 0.78 | 0.37 | 0.73 | 4.5 / 3.4 / 2.9! -> 5.4 / 4.1 / 3.5 | no |
| Fulgurite Husk | desert | rock | 0.61 | 88 / 58 | 0.78 | 0.37 | 0.73 | 2.8! / 2.5! / 2.5! -> 3.4 / 3.0! / 3.0! | no |
| Dune Slab | desert | rock | 0.59 | 85 / 56 | 0.75 | 0.33 | 0.71 | 3.2 / 2.6! / 2.6! -> 3.8 / 3.2 / 3.1 | no |
| Glass Spire | desert | rock | 0.56 | 80 / 53 | 0.71 | 0.33 | 0.67 | 1.9! / 1.6! / 1.5! -> 2.3! / 2.0! / 1.9! | no |
| Ember Imp | ember | alien | 0.74 | 106 / 71 | 0.94 | 0.46 | 0.88 | 8.9 / 4.4 / 3.8 -> 10.6 / 5.2 / 4.5 | yes |
| Magma Skull | ember | heavy | 1.22 | 176 / 117 | 1.56 | 0.49 | 1.46 | 6.3 / 6.1 / 5.8 -> 7.6 / 7.4 / 7.0 | no |
| Cinder Fang | ember | chaser | 0.82 | 118 / 79 | 1.04 | 0.48 | 0.98 | 7.6 / 4.6 / 3.4 -> 9.1 / 5.5 / 4.1 | yes |
| Cinder | ember | fighter | 0.78 | 113 / 75 | 1.00 | 0.45 | 0.94 | 3.8 / 1.4! / 1.0! -> 4.6 / 1.7! / 1.2! | yes |
| Scorch | ember | fighter | 0.81 | 117 / 78 | 1.04 | 0.45 | 0.97 |  ->  | yes |
| Brand | ember | fighter | 0.80 | 116 / 77 | 1.02 | 0.45 | 0.96 | 1.6! / 1.4! / 0.9! -> 1.9! / 1.7! / 1.1! | yes |
| Pyre | ember | fighter | 0.81 | 117 / 78 | 1.04 | 0.45 | 0.97 | 6.2 / 3.6 / 3.4 -> 7.5 / 4.3 / 4.0 | yes |
| Cinder Chunk | ember | rock | 0.54 | 78 / 52 | 0.69 | 0.33 | 0.65 | 3.8 / 3.8 / 3.4 -> 4.5 / 4.5 / 4.1 | yes |
| Lava Islet | ember | rock | 0.58 | 83 / 55 | 0.74 | 0.33 | 0.69 | 6.7 / 3.1 / 2.9! -> 8.0 / 3.7 / 3.5 | yes |
| Magma Rock | ember | rock | 0.54 | 78 / 52 | 0.69 | 0.33 | 0.65 |  ->  | yes |
| Obsidian Shard | ember | rock | 0.56 | 80 / 53 | 0.71 | 0.33 | 0.67 |  ->  | yes |
| Cryo Jelly | frost | alien | 0.63 | 91 / 61 | 0.81 | 0.34 | 0.76 | 5.4 / 4.7 / 4.4 -> 6.5 / 5.7 / 5.2 | no |
| Glacier Golem | frost | heavy | 1.13 | 163 / 109 | 1.44 | 0.49 | 1.35 | 9.8 / 8.9 / 6.9 -> 11.8 / 10.7 / 8.3 | no |
| Frost Lancer | frost | chaser | 0.79 | 114 / 76 | 1.01 | 0.48 | 0.95 | 6.5 / 2.9! / 1.7! -> 7.8 / 3.4 / 2.1! | no |
| Flake | frost | fighter | 0.83 | 120 / 80 | 1.06 | 0.45 | 1.00 | 9.8 / 6.1 / 4.7 -> 11.8 / 7.3 / 5.7 | no |
| Icicle | frost | fighter | 0.75 | 109 / 73 | 0.96 | 0.45 | 0.91 | 11.8 / 6.5 / 5.2 -> 14.2 / 7.7 / 6.2 | no |
| Frost Kite | frost | fighter | 0.82 | 118 / 78 | 1.04 | 0.45 | 0.98 | 4.3 / 3.1 / 2.8! -> 5.1 / 3.7 / 3.4 | no |
| Hailstorm | frost | fighter | 0.77 | 111 / 74 | 0.98 | 0.45 | 0.92 | 4.1 / 3.1 / 2.9! -> 4.9 / 3.7 / 3.5 | no |
| Frozen Chunk | frost | rock | 0.61 | 88 / 59 | 0.78 | 0.33 | 0.73 | 9.0 / 8.3 / 6.5 -> 10.8 / 9.9 / 7.8 | no |
| Rime Star | frost | rock | 0.60 | 87 / 58 | 0.77 | 0.33 | 0.73 | 6.5 / 6.2 / 6.0 -> 7.8 / 7.4 / 7.2 | no |
| Ice Shard | frost | rock | 0.61 | 88 / 59 | 0.78 | 0.33 | 0.73 | 4.4 / 4.1 / 3.9 -> 5.3 / 4.9 / 4.7 | no |
| Bile Mite | space | alien | 0.59 | 86 / 57 | 0.76 | 0.34 | 0.71 | 3.0 / 2.9! / 0.9! -> 3.6 / 3.5 / 1.1! | no |
| Bastion | space | heavy | 1.10 | 159 / 106 | 1.40 | 0.49 | 1.32 | 8.2 / 7.9 / 7.5 -> 9.9 / 9.4 / 9.0 | no |
| Steel Hound | space | chaser | 0.82 | 118 / 79 | 1.04 | 0.48 | 0.98 | 4.5 / 3.6 / 3.2 -> 5.4 / 4.3 / 3.8 | no |
| Needle | space | fighter | 0.83 | 121 / 80 | 1.07 | 0.45 | 1.00 | 5.7 / 3.4 / 2.9! -> 6.9 / 4.0 / 3.5 | no |
| Steel Claw | space | fighter | 0.83 | 121 / 80 | 1.07 | 0.45 | 1.00 | 4.7 / 4.3 / 4.3 -> 5.7 / 5.2 / 5.2 | no |
| Twin Claw | space | fighter | 0.83 | 121 / 80 | 1.07 | 0.45 | 1.00 | 6.3 / 2.9! / 2.9! -> 7.5 / 3.5 / 3.5 | no |
| Warden | space | fighter | 0.83 | 120 / 80 | 1.06 | 0.45 | 1.00 | 5.9 / 4.2 / 4.2 -> 7.0 / 5.1 / 5.0 | no |
| Cluster Rock | space | rock | 0.60 | 87 / 58 | 0.77 | 0.33 | 0.73 | 5.1 / 4.0 / 3.0 -> 6.1 / 4.8 / 3.6 | no |
| Beacon Rock | space | rock | 0.61 | 88 / 59 | 0.78 | 0.33 | 0.73 | 3.8 / 1.4! / 1.1! -> 4.6 / 1.6! / 1.3! | no |
| Coal Rock | space | rock | 0.61 | 88 / 59 | 0.78 | 0.33 | 0.73 | 4.9 / 3.9 / 1.5! -> 5.8 / 4.7 / 1.7! | no |
| Static Mote | storm | alien | 0.63 | 91 / 60 | 0.80 | 0.49 | 0.75 | 17.4 / 3.1 / 2.6! -> 20.9 / 3.8 / 3.2 | yes |
| Cloud Whale | storm | heavy | 1.14 | 165 / 110 | 1.46 | 0.49 | 1.37 | 1.4! / 1.2! / 1.0! -> 1.6! / 1.5! / 1.2! | yes |
| Cyclone Drone | storm | chaser | 0.86 | 125 / 83 | 1.10 | 0.55 | 1.04 | 4.7 / 2.5! / 2.3! -> 5.6 / 3.0! / 2.8! | yes |
| Stormray Pup | storm | fighter | 0.85 | 122 / 81 | 1.08 | 0.52 | 1.02 | 3.9 / 1.7! / 1.2! -> 4.7 / 2.0! / 1.4! | yes |
| Stormray | storm | fighter | 0.79 | 114 / 76 | 1.01 | 0.45 | 0.94 | 1.4! / 0.9! -> 1.7! / 1.1! | yes |
| Thunderhead | storm | fighter | 0.77 | 111 / 74 | 0.98 | 0.45 | 0.92 | 1.7! / 1.6! / 1.3! -> 2.0! / 1.9! / 1.5! | yes |
| Tempest Anvil | storm | fighter | 0.80 | 116 / 77 | 1.02 | 0.45 | 0.96 | 2.7! / 1.8! / 1.3! -> 3.2 / 2.2! / 1.5! | yes |
| Hailstone | storm | rock | 0.55 | 79 / 53 | 0.70 | 0.37 | 0.66 | 3.7 / 2.4! / 2.3! -> 4.4 / 2.9! / 2.8! | yes |
| Cloud Islet | storm | rock | 0.54 | 78 / 52 | 0.69 | 0.33 | 0.65 | 1.1! / 1.1! / 1.1! -> 1.4! / 1.3! / 1.3! | yes |
| Ore Shard | storm | rock | 0.56 | 81 / 54 | 0.72 | 0.33 | 0.67 | 9.4 / 3.7 / 2.1! -> 11.3 / 4.5 / 2.5! | yes |
| Anvil Chunk | storm | rock | 0.55 | 80 / 53 | 0.71 | 0.37 | 0.66 | 2.0! / 1.9! / 1.6! -> 2.4! / 2.3! / 1.9! | yes |
| Glow Jelly | tide | alien | 0.63 | 91 / 60 | 0.80 | 0.40 | 0.75 | 3.0 / 2.7! / 1.8! -> 3.6 / 3.2 / 2.2! | no |
| Nautilus Bulwark | tide | heavy | 1.11 | 161 / 107 | 1.42 | 0.49 | 1.33 | 2.8! / 2.7! / 2.4! -> 3.4 / 3.2 / 2.9! | no |
| Wire Eel | tide | chaser | 0.84 | 122 / 81 | 1.07 | 0.48 | 1.01 | 1.6! / 1.4! / 1.4! -> 1.9! / 1.7! / 1.7! | yes |
| Remora | tide | fighter | 0.77 | 111 / 74 | 0.98 | 0.45 | 0.92 | 1.4! / 1.4! / 1.4! -> 1.7! / 1.7! / 1.6! | yes |
| Needlefish | tide | fighter | 0.82 | 119 / 79 | 1.05 | 0.45 | 0.98 | 2.9! / 2.3! / 2.1! -> 3.5 / 2.8! / 2.5! | yes |
| Lantern Angler | tide | fighter | 0.79 | 114 / 76 | 1.01 | 0.45 | 0.95 | 3.8 / 3.5 / 3.0 -> 4.6 / 4.3 / 3.6 | yes |
| Hammerhead | tide | fighter | 0.78 | 112 / 75 | 0.99 | 0.45 | 0.93 | 1.4! / 1.3! / 1.3! -> 1.6! / 1.5! / 1.5! | yes |
| Brain Coral | tide | rock | 0.52 | 74 / 49 | 0.66 | 0.33 | 0.62 | 1.2! / 1.1! / 1.0! -> 1.5! / 1.4! / 1.2! | no |
| Kelp Islet | tide | rock | 0.54 | 78 / 52 | 0.69 | 0.33 | 0.65 | 2.3! / 1.5! / 1.5! -> 2.7! / 1.9! / 1.8! | yes |
| Staghorn Spire | tide | rock | 0.54 | 78 / 52 | 0.69 | 0.33 | 0.65 | 1.9! / 1.6! / 1.1! -> 2.3! / 2.0! / 1.4! | yes |
| Spine Urchin | tide | rock | 0.54 | 77 / 51 | 0.68 | 0.33 | 0.64 | 2.2! / 1.7! / 1.6! -> 2.6! / 2.0! / 1.9! | yes |
| Snap Sprout | verdant | alien | 0.56 | 81 / 54 | 0.71 | 0.34 | 0.67 | 6.3 / 6.2 / 6.1 -> 7.6 / 7.4 / 7.3 | yes |
| Bloom Maw | verdant | heavy | 1.13 | 164 / 109 | 1.45 | 0.49 | 1.36 | 32.0 / 16.5 / 13.1 -> 38.4 / 19.9 / 15.7 | yes |
| Dragonsting | verdant | chaser | 0.79 | 114 / 76 | 1.00 | 0.48 | 0.94 | 4.5 / 3.0 / 2.4! -> 5.3 / 3.6 / 2.8! | no |
| Gnat | verdant | fighter | 0.79 | 114 / 76 | 1.01 | 0.45 | 0.94 | 4.6 / 4.6 / 2.9! -> 5.5 / 5.5 / 3.5 | no |
| Wasp | verdant | fighter | 0.76 | 110 / 73 | 0.97 | 0.45 | 0.91 | 15.9 / 2.0! / 1.6! -> 19.1 / 2.4! / 1.9! | no |
| Mantis | verdant | fighter | 0.78 | 113 / 75 | 1.00 | 0.45 | 0.94 | 17.6 / 13.5 / 4.5 -> 21.1 / 16.2 / 5.4 | no |
| Hornet Queen | verdant | fighter | 0.78 | 112 / 75 | 0.99 | 0.45 | 0.93 | 9.3 / 8.3 / 8.0 -> 11.1 / 10.0 / 9.6 | yes |
| Bramble Knot | verdant | rock | 0.55 | 80 / 53 | 0.71 | 0.33 | 0.66 | 8.5 / 7.9 / 4.4 -> 10.2 / 9.5 / 5.3 | yes |
| Thorn Pod | verdant | rock | 0.63 | 90 / 60 | 0.80 | 0.33 | 0.75 | 15.0 / 4.8 / 4.4 -> 18.0 / 5.7 / 5.3 | yes |
| Spore Rock | verdant | rock | 0.61 | 88 / 58 | 0.78 | 0.33 | 0.73 | 19.8 / 11.1 / 7.4 -> 23.7 / 13.3 / 8.9 | yes |
| Vine Rock | verdant | rock | 0.61 | 88 / 59 | 0.78 | 0.33 | 0.73 | 14.3 / 6.3 / 4.4 -> 17.2 / 7.5 / 5.3 | yes |

Smallest on screen (0.52-0.61 u, 49-60 px at 720): all rocks - Tide Brain Coral 0.52, Kelp Islet / Staghorn / Urchin 0.54, Ember Magma / Cinder 0.54, Storm Cloud Islet 0.54, Desert Glass Spire 0.56, Verdant Bramble Knot 0.55 - and the Verdant / Space aliens (0.56 / 0.59). These are smaller than the player's own hull (0.78 u).

### 2.4 Mines, hazards, boss, shots

* **Mines**: 0.65 u (`MineWidth`), 94 / 63 px: smaller than the player; legible only through their glow. Left unchanged here (they ride the rail inner edge, the collider is pinned at 0.621 u by `RailMineArtTest`, rail clearance and the laser mount x follow the rail inset); listed as a follow-up with its own redraw.
* **Rocks** also carry `HazardSize` tiers (small / typical / large around the nominal); the tiers keep the same mean area and scale with the role size, so x1.2 on the role size scales the whole rock distribution.
* **Elite hazards** (Frost slabs 0.78 u, Verdant trunks, Tide pearls 0.36, Frost orbs 0.36): already deliberate, drawn and sized as mechanics, not ships. Elite shots are 0.16-0.5 u (23-72 px at 1080); anything under 0.2 u is 29 / 19 px and sits at the edge: Coalrunner / Cryo bolts 0.2, the lowest are 0.16. Left alone (shot size is a fairness number), but the minimum shot size is asserted at 0.16 u.
* **Bosses**: 2.85-3.21 u drawn (3.3 u cell, ~4x the player, 413-466 px at 1080). Fine; unchanged.

### 2.5 Priorities and the redraw list

1. **Elites below the floor** (Tide x4, Frost Floe Harrower / Rimebreaker, Storm x5, Desert x6, Verdant Timber Hauler / Thornlash / Sporebloom): scale, data only.
2. **Enemies**: scale x1.2 (code constants), then redraw for detail: ships whose key features (eyes, ports) are under ~3 px even after x1.2 - Ember fighters 1 / 3 (eye 1.4 / 0.9 px), Storm Cyclone Drone / fighters 2-4, Tide Wire Eel / Remora / Hammerhead, Desert fighter 4, Storm / Tide / Desert aliens and rocks, Verdant Hornet / Gnat features, Tide rocks, Frost Frost Lancer. The brief for Codex: bold the key eye / weapon / cockpit to >= 5 art px at 192 px cells and give a 2 px pale (not 1 px) outline for ships drawn at 0.33-0.5 screen px per art px, or render these on a 256 px cell.
3. **Elites with sub-3 px features at 720 even after scaling**: Verdant Thornlash, Tide Riptide / Thunderfin / Trawler, Desert Dune Stalker / Lens Matriarch / Scorpion Walker, Storm Refinery Tender / Static Bastion / Skyhook Harrier: Codex redraws at the new size (staged, not wired).

## 3. Design language

Measured on the idle drawings (mean over the world): luminance (0-1), saturation, share of near-white highlights (>0.75), share of near-black (<0.18), top-minus-bottom luminance (light from above if > 0), pale outline detected (a bright 1 px boundary against a darker interior).

| Group | World | lum | sat | highlights | near-black | top-bottom | pale outline |
|---|---|---|---|---|---|---|---|
| elites | desert | 0.36 | 0.38 | 0.11 | 0.26 | +0.034 | 3/6 |
| elites | ember | 0.23 | 0.53 | 0.01 | 0.49 | +0.010 | 2/6 |
| elites | frost | 0.35 | 0.46 | 0.18 | 0.41 | +0.088 | 0/5 |
| elites | space | 0.22 | 0.42 | 0.02 | 0.52 | +0.016 | 0/4 |
| elites | storm | 0.25 | 0.31 | 0.03 | 0.49 | +0.053 | 0/5 |
| elites | tide | 0.30 | 0.53 | 0.06 | 0.37 | +0.071 | 0/5 |
| elites | verdant | 0.26 | 0.53 | 0.03 | 0.45 | +0.017 | 4/5 |
| enemies | desert | 0.44 | 0.42 | 0.15 | 0.22 | +0.086 | 2/11 |
| enemies | ember | 0.26 | 0.52 | 0.06 | 0.50 | +0.007 | 10/11 |
| enemies | frost | 0.34 | 0.43 | 0.13 | 0.40 | +0.073 | 0/10 |
| enemies | space | 0.19 | 0.46 | 0.03 | 0.62 | +0.041 | 0/10 |
| enemies | storm | 0.31 | 0.38 | 0.14 | 0.45 | +0.070 | 11/11 |
| enemies | tide | 0.39 | 0.41 | 0.19 | 0.34 | +0.058 | 8/11 |
| enemies | verdant | 0.50 | 0.66 | 0.22 | 0.13 | +0.069 | 7/11 |

### 3.1 What is consistent

* Light from the top (+0.01 to +0.09 top-bottom everywhere; only Desert Mirage Wisp, Ember Brand and Storm Cloud Islet are bottom-lit).
* No pink outside Space: 0 of 35+40 non-Space ships carry magenta/pink accents (Space: 37-40%, by design).
* Each world has its own material palette and enemies match their elites' world (bronze / ice-blue / green-gold / teal / slate / sand).

### 3.2 Where it breaks

1. **Outline**: the thin pale outline (`enemy-visibility-add-border`) is present on Storm enemies (11/11), Ember (10/11), Tide (8/11), Verdant (7/11) but on **no Frost or Space enemy and no Frost, Space, Storm or Tide elite**; Desert has it on 2/11 enemies and 3/6 elites. So the same world reads two different ways: a Storm fighter is outlined, the Storm elite next to it is not (and is the darkest, lowest-contrast set: mean lum 0.25, 3% highlights).
2. **Value structure**: elites are darker and have fewer highlights than the same world's enemies in Ember, Storm, Tide, Verdant and Desert (Frost and Space are level; Ember 0.23 / 1% vs 0.26 / 6%; Storm 0.25 / 3% vs 0.31 / 14%; Tide 0.30 / 6% vs 0.39 / 19%; Verdant 0.26 / 3% vs 0.50 / 22%). A bigger, more dangerous ship should not be the dimmer one.
3. **Detail density**: elites are drawn at roughly 1:1 and carry 20-110 accent specks (Frost Floe Harrower: 112, Frost Whiteout 99, Desert Mirror Bastion 27), the enemies at 0.33-0.55 px/px. Small-specks-plus-no-outline is what makes Frost and Storm ships dissolve into the backdrop.
4. **Eye / weapon motif**: Ember and Space ships read by one hot core (orange or magenta); Verdant by a green eye; Tide and Storm have no consistent single focal glow (Storm Anvil Cloud / Refinery Tender: no accent at all). Rule below.
5. **Neon accent usage**: Space has magenta + cyan engines; every other world should accent with its own cue colour (`WorldCue`): the Ember / Desert / Verdant accents are good; Storm and Tide elites under-use theirs (saturation 0.31 for Storm elites vs 0.38 for Storm enemies).
6. **Dark outlines**: Verdant Resin Warden is hard-outlined in near-black, unlike the pale outline rule used by the rest.

### 3.3 The design language (rules)

1. **Size tiers** (section 2.1). A ship's tier, not its world, decides its on-screen size.
2. **Silhouette first**: one dominant shape per ship with a recognisable nose; role is told by silhouette (wings = fast, slab = hauler, claws = striker); one asymmetry at most.
3. **Outline**: every enemy, elite and mine has a **pale (value > 0.7, world-tinted) outline, 1 art px for elites (1:1 drawn), 2 art px for ships shown below 0.6 screen px per art px**. No dark outlines, no outline-free ships.
4. **Value structure**: three values - near-black underside, mid body, bright rim and accents; highlights on >= 4% of the pixels; light from the top-left; elites at least as bright as their world's enemies.
5. **Focal point**: exactly one hot accent in the world's cue colour (cockpit / eye / weapon core), >= 6 art px across for elites (>= 3 screen px at 720 after scaling) and >= 5 art px for fodder; weapons are drawn lit when charged (the tell cell), never smaller than that.
6. **Detail density**: no detail the size of one art pixel inside the body; panel lines in the body hue, not in white specks; 20-40 accent specks maximum for an elite.
7. **Neon**: engines / weapon cores are the neon, in the world's cue colour (no pink outside Space, hearts and shots stay magenta / violet / cyan per `EliteTest`); the player's red is never used.
8. **Player ships are not touched**.

### 3.4 Deviations (ships to redraw or touch up, staged for vetting)

* No pale outline on an elite: Frost x5 (Cryo Siren, Floe Harrower, Glacier Tender, Rimebreaker, Whiteout Sentinel), Space x4, Storm x5, Tide x5, Desert Glass Cannon / Lens Matriarch / Mirror Bastion, Ember Brass Vulture / Cauterizer / Kilnback / Sunstoke, Verdant Resin Warden (which is hard-outlined in near-black instead).
* No pale outline on an enemy: all ten Frost and Space ones and Desert x9 of 11, Tide x3 of 11, Verdant x4 of 11 (the exact list is the `pale outline` column of 2.3).
* Darkest and lowest-contrast elites (the first to redraw): Storm Anvil Cloud, Refinery Tender, Static Bastion, Rod Dancer; Space Singularity Hauler / Eventide Bastion; Ember Kilnback / Cauterizer.
* Accent-starved: Storm elites (accents 2.4-3.3 px even at x1.5), Tide Thunderfin / Trawler / Riptide, Verdant Thornlash.

The mechanical fix for the outline (a 1 px, world-tinted pale outline script run over the elite strips: `enemy-visibility-add-border`) can be done without a redraw and is part of the staged art branch; the focal-point / value changes need Codex.

## 4. Knock-on effects of the scale

| Area | What changes |
|---|---|
| Elite defs | `cellWorldSize` x k and `hullRadius` x 0.95k (data); muzzles and nozzles are in art px and follow; hearts orbit is a share of `hullRadius`; shots keep their size |
| `EliteSystem` / evasion / `ClampGoal` | all read `hullRadius`: rail clearance and the avoid radius grow with it (checked by `EliteEvasionTest`, `EliteSurvivalProbe`) |
| Landing sites | parked sprite is drawn `scale .3` of the new size: the parked ship grows with it; `landscape pads` are authored per landmark and the site scale is unchanged |
| Lift-off / dock flare / heat shimmer | use `cellWorldSize`: follow |
| Codex card / burst / title screen traffic elite | scale with `cellWorldSize` (`EliteScale`); the codex card must still fit (`CodexTest`) |
| Death strips, debris | cell-sized: follow |
| Enemy roster | `EnemyRoster.FrameWorldSize` / `ColliderSize` / `TargetWidth` / `TargetCollider` (x1.2 for Rock, Fighter, Chaser, Alien, Big), roster pins in `EnemyRosterTest`, `HazardSizeTest` (target = TargetWidth x tier), `SpawnSpaceTest` (bodies are >= TargetWidth) |
| Enemy muzzle points | `EnemyBehaviour.muzzle` (u below the centre) x 1.2 so shots still leave the cannon |
| Spawn lanes / formation widths | `SpawnLane.HalfExtents` / `SpawnSpace.BodyHalf` read the collider and `TargetWidth`: rows get 1-2 slots fewer; `EnemyDensityTest` re-tuned if the probe moves; `SpawnLane.ShipGap` (the player's gap) unchanged: difficulty decision, flagged |
| Rail clearance | `BossRails` inner edge unchanged; fighters 1.0 u of a 6.4 u lane still leave > 5 u; elites 1.65 u: `ClampGoal` uses hullRadius |
| Shield burst | scales with the hit body (`TargetExplosion`); unchanged |
| Tests that assert sizes | `EnemyRosterTest` (+/-15% of the new target, heavies 1.2-1.5 u), `HazardSizeTest`, `SpawnSpaceTest`, `EliteTest` (hearts vs player, muzzles on art), `ScreenFitTest` (elites still fit), new `ShipTierTest` (tier floors) |

## 5. What this branch does (as landed)

* **Lock-on** (code + data): `EliteDef.followLag` / `aimCommit` / `faceTurnMax` (+ `MinAimedTell` 0.6 s); `EliteShip.Follow` feeds `Brain.Goal`, `EliteShip.AimSeen` feeds every aimed attack (`EliteAttack.Aimed`: blink_shards, resin_mortar, ward / pearl curtain, gravity_sling, floe_cast, frost_bloom, armour_shatter, lance / surf dash, rift_rail). Per-elite `followLag` in each def (0.15-0.45); Ash Wraith and Brass Vulture tells 0.65 s. Exceptions kept exact on purpose: the Floe Harrower (reads the pilot's drift: 0.15) and Cryo Siren (0.15; its crowd check reads the exact position).
* **Elite scale** (data): `cellWorldSize` x k and `hullRadius` x 0.95 k for the 34 elites below the floor (k 1.05-1.73, table in 2.2); heart orbits capped so they stay tighter than the player's. Ember Ash Wraith / Brass Vulture / Cauterizer / Sunstoke unchanged. Evasion look-ahead scales with the hull (x1.0-1.4), the hold-ground jink reach too.
* **Enemy scale** (code): `EnemyRoster.TierScale` 1.2 for Rock / Alien / Fighter / Chaser / Big (frames, colliders, targets, heavy width 1.32 u, enemy muzzle offsets); mines and bosses unchanged. Knock-on: `PilotAirspace.ColumnMargin` .1 -> .05 and `RailClear` 2 -> 1.85 (columns of the bigger bodies still share the lane at the same 50% cap and stay clear of the mines' slide).
* **Tests**: new `ShipTierTest` (every elite's drawn silhouette >= floor, hit radius share, tier ordering, lanes) and `EliteLockOnTest` (data limits for all defs, brain lag, aim frozen after the commit, facing limit); adjusted pins: `EnemyRosterTest` (heavy 1.2-1.5 u; slot sampling per try), `EnemyBehaviourTest` (fixture spacing), `ShieldShockwaveTest` (elite offset by hull), `EliteEvasionTest` (look-ahead bound x hull), `HazardSizeTest` (mean-area tolerance .07), `RailsVettingTest` (<= 4 spawns in the 2 s after the calm window).
* **Art**: nothing redrawn, nothing outlined. The user rejected full outlines (Glow Jelly, Singularity Hauler look bad with them); the static outline pass and a "bottom half only" variant are staged unwired on `art/ship-outline-staged`. The Codex brief is `docs/ship-scale-codex-brief.md` (not run).
* **Enemy sizes**: the uniform x1.2 stays; natural size variety between enemy types is kept (small ones may stay small), so there is no per-enemy floor for fodder (an experiment with per-enemy floors was dropped at the user's request). Only elites have a floor (`ShipTierTest`).
