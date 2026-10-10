# Codex prompts: world attack art

Same structure as `.claude/skills/add-world/art-briefs.md`: **slots** (per world, filled below) + **common preamble** + **one prompt per atlas
type** + **verification list**. Launch with `scripts/run_codex.sh <job> <prompt.md> [ref-image ...]` (absolute paths for `-i`; one worktree `codex-<job>`,
branch `art/<job>`, max 2 jobs at once; after a job run `verify_attack_art.py`, open every image, write the critique, commit on the job's branch;
**the coordinator shows the sheets to the user before anything is wired**). File list, sizes and cell layouts: `world-attacks-art.md` (the single source
for geometry; the prompts repeat the numbers so each one is self-contained).

Pre-launch lint (every time): search the finished prompt for the *other* worlds' nouns (Frost prompt must not say lava/leaf/water...; Ember must not say ice;
Verdant must not say ice or flame; Tide must not say lava or ice) and for the word "red" outside "no red".

## 1. Slots (filled per world in section 4)

| Slot | Meaning |
| --- | --- |
| `<WORLD>` / `<w>` | display name / lower-case key |
| `<MATERIAL>` | what this world's hostile things are made of, 1-2 sentences |
| `<BODY_RAMP>` | the body palette in words and hexes (the only saturated colour besides pink) |
| `<PINK>` | the hostile cue: hot pink-white core / fringe, hues 312-326 (hexes `#FF4FD8`, `#FF8AE6`, `#FFE0F8` as the family; never red, never violet) |
| `<AVOID>` | hues kept clear of large areas (pickup hues 178 cyan, 82 green, 259 violet, 37 amber, player red 345-15) with the world's own exceptions |
| `<REFS>` | `-i` images: the world's `<Key>_shots.png`, 2-3 roster strips, one backdrop tile |
| `<OUT>` | `./Pause/Assets/Art/Attacks/<World>~/` |

## 2. Common preamble (start EVERY prompt with this)

```
You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait). Work ONLY inside this git worktree; do not
edit any .cs / .meta / existing file; do not delete anything; do not commit. New files go ONLY to <OUT> (generators, prompts,
candidates in its src~/ subfolder; the trailing ~ matters: Unity ignores it).
Read ./docs/art-style.md (neon pixel art, rugged cyberpunk/steampunk industrial, 1 px dark outline on solid forms, 4-6 tone ramps per
material, rim light, glowing neon cores; one electric hue per world on a dark base; NO red anywhere: red means friendly),
./docs/world-attacks-design.md (sections 0.1 and the world's section) and ./docs/world-attacks-art.md (the file list: sizes are binding).
WORLD: <WORLD>. MATERIAL: <MATERIAL>. BODY COLOURS: <BODY_RAMP>.
THE HOSTILE CUE (the most important rule): everything here is a HOSTILE attack. Every piece MUST carry the world's "this hurts"
cue: a hot pink core / fringe <PINK> -- at least 30% of every projectile's opaque pixels pink-family or white-hot, and a 2 px pink-white
edge or core line on every strip (jet, band, ring, strike, whip). The body material (<MATERIAL>) is the SECONDARY colour. Hostile shapes are
pointed, elongated or angular -- never a smooth round disc (atoms, the player's pickups, are round); a round thing (a pearl, a bubble) gets a
spiked or scalloped pink rim. Never red (hue 345-15), never a saturated <AVOID>. No baked glow halo (the game adds a keyline).
Everything points DOWN the screen unless the file says otherwise. Nothing within 4 px of a cell border (alpha <= 24).
Frame pairs a/b are two beats of a 8-12 fps loop: b differs from a by >= 3% of the opaque pixels (flicker, drip, spin, spray).
METHOD (mandatory): use IMAGE GENERATION for the painted content: generate several candidates, inspect them, pick the best, then
pixel-clean (author on a 64 px native cell, upscale x2 by nearest-neighbour ONLY, NO blur, limited palette PER MATERIAL RAMP -- rich
tonal ramps, not one global palette). Procedural-only results will be rejected.
SCALE CHECK: these draw at 20-50 px on a phone. Downscale each sprite to 40 px tall and make sure the pink cue and the silhouette still read
over a dark and a bright tile.
VERIFY programmatically (write verify.py and print its table): exact sizes, RGBA, cell non-empty (except the reserved cells), alpha border
margin, pink share per cell (hue 312-326 +-8 or near-white, saturated pixels only), pixels within 20 deg of 178/82/259/37, red-band pixels
(must be 0), frame a-vs-b difference, x2-nearest round trip. Fix until all pass.
Also write preview.png (a contact sheet on dark #0b0b1a at 2x, plus each sprite at 40 px over two real backdrop tiles) and, where it
animates, preview.gif. Finish with a short summary, the measured numbers, and your honest caveats (what is weakest).
```

