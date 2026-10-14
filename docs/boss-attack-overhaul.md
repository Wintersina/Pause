# Boss attack overhaul: bullet hell, one ladder (branch `feat/boss-bullet-hell`)

The user: "the bosses should progressively become harder and the attack patterns should be more bullet hell style and each
boss from each level should be harder than the last. Let's build this out fully for new different attack patterns. The
current bosses each have too similar attack patterns."

Kept as it was: 12 hearts / 12 hit points per boss (each heart one full hit), the 120 s escape clock, the thirds-of-the-fight phases, the free shots and
ultimate, the friendly-fire rules (a boss shot still takes out a rock it meets), the entrance, the death sequence, all artwork.
What changed: what the bosses fire, how much, and how it escalates. Every number lives in two files:
`Scripts/Bosses/Patterns/BossPatternTables.cs` (the attacks) and `Scripts/Bosses/Patterns/BossLadder.cs` (the ladder: density,
enrage, pace, floors).

## 1. Audit (before)

### The order the game visits the bosses

`WorldManager.Worlds` and `BossCatalog.All` are index-aligned: **Space (0) -> Frost -> Verdant -> Ember -> Tide -> Storm ->
Desert (6)**. (The catalog comment order and the world order are the same; Space is the first world, not an "end" boss.) A boss
fights when its level clock runs out; later loops add `LoopRules` (shorter cooldowns, a head start on the rotation).

| # | World | Boss | Fought with (before) |
| --- | --- | --- | --- |
| 1 | Space | VOID ARCHON, capital carrier | default table (3) |
| 2 | Frost | HOARFROST LEVIATHAN, cryo fortress | themed table (5) |
| 3 | Verdant | THE BLOOM QUEEN, hive mother | themed table (5) |
| 4 | Ember | CINDER DRAKE, volcanic wyrm | themed table (5) |
| 5 | Tide | IRON KRAKEN, tyrant of the deep | themed table (5) |
| 6 | Storm | TEMPEST ENGINE, storm-making turbine | default table (3); a themed table existed but was off |
| 7 | Desert | DUNE-EATER, feral strip-miner | default table (3); a themed table existed but was off |

### What each boss fired

Every boss had the SAME three-attack shape: one plain shot attack (aimed volley or fan), one lob (hail rained on 4 of 5
columns) or a second fan, and one sweeping laser; unlocked 1 / 2 / 3 by thirds, final third at x0.75 cooldowns. Numbers as in
`BossCatalog` (tell = telegraph seconds, cd = cooldown):

| Boss | Attacks (before) | Shot attacks | Laser | Themed extras (4 bosses fought them) |
| --- | --- | --- | --- | --- |
| Void Archon | chin cannon: 3 aimed bolts, 4.8 u/s, tell .7; core burst: 2 fans of 7 shards, 100 deg, ricochet x1, 3.1 u/s, tell .8; pod lasers: 2 beams down, sweep 18 deg, hold .9, tell .95 | 17 shots a rotation | pod lasers | (off) ion arcs jet, scan line wave |
| Hoarfrost Leviathan | icicle spray: 3 fans of 5 bolts, 70 deg, ricochet x2, 3 u/s, tell .8; glare beams: 2 beams at the ship, tell .8; blowhole hail: 2 lobs of 4 shards, tell .8 | 23 | glare | icicle drop (strike x3 lanes), cold blast (ring) |
| Bloom Queen | stinger thorns: 4 aimed bolts, 4.1 u/s, tell .6; spore bloom: 12 shards, ricochet x1, 2.5 u/s, tell .9; acid cannons: 2 beams, tell 1.0 | 16 | acid | vine lash, trunk toss |
| Cinder Drake | fire breath: 2 fans of 9 bolts, 120 deg, ricochet x1, 3.3 u/s, tell .7; furnace slugs: 3 volleys of 3 aimed shards, 4.9 u/s, tell .6; brow laser: 1 beam at the ship, sweep 55 deg, tell .8 | 27 | brow | flame sweep, eruption columns |
| Iron Kraken | port cluster: 3 fans of 5 (alternating cannons), ricochet x1, 3 u/s, tell .8; beak jet: 1 beam down, tell .9; starboard cluster: 2 lobs of 4 shards, tell .8 | 23 | beak jet | thunder strike, surf wave |
| Tempest Engine | core arc fan: 3 fans of 5, 84 deg, ricochet x1, 2.6 u/s, tell 1.0; crown hail: 2 lobs of 4, tell .9; coil beams: 2 beams, tell 1.0 | 23 | coil | (off) crown strike, chain lightning |
| Dune-Eater | grit fan: 3 fans of 5, 84 deg, ricochet x1, 2.5 u/s, tell 1.0; dune lob: 2 lobs of 4, tell .9; lens beam: 1 beam, tell 1.0 | 23 | lens | (off) burrower strike, sandstorm wall |

