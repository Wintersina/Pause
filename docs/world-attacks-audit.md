# World attacks audit (what every hostile attack is today)

Branch `feature/attacks-design`, read-only audit of master `407a2a86`. Companion docs:
`world-attacks-design.md` (the themed attack for each enemy), `world-attacks-art.md` (art list),
`world-attacks-codex-prompts.md` (Codex prompts), `world-attacks-implementation-plan.md` (how to build it).

User requirement (verbatim): "the attack patterns for each world should match the enemies there, for example,
frost world there will be ice attacks and blasts etc, for fire world fire attacks, flame throwers, fire lasers etc,
for water world, surf wave attacks, water guns, thunder etc... for space, laser neon attacks etc. does that make
sense, not all enemies should be doing same attacks as first world; if you need animations have codex make it, else
do it yourself; and for forest world (he wrote 'frost' by mistake: Verdant) have vine attacks, leaf attacks, tree
trunks etc. ... and update skill to remember that for next worlds we create".

Method: read `EnemyBehaviours.cs`, `EnemyBrain.cs` (`EnemyVolley`), `RailMineLaser.cs`, `EliteShots.cs`,
`EliteAttacks.cs`, `EliteArt.cs` (`EliteFxArt`), `HostileShotPalette.cs`, `ShotOutline.cs`, `HostileShots.cs`,
`BossCatalog.cs`, `BossProjectiles.cs`, `BossArt.cs`, `BossAttackFx.cs`, the 15 elite def JSONs, and opened the four
boss shot atlases and `boss_attack_fx.png`.

## 1. Headline findings

1. **Every roster and elite shot, in every world, is the same four procedural sprites.** `EliteShot.Launch` picks
   `EliteFxArt.Bolt` (a 10x20 dart), `Shard` (a 12x16 arrowhead), `Shell` (a 14x24 dart with fletching), `Slag` (a 19 px
   burr), `Orb` (a 21 px burr), `Slab`, and the `Glob` is the same burr again (pool = a 24x14 puddle). The per-world colours in
   `EnemyBehaviours.ShotColor` (Frost cyan, Verdant lime, Ember amber) are collapsed by `HostileShotPalette.Body`
   (hue forced into 312-326), so a Frost Icicle's bolt, a Verdant Hornet's stinger and an Ember Scorch's bolt are the
   same pink dart. Only Space looks "right" (neon pink on indigo), and only because the dart *is* a neon bolt.
2. **Bosses are the only themed shots, and only in their painted art.** `<World>_shots.png` (1024x128, 8 cells of 128:
   bolt 0-1, shard 2-3, telegraph 4, beam 5-6, charge 7) is painted in the world's material (Frost: ice crystals in
   cyan with a bronze cap; Ember: fire spikes and shards; Verdant: lime thorn darts and spiky spore balls; Space: pink
   neon lozenges and hearts). In game each gets a pink rim (`BossArt.ShotRim`, tinted `HostileShotPalette.Trace(Body(boss.flash))`).
   Their beams are pink bands in Space/Frost/Ember, but **lime in Verdant** (cells 5-6 of `Verdant_shots.png`); only the
   `HostileGlow` sheath is pink there. The motion is still four generic kinds: `Aimed`, `Fan`, `Lob`, `Beam`.
3. **No hostile attack has a sound.** There is no shot, beam, telegraph or impact audio under `Scripts/Gameplay/Elites`,
   `Scripts/Bosses` (apart from `BossWarningAudio`, the WARNING stinger) or `Scripts/Gameplay/Enemies`. Death sounds exist
   (`EnemyDeathAudio`); attack sounds are a whole missing layer.
4. **Behaviour primitives are already varied** (aimed, fan, ring, lob pool, laser, lunge, curtain, sling, slab, orb), so
   the work is mostly *skin + motion + FX*, with only five genuinely new hazard shapes (cone/column, sweeping band, expanding
   ring, lane strike, lashing arc).
5. **18 roster enemies shoot** (Space 5, Frost 5, Verdant 3, Ember 5; one of them, Bile Mite, only 2 in 5) and **6 more
   attack with the body** (`Lunge`: Needle, Steel Claw, Flake, Wasp, Mantis, Cinder). The four mines fire the *same* laser.
   Rocks (14), chasers (4) and three aliens (Cryo Jelly, Snap Sprout, Ember Imp) never attack; Gnat has no attack either.
6. **The Tide stand-in has no attacks of its own**: `EnemyRoster.For` clamps world 4 to Ember's cast, `BossCatalog.ForWorld`
   clamps to Ember's boss, there are no Tide elites. Its attack set is *designed* here, not audited.

