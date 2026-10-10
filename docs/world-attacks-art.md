# World attacks: art list

Companion to `world-attacks-design.md`. Ready-to-run prompts for every file below: `world-attacks-codex-prompts.md`.
Palette and readability rules: design doc section 0.1 (the pink-cue contract PC1-PC5). Codex makes anything that is *painted*
(bodies, trails, impact bursts, jets, bands, bolts); everything geometric and everything that must match a hitbox exactly is code.

## 1. What needs NO Codex (code / procedural)

| Item | Why code | Where |
| --- | --- | --- |
| Space's whole attack set: pulse lance bars, rail-gun slug + afterimage, ion arcs (zig-zag polylines), scan-line band, ion spit comet tail, trails | neon lines are exactly what `EliteFxArt` already draws procedurally (`Dart`, `Bar`, `Streak`) | new `SpaceAttackFx` over `EliteFxArt`, `EliteFx.Emit` |
| All telegraph previews: dotted footprint outlines (cone, column, band gap, ring gap, strike column), the lane glyph ring | must equal the hit geometry, so they are drawn from it (FR2) | `AttackPreview` (new), `EliteFxArt.Ring`/`Sight` |
| Snow cloud over Hailstorm (a few `EliteFxArt.Puff` sprites + falling flake dots) | cheap, tinted, alpha-animated | `HailCloudFx` |
| Heat shimmer along the Ember beam / flame | already exists: `EliteFx.HeatShimmer` | reuse |
| Hit boxes, pools, fuses, lobs, slings, Slab glides | existing `EliteShot` | reuse |
| Cosmetic trails for dashes (afterburner, foam wake, ion afterimage, static trail) | particles from the world's trail puff cells (Codex, in `fx` atlases) *or* tinted `Puff` | `ShotTrail` |
| Jagged lightning shapes of the Tide `thunder arcs` between rails | polyline with 3 random kinks per frame | code (`Strike` Thunder body is Codex; arcs code) |
| Pink keyline of every sprite | `ShotOutline` (bold on bright worlds) | reuse |
| Sounds | synthesised (`AttackSrc~`), not Codex | design doc 0.3 |

Everything else below is Codex. Space has **0 art files** (an optional `space_attack_shots.png` if the procedural slug looks thin).

## 2. Conventions for every file

* Folder (staged, ignored by Unity): `Pause/Assets/Art/Attacks/<World>~/` (+ `src~/` for generators and candidates). Shipped files go to
  `Pause/Assets/Art/Resources/Attacks/<World>/` in the wiring change. Names: `<w>_attack_<kind>.png` (`frost_attack_shots.png`...).
* PNG, RGBA, transparent, **nearest-neighbour only**, authored on a 64 px native cell and upscaled x2 (128 px cell) with no smoothing
  (the verifier downsamples x2 and upsamples x2 and compares); max ~24 colours per material ramp.
* Shots and fall-down things point **DOWN the screen** (the boss atlas convention; the code rotates to the heading).
* Frames a/b alternate at 8-12 fps (`EliteArt.Tick` units); b must differ from a by >= 3% of the opaque pixels (flicker, drip, spin).
* Colours: **pink-white core** (hue 312-326, >= 30% of the opaque area pink family or white), the world ramp around it, a 1 px dark
  ink outline `#0B0B1A`-ish; never red (hue 345-15 at sat > .5), never a saturated pickup hue (see per-world slots); no glow halo
  baked in (the game adds `ShotOutline`); nothing within 4 px of a cell border (alpha <= 24).
* Draw size at runtime: height = 2 x the nominal `shotSize` (hit radius unchanged; `ReadabilitySweep` decides the final factor, fall back to 1.5 x).
* World units: jets and strips use **128 px per world unit** (so a 128 px cell is 1 u).

## 3. File list per world

Cell = 128 x 128 unless stated. "Used" cells are drawn; reserved cells stay fully transparent.

### 3.1 `<w>_attack_shots.png` -- 1024 x 256, 8 x 2 cells, all four worlds (Frost, Verdant, Ember, Tide)

Row 0, left to right: `bolt` a,b / `shard` a,b / `shell` a,b / `slag` a,b. Row 1: `pool` a,b / `sigA` a,b / `sigB` a,b / `sigC` a,b.