## 3. Per-atlas prompts (the TASK part, after the preamble)

### P1. Shots atlas -- `<w>_attack_shots.png` (all four worlds)

```
TASK: <OUT>/<w>_attack_shots.png, exactly 1024x256 RGBA = 8 columns x 2 rows of 128x128 cells. The hostile projectile set for <WORLD>.
Row 0: bolt a,b | shard a,b | shell a,b | slag a,b.   Row 1: pool a,b | sigA a,b | sigB a,b | sigC a,b.
CELL CONTENTS (the world sheet in section 4 fills these in): <SHOTS_TABLE>
Rules: fast shapes (bolt, shell) are ~1:3 and point DOWN; fill >= 80% of the cell height, centred; blobs fill ~70%. The pool cells are wide,
soft-edged and animate by 2 beats (wobble). Each projectile has a clear pink-white CORE (the brightest, most saturated part) wrapped by the
<MATERIAL> body, and a 1 px dark ink outline; the a/b beats alternate the core's size/shape and the loose bits. Reserved cells stay fully transparent.
References (-i): <REFS>. The existing boss shot atlas shows the material language; match its level of detail, but these are smaller, bolder, simpler.
```

### P2. FX atlas -- `<w>_attack_fx.png` (all four worlds)

```
TASK: <OUT>/<w>_attack_fx.png, exactly 1024x256 RGBA, 8 x 2 cells of 128.
Row 0: impact 1-4 (a burst when a shot meets a rail or is shot down: grows to ~90% of the cell by frame 3, fades by 4) | special 1-4 (<SPECIAL>).
Row 1: trail 1-4 (four small particle sprites the game emits behind shots, content ~48 px, centred, <TRAIL>) | glyph a,b (a ground marker for "danger here":
a dotted pink-white ring 100 px across with <GLYPH_MOTIF> in its centre) | flash a,b (a muzzle flash ~70 px, pink-white core with <MATERIAL> spikes).
All pink-cored; impact frames 1-2 are almost white-pink, the material colour appears as the burst cools.
```

### P3. Strike atlas -- `<w>_attack_strike.png` (Frost, Ember, Tide)

```
TASK: <OUT>/<w>_attack_strike.png, exactly 768x512 RGBA.
Row 0 (y 0-383), six cells of 128 x 384: the COLUMN BODY, a vertical hazard filling the cell height (top to bottom), played once over 0.25 s at 24 fps:
frame 1 ignites, 2-4 full strength, 5-6 fading. <STRIKE_BODY>. The hit width is the middle 46 px; draw glow/spray out to 120 px wide; a pink-white core line down the
middle (>= 6 px wide) and a pink-white fringe. The ends fade inside the cell.
Row 1 (y 384-511), six cells of 128 x 128: glyph a,b (the lane marker that blinks during the 1 s tell: a pink-white dotted ring 100 px with the world's motif),
burst 1-3 (the ground impact), 1 reserved (transparent).
```

