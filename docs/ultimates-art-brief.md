# Ultimates: audit and art brief

User feedback (Oct 10): "the UFO laser attack is really ugly; we need proper animations for all the ultimates (boss / elite signature
attacks) that may require proper attack art." This is the audit (what is there, what is wrong, ranked) and the per-attack art spec.
Ready-to-run Codex prompts: `ultimates-codex-prompts.md`. Geometry source for the older themed-hazard atlases: `world-attacks-art.md`.
Codex is rate-limited until Oct 13 22:53, so nothing here is generated yet.

## 1. The "UFO laser"

It is the **Void Archon's `pod lasers`** (Space boss, the capital carrier; `BossCatalog` attack 2, `BossAttackKind.Beam`, tell pose 2, tell .95 s,
hold .9 s, sweep 18 deg, two beams from the PodL / PodR muzzles). It is a `BossBeam` (`BossProjectiles.cs`). Previews:
`scratchpad/ultimates_audit/space_podlasers_before.png` / `_after.png` (frames from `BossAttackPreview -only space -attack 2`).

Why it looked ugly (read from the code and the before strip):

1. **The beam body is two stretched sprite cells** (`BossArt.Shot(Beam0 / Beam0+1)`, 2 frames, 2 ticks each = 12 fps) scaled to `width x length`.
   One constant-width hot-pink strip with a hard edge, no falloff, no travelling energy: a flat rectangle. The "animation" is a width flicker
   (1.0 / .85 on twos) and the two-cell swap.
2. **The tell is a 1-2 px dark-magenta line** (`Telegraph` cell, blinking) that scans the arc, plus the generic charge ring. The pod itself shows no
   buildup: the energy does not visibly gather before it fires. The only "charge" is the shared dotted ring (`BossAttackFx.Ring`).
3. **No ignition**: the beam grows at 26 u/s from a 2-frame 1-unit flash cell, so it starts as a thin stub with no burst at the root.
4. **No impact on the floor / bottom**: the beam just runs off the screen bottom; a rail hit shows a 3-frame generic spark only on a rail.
5. **No fade-out frames**: it thins linearly over .1 s (width x alpha scale) and vanishes.
6. **Same art for 5 bosses and every rail mine** (Frost glare, Verdant acid, Ember brow, Tide beak jet, and `RailMineLaser` borrows the same cells):
   no beam looks like the thing that fires it (a plasma lance, a cold ray, an acid hose, a fire laser, a water jet).
7. The failing test `BossAttackTest "Space pod lasers ... opaque pixel"` was a **real (small) art/data bug, not a test bug**: `BossEmitterTable` is
   measured on the 12 base drawings; the Archon's six engine-idle cells (20..25) fell back to the base idle muzzle, which is 2-9 px off the ink in
   cells 20, 22, 24, 25. For the .1 s fade (the boss has already returned to idle) the beam root hung off the pod. Fixed with a measured table
   (`BossEmitters.SpaceIdleMuzzles`).

Code-only improvement done now (`SpaceBeamFx`, see section 5): procedural beam, windup flare, muzzle bloom, rail impact star. Hitboxes, tells,
timings untouched. It is a stand-in: the authored kit (`space_attack_laser*`, entry U1) replaces it.

## 2. Inventory of every boss and elite signature attack

Legend. Visual: **A** authored art in the boss/elite sheets, **P** procedural at runtime, **P-lo** procedural at 32 px/unit (no atlas shipped: nothing under
`Resources/Attacks/` exists yet, `AttackHazardArt`/`AttackHazardArtJetWave` fall back to code). Tell: T = dedicated tell visual, t = only the boss/elite pose
change. Loop = sustained animation. End = impact/fade frames. "Fights" = in the live rotation today (`themedAttacks` is on for Frost and Ember only).

### 2.1 Boss signature attacks (5 bosses)