| Cell | Frost | Verdant | Ember | Tide |
| --- | --- | --- | --- | --- |
| bolt (fast, thin, ~1:3, pointing down) | **Icicle spear**: long pale crystal, pink-white core, 2 frost chips orbiting | **Thorn dart**: bark-brown spike, pink tip, a sap drop | **Ember pellet**: spark with a short fire tail, white-hot pink-fringed | **Spout drop**: teardrop of white water, pink-white core, 3 spray dots |
| shard (small, 1:1.3) | **Hail chip**: faceted white-lilac pellet, pink core | **Leaf**: dark-green blade, pink midrib, curled edge (a: flat, b: edge-on) | **Cinder shard**: hot black-plum splinter, pink crack | **Pearl**: scalloped clam cup holding a pink pearl (not a disc), 2 bubbles |
| shell (heavy, ~1:1.7) | **Ice boulder**: lumpy snowball with a pink core | **Log chunk**: short bark trunk, pink-lit cut end | **Fireball**: big orb, white core, pink fringe, tail | **Depth charge**: horned sea-mine inside a pink-rimmed bubble |
| slag (blob, ~1:1) | **Slush blob**: grey-white slush, pink core | **Sap glob**: amber-brown sap, pink glint (sat <= .6) | **Magma blob**: molten, pink fringe, drips | **Ink blob**: black-teal ink, pink rim |
| pool (lingering, wide, 1.0 u) | **frost mist** pool | **spore cloud** (soft clusters, pink core wisps) | **lava pool** | **net mesh** pool (knotted net, pink knots) |
| sigA | shatter chip (small) | **Crescent leaf-blade** (horizontal, 128 wide crescent, thin) | slag splat decal | **ink pool** |
| sigB | reserved | **Seed pod** (lobbed: oval husk, thorns, pink seam) | reserved | reserved |
| sigC | reserved | pod burst (open husk) | reserved | **bubble ring** (pink-rimmed, spiked: PC4) |

Used cells of the shots file: Frost 12, Verdant 16, Ember 12, Tide 14 (the rest reserved).

### 3.2 `<w>_attack_fx.png` -- 1024 x 256, 8 x 2 cells, all four worlds