### P4. Jet atlas -- `<w>_attack_jet.png` (Ember flame cone, Tide water jet)

```
TASK: <OUT>/<w>_attack_jet.png, exactly the size in the world sheet's JET line, RGBA. Row 0: the JET BODY described in that JET line.
Row 1: nozzle 1-3 (a flare at the muzzle growing through the tell: small, medium, large; 128x128) and tip/splash 1-3 (the far-end effect: sparks dying / splash).
The loop is 6 frames at 12 fps, seamless (frame 6 flows into 1). Apex/top edge is the muzzle: the art is anchored there. Pink-white core line along the whole jet.
```

### P5. Wave atlas -- `tide_attack_wave.png` (Tide; Space is procedural)

```
TASK: <OUT>/tide_attack_wave.png, exactly 512x480 RGBA. Rows 0-3: four bodies of 512x96: a horizontal band of surf, SEAMLESS left-right (pixel columns 0 and 511 continue each
other), a foam crest on its lower edge, a dark teal-white wash above; 4-frame 12 fps loop; the pink-white crest line (3 px) is the keyline. Row 4 (96 px tall): capL, capR
(96x96 rounded ends of the band), gapMarker a,b (96x96: an arrow-and-ripple glyph shown at the gap's two edges during the tell).
```

### P6. Ring atlas -- `frost_attack_ring.png` (Frost cold blast)

```
TASK: <OUT>/frost_attack_ring.png, exactly 1024x128 RGBA = 8 cells of 128x128:
cells 0-3: bar 1-4 (one tangent arc bar of the cold-blast ring, lying HORIZONTAL, 112 x 36 px: pale frost crystal, a pink-white core line, jagged rime edge; the game places 18 of them
round a circle and omits those in the gap; 4 frames of 8 fps shimmer);
cells 4-5: gapMarker a,b (the two ends of the crack: a pair of jagged ice edges with a pink-white arrow pointing into the gap);
cells 6-7: glyph a,b (a snowflake ring 110 px across pulsing at the muzzle during the 0.9 s tell, pink-white).
```

### P7. Beam atlas -- `<w>_attack_beam.png` (Frost, Verdant, Ember, Tide): the mine's beam skin

```
TASK: <OUT>/<w>_attack_beam.png, exactly 1024x128 RGBA = 8 cells of 128. It plugs into the rail-mine laser (a beam across the lane from the mine's core):
c0 aim (a thin aim line, tileable along its length, content 12 px wide, dotted <BEAM_AIM>) | c1,c2 beam a,b (the beam, content ~64 px wide, vertically stretchable without visible
distortion: no features that must keep an aspect ratio; white-hot pink core ~16 px, <BEAM_BODY>) | c3,c4 flash a,b (the muzzle flash at the mine's core, ~70 px) |
c5,c6,c7 spark 1-3 (the impact on the rail, ~64 px, grows and fades).
Reference the existing rail-mine laser cells (-i the world's <Key>_shots.png cells 4-7 and Enemies/Mines/rail_mines_neon.png) for placement conventions. Lime/cyan/amber limits below.
```

### P8. Lash atlas -- `<w>_attack_lash.png` (Verdant vine, Tide tentacle) and P9. Log atlas -- `verdant_attack_log.png`

```
TASK (lash): <OUT>/<w>_attack_lash.png, exactly 1024x128 RGBA, 8 cells of 128: link a,b (one 128 px section of <LASH_BODY>, pointing DOWN, its top and bottom edges
join exactly so copies tile end to end along a curve) | tip a,b (<LASH_TIP>) | root a,b (<LASH_ROOT>) | dash a,b (a short dotted arc segment, pink-white, tileable: the preview).
```