Fight-wide: phases at thirds of the 120 s clock or of the 12 hearts (whichever is further on); final third cooldown x0.75
(`BossConfig.FinalPhaseCooldownScale`); first attack 1.2 s after the fight starts; pool 48 shots / 6 beams.

### Similarity matrix (before)

`=` effectively the same attack (same kind, nearly the same numbers and rhythm), `~` same kind with different art/emitters, `.` different.

| | Space | Frost | Verdant | Ember | Tide | Storm | Desert |
| --- | --- | --- | --- | --- | --- | --- | --- |
| plain fan (3 volleys x 5, 2.5-3.3 u/s, 1 ricochet) | core burst ~ | icicle spray ~ | spore bloom ~ | fire breath ~ | port cluster ~ | **core arc fan =** | **grit fan =** |
| lob onto 4 of 5 columns | . | blowhole hail ~ | . | . | starboard cluster ~ | **crown hail =** | **dune lob =** |
| one or two sweeping lasers | pod lasers ~ | glare beams ~ | acid cannons ~ | brow laser ~ | beak jet ~ | **coil beams =** | **lens beam =** |
| strike lanes x3 @1.9 u | . | icicle drop ~ | . | eruption ~ | thunder strike ~ | crown strike ~ | burrower strike ~ |
| wave / scan line with a gap | scan line ~ | . | . | . | surf wave ~ | chain lightning ~ | sandstorm wall ~ |

Reading it: Storm and Desert were the same table with different names (fan 84 deg x3 volleys, lob 4 of 5, one beam); five of
seven bosses used the same lane strike; four used the same wave band; every boss had one fan, one lob-or-fan, one laser. Rotation
shot density was 1.1 - 2.7 shots a second for every boss (Space and Storm the highest), nothing rose with the world, and a boss
later in the game was not harder than the first. (Measured by the dodge bot over the same phase-3 window as below: 0.6 - 5.8
hits a minute, Cinder Drake the highest because of its hazards, Bloom Queen the lowest.)

### The player (what every pattern must be dodgeable by)