| # | Boss / attack (kind) | Visual | Tell / charge | Live loop | Impact / end | Proper animation? | Fights |
| --- | --- | --- | --- | --- | --- | --- | --- |
| B1 | Space `pod lasers` (Beam, 2 pods) | P-stretched 2 cells; now P (SpaceBeamFx) | thin sight line + pod ring + pose | 2-frame flicker (now 4-frame noise loop) | none / 3-frame rail spark (now bloom + star) | **No** | yes |
| B2 | Space `core burst` (Fan shards, bounce) | A shot cells (2-frame shard) | reactor hatch pose + ring | 2 frames | generic spark on rail | partial (shots fine, no burst art at the core) | yes |
| B3 | Space `ion arcs` (Jet Lance, 2 pods) | P-lo (neon line) | nozzle flare P-lo, footprint | 2 frames flicker | tip sparks P-lo | **No** | no (needs Streak, art, sounds) |
| B4 | Space `scan line` (Wave Scan) | P-lo band, 2 frames | gap markers P-lo | 2 frames | none | **No** | no |
| B5 | Frost `glare beams` (Beam, 2 eyes, AtShip) | P-stretched 2 cells | sight line + ring | 2-frame | rail spark | **No** | yes |
| B6 | Frost `icicle drop` (Strike x3) | P-lo column | lane glyph | single column body | burst P-lo | **No** | yes |
| B7 | Frost `cold blast` (Blast ring + crack) | P-lo bars | muzzle glyph, gap marker | bars shimmer 2 frames | none | **No** | yes |
| B8 | Frost `blowhole hail` (Lob) | A shot cells | pose | 2 frames | splash spark | partial | yes |
| B9 | Verdant `acid cannons` (Beam, 2 cannons, sweeping in) | P-stretched 2 cells | sight + ring | 2-frame | rail spark | **No** | yes |
| B10 | Verdant `spore bloom` (Fan) | A shot cells | pose + sparks | 2 frames | spark | partial | yes |
| B11 | Verdant `vine lash` / `trunk toss` (Lash, Roll) | not built | - | - | - | - | no |
| B12 | Ember `brow laser` (Beam, AtShip, 55 deg rake) | P-stretched 2 cells | sight + ring | 2-frame | rail spark | **No** | yes |
| B13 | Ember `flame sweep` (Jet Flame, jaw) | P-lo cone (6 frames if atlas) | nozzle flare P-lo | flicker | tip sparks P-lo | **No** | yes |
| B14 | Ember `eruption columns` (Strike x3) | P-lo column | lane glyph | single body | burst P-lo | **No** | yes |
| B15 | Ember `furnace slugs`, `fire breath` (Aimed / Fan) | A shot cells | pose | 2 frames | spark | partial | yes |
| B16 | Tide `beak jet` (Beam, down, 30 deg sweep) | P-stretched 2 cells | sight + ring | 2-frame | rail spark | **No** | yes |
| B17 | Tide `thunder strike` (Strike x3) | P-lo column | lane glyph | single body | burst P-lo | **No** | no |
| B18 | Tide `surf wave` (Wave Surf) | P-lo band | gap markers | 2 frames | none | **No** | no |
| B19 | Tide `starboard cluster` / `port cluster` (Lob / Fan) | A shot cells | pose | 2 frames | spark | partial | yes |

### 2.2 Elite signature attacks (16 attack ids, 15 elites; no Tide elites yet)

All elite shots are tiny procedural sprites (`EliteFxArt`: Dart 10x20, Burr, Puddle, Chevron slab ...) plus the elite strip's authored tell/action cells
(`EliteDef.cells.tell/action`, or a muzzle flash + recoil when -1). Telegraph lines are `EliteFxArt.Sight` (a bar). No elite has authored attack/FX art.