```
TASK (log): <OUT>/verdant_attack_log.png, exactly 1024x256 RGBA = 4 x 2 cells of 256x128. A heavy TREE TRUNK lying horizontal (256 px = 1.1 u long, ~110 px thick) seen from above
rolling along: 8 frames, the bark texture scrolling 1/8 turn per frame (12 fps, seamless), dark wet bark, moss patches, snapped splinters at both ends, and a glowing PINK-white cut ring on each end
(the hurt cue, 20% of the opaque area). No face, no cartoon look.
```

## 4. World slot sheets (copy the block, fill nothing)

### Frost

```
<WORLD>=Frost  <w>=frost  <OUT>=./Pause/Assets/Art/Attacks/Frost~/
<MATERIAL>=pale rime ice, frozen crystal, sleet and cold mist
<BODY_RAMP>=white-lilac ice (#EAF2FF, #CFDDF2, #A9BFE0, #7E96C4: hue 205-225, saturation <= .35) with a 1 px ink outline #0B0B1A; steel-blue shadow #4A5F8A only on the underside
<PINK>=a hot pink-white core: #FFE0F8 centre, #FF8AE6 mid, #FF4FD8 fringe, lit from within a crystal
<AVOID>=cyan 178 (the shield atom: NO saturated cyan, the ice is white-lilac), violet 259, any red
<REFS>=Pause/Assets/Art/Resources/Bosses/Frost_shots.png, Resources/Enemies/frost_fighter_2.png, frost_fighter_4.png, frost_big.png, one Frost backdrop tile
SHOTS_TABLE=
 bolt = ICICLE SPEAR: a long slim pale crystal spear pointing down, pink-white core running its length, two tiny frost chips orbiting (b: chips swapped).
 shard = HAIL CHIP: a faceted white-lilac pellet with a pink core (b: rotated facet).
 shell = ICE BOULDER: lumpy packed-snow ball, rime spikes, a pink core glowing through cracks.
 slag = SLUSH BLOB: grey-white slush lump, drips, pink core.
 pool = FROST MIST: a low drifting cloud of ice dust, pink-white motes inside (2 beats).
 sigA = SHATTER CHIPS: three tiny crystal splinters flying apart, pink-lit (a,b = apart / further apart).
 sigB, sigC = reserved (transparent).
SPECIAL=crystal shatter: chips and a pale starburst, pink in the first frames, white-lilac in the last
TRAIL=cold mist puffs and tiny ice glints, white-lilac with a pink speck
GLYPH_MOTIF=a six-point snowflake
STRIKE_BODY=an ICICLE DROP: a long glassy icicle plunging down the column (tip down), mist streaming up behind it, a pink-white core line, a splash of crystal at the tip in frames 4-6
RING (P6) uses this sheet unchanged.
BEAM_AIM=frost-white dotted line with tiny crystals; BEAM_BODY=a cold ray: pale lilac-white body with crystal sparkles along its edges and a hot pink-white core (NO saturated cyan)
```

### Verdant