| | |
| --- | --- |
| Ship hit circle | radius 0.28 u (`DodgeBot.ShipRadius`, `ShipReachTest`); boss bolts 0.13, shards 0.12; a shot and the ship touch at 0.41 u |
| Speed | 7 u/s top (`BossAttackTest.MaxShipSpeed`); the finger drives the ship absolutely (glide cap 60 u/s), so human limits are reaction (0.25 s in the bot) and thumb travel |
| Finger offset | the ship sits 1.25 u above the finger (`ShipReach.FingerOffset`): the thumb never covers the hull, and the lowest 1.25 u of the screen cannot be reached by the ship's centre |
| Play zone | sideways +/-2.21 (16:9) .. 2.45 u (rails' inner edge minus the hull, `ShipReach.HalfWidth`); up to `BossConfig.FightCeilingShare` 65 % of the view and never closer than the boss's lowest muzzle + a gap; down to the safe area's bottom + hull. About 4.4 x 3.2 u on a 16:9 phone |
| Boss | rests near the top (`BossConfig.BossY`), 6.4 u above the ship's row on the authored view, swaying; shots are scaled so they reach the ship's row in the same time on every screen (`ShotScale`, 0.5 - 1.6) |
| Shield burst | the blue atom's shield shoves shots away and eats a hit (`ShieldShockwave`); a shielded ship may ram the boss |
| Free shots / ultimate | 2 red free-shot atoms per fight (`BossFreeShotAtoms`), the ship's weapons shoot down any hostile shot they cross; the ultimate (cooldown 30-60 s) erases every hostile shot (`HostileShots.PowerCleared`) |
| Hearts | the player's lives (3, up to 5 with skins); a hit also grants a post-hit grace of about 1.5 s |

So a 4.4 u wide lane holds 5 ship widths: any forced gap smaller than about 1.6 u is a pixel-hunt, and a gap that moves faster
than ~4 u/s outruns a thumb that has to see it first.

## 2. Design

### The ladder

Boss 1 is the easiest, boss 7 the hardest, defined by numbers **measured on the real tables** (`BossLadder.Measure`: every
slot of the phase-3 rotation played twice through `PatternSim`, tells and cooldowns included). `BossLadderTest` asserts the
order, so a tuning change that breaks the ladder fails the gate.

| # | Boss | attacks | shots/s | max live | speed (u/s) | min tell (s) | safe gap (hitboxes) | overlap | enrage | density | pace |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Void Archon | 5 | 3.4 | 21 | 2.8-4.4 | 0.90 | 3.7 | 1 | 1.10 | .75 | 1.00 |
| 2 | Hoarfrost Leviathan | 5 | 4.4 | 25 | 2.7-4.8 | 0.85 | 6.3 | 2 | 1.15 | .62 | 1.00 |
| 3 | Bloom Queen | 6 | 4.5 | 39 | 2.4-5.1 | 0.80 | (seeds, no wall) | 3 | 1.20 | .64 | .95 |
| 4 | Cinder Drake | 6 | 6.4 | 47 | 2.3-5.4 | 0.75 | (bombs, no wall) | 3 | 1.25 | .62 | .85 |
| 5 | Iron Kraken | 7 | 6.9 | 50 | 3.0-5.7 | 0.70 | 3.1 | 3 | 1.30 | .60 | .80 |
| 6 | Tempest Engine | 7 | 7.6 | 53 | 2.6-6.0 | 0.70 | 1.8 | 3 | 1.40 | .50 | .75 |
| 7 | Dune-Eater | 7 | 11.0 | 81 | 2.6-6.4 | 0.65 | 1.9 | 4 | 1.50 | .66 | .70 |

(The gap column is the measured narrowest forced gap; the targets it must meet are 3.4, 3.0, 2.7, 2.4, 2.1, 1.6, 1.4.)

* **shots / second**: shots fired over the whole phase-3 rotation, enrage and density on. The headline: strictly increasing.
* **max live shots**: the most alive at once (pool 144; the last boss peaks at about 80).
* **speed**: slowest .. fastest shot; the fastest is an aimed "accent" (slower bosses fire it at 4.4 u/s, the last at 6.4 u/s).
* **min tell**: the shortest telegraph of the table: 0.90 s down to 0.65 s.
* **safe gap**: the narrowest forced opening any wall / ring / lattice / mine field leaves, in ship-hitbox widths (a hitbox is
  0.56 u; the clear space between two shots over that): a target per boss (`BossLadder.GapTarget`: 3.4 .. 1.4 hitboxes), every
  stream is checked against it.
* **overlap**: the most streams running at once inside one attack: 1 for the Archon, up to 4 for the Dune-Eater.
* **enrage**: the last third multiplies every stream's firing rate (1.10 up to 1.50).
* **density**: the one calibration knob (`BossLadder.Density`, x0.50 .. 0.75): every boss's stream rates are scaled by it, in
  every phase, so the tables stay readable. It is what `BossLadderBotTest` is calibrated with (see section 5).
* **pace**: `BossLadder.Cooldown` keeps 100 % of each slot's cooldown for the first two bosses and 70 % for the last.
* **phases and patterns**: still three phases (thirds; hearts or clock). Attacks per boss 5, 5, 6, 6, 7, 7, 7; phase 1 plays
  2, phase 2 plays 3 or 4, phase 3 plays all of them.

### Vocabulary

Eleven stream kinds (`StreamKind`), combined into attacks (`BossPattern`: up to four streams, each with its own start time, so
they overlap), plus the lasers and area hazards the game already had:

| Kind | What it does | Telegraph |
| --- | --- | --- |
| Aimed | `count` bolts at the ship, `spread` degrees | the boss's tell pose + charge glow |
| Fan | `count` shots over `spread` degrees, turning a little each volley; can ricochet | same |
| Spiral | rotating arms (only the downward half fires) or a pendulum sweep; `mirror` turns the second part the other way | same |
| Ring | a radial ring (the downward half) with a gap aimed beside the ship; turns each ring | a sight line down the first gap |
| Curtain | a wall across the lane with a gap that steps up to `gapStep` u per wall | a blinking lane down the first gap |
| Rain | drops from the top into random lanes, always leaving free lanes | the boss's tell; drops are visible a second before they arrive |
| Lattice | staggered rows drifting `angle` degrees, flipping drift every few rows | tell + the rows are visible from the top |
| Bomb | slow bombs that swell and burst into a ring after `fuse` s; bracket the ship, never on it | a marker at each landing spot |
| Seeds | slow shots that home for `homeSeconds` then fly straight | tell; slow |
| Mines | stationary mines on spots (clear of the ship, `minSep` apart) that go after `fuse` s | a ring marker at every spot during the tell |
| Pincer | two jaws aimed either side of the ship and closing by `gapStep` u a volley | tell |
| (legacy) Beam / Jet / Strike / Lash / Roll / Wave | the game's existing lasers and area hazards, kept where they are a boss's signature | their own tells |

New projectile behaviours (`BossProjectile`): homing, fuse + burst, stationary; pattern shots never break each other or get burnt
by their own boss's beams (`IHostileShot.ShotSelfSafe`), but the ship's weapons, the shield, the blink and the ultimate still
remove them (they carry the same hostile hitbox tag as every boss shot). Bursts and mines use the shard art, the telegraph
markers use the boss's own sight-line sprite: no new art.

### Signatures (who owns what)

No two bosses share a signature mechanic; only the plain basics (aimed volleys, fans, lasers) repeat, and every boss opens with
one. `BossPatternTest` enforces: every non-basic stream or hazard kind belongs to exactly one boss, and no attack after phase 1
repeats another boss's signature.

| Boss | Attacks (phase) | Signature |
| --- | --- | --- |
| Void Archon | chin cannon (1), ion arcs (1) aimed pod pair, pulsar spiral (2), pod lasers (3), mirror sweep (3) | rotating spiral, mirrored pendulum sweep |
| Hoarfrost Leviathan | icicle spray (1) ricochet fan, cold snap (1) ring with a gap, glare beams (2), shatter (3) ricochet fan x3 + aimed eyes, frozen crown (3) rings + fan | ring with a gap |
| Bloom Queen | stinger thorns (1), vine lash (1), spore seeds (2), trunk toss (3), overgrowth (3) seeds + fan, thorn storm (3) aimed + seeds | homing seeds, vine lash, trunk toss |
| Cinder Drake | fire breath (1), cinder spit (1), magma bombs (2), brow laser (3), eruption columns (3), slag storm (3) bombs + fan + aimed | delayed-burst bombs, eruption columns |
| Iron Kraken | port cluster (1), beak jet (1), tidal wall (2) curtain, starboard cluster (2), surf wave (3), riptide (3) curtain + aimed, maelstrom (3) curtain + fan + aimed | curtains with a drifting gap, surf wave |
| Tempest Engine | core arc fan (1), crown fan (1), storm rain (2), coil beams (2), lightning lattice (3), thunderhead (3) rain + aimed + fan, cage storm (3) lattice + rain + aimed | rain from the top, lattice grids |
| Dune-Eater | grit fan (1), dune fan (1), buried mines (2) + aimed trickle, lens beam (2), closing jaws (3), quicksand (3) mines + jaws, dune-eater's maw (3) mines + jaws + fan + aimed | mine fields, closing pincer |

All shots wear the world's cue colour already (`WorldCue`, through the boss's recoloured shot atlas and `HostileGlow`): pink
only for the Void Archon. The telegraph markers are the boss's own sight-line art tinted `HostileGlow.Tint(boss.flash)`.