Row 0: `impact` 4 frames (the pop when a shot meets a rail / the board edge / is shot down; 128 cell, bursts to ~90% of it; plays 14 fps) + `special` 4 frames
(the world's signature impact: Frost shatter chips, Verdant splinters + spore puff, Ember slag splat, Tide splash crown).
Row 1: `trail` 4 frames (small particle puffs the game emits behind shots, content ~48 px, centred) + `glyph` a,b (ground mark: the lane / landing marker,
a pink-white ring with the world's motif, dotted) + `flash` a,b (muzzle flash, ~70 px).

### 3.3 `<w>_attack_strike.png` -- 768 x 512, Frost (icicle drop), Ember (eruption), Tide (thunder)

Row 0 (128 x 384 cells x 6): the **column body**, top to bottom, plays once over the .25 s live window at 24 fps: Frost a falling spear + mist; Ember a magma geyser
(base wide, white-hot centre); Tide a vertical jagged lightning bolt with a blue-white glow. Row 1 (128 x 128 x 6): `glyph` a,b (the lane marker that blinks during the tell; the
hit width is .36 u = 46 px: draw the marker 1 u wide) + `burst` 3 frames at the ground + 1 reserved.

### 3.4 `<w>_attack_jet.png` -- Ember (flame cone), Tide (water jet)

* **Ember** 768 x 384: row 0 six frames 128 x 256 = the flame cone, **apex at the top centre**, 1.8 u long (230 px of 256), 0.76 u wide at the tip, flickering edges, white-pink core, orange
  body, pink fringe (loop, 12 fps). Row 1 six cells 128 x 128: `nozzle` 3 frames (a flare at the muzzle, 3 growth stages for the tell) + `tip` 3 frames (sparks dying at the far end).
* **Tide** 768 x 448: row 0 six frames 128 x 320 = a straight pressure jet column .5 u wide, 2.5 u long (320 px), white water, pink-white core, spray beads, loop 12 fps; row 1 six cells 128 x 128: `nozzle` 3 + `splash` 3.

### 3.5 `tide_attack_wave.png` -- 512 x 480 (the surf wave), tileable in x

Rows 0-3: four 512 x 96 **body** frames of the band, **seamless left-right**, foam crest on its lower edge, 12 fps loop; the band's hit thickness is .4 u = 51 px, draw 96 px tall (crest + wash).
Row 4 (96 px): `capL` `capR` (96 x 96 rounded ends that meet the gap), `gapMarker` a,b (96 x 96 arrow-and-ripple glyph shown at the gap edges during the tell). Pink-white crest line is the keyline (PC1).

### 3.6 `frost_attack_ring.png` -- 1024 x 128 (the cold-blast ring)

Eight 128 x 128 cells: `bar` x4 (a tangent arc bar 112 x 36, frost-crystal bar with a pink-white core line; the code places 18 of them on a circle and omits the ones in the gap),
`gapMarker` a,b (the crack edge), `glyph` a,b (the snowflake ring pulsing at the muzzle during the tell).

### 3.7 `<w>_attack_beam.png` -- 1024 x 128, 8 cells, the mine's beam skin (Frost, Verdant, Ember, Tide)

Same slot order the mine laser already reads from `BossArt` + `BossAttackFx` (so the skin plugs into `MineLaserArt`):
c0 `aim` (the thin aim-line tile, 128 wide, stretches along its length), c1-2 `beam` a,b (the beam, content ~64 px wide in a 128 cell, stretches along its length, white-pink core),
c3-4 `flash` a,b (the muzzle flash), c5-7 `spark` a-c (the rail impact). Frost: crystalline cold beam with frost sparkle; Verdant: a thorned vine (pink thorn tips); Ember:
white-hot orange beam with magma drips; Tide: white-water jet with spray. **Must not contain lime (Verdant), cyan 178 (Frost, Tide), amber 37 (Ember) in more than 8% of its saturated area.**

### 3.7b `ember_attack_mineflame.png` -- 768 x 768, Ember's mine flame-thrower (replaces the 3.7 beam skin for Ember only)

The Ember rail mine fires a **flame-thrower**, not the boss-cell laser (user ask, Oct 10). `MineFlameArt` draws all of it procedurally today
(six looping 48 x 448 tongue frames, a growing pilot flame, a dashed gas-jet aim line, a muzzle burst, a rail scorch, a heat haze) and
`AttackArt.MineFlame(3)` serves Codex's atlas slot by slot as soon as `Resources/Attacks/Ember/ember_attack_mineflame.png` exists
(a missing cell stays procedural). Grid, 128 px cells, nearest-neighbour:

* row 0 (y 0): `beam` a-f, six cells **128 x 384**, one loop at 12 fps. Local bottom = the muzzle, flow runs UP (away from the mine). It is stretched over the whole lane
  (about 6 u long, 0.4 u wide), so keep detail in vertical streaks and tongues, no features that must keep an aspect ratio. Content ~100 px wide: white-yellow core (~20 px),
  amber and orange body, a **deep red-orange edge**, a thin **neon-pink rim** (hostile cue), flickering tongues licking out of the edges, ember specks, soft turbulent edge.
* row 1 (y 384): `pilot` a-d (the gas flare building at the muzzle: a teardrop flame, base at the cell's bottom centre, tip up; the code scales it from 0.1 to 0.36 u through
  the 1.1 s tell), `aim` a,b (a 128 tile, content 12 px wide, a dashed gas jet, tileable along its length; code uses cell a).
* row 2 (y 512): `burst` a-d (the muzzle flare when it fires, ragged tongues, ~120 px), `impact` a,b (the scorch where the flame meets the far rail).
* row 3 (y 640): `impact` c, `haze` a,b (a very faint warm heat-shimmer veil 128 x 128, wide soft sides, under 25 % alpha), three reserved.

**Palette exception (Ember mine only).** The game's rule is "no red but the player's" (`HostileGlow.IsPlayerRed`, 28 deg around red). The user asked for a red / fire look for these mines,
so this one attack may carry a red-orange edge: **hue >= 17 deg and never within 22 deg of the player's red (#FF3E4E, hue 355)**; the shipped edge colour is #D04C0E (hue 19, 24 deg away).
The hostile cue is kept as a thin **neon-pink rim and pink ember specks (hue 318-330)**, and a dark plum outline (#220C26) separates the flame from the lava backdrop.
Do not use the player's red, blue, lime or cyan. `RailMineLaserTest` measures the gap on the shipped pixels. Every other world's mine, and Tide's, keep the 3.7 skin and the old rule.

### 3.8 `<w>_attack_lash.png` -- 1024 x 128, 8 cells, Verdant (vine whip) and Tide (tentacle whip)

c0-1 `link` a,b (one 128 px section of the whip pointing down, tiles end to end along the curve), c2-3 `tip` a,b (Verdant: a thorn with a pink tip; Tide: a barnacled tentacle tip with a
pink sucker core), c4-5 `root` a,b (where it leaves the body: a bud / a hatch collar), c6-7 `dash` a,b (dotted arc preview segment, pink-white).

### 3.9 `verdant_attack_log.png` -- 1024 x 256, 4 x 2 cells of 256 x 128

Eight frames of a **tree trunk rolling** (cylinder seen from above, lying horizontal: 256 px = 1.1 u long, 128 px tall = .55 u), bark texture scrolling one eighth of a turn per frame, moss and a pink-lit
cut end ring on both ends (the "this hurts" cue), 12 fps. Used for the `Roll` lob (airborne tumble rotates the same frames) and the Timber Hauler elite.

## 4. Per-world totals

| World | Files | Cells (grid total) | Drawn cells | Notes |
| --- | --- | --- | --- | --- |
| Space | 0 (optional 1) | 0 | 0 | all procedural |
| Frost | 5: shots, fx, strike (icicle drop), ring (blast), beam | 16 + 16 + 12 + 8 + 8 = 60 | 12 + 16 + 11 + 8 + 8 = 55 | biggest win: spears, hail, shatter |
| Verdant | 5: shots, fx, lash, beam, log | 16 + 16 + 8 + 8 + 8 = 56 | 16 + 16 + 8 + 8 + 8 = 56 | vines, leaves, trunks |
| Ember | 5: shots, fx, jet (flame), strike (eruption), beam | 16 + 16 + 12 + 12 + 8 = 64 | 12 + 16 + 12 + 11 + 8 = 59 | flame cone is the hardest loop |
| Tide | 7: shots, fx, jet (water), wave, strike (thunder), lash, beam | 16 + 16 + 12 + 8 + 12 + 8 + 8 = 80 | 14 + 16 + 12 + 8 + 11 + 8 + 8 = 77 | needs Tide's palette decision first |
| **Total** | **22 files** | **260** | **247** | plus optional Space file |

## 5. Codex jobs (two at a time; the add-world numbering continues: J12 = attack art)

| Job | Content | Depends on |
| --- | --- | --- |
| J12a Frost | shots + fx | none (reference: `Frost_shots.png`, Frost roster strips) |
| J12b Frost | strike + ring + beam | J12a (style lock) |
| J12c Ember | shots + fx | none (reference: `Ember_shots.png`) |
| J12d Ember | jet + strike + beam | J12c |
| J12e Verdant | shots + fx + log | none (reference: `Verdant_shots.png`) |
| J12f Verdant | lash + beam | J12e |
| J12g Tide | shots + fx + beam | Tide palette (OPEN question 1 in `world-5-6-briefs.md`) and the Tide roster strips for the style |
| J12h Tide | jet + wave + strike + lash | J12g |

Order for the first world pass: J12a + J12c, then J12b + J12d, then J12e + J12f; Tide after its roster is drawn. The coordinator opens every sheet for the user
before wiring (memory: Codex headless art workflow).

## 6. Acceptance for every file (checked by `scripts/verify_attack_art.py`, to be written in plan phase 0b)

* exact size, RGBA, cell grid; empty cells exactly the reserved ones; no pixel with alpha > 24 within 4 px of a cell border;
* pink share: >= 30% of the opaque pixels of every *shot* cell are pink-family (hue 312-326 +-8) or near-white; every strip has a pink-white edge or core line;
* hue audit: < 8% of the saturated pixels within 20 deg of 178 / 82 / 259 / 37 (the pickup hues) except where the per-world slot allows; zero pixels in the red band at sat > .5;
* frame a vs b differ by >= 3% of the opaque pixels; the x2-nearest round trip is identical;
* contact sheet `preview.png` on `#0b0b1a` at 2x plus the same sprites over the world's real backdrop tiles (3 tiles, 2 sizes) -- the readability the game will see.