| # | Elite (world) / attack id | What the player sees | Tell | Live | End | Proper animation? |
| --- | --- | --- | --- | --- | --- | --- |
| E1 | Rift Lancer (Space) `rift_rail` | a line of bolts down a locked lane ("rail of light") | sight line, locked | bolts only | none | **No**: should be a real rail beam |
| E2 | Eventide Bastion (Space) `ward_curtain` | row of slabs with one gap | gap marker (bar) | slab chevrons | none | **No** |
| E3 | Singularity Hauler (Space) `gravity_sling` | two orbs sling in on a ring, cross | blinking ring | orbs | none | **No** (no gravity feel) |
| E4 | Orbit Reaver (Space) `crescent_volley` | shards from claws at a locked spot | pose | shard darts | none | **No** |
| E5 | Cryo Siren (Frost) `frost_bloom` | stream of bolts swept over a locked arc (a beam sweep every 3rd) | sight arc | bolts | none | **No** (reads as spray, not a sweep) |
| E6 | Floe Harrower (Frost) `floe_cast` | slab row + fast lance down the gap | sight in gap lane | slabs | none | **No** |
| E7 | Glacier Tender (Frost) `drone_deploy` | releases drones | pose | drones (elite sprites) | scuttle | partial |
| E8 | Rimebreaker (Frost) `ice_ram` | shard pair per rock it breaks, ram dash | pose | shards | none | **No** |
| E9 | Whiteout Sentinel (Frost) `armour_shatter` | enraged charges, plate shards | sight line | dash | plate shatter bits | **No** |
| E10 | Cauterizer (Ember) `siege_cannon` | big shell, slow | muzzle charge | shell | none | **No** (no shell blast) |
| E11 | Kilnback (Ember) `slag_drop` | slag drops that pool | ring | slag + pool | pool | partial (pool P) |
| E12 | Sunstoke (Ember) `lance_dash` | dash + needles | pose | needles | none | **No** (no heat trail) |
| E13 | Brass Vulture (Ember) `claw_dive` | dive at locked spot | pose | shards | none | **No** |
| E14 | Ash Wraith (Ember) `blink_shards` | blink + shard ring | blink | shards | none | **No** |
| E15 | Coalrunner (Ember) `broadside` | volleys from muzzles | pose | bolts | none | **No** |
| E16 | Resin Warden (Verdant) `resin_mortar` | globs land as sticky pools | ring | glob | pool | partial |

Rail mines (the one other "laser"): `RailMineLaser` borrows the boss beam cells with magenta recolour; any beam kit improvement should also reskin it
(`<w>_attack_beam.png`, prompt P7 of the older doc).

## 3. Ranking

Score = how bad it looks x how often / how large the player sees it. Phase-3 boss attacks and attacks that cover the screen rank highest.

| Rank | Attack | Why |
| --- | --- | --- |
| 1 | **B1 Space pod lasers** | the complaint; two full-height beams in the final third of the first boss; flat rectangles |
| 2 | B12 Ember brow laser, B5 Frost glare beams, B16 Tide beak jet, B9 Verdant acid cannons | same flat 2-cell beam, each is the 3rd attack of its boss |
| 3 | B7 Frost cold blast, B13 Ember flame sweep, B6/B14 Frost icicle / Ember eruption columns | live now (Frost, Ember) but P-lo: a 32 px/unit pixel bar next to a 384 px boss atlas; no real flame, no real ring |
| 4 | E1 rift rail, E5 frost bloom sweep, E2 ward curtain, E6 floe cast | whole-lane elite attacks: lines of tiny darts instead of a beam / wall |
| 5 | B3/B4 Space ion arcs / scan line, B17/B18 Tide thunder / surf | not fighting yet; must be authored before they are switched on |
| 6 | E10 siege cannon, E3 gravity sling, E9 armour shatter, E12 lance dash | single-target but loud tells |
| 7 | the remaining elite shot attacks (E4, E8, E13-E15, E7, E11, E16) | covered by the world `*_attack_shots` / `fx` atlases (P1, P2) once they exist |

## 4. Art spec (what Codex must paint)

Common rules for every entry (the prompt file repeats them):

* **Palette**: world body ramp (below) with the **hostile pink cue** (hue 312-326: `#FF4FD8`, `#FF8AE6`, `#FFE0F8`) as core / fringe, >= 30% of opaque
  pixels pink-family or white-hot; **no red** (hue 345-15) anywhere, only the player is red; never a saturated pickup hue (178 cyan, 82 green, 259 violet, 37 amber).
* **Rich tonal detail**, 5-7 tone ramp per material, not posterised; neon pixel art, 1 px ink outline `#0B0B1A` on solid parts (not on glow/energy).
* **Margins >= 6 px** of alpha <= 24 around the content of every cell. **Transparent background, no baked halo or glow-box** (alpha falls to 0 smoothly by pixel
  steps, no matte fringe; the game adds the sheath/keyline).
* **Loop motion**: every sustained loop changes >= 3% of opaque pixels per frame (energy rolling, flame licks, sparks); frame a-vs-b by pixel difference.
* **Scale**: 128 px = 1 world unit. Draw size ~ the hit width x 3 with the glow, so the hit core (`BeamHitFraction` .7 x width) is the bright part.
* Author on a 64 px native cell, upscale x2 nearest-neighbour, no blur.