### Fairness

* every wall has a gap of at least the boss's target (above) and its gap never moves faster than 4.2 u/s, even enraged
* mines are never placed within 1.4 u of the ship nor closer than 1.3 u to each other; bombs bracket the ship, never land on it
* a ring's gap is aimed beside the ship; a pincer starts aimed wide and closes; a lattice leaves vertical corridors of at least
  its target
* **solver**: `PatternSim.Solve` is a time-expanded flood fill over the ship's zone: the set of places the ship can be after each
  0.05 s is what it could reach at 5 u/s (a human, not the 7 u/s top) from the places it could be a step ago, minus every cell
  touched by a shot (ship + shot radius + 0.04 margin). A pattern is survivable when that set never empties. It is run for every
  pattern's static streams (the ones that do not aim at the ship), at the boss's enrage x density rate, from three start columns, at
  several shot scales (the speed multiplier a screen gets), and the smallest reach it leaves is required to be at least 10 % of
  the zone. It is also run on an impossible wall (gap 0.2 u) and must say no (the regression injection)
* the streams that aim at the ship (aimed, pincer, seeds) depend on where the ship goes, so they are covered by the **dodge bot**
  instead (section 5), not by the solver; bombs are in both
* telegraphs: a table's shortest tell is its ladder floor (0.9 .. 0.65 s); the first shot of an aimed attack still needs about a
  second to arrive (the boss is 6+ u away)