## 2. The rules every attack already obeys (must survive the redesign)

| Rule | Where | Value |
| --- | --- | --- |
| Hostile hue family | `HostileShotPalette.HueMin/HueMax` | 312-326 deg, sat .62-.8 (body), core = body 75% to white, trace (outline light ring) = body 30% to white |
| Never the player's red | `HostileGlow.IsPlayerRed` (hue past 332 to 15) | `EnemyBehaviourTest`, `AtomClarityTest` |
| Pickup hues kept clear | cyan 178, green 82, violet 259, amber 37, red ~7 | `AtomClarityTest`: `MinHueGap` plus `MinShotCues` of 5 cues (hue, softness, size, silhouette, hostile-edge) for roster/elite shots; `MinBossCues` for painted boss shots |
| Outline | `ShotOutline.For(sprite, size, bold)`; bold two-ring keyline (`BoldKey` 2,2,8 near-black + `BoldTrace`) on bright worlds (`BackdropCatalog.CurrentIsBright`, `Spec.Bright`) | `HostileProjectileTest`, `ReadabilitySweep` (3:1 WCAG stand-out) |
| Danger flicker | `HostileShotPalette.FlickerHot`, 5 Hz hard on/off core | every `EliteShot` |
| Telegraph floor | `EnemyBrain.TellFloorSeconds` .45 s; aim locked at windup, `aimCone` limited, no lead | `EnemyBehaviourTest` |
| Fire only when fair | `MinFireAbove` 1.6 u above the pilot, `MinFireDistance` 1.8 u away, in view | `EnemyBrain` |
| Budget | `EnemyThreat.MaxEnemyShots` 12 roster shots alive, `VolleyGap` .4 s between two enemies' windups, `ShotWeight` .5 per live shot in the spawner ceiling, `EnemyDensity.MaxPilotLoad` | `EnemyDensityTest` |
| Shot vs shot | `HostileShots` masses Light 1 / Heavy 2 / Fixed 3, `VolleyGap` .6 s (same volley never clashes); lasers burn crossing shots; player weapons shoot light and heavy shots down; pools are fixed | `HostileProjectileTest` |
| Friendly fire | `FriendlyFire.HostileHit` on every hazard except the shooter | `HostileFireTest` |
| Pause | timers only advance through `Step(dt)` on running frames | all |

## 3. Shot sprite and hitbox reference (what a "kind" means today)

`EliteShot.Launch` (file `Gameplay/Elites/EliteShots.cs`): drawn height = `shotSize`; hit radius = `shotSize` x factor.

| `EliteShots.Kind` | Sprite (`EliteFxArt`) | Hit radius factor | Life | Mass | Special |
| --- | --- | --- | --- | --- | --- |
| Bolt | `Bolt` 10x20 dart | .30 | 4 s | Light | |
| Shard | `Shard` 12x16 arrowhead | .32 | 4 s | Light | can bounce (`def.shotBounces`) |
| Slag | `Slag` 19 px burr | .42 | 7 s | Heavy | sinks with the board, wobbles, `ride` 0 |
| Shell | `Shell` 14x24 finned dart | .36 | 4 s | Heavy | pierces 2 hazards |
| Glob | `Slag` burr, then `Pool` puddle | .40 | `lobSeconds` + `poolSeconds` | Fixed (pool) | `Lob()`: arc to a marked spot (`mark` ring), harmless and untouchable in the air, lands as a pool that rides the board |
| Slab | `Slab` (ice block; Frost only) | .42 | 12 s | Fixed | glides to a spot, drifts, bounces off rails, soaks `hazardArmour` hits |
| Orb | `Orb` burr + `Ring` fuse mark | .40 | 6 s | Heavy | `Fuse()`: bursts into `hazardCount` shards; shot down first it just pops |
| (any) Sling | | | | | `Sling(bend, to, seconds)`: curves through a ringed well, deadly all the way |

Roster-shot-only differences (`AsRosterShot`): never hurts its shooter, may ride the board (`ride`), pilots' shots fly in
world space at `EnemyVolley.PilotShotSpeed` 1.25 x `EnemyBrain.ViewScale`.

## 4. Roster audit (all four live worlds; the Tide stand-in flies Ember's cast)

Columns: attack primitive (`EnemyAttack`), shot kind, count x spread, speed (u/s, x1.25 x view for a pilot), drawn size
(hit radius), tell (s, never under .45), cooldown, volleys, and how themed it already is
(0 = generic pink dart; 1 = name/tell art themed but the attack is generic; 2 = attack matches the material).