### 4.1 Boss laser kit (B1, B5, B9, B12, B16): one kit per world, 3 files

| File (staged `Pause/Assets/Art/Attacks/<World>~/`, shipped `Resources/Attacks/<World>/`) | Size | Content |
| --- | --- | --- |
| `<w>_attack_laser.png` (new slot `laser`) | 1024 x 384 | row 0: `windup` 1-8 (128 px cells): the muzzle gathering, 8 frames at 12 fps (small spark -> swelling core -> overbright, last frame = ignition white-out) ; row 1: `muzzle` a-d (4-frame loop, 12 fps, bloom at the root during the burn) + `fade` 1-4 (root dying); row 2: `impact` 1-4 (rail / floor hit, grows to 90% of the cell, fades) + `spark` a-d (4-frame loop of sparks flying off the rail) |
| `<w>_attack_laserbody.png` (slot `laserbody`) | 512 x 256 | 4 frames of **128 x 256, tile vertically seamless**, 12 fps loop: the beam, centre column; core <= 16-20 px wide, body ~44 px, soft glow to 80 px, content of 100 px max width, world-material flavour along the edge (see per world). The game tiles/stretches it along the length, so no feature may depend on aspect |
| `<w>_attack_lasertell.png` (slot `lasertell`) | 512 x 128 | `sight` a,b: 128 x 128 tileable dotted aim line (4 px core, pink, soft) + `lock` a,b: a reticle that blinks on the aim end (96 px) |

Timing: tell windup plays across `tellSeconds` (.8-1 s = ~8 frames at 12 fps, hold last); live loop for `hold` .6-.9 s; fade 4 frames over `BeamFadeSeconds`
(raised from .1 to .25 s on wiring: only the visual fades, the hitbox still ends with the hold).

Per world:

| World | Beam | Body ramp | Notes |
| --- | --- | --- | --- |
| Space (B1) | **plasma lance**: white-hot core, hot pink body, violet-magenta (hue 300-326) outer glow, rolling charge pulses, arc-sparks crawling on the edge, a tight muzzle bloom with lens cross | `#FFE0F8 #FF8AE6 #FF4FD8 #C02AA8 #7A1E78`; steel pod light `#B6BCD0` in the flare | hostile pink is the main colour here |
| Frost (B5) | **cryo ray**: white-lilac body, crystal sparkles along edges, pink-white core, frost mist at the root, ice spikes at impact | `#EAF2FF #CFDDF2 #A9BFE0` + pink core | no saturated cyan |
| Verdant (B9) | **acid hose**: dark leaf-green body (value <= .55), sap drips, pink-white core, bubbling edge, spray at impact | `#1E3A24 #2F5A33 #4A7A44`, sap `#8A6A2A` | no lime |
| Ember (B12) | **fire laser**: white-hot core, orange body (hue 22-30), heat ripple edge, magma drips at the rail, ember sparks | `#FFF4D0 #FF9A2E #E0701C #8A3A1A`, pink fringe | no amber 37, no red |
| Tide (B16) | **pressure jet**: white-water body, spray beads, foam edge, pink-white core, splash crown at impact | `#F0FFFA #BFEFE4 #8FD0C4` + pink core | no cyan 178 |

### 4.2 Themed hazard atlases (B3, B4, B6, B7, B13, B14, B17, B18): already specified

The jet / wave / ring / strike atlases (`<w>_attack_jet.png`, `_wave`, `_ring`, `_strike`) are fully specified in `world-attacks-art.md` and
`world-attacks-codex-prompts.md` (P3-P6) and the code already consumes them (`AttackArt`); they only need generating. This brief adds **the missing windup
and end frames those prompts lack**:

* every `strike` column: add `tell` 1-6 (6 frames, 12 fps, the lane marker building: ring closes, mist/ember gathers, last frame flashes) and `end` 1-4;
  file `<w>_attack_strike_fx.png`, 768 x 256 (two rows of six 128 cells).
* every `jet`: nozzle 1-3 (exists) -> make it 6 frames; add `end` 1-4 (jet tearing off at the nozzle); file `<w>_attack_jet_fx.png` 768 x 256.
* Space `ion arcs` / `scan line` (no art at all): `space_attack_arc.png`, `space_attack_scan.png` below.