```
<WORLD>=Verdant  <w>=verdant  <OUT>=./Pause/Assets/Art/Attacks/Verdant~/
<MATERIAL>=wet bark, thorned vine, dark leaf, sap and spores of a jungle planet
<BODY_RAMP>=bark browns (#3A2A1A, #5A3F26, #7A5A36), deep leaf green (#1E3A24, #2F5A33, #4A7A44: value <= .55, hue 100-135), sap amber-brown #8A6A2A (sat <= .6) with ink outline #0B0B1A
<PINK>=thorn tips and seams lit hot pink-white: #FFE0F8 / #FF8AE6 / #FF4FD8
<AVOID>=repair-atom green 82 (NO bright lime: the leaf is DARK), amber 37 beyond sap, violet 259, any red
<REFS>=Pause/Assets/Art/Resources/Bosses/Verdant_shots.png, Resources/Enemies/verdant_fighter_4.png, verdant_big.png, verdant_alien.png, one Verdant tile
SHOTS_TABLE=
 bolt = THORN DART: a bark-brown spike pointing down, a pink-lit tip, a sap drop beside it (b: drop moves).
 shard = LEAF: a dark-green blade with a pink midrib and a curled edge (a: flat, b: seen edge-on).
 shell = LOG CHUNK: a short, thick bark trunk piece pointing down, a pink-lit cut end.
 slag = SAP GLOB: amber-brown sap blob with a pink glint, stringy drips.
 pool = SPORE CLOUD: soft clusters of pale spores round a pink-white core, 2 beats.
 sigA = CRESCENT LEAF-BLADE: a thin horizontal crescent blade 128 wide, dark green with a pink-white cutting edge.
 sigB = SEED POD: an oval husk with thorns and a glowing pink seam (a,b).   sigC = POD BURST: the husk split open, spores spilling.
SPECIAL=splinter burst and a spore puff (bark chips, pale spores, pink in frames 1-2)
TRAIL=leaf bits, spore dots, tiny sap drops
GLYPH_MOTIF=a thorn ring / a seed
BEAM_AIM=a dotted line of tiny thorns; BEAM_BODY=a THORNED VINE stretched along the beam: dark bark body, thorn tips glowing pink-white, a pink-white core line, NO lime glow
LASH_BODY=a thick thorny vine section, dark green-brown bark, thorns on both sides; LASH_TIP=a big hooked thorn with a hot pink-white tip (a,b: twitch); LASH_ROOT=a petal-base bud / a root clump (a,b: pulse)
```

### Ember

```
<WORLD>=Ember  <w>=ember  <OUT>=./Pause/Assets/Art/Attacks/Ember~/
<MATERIAL>=magma, slag, gas flame, ash and brass of a volcanic forge world
<BODY_RAMP>=flame yellow-white core (#FFF4D0), hot orange (hue 22-30, sat <= .8: #FF9A2E, #E0701C), deep ember (#8A3A1A), plum-black smoke (#2A1030), ink outline #0B0B1A
<PINK>=every flame edge fades hot pink-white -> magenta: #FFE0F8 / #FF8AE6 / #FF4FD8 (the pink reads as heat beyond orange)
<AVOID>=amber 37 (keep the orange at 22-30, never a yellow-amber body), pure red / crimson (hue 345-15) anywhere, cyan, green
<REFS>=Pause/Assets/Art/Resources/Bosses/Ember_shots.png, Resources/Enemies/ember_fighter_3.png, ember_big.png, ember_fighter_4.png, one Ember tile
SHOTS_TABLE=
 bolt = EMBER PELLET: a small bright spark with a short flickering fire tail pointing up, white-hot core, pink fringe.
 shard = CINDER SHARD: a hot black-plum splinter with a pink crack down its length (a,b: crack glow).
 shell = FIREBALL: a big orb with a white core, orange body, pink fringe and a long fire tail.
 slag = MAGMA BLOB: a molten lump, pink-fringed, with dripping strands.
 pool = LAVA POOL: a wide glowing puddle, pink-white hot centre, orange crust, 2 beats.
 sigA = SLAG SPLAT: a flat decal-like splash of molten slag (a,b). sigB, sigC = reserved.
SPECIAL=slag splat / spark burst: a flat molten splash, flung embers, pink-white then orange then dark
TRAIL=ember sparks and thin smoke wisps
GLYPH_MOTIF=a flame ring
JET (P4)=size 768x384. Row 0: six frames 128x256 = a FLAMETHROWER CONE, apex at the top centre (the nozzle), 1.8 u long (230 px), 0.76 u (97 px) wide at the far end, flickering torn edges, a white-pink core line, an orange body, a pink fringe, drifting sparks; seamless loop. Row 1: nozzle flare x3 (growing), spark-out tip x3.
STRIKE_BODY=an ERUPTION: a magma geyser column rising from the bottom of the cell, wide at the base (110 px), narrower at the top, white-hot centre, orange body, pink fringe, flying slag blobs
BEAM_AIM=a dotted orange-white line with heat dots; BEAM_BODY=a FIRE LASER: white-hot pink core ~16 px, orange-gold body, rippling heat edge, magma drips at the edges
```