### Space (attack art baseline: neon pink on indigo, so generic = on theme)

| Key | Attack today | Numbers | Tell | Themed |
| --- | --- | --- | --- | --- |
| `space_rock_crater` / `_cluster` / `_dark` | none (hazard) | | cosmetic glint | n/a |
| `space_mine` Rail Mine | `Laser`: 2 beams per ride across the lane at +-35 deg, from the core | `RailMineLaser`: aim .7 s, beam .4 s (hit thickness .28), cool .2 s, `ShotGapSeconds` 1 s, `ShotsPerRide` 2, `MinAngleChangeDeg` 8 | arming loop .9 s | 2 (neon laser) |
| `space_big` Bastion | `Shot` Bolt x2, parallel, `shotGap` .42 | 2.6 u/s, .2 (r .06), Muzzle .5, cd 2.6, 3 volleys | .8 | 1 |
| `space_fighter_1` Needle | `Lunge` 1.5 dive, .22 s | | .45 | 0 |
| `space_fighter_2` Steel Claw | `Lunge` toward pilot x 1.0, dive 1.3 | | .55 | 0 |
| `space_fighter_3` Twin Claw | `Shot` Bolt x2 spread 26 | 2.8 u/s, .18 (r .054), cd 2.2, 3 volleys | .6 | 1 |
| `space_fighter_4` Warden | `Shot` Shell x1, aimed, cone 28 | 3.4 u/s, .3 (r .108), pierce 2, cd 2.4, 4 volleys | .9 | 1 |
| `space_chaser` Steel Hound | none (chaser) | | | n/a |
| `space_alien` Bile Mite | `Shot` Shard x1, `Armed(.4)` | 2 u/s, .16 (r .051), cd 3.5, 2 volleys | .55 | 0 |

### Frost (shot colour cyan on paper; drawn pink like every world)