Space extras: `space_attack_arc.png` 768 x 384: row 0 `bolt` 1-6 (a 128 x 256 lightning lance, zig-zag with forks, re-rolled every frame, 12 fps), row 1 `nozzle` 1-3 + `tip` 1-3 (128 px).
`space_attack_scan.png` 512 x 480: like the Tide wave (4 seamless 512 x 96 band frames: a neon scan bar with pixel-grid noise and a bright leading edge; row 4 caps and gap markers).

### 4.3 Elite signature kits (16 attack ids), 7 files

The elite shot sprites use `EliteFxArt` procedural darts. Stage 1 (cheap): the world `<w>_attack_shots.png` and `<w>_attack_fx.png` (P1/P2 of the
older doc) skin them. Stage 2 (this brief): signature pieces that no shot sprite can do:

| File | For | Size | Content |
| --- | --- | --- | --- |
| `space_attack_rail.png` | E1 rift rail | 1024 x 384 | `sight` a,b (aim line), `slug` a,b (bright railgun slug with a 3-frame sonic ring), `trail` 1-4 (the rail of light left behind, 128 x 384 vertical tile), `kick` 1-4 (recoil flash) |
| `space_attack_curtain.png` | E2 ward curtain | 1024 x 256 | `slab` a,b (neon shield slab 128 x 96, tileable), `capL` `capR`, `gap` a,b (arrow glyph), `spark` 1-2 |
| `space_attack_sling.png` | E3 gravity sling | 1024 x 256 | `orb` a-d (a spiked pink gravity orb, 4-frame spin), `ring` 1-4 (the closing gravity ring, 256 px content, 4 frames), `lens` a,b (space-warp streaks) |
| `space_attack_crescent.png` | E4 crescent volley | 1024 x 128 | `shard` a-d (crescent blade, 4-frame spin), `lock` a,b (reticle), `puff` a,b |
| `frost_attack_sweep.png` | E5 frost bloom, E8 ice ram, E9 armour shatter | 1024 x 384 | `stream` 1-6 (a sweeping bolt-stream skin 128 x 256, 6 frames), `ram` 1-4 (shard pair spray), `plate` 1-4 (armour plate shattering, 128 px cell), `charge` a,b (enraged dash trail 128 x 256) |
| `ember_attack_siege.png` | E10 siege cannon, E11 slag drop, E12 lance dash, E13 claw dive | 1024 x 384 | `muzzle` 1-4 (cannon blast), `shell` a-d (fireball with trail, 4 frames), `crater` 1-4 (impact), `heat` 1-4 (dash heat trail tile 128 x 256), `dive` a,b (claw dive streaks) |
| `verdant_attack_resin.png` | E16 resin mortar | 1024 x 256 | `glob` a-d (amber-brown sap glob with a pink glint, wobble), `splat` 1-4, `pool` a-d (sticky pool 1 u across, bubbling, 4-frame loop) |

Elites already have tell cells; the elite def's `tell`/`action` cells (authored in the elite strips) are NOT part of this brief.

## 5. What was done in code (no art), and the remaining gap

`SpaceBeamFx` (new): procedural 48 x 192 beam body in 4 noise frames (soft-edged core + body + glow, rolling brightness), radial glow sprite, spark star.
`BossBeam` (only when `artKey == "Space"`): windup flare at the pod that swells with tell progress (flickering), a hot throbbing bloom at the root while live
(bigger flash on ignition), a rail impact glow + spinning spark star. Hitbox, `BeamHitFraction`, tells, timing, aim, sweep untouched; the visual width
stays `width` so its opaque part never exceeds the hit (+ sheath). Still missing vs the spec: tell is the old sight line, no fade frames beyond the width
thin, no floor impact, no authored per-frame material detail. These are the `laser` kit's job.

## 6. Wiring plan once art lands (for the coordinator)

1. `BossBeam`: kit lookup `AttackArt.Get(world, "laser"/"laserbody"/"lasertell")`; if present use it and bypass `fancy`; `BossConfig.BeamFadeSeconds` visual-only fade .25 s.
2. `AttackHazardArt*`: the strike/jet fx files for tell/end frames (the jet / strike classes already select by `HasAtlas`).
3. Elite signature kits: `ShotSkins` keyed by attack id (new), `EliteShots` rift rail draws the trail tile along its lane.
4. Switch `themedAttacks` on for Space / Tide only after their art ships and `AttackBudgetTest` / `BossThemedTest` pass.