### Tide

```
<WORLD>=Tide  <w>=tide  <OUT>=./Pause/Assets/Art/Attacks/Tide~/
<MATERIAL>=pressurised sea water, foam, brine, barnacled brass, pearl, lightning
<BODY_RAMP>=sea-foam white (#F0FFFA), pale teal (hue 165-190, sat <= .4: #BFEFE4, #8FD0C4), deep teal-black (#0E2A30), brass #8A6A2E, ink outline #0B0B1A (final palette follows the user's Tide decision: confirm before launching)
<PINK>=pink-white pressure cores, pink foam crests, pearl nacre: #FFE0F8 / #FF8AE6 / #FF4FD8
<AVOID>=shield cyan 178 (water is WHITE-teal, never a saturated cyan), repair green 82, violet 259, amber 37, any red
<REFS>=Pause/Assets/Art/Resources/Bosses/Frost_shots.png (the closest water-like crystal reference), Tide backdrop tiles, the Tide roster strips (when drawn)
SHOTS_TABLE=
 bolt = SPOUT DROP: a teardrop of white water pointing down, a pink-white core, three spray dots.
 shard = PEARL: a scalloped clam cup holding a pink pearl, two bubbles (NOT a smooth disc).
 shell = DEPTH CHARGE: a horned sea mine inside a pink-rimmed spiky bubble.
 slag = INK BLOB: black-teal ink with a pink rim.
 pool = NET: a knotted mesh pool, pink knots, 2 beats.
 sigA = INK POOL: a flat spreading ink stain, pink rim. sigB = reserved. sigC = BUBBLE RING: a ring of spiked pink-rimmed bubbles.
SPECIAL=splash crown: a ring of water droplets and spray, pink-white in frames 1-2
TRAIL=bubbles and foam flecks
GLYPH_MOTIF=a ripple / wave ring
JET (P4)=size 768x448. Row 0: six frames 128x320 = a PRESSURE JET column .5 u wide, 2.5 u (320 px) long, white-water body with a pink-white core line, spray beads and a foam end; seamless loop. Row 1: nozzle x3, splash x3.
STRIKE_BODY=THUNDER: a vertical jagged lightning bolt, blue-white glow with a pink-white core line, branching twigs
WAVE (P5), LASH: LASH_BODY=a barnacled brass-and-steel TENTACLE section; LASH_TIP=a pink-cored sucker pad; LASH_ROOT=a hatch collar
BEAM_AIM=a dotted white-water line; BEAM_BODY=a WHITE-WATER JET: spray beads, pink-white core line, foam edges
```

## 5. Per-job verification list (paste at the end of each prompt)

```
verify.py must print, per file: size OK / RGBA / reserved cells empty / border alpha OK / pink share per cell (>= 30% for projectiles) / hue audit table (pixels within 20 deg of 178,82,259,37; red band = 0) / a-b difference >= 3% / nearest x2 round trip OK / seamless-edge check (wave, jet loop, log).
```

Local re-check: `scripts/verify_attack_art.py <dir> <World>` (written in plan phase 0b; mirrors these checks).

## 6. What to look at when you critique (copy into the reply to the user)

* Does every projectile read **pink first, material second** at 40 px over a bright and a dark tile?
* Do the five shapes of one world differ at a glance (spear vs chip vs boulder vs blob vs pool)? Does any look like an atom (round, smooth, calm)?
* Frames a/b: do they flicker (hostile) rather than breathe (friendly)?
* Strips: seamless? Is the core line continuous? Does the cone/band/column end cleanly inside its cell?
* Material truth: does Frost look like ice (not glass beads), Ember like fire (not orange paint), Verdant like bark and thorn (not lime), Tide like pressurised water (not glass)?