| Key | Attack today | Numbers | Tell | Themed |
| --- | --- | --- | --- | --- |
| `frost_rock_shard` / `_chunk` / `_rime` | none | | cosmetic glint | n/a |
| `frost_mine` Geode Mine | `Laser` (same as Space's) | aim .7, beam .4, `Creep`s down its rail | charging loop .9 s | 0 (a pink beam, no frost) |
| `frost_big` Glacier Golem | `Shot` Shard x3 fan 22 | 2.4 u/s, .22 (r .07), cd 3.2, 3 volleys | .9 | 0 |
| `frost_fighter_1` Flake | `Lunge` .8 x, dive 1.5 | | .55 | 0 |
| `frost_fighter_2` Icicle | `Shot` Bolt x1 aimed, cone 30 | 3.6 u/s, .18 | .55 | 0 |
| `frost_fighter_3` Frost Kite | `Shot` Shard x2 spread 34 | 3 u/s, .18 | .55 | 0 |
| `frost_fighter_4` Hailstorm | `Shot` Shard x5 spread 24 | 2 u/s, .2 (r .064), cd 3.4 | 1.0 | 1 ("hail" in name only) |
| `frost_chaser` Frost Lancer | none (chaser, dash) | | | n/a |
| `frost_alien` Cryo Jelly | none | | | n/a |

### Verdant

| Key | Attack today | Numbers | Tell | Themed |
| --- | --- | --- | --- | --- |
| `verdant_rock_pod/_spore/_knot/_vine` | none | | cosmetic | n/a |
| `verdant_mine` Burr Mine | `Laser` (same) | aim .7, beam .4, `Patrol`s the rail | charging 1.0 s | 0 (boss laser art recoloured pink; the boss's own is lime) |
| `verdant_big` Bloom Maw | `Lob` glob onto the pilot's spot, pool 2.5 s | size .3 (r .12), cd 3.5, 3 volleys | .9 + landing ring | 1 (a pink burr and puddle, not green resin) |
| `verdant_fighter_1` Gnat | none | | | n/a |
| `verdant_fighter_2` Wasp | `Lunge` x1.0 dive 2.0 (deepest) | | .5 | 0 |
| `verdant_fighter_3` Mantis | `Lunge` sideways slash, dive 0 | | .6 | 0 |
| `verdant_fighter_4` Hornet Queen | `Shot` Bolt x3 spread 12 aimed | 3.2 u/s, .16 (r .048), cd 2.6, 4 volleys | .8 | 0 |
| `verdant_chaser` Dragonsting | none | | | n/a |
| `verdant_alien` Snap Sprout | none | | | n/a |

### Ember

| Key | Attack today | Numbers | Tell | Themed |
| --- | --- | --- | --- | --- |
| `ember_rock_*` x4 | none | | cosmetic | n/a |
| `ember_mine` Crucible Mine | `Laser` (same) | aim .7, beam .4 | charging 1.1 s | 0 |
| `ember_big` Magma Skull | `Shot` Slag x2 spread 56 | 1.5 u/s, .32 (r .134), cd 3.6, 3 volleys | 1.0 | 1 (slag sinks and lingers, drawn as a pink burr) |
| `ember_fighter_1` Cinder | `Lunge` dive 1.7 | | .45 | 0 |
| `ember_fighter_2` Scorch | `Shot` Bolt x1 straight, up to 3-4 volleys | 3.8 u/s, .18, cd 1.4 | .5 | 0 |
| `ember_fighter_3` Brand | `Shot` Bolt x1 aimed cone 34 | 3.4 u/s, .18, cd 1.8 | .5 | 0 |
| `ember_fighter_4` Pyre | `Ring` Bolt x8 | 2.2 u/s, .18, cd 3.8, 3 volleys | 1.1 | 1 |
| `ember_chaser` Cinder Fang / `ember_alien` Ember Imp | none | | | n/a |

### Tide (stand-in)

Tide currently reuses Ember's whole cast, boss and mine row (`EnemyPalette.ThemeFor(4)` = Ember's; `BossCatalog.ForWorld(4)`
clamps to Ember). Its attacks are designed from the brief's roster in `world-5-6-briefs.md` (see the design doc, section 6).

## 5. Elite audit (15 defs under `Art/Resources/Elites/Defs`, brain + attack id + shot)

| Elite | World | Attack id | Shot | Tell (def) | What it does | Themed |
| --- | --- | --- | --- | --- | --- | --- |
| Eventide Bastion | Space | `ward_curtain` | Bolt x7 3.4 u/s, .22 | .9 | curtain of slow bolts onto a row of spots with one gap (`lobSpacing` .75) | 2 (neon wall) |
| Orbit Reaver | Space | `crescent_volley` | Shard x4 4.6 u/s | .6 | circles, claws fire shards at a locked spot from changing angles | 2 |
| Rift Lancer | Space | `rift_rail` | Bolt x5 8.5 u/s, .18 | .85 | blinking sight line, then a rail of fast bolts down it | 2 (rail gun) |
| Singularity Hauler | Space | `gravity_sling` | Bolt x4 3.0 u/s, .24 | .8 | `Sling`: shots curve through a ringed well | 2 |
| Rimebreaker | Frost | `ice_ram` | Shard x2 4.5, bounces 1 | .65 | ploughs down a locked lane, shards off the prow and rocks | 1 |
| Floe Harrower | Frost | `floe_cast` | Slab (hazardSize/Armour) | .8 | staggered ice slabs with one gap, then a lance | 2 (the only painted-ice hostile object) |
| Cryo Siren | Frost | `frost_bloom` | Orb fuse -> shard ring; beam sweep every 3rd | .6 | | 2 |
| Glacier Tender | Frost | `drone_deploy` | tethered drones | .8 | shielded while two live | 1 |
| Whiteout Sentinel | Frost | `armour_shatter` | Shard x5 4.0 | .6 | ice plates soak hits, spray shards, then it charges | 1 |
| Resin Warden | Verdant | `resin_mortar` | Glob x3 lob, `lobSpacing` 1.1, `lobAhead` 1.8 | .7 | row of sticky pools | 1 (pink pools) |
| Sunstoke | Ember | `lance_dash` | Bolt x2 5.0 | .6 | dash, fan of needles at the tip | 0 |
| Coalrunner | Ember | `broadside` | Bolt x4 5.5 | .5 | strafes both side cannons | 0 |
| Kilnback | Ember | `slag_drop` | Slag x4 1.0, .34 | .6 | sinking slag in the lane it blocks | 1 |
| Brass Vulture | Ember | `claw_dive` | Shard x3 5.0 | .5 | dives through the pilot's spot, flings scrap | 0 |
| Ash Wraith | Ember | `blink_shards` | Shard x3 6.0 | .45 | blinks, fans shards | 0 |
| Cauterizer | Ember | `siege_cannon` | Shell x1 9.0, .4 | 1.1 | sight line, one piercing shell | 1 |

Verdant has only one elite (Resin Warden); Tide has none. `EliteShip`, `EliteBrains` hold the movement; shots go through
`EliteShots.Fire(owner, def, kind, at, velocity)`.

## 6. Boss audit

`BossCatalog.Build()`; three `BossAttack`s each, unlocked in thirds (`UnlockedAttacks`), last third at
`FinalPhaseCooldownScale` .75; shot hit radius `BossConfig.BoltHitRadius` .13 / `ShardHitRadius` .12; beam hit =
`BeamHitFraction` .7 of its width; `BeamGrowSpeed` 26 u/s; `BeamSightWidth` .3 x; FX sheet `boss_attack_fx.png` 6x4 (spark x3,
muzzle flash x2, charge ring) per world row, each row in the world's accent (Space pink, Frost cyan, Verdant lime, Ember orange).

| Boss | Attack | Kind | Tell | Numbers | Art / theme today |
| --- | --- | --- | --- | --- | --- |
| Void Archon (Space) | chin cannon | Aimed, 3 volleys x1 | .7 | 4.8 u/s Bolt, absorb | neon lozenge (themed) |
| | core burst | Fan 2 volleys x7, 100 deg | .8 | 3.1 u/s Shard, bounce 1 | plasma hearts (themed) |
| | pod lasers | Beam from PodL, PodR, sweep 18, width .32, hold .9 | .95 | middle safe | pink beams (themed) |
| Hoarfrost Leviathan (Frost) | icicle spray | Fan 3 x5, 70 deg, Bolt, bounce 2 | .8 | 3 u/s | **painted ice crystals: themed art, generic fan motion** |
| | glare beams | Beam EyeL/EyeR, AtShip, sweep 10, .26, hold .6 | .8 | | pink band, no frost cue |
| | blowhole hail | Lob 2 x4, Shard, `lobUp` 2.2, gravity 6, fall 3.6 | .8 | | painted ice shard rain |
| Bloom Queen (Verdant) | stinger thorns | Aimed 4 x1, Bolt | .6 | 4.1 u/s | painted lime thorn dart |
| | spore bloom | Fan from 6 petals radial, 2 each, 24 deg, Shard, bounce 1 | .9 | 2.5 u/s | painted spiky spore ball |
| | acid cannons | Beam CannonL/R, aim 30 sweep -30, .3, hold .8 | 1.0 | | **lime beam art (breaks the pink rule), pink sheath only** |
| Cinder Drake (Ember) | fire breath | Fan 2 x9, 120 deg, Bolt, bounce 1 | .7 | 3.3 u/s | painted fire spike |
| | furnace slugs | Aimed 3 volleys x3, 24 deg, Shard | .6 | 4.9 u/s | painted magma shard |
| | brow laser | Beam AtShip sweep 55, .34, hold .8 | .8 | | pink band |

Boss shot frames are exactly two per shape (`BossArt.ShotTicks`), drawn at their native 128 px cell; the rim is built per
cell from alpha (`BossArt.BuildShotRims`), so any new boss shot cell automatically gets the pink outline.

## 7. Identical-across-worlds list (what the user will notice)

| Attack | Worlds that all do it | Verdict |
| --- | --- | --- |
| Rail-mine laser | Space, Frost, Verdant, Ember (and Tide's stand-in) | one beam everywhere: needs 4 skins |
| Aimed single bolt | Space f3/f4, Frost Icicle, Ember Scorch/Brand, Verdant Hornet, Alien | identical pink dart |
| Fan of shards | Frost Golem/Kite/Hailstorm, Space Bile Mite | identical pink arrowhead |
| Ring of 8 | Ember Pyre (and Tide stand-in) | |
| Lunge dash | Space x2, Frost, Verdant x2, Ember | body-only, invisible theme |
| Slag lob/sink | Ember Magma Skull, Kilnback | burr, not magma |
| Boss "Fan/Aimed/Lob/Beam" | all four | same four motion kinds; only the painted cell differs |
| Elite curtain/rail/sling/orb | Space / Frost | already bespoke |

## 8. Themed-ness scoreboard (today)

Roster enemies that attack, by how well the attack matches the material today (2 = matches, 1 = name/tell only, 0 = generic
pink dart or a body dash); elites in brackets; bosses excluded (their art is themed, their motion is not).

| World | Attackers | Themed 2 | Themed 1 | Themed 0 |
| --- | --- | --- | --- | --- |
| Space | 7 (4 elites) | 1 [4] | 3 | 3 |
| Frost | 6 (5 elites) | 0 [2] | 1 [3] | 5 |
| Verdant | 5 (1 elite) | 0 | 1 [1] | 4 |
| Ember | 6 (6 elites) | 0 | 2 [2] | 4 [4] |

Conclusion: the roster gap is **art + motion identity**, not attack count. See the design doc.