* small phones: shot speeds are scaled by `BossConfig.ShotScale` exactly as before (time to reach the ship is constant); walls spawn
  at the view's top whatever its height; the spawn height, ship reach and zone are read from the live frame

## 3. Implementation

* `Scripts/Bosses/Patterns/BossPattern.cs`: `StreamKind`, `PatternStream` (all the numbers), `BossPattern`, `PShot`,
  `IPatternHost`, `BossPatternRunner` (time-driven emission, planned markers, no allocation per frame).
* `BossPatternTables.cs`: the seven rotations as data. `BossLadder.cs`: the ladder (density, enrage, pace, floors, `Measure`).
* `BossPatternSim.cs`: flat flight model (same rail rules as `BossProjectile.Step`) + the flood-fill solver. Both core files use
  nothing but `Vector2` and `Mathf`, so they were also tuned in a standalone harness (a dozen ms per boss).
* `BossActor`: a new `Pattern` phase (tell -> pattern -> cooldown) in the existing scheduler; implements `IPatternHost`.
  `BossAttackKind.Pattern` is a new kind; the old kinds, executors and tables are untouched (their suites run on them with
  `BossCatalog.UseOverhaul = false`).
* `BossProjectile` / `BossProjectilePool`: homing, fuse/burst (`Burst`), stationary mines, `FireShot`, `Warm` (the pool's shots and
  hitboxes are built during the boss's entrance, so a fight makes no objects mid-pattern; the pool is 144 shots, was 48).
* `BossPatternMarkers`: 16 pooled sprites for the telegraphs. `BossHell` builds `BossDef.hell` from the tables (`BossDef.attacks`
  is the hell table unless `overhaulAttacks` / `BossCatalog.UseOverhaul` is off).
* Small fixes on the way: `BossArt.Shot` / `BossArt.ShotRim` / `BossArt.BodySheet` built a path string per call (hundreds a second
  with 100 shots up); they now keep the last boss's sheet. The Codex's boss page lists the new attacks.
* Sound: unchanged. Pattern shots use the existing hostile-shot pew (`PewSound`, already capped at 12 a second), bombs and mines
  use the boss's existing spark; no beeps or coins.

## 4. Tests

| Suite | What |
| --- | --- |
| `BossLadderTest` (gate, 1 s) | the ladder table is measured and ordered (shots/s strictly increasing; max live, fastest shot, attacks, overlap, enrage non-decreasing; tell, gap target non-increasing); floors; pool fit; every stream's gap vs its target; gap speed; the solver on every static pattern; the injected impossible wall |
| `BossPatternTest` (gate) | tables and phase prefixes; signature mechanics unique and as designed; the runner (shapes, gaps, rails, bursts, homing, mines, determinism, enrage); a live 40 s phase-3 rotation of Void Archon, Cinder Drake and Dune-Eater (full lane: all 7) through the real `BossEncounter`: patterns play, telegraphs show, pool cap, hostile tag, no pink outside Space, steady pattern frames allocate nothing, nothing left behind |
| `BossLadderBotTest` (gate, ~1 min) | the dodge bot (reaction 0.25 s, perception error, acceleration) flies each boss's phase-3 rotation, 12 rolls of 36 s per boss: beatable in principle (under 14 hits a minute), harder bosses hit it more |
| existing | BossAttackTest, BossThemedTest, ShipReachTest keep their old tables (`UseOverhaul` off); BossEntranceTest, BossHeartsTest, BossDamageTest, BossEscapeClockTest, BossEncounterTest, BossImpactTest, BossFreeShotAtomsTest, BossIntroTest, FriendlyFireCoverageTest, CodexTest, LoopTest and the rest run on the new tables |

### The greedy bot

| Boss | greedy bot, new table (hits/min) | old table |
| --- | --- | --- |
| Void Archon | 2.9 | 3.3 |
| Hoarfrost Leviathan | 4.9 | 2.6 |
| Bloom Queen | 6.9 | 0.8 |
| Cinder Drake | 7.2 | 5.8 |
| Iron Kraken | 10.7 | 3.8 |
| Tempest Engine | 10.4 | 2.1 |
| Dune-Eater | 11.4 | 3.8 |

(12 rolls of 36 s of the phase-3 rotation each; a hit then 1.5 s of grace counts once. The order is monotone within 1 hit/min noise, Iron Kraken and Tempest Engine are level.)

Calibration: `PAUSE_LADDER_SWEEP=1 PAUSE_DENSITY="d0,..,d6;e0,..,e6" scripts/unity-batch.sh -executeMethod AllTests.RunSuites
-suites BossLadderBotTest` prints hits a minute for density vectors (the bot's hits are chaotic: one extra hit per roll moves a
boss by 2-3 a minute, so twelve rolls and deterministic seeds). The bot is a stand-in, not a player: it never shoots the shots down
or uses the shield, it replans to the nearest safe spot, and above ~15 hits a minute it saturates (the top four bosses are all
"hard" at density 1.0, which is why `Density` exists).

## 5. Art needed (none blocking)

Everything reuses the existing boss shot atlases, spark and telegraph sprites. Nice to have from Codex, not wired anywhere:

1. A **bomb** shot cell per world (bigger, fused-wick look) for Cinder Drake's magma bombs; today they are the world's shard
   cell that swells in the last half second.
2. A **mine** cell for Dune-Eater's buried mines (a half-buried disc with a blinking centre); today a still shard.
3. A **gap marker** (a soft lane, not a stretched dash) for the Kraken's tidal wall and a **spot marker** (ring) for mines /
   bombs; today both use the laser's sight-line sprite scaled.
4. An **enrage** read: a flash on the boss when the last third starts (the cooldown and rate change today with no visual).

## 6. Balance risks and what is not verified

* **Feel on a device is untested.** All numbers come from the sim, the solver and the dodge bot. Playtest in this order:
  **Dune-Eater** (closing jaws and quicksand: three to four streams at once, the highest density), **Cinder Drake** (bombs: the
  rings only read once you have seen one; flame / eruption hazards are the old ones), **Iron Kraken** (tidal walls: the gap moves
  1.3-1.5 u per wall and the first wall's lane marker is the only telegraph), then Tempest Engine (rain + lattice) and the early
  three for the ramp.
* The bot says the last boss is about four times harder than the first, and about three times the old tables; that is the
  intent, but the bot cannot shoot shots down, use the shield, or read a pattern ahead as a human can. If the top is too hard,
  lower `Density` for the last three (one number each); if too easy, raise it.
* `BossLadder.Density` is also what keeps the strictly-increasing shots/s true: changing one boss's number can reorder the
  ladder; `BossLadderTest` tells at once.
* The telegraph markers are stretched copies of the laser sight sprite: the look is unverified.
* Lattice, rain and curtains spawn at the top of the view and cross the boss's own sprite on the way down: unverified how that
  reads on a tall (22:9) screen.
* The old themed tables for Frost / Verdant / Ember / Tide are no longer fought (kept for their suites and the Codex-less
  budget tests); Storm / Desert's themed tables never were.
