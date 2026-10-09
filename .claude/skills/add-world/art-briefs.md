# Codex prompt templates for a new world

Reusable, parametrised versions of the prompts that produced Frost, Verdant and
Ember (the originals are in the coordinator's scratchpad; the structure and numbers
below are copied from the ones that worked). Fill the `<...>` slots, keep the numbers.

Launch with `scripts/run_codex.sh <job> <prompt.md> [ref-image ...]` (the refs become `-i` flags; one worktree
`codex-<job>`, branch `art/<job>`, `codex exec -C <wt> -s workspace-write -c
model_reasoning_effort='"high"'`). Max 2 jobs at once; Codex cannot be messaged
mid-run. After each job: run the verifier named in the template, open the images,
write the critique, commit on the job's branch.

## Slots (define once per world, paste into every prompt)

| Slot | Meaning | Example (Ember) |
| --- | --- | --- |
| `<WORLD>` / `<w>` / `<N>` | display name / lower-case key / index | Ember / ember / 3 |
| `<LOOK>` | 2-4 sentences of what the world is and looks like from above | molten volcanic forge world: black basalt, glowing lava rivers, brass refineries... |
| `<PALETTE>` | the dark base + electric hue + accents, in words *and* hexes | basalt/brass/amber: black basalt, amber-gold lava, white-gold furnace light, a few magenta heat lamps |
| `<NO>` | what this world must NOT contain | no green, no ice, no cyan; never the player's red |
| `<HUE_RULE>` | hue constraints | every hue at least 22 degrees from the player's red #FF3E4E; plus the pickup hues (cyan 178, green 82, violet 259, amber 37) and hostile pink (312-326) kept clear of large areas |
| `<GROUND>` | what the ship flies over | lava rivers, forge city plate maze, ash ridges, obsidian night volcano |
| `<TIME_SETS>` | the 4 variants, with the night one declared | v1 caldera fields, v2 forge city, v3 ash plains and ember rivers, v4 obsidian NIGHT volcano |
| `<ENEMY_THEME>` | the cast's materials | scorched iron, obsidian, molten orange, brass |
| `<LIGHT>` | the signature enemy light | sodium orange |

## 0. Common preamble (start EVERY prompt with this)

```
You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait). Work ONLY inside this git
worktree; do not edit any .cs / .meta / existing file; do not delete anything; do not commit.
New files go ONLY to <OUTPUT FOLDER> (generators, prompts, candidates in its src~/ subfolder; the trailing ~
matters: Unity ignores it).
Read ./docs/art-style.md (neon pixel art, rugged cyberpunk/steampunk industrial, 1 px dark outline on solid forms,
4-6 tone ramps per material, rim light, glowing neon cores; one electric hue per world on a dark base; NO red
anywhere: red means friendly) and ./docs/user-art-and-world-brief.md.
WORLD: <WORLD>, world <N> in the loop Space -> Frost -> Verdant -> Ember -> ... : <LOOK>
PALETTE: <PALETTE>. <NO>. <HUE_RULE>.
The level is flown at ATMOSPHERE LEVEL, looking straight down from just under the cloud ceiling. NOT SPACE: no
stars/planets/stations/comets/asteroids anywhere in <WORLD>'s backdrop art.
METHOD (mandatory): use IMAGE GENERATION for the painted content: generate several candidates, inspect them,
pick the best, then pixel-clean (nearest-neighbour only, NO blur, limited palette PER MATERIAL RAMP -- but rich
tonal ramps, not a global small palette). Procedural-only results will be rejected.
VERIFY programmatically (write verify.py and print its table): <list the checks for this job>. Fix until all pass.
Also write preview.png (a contact sheet on dark #0b0b1a at 2x) and, where it animates, preview.gif.
Finish with a short summary, the measured numbers, and your honest caveats (what is weakest).
```

**Pre-launch lint** (do it every time): search the finished prompt for the other worlds' nouns
(`ice`, `frost`, `forest`, `lava`, `ember`, `icebreaker`...). `prompt_ember_death.md` shipped with
"Ember = icebreaker steel + glacier ice + cyan" copied from the Frost prompt.

## 1. Planet + planetfall + lift-off (J1) -- `Art/Worlds/<World>/descent~/`

References (`-i`): `Backgrounds/Resources/Worlds/{Frost,Verdant,Ember}/Planetfall/*_planet.png`,
`*_planet_limb.png`, `*_entry_fx.png`, and each world's `descent*/manifest.json` (read-only).

```
<preamble, OUTPUT FOLDER = ./Pause/Assets/Art/Worlds/<World>/descent~/>
CONTEXT: worlds are visited in order Space -> Frost -> Verdant -> Ember -> <WORLD>. Between planets the ship LIFTS OFF
through the clouds, flies a calm stretch of space, and then a new planet appears and the ship dives through its
atmosphere (planetfall). The SAME art plays the dive onto the planet and, later, the lift-off from it backwards.
Approved sets exist for Frost, Verdant and Ember (references): the user loved them. Make the matching <WORLD> set:
seven files with the SAME roles, sizes, cell grids and registration rules, named <w>_planet.png, <w>_planet_limb.png,
<w>_cloud_deck.png, <w>_cloud_deck_dark.png, <w>_entry_fx.png, <w>_breakthrough.png, <w>_entry_streaks.png,
+ manifest.json with the same pivot data as theirs (planet centre/radius MEASURED on the actual pixels, limb apex and
edge heights, entry-fx opening centre/size per frame, frame counts) + preview.png/preview.gif.
LOOK: <LOOK>. Planet: spherical shading, soft pixel-dithered terminator, a distinctive SILHOUETTE different from
Frost's ring city, Verdant's vine ring and Ember's brass radiator ring (<RING_OR_FEATURE>), readable at small size.
Files:
1) <w>_planet.png 1024x1024 RGBA: the planet from orbit on transparent; disc fully inside with >= 24 px margin;
   thin luminous atmosphere rim in <RIM_COLOUR>; day/night terminator; city/industry lights on the night side.
   (Also deliver a 4096 master in descent~/masters/ -- not shipped.)
2) <w>_planet_limb.png 2048x1024: the close-approach curved horizon filling the lower ~65%; layered atmosphere band
   along the curve; detail below (<GROUND>); transparent above; horizon y = 355 at the centre column, ~720 at the side edges.
3) <w>_cloud_deck.png + <w>_cloud_deck_dark.png 2048x1024: high cloud tops seen from above (<CLOUD_LOOK>);
   SEAMLESSLY tileable on both axes (wrap edges identical); the dark variant is the deeper layer.
4) <w>_entry_fx.png 3072x1024 = 6 loop frames of 512x1024: an atmospheric-entry plasma SHROUD trailing below the ship
   position, which is the cell's (256, ~338): leave a clean round opening about 146 px wide x 165 tall there (flood-fill
   it per frame; its centre x may wander a few px). Fierce and WIDE like Frost's (white-hot core, <HEAT_COLOURS> edge
   flames, shed sparks), flickering strongly -- NOT a thin ribbon. No plasma clipped at the cell sides.
5) <w>_breakthrough.png 5120x1024 = 5 frames of 1024x1024: the burst as the ship breaks out of the clouds
   (<BURST_LOOK>), frame 0 small and sharp, frame 4 nearly full-cell and thinning.
6) <w>_entry_streaks.png 1024x2048, tileable vertically: dense layered speed lines / ribbons (<STREAK_COLOURS>).
The cloud colour and brightness must CONTINUE into the world's backdrop ceiling (cloud banks painted later): say in the
manifest what the ceiling should match.
VERIFY: sizes, RGBA, tile seams (both axes for decks, vertical for streaks), frame borders clear, opening geometry per
entry frame, planet disc fitted by 360 rays (record centre/radius), margins >= 18 px, hue rules.
```

Local check: `scripts/verify_planetfall_art.py <dir> <w>` (prints the `PlanetfallDef` numbers).

## 2a. Backdrop run A -- the 4 ground sets (J2) -- `Art/Worlds/<World>/backdrop_v3~/`

Refs: `Backgrounds/Resources/Worlds/Ember/Backdrop3/v1..v4/*.png` (the look to match: **mid layers**),
`.../Verdant/Backdrop3/v1/*.png`, `<w>` planetfall `limb`/`cloud_deck`.

```
<preamble>
DELIVERABLES: FOUR full ground sets, each four seamless 512x1024 RGBA PNGs (seamless on BOTH axes: verify by wrapping),
named exactly v1/{sky,far,mid,flow}.png ... v4/... The game picks one set at random per landing. They must read as
clearly DIFFERENT PLACES at a glance (different dominant shapes), not recolours:
 <TIME_SETS with one line each: the dominant shapes, what 'flow' shows, and which set is the NIGHT side>
Roles: sky = the deepest hazy ground plane (lowest contrast, NOT a night sky), far = distant ground, mid = nearer and
most detailed (still low enough contrast that bullets and enemies read on top), flow = a scrolling strip layer with
transparency (0.5%-25% opaque, never a fully opaque texel) so the layers beneath show through.
BRIGHTNESS: p90 of HSV value of opaque pixels between 0.40 and 0.50 for the day sets, between 0.32 and 0.42 for the
night set; far ~0.05 below mid, sky ~0.05 below far (depth); only SMALL very bright accents; moderate contrast between
neighbouring features; no big white areas. THE TARGET LOOK IS THE MID LAYER OF THE EMBER REFERENCE: dark base, hot
glowing accents, dense detail, high contrast. Sky and far must be built from the SAME material language as mid
(not flat haze: Ember's first sky/far were flat tan and had to be redone), just progressively hazier.
Do not bake smoke, steam or animated-looking effects into tiles (separate layers); static lights are fine.
Also write manifest.json (per file: size, role, variant, notes, measured p90, wrap-edge difference) and
preview_variants.png: the four variants as columns (sky/far/mid/flow at 1x) plus a phone-portrait 1080x2400 composite per
variant (sky+far+mid(+flow) stacked the way the game draws them).
VERIFY: sizes, RGBA, wrap seams (edge columns/rows identical), p90 ranges, opacity (flow fraction), hue rules.
```

Local check: `scripts/verify_backdrop_tiles.py <backdrop_v3~> [--forbid-hue a b ...]`.

### 2a-redo -- contrast redo of weak layers (use when sky/far are flat or one set is dull)

```
<preamble, with: "The ground sets live (staged, Unity-ignored) in ./Pause/Assets/Art/Worlds/<World>/backdrop_v3~/ ...">
USER FEEDBACK (verbatim): "<quote>". REDO exactly these files so they match the mid layers' contrast and drama:
<list>. Save the current versions first to src~/originals_v1/<variant>_<layer>.png. Do NOT touch <files that were good>.
Requirements: sky/far still hazier/lower-contrast than mid (depth) but built from the SAME dark-base + hot-accent language,
not tan haze; <the dull set> re-imagined as <richer description>, still clearly different from the others; seamless both
axes; p90 bands as above; untouched files byte-identical. Update manifest.json with a 'redo' section and write
preview_variants_v2.png with before/after for the changed layers.
```

## 2b. Backdrop run B -- pieces (J3) -- same folder

```
<preamble>
Keep the world, palette, viewpoint and pixel style of the ground sets already in ./Pause/Assets/Art/Worlds/<World>/backdrop_v3~/
{v1..v4}/*.png. Reference for the atlas conventions: Ember's / Verdant's landmarks, pipes, fires, weather, sites atlases.
USER FEEDBACK to honour: LOTS of pipes, smoke and fire/life (<WORLD-appropriate equivalents>); bring contrast and drama.
Atlas conventions EXACTLY: 1024x1024 RGBA PNG atlases of 256x256 cells, JSON {"sprites":[{"n":name,"x","y","w","h"}]}
with y measured FROM THE BOTTOM (Unity rects); each piece centred with >= 14 px clear margin; transparent; crisp
neon pixel art; nearest-neighbour; NO blur. Each ground-standing piece carries only a SMALL feathered base (not a big
terrain plate: they must not read as stickers on the tile).
DELIVER FIVE atlases + JSONs:
1) landmarks (p90 value <= 0.55), 16 pieces: <list: 2 tall structures, 3 compounds, 2 towers, workshop, dome cluster,
   2 rigs/derricks, 2 vessels, 1 pump/fall station, 1 viaduct/bridge> -- smoke is animated separately.
2) pipes (p90 <= 0.55), 16: straight x2, bend x2, tee, cross, manifold x2, pipe bridge x2 (on trestles), pump house,
   leak x2 (burst seam with a visible emitter base), <world-fluid> pipe, tangle x2. Pipe-end points listed per piece.
3) <fires/feature grounds> 16: <static features: vents, scars, beds, pools, flare stacks, wreckage, a crew crawler>;
   emitter points marked in the manifest.
4) weather 16 (lighter, semi-transparent, soft pixel-clustered edges, low alpha; never hide enemies/bullets):
   cloud_bank_00..03 (big masses for the CEILING at level start: continue the planetfall deck -- same colour/brightness),
   cloud_wisp_00..03, mist_00..02, <gust>_00..02, <pall>_00..01, <particles>_00.
5) sites 12 (ground launch sites for elite ships, closed/open pairs, dark with strong shape and small bright lights,
   emergence point at the cell centre): <5 site kinds as _closed/_open pairs> + lights_off, lights_on.
Append a "run_b" section to manifest.json: per piece the atlas, name, role (Landmark/Pipe/Feature/Cloud/Atmosphere/Site),
nominal emitter/pipe-end points, size hint, notes. Write preview_b.png (contact sheet + a phone mock composing pieces on
v1/mid and v2/mid).
VERIFY: dimensions/RGBA, every rect inside the image and non-overlapping, margins >= 14, p90 rules, no space imagery,
no hue violations.
```

Local check: `verify_backdrop_tiles.py <dir> --atlas-margin 14` (atlas part).

## 2c. Backdrop run C -- animated loops (J5) -- same folder

```
<preamble>
Study runs A and B in this folder. USER FEEDBACK: more pipes, smoke and fire; keep the high-contrast dark look.
Atlas conventions as run B (1024x1024, 256 cells, JSON y from the bottom), names <loop>_00.._NN. EVERY loop SEAMLESS
(last frame flows into first), frames genuinely DIFFERENT, pieces centred with >= 6 px margin, GROUND-ATTACHED loops
anchored at the BOTTOM-CENTRE of the cell (x=128, y~236 from the top; the plume/flame base of EVERY frame on that point,
no sideways offset, no base wobble -- state it in the manifest). Image generation for the painted key frames, art-directed
in-betweens.
READABILITY: smoke/steam <= ~70% alpha at densest, never hiding enemies/bullets; flames small-to-medium, bright, dark
outlines; lights small and crisp. Embers/spores/rain particles CLEARLY visible: 2-4 px bright specks with trails.
LESSON (must apply): blinking lights need a MEASURED brightest/darkest frame brightness ratio >= 4.0 (lamp OFF, igniting,
FULL ON with a large pixel-stepped bloom, fading), window flicker >= 2.2, strobe >= 4; verify with alpha-weighted brightness
sums per frame and print them.
DELIVER (8-frame loops unless said): 1) smoke: smoke_a (thick dark stack smoke with underlight), smoke_b (lighter steam-smoke
column) 2) <fire-loops>: flare_00..07, fountain/vent_00..07 3) eruption/column: erupt_a (HUGE billowing column), erupt_b
(thin ribbon leaning with the wind) 4) leaks (4-frame loops): steam_vent, pipe_drip, <particles>_rain, <surface>_bubble
5) lights (4-frame loops): beacon_<colourA>, beacon_<colourB>, window_lights, strobe_white.
Append "run_c" to manifest.json: per loop frames, recommended fps, anchor, intended use (which pieces), draw alpha.
Write preview_c.png (every loop's frames, with per-frame brightness sums for the blinkers) and preview_c.gif (600x1300
mock with everything playing in place).
VERIFY: dimensions, RGBA, rects, margins, seamlessness (frame N->0 difference comparable to adjacent), frames differ, blink
ratios, plume-base offset from the anchor per frame (<= 0.5 px target, report the max), hue rules.
```

## 3. Enemy idle strips (J4) -- `Pause/Assets/Art/Resources/Enemies/` (live) or staged `Art/Enemies/src~/<w>_enemies/`

Refs: `Resources/Enemies/{ember,frost,space}_*.png`, the finished backdrop mid tiles (`v1..v4/mid.png`), the boss atlas.

```
<preamble, OUTPUT = ./Pause/Assets/Art/Enemies/src~/<w>_enemies/ (sources) and the 11 strips beside them; do NOT
write into Resources/ until I say>
Draw the <WORLD> cast: 11 horizontal strips, 7 square cells each, cell side = strip height (192 px; the big 256): 1344x192
(1792x256 for <w>_big). Cells 0-3 IDLE: ONE fixed hull and anchor -- the body centroid within 3 px of cell 0 and its area
within +-4% in all four idle cells; only a small detail moves (core pulse, wing, antenna, drip, glint) so the loop breathes.
Cell 4 and 5 = the TELL (anticipation, then the discharge), cell 6 = HIT: the same hull in a white-hot flash with a cracked
part and a few chips -- NEVER a blown-out whole-body white or an off-palette recolour. Same scale and anchor in all 7
cells. >= 6 px clear margin to every cell edge in EVERY cell. 1 px dark outline plus a LIGHT RIM (upper-left, 2 px) and a
few bright accent pixels so the silhouette pops off the dark backdrop (target: >= 10% of the body's pixels 3:1 clear of the
v1..v4 mid tiles' mean; aliens >= 15%).
DETAIL FLOOR (Unity tests): the key pose (cell 0) has >= 300 distinct colours (aim 1500+) and >= 3000 colour boundaries
(the big >= 6000); NO global small palette, NO posterising to ~20 colours (a previous pass did and was rejected); smooth
4-6 tone ramps per material plus 1-2 px detail (rivets, scratches, rust, glints). Nothing on a cell's outer columns; no
ruler-straight vertical edge >= 29 px (cells must hold ONE pose each, composed on the grid, not sliced from a sheet).
Palette: <ENEMY_THEME>, signature light <LIGHT>; shots are not drawn here. NEVER the player's reds (#D8232C #86121F #FF5B45
within L1 24 on more than 2% of texels).
THE CAST (design each from its role AND its counter, one line each):
 <w>_alien "<Name>": <design>      <w>_chaser "<Name>": <design>      <w>_big "<Name>": <design> (reads properly BIG)
 <w>_fighter_1 "<Name>" (tier 1) ... <w>_fighter_4 "<Name>" (tier 4, heaviest): <designs; four DISTINCT silhouettes>
 <w>_rock_<a> .. <d>: four rocks; <d> is the FLOATING one (a chunk of the world's ground, upright, sways): <designs>
 Rocks idle must still MOVE visibly (a "rock" with four identical idle cells was rejected).
The mine is NOT a strip: it is drawn separately as new rows of the neon mines atlas (template MINES below) -- skip it here.
Write preview.png (each strip at 2x on #0b0b1a, and each key pose over the four v*/mid.png tiles) and audit.py that prints,
per strip: tones, boundaries, outline columns, longest straight cut, idle centroid drift and area ratio, margins per cell,
red share, stand-out share on the tiles.
```

Local check: `scripts/verify_enemy_strip.py <strips>` + `Pause/Assets/Art/Enemies/src~/audit_cells.py`.

### 3-fix -- fix pass on named strips

```
<preamble>. You MAY overwrite exactly these live strips: <list>. Save originals first in src~/<w>_fixes/original/.
Study the good strips (<list>) as the quality tier. Measured defects: <per strip: cell, number, the rule>. Every cell NOT
listed must stay byte-identical. Dimensions unchanged. Verify and print before/after numbers (area ratio, centroid offsets,
margins, tones/boundaries) and write preview.png with before/after rows at 2x.
```

### MINES -- the rail-mine rows (the user-approved neon atlas style)

```
<preamble, OUTPUT = ./Pause/Assets/Art/Enemies/src~/<w>_mines/rail_mines_neon_<N>.png>
Extend the rail-mine atlas for <WORLD> (and <WORLD2> if drawn together): the original atlas
./Pause/Assets/Art/Resources/Enemies/Mines/rail_mines_neon.png (1254x1254: rows = worlds, columns = Dormant, Waking,
Charging, Burst) is the style reference and MUST NOT be altered. New file: same 4 columns, one row per new world, cells in
the same proportions and the same neon-pixel language (clamp on the rail side, body, glowing core, burst in the world's
colours; clamp reach ~0.33 u so the rail-grip maths holds), each frame's dormant body centred on the same pivot as the
original rows. Core glow: the brightest pixels in Waking/Charging mark where the laser leaves.
```

## 4. Enemy death strips (J7) -- `Pause/Assets/Art/Resources/Enemies/Death/<key>.png`, scripts in `Art/Enemies/src~/<w>_death/`

Refs: `Resources/Enemies/Death/{space,frost,ember}_*.png` (density, timing feel, flash/rupture/smoke language).

```
<preamble, with "Write the new PNGs to ./Pause/Assets/Art/Resources/Enemies/Death/<key>.png (that folder holds the others:
look at them); scripts/candidates/prompts to ./Pause/Assets/Art/Enemies/src~/<w>_death/">
TASK: <WORLD> enemy DEATH ANIMATION strips (3-frame death flipbooks like space_*.png). SPEC: for each key make <key>.png,
transparent RGBA, width = 3 x height, three square cells; cell side = the enemy's own cell side (576x192; the big 768x256) so
each death cell is drawn at EXACTLY the same scale and anchor as that enemy's idle cell 3 (the body must not jump when
the death starts). Keys (12): <w>_alien, _chaser, _fighter_1..4, the four rocks, _mine (design from the neon mine atlas's
Dormant cell), 768x256 for <w>_big. Cell 0 (shown 0.08 s) = the intact enemy (idle cell 3) with a white-hot <flash colour>
starburst at the core; cell 1 (0.11 s) = RUPTURE: body cracked and half dissolved, dark and desaturated, radial discharge
cracks, chips flying outward; cell 2 (0.20 s) = NO hull, only drifting <debris/vapour/sparks>, a few chips. Bright parts >= 6 px
from the cell edges (sparks may fill 80-90% of the cell in cell 2). Palette: <ENEMY_THEME>; keep the flash and fragments
inside the same ramps as that enemy's idle strip so nothing looks pasted from another world.
PER-ENEMY DESIGN: first LOOK at each <w>_* idle strip, then design each death from that enemy's own anatomy (core, wings, pipes,
jaw). <one line per enemy>. Rocks just SHATTER into <material> chunks, then dust. No <forbidden element>.
Write preview.png (per key: idle cell 3 then the 3 death cells at 2x on dark ground) + preview.gif cycling cells 0,1,2 for 4
representative enemies at game timing (0.08/0.11/0.20 s).
VERIFY: sizes (width = 3 x height), RGBA, bright parts >= 6 px from edges, cell 0 anchored to the idle cell (centroid within 3 px),
no off-palette hue dominating.
```

Local check: `verify_enemy_strip.py --death <strip> --idle-png <idle strip>`.

## 5. Rails -- built-in image edit, `Pause/Assets/Art/Resources/Worlds/<World>/rail_<name>_wide_v1.png`

Use Codex's built-in image-edit tool with the closest existing rail (`Resources/Worlds/{Space,Frost,Verdant,Ember}/rail_*`)
as the **edit target**:

```
Use case: style-transfer. Asset type: production scrolling side rail texture for a pixel-art sci-fi game. Edit target: supplied
<CLOSEST> rail. Make a <WORLD> world variant of this exact rail. Preserve the exact image dimensions (725 x 2170), vertical
full-height framing, rail thickness, silhouette and position, black/transparent exterior margins, straight front orthographic
view, densely detailed rugged pixel art, riveted metal armour plates, copper brackets, ribbed hoses, vents and structural rhythm.
Weathering: <world-specific: salt-encrusted barnacles and kelp stains / scorched scale and ice-pitting / ...>. Replace the lamps
and coils with <world lamp colour> (NOT red, NOT hostile pink); sparse bright lamp cores. Keep dark heavy structure, crisp
stepped pixel shading, no painterly smoothing, no large effects. Continuous vertical rail cut flush at top and bottom, matching
cross sections suitable for vertical repetition, no end caps. Genuine transparent background outside the rail, no text, no scene.
```
then a **seam refinement** edit ("fix ONLY the vertical tiling seam; modify narrow strips near top and bottom so the last row
joins the first row; leave the middle 90% unchanged"). Verify: 725 wide, `a[0] == a[-1]`, transparent margins, visibility vs Space.

## 6a. Boss base set (J6) -- `Art/Bosses/<Key>~/` (staged), final in `Art/Resources/Bosses/`

Refs: `Resources/Bosses/{Space,Frost,Verdant,Ember}.png` + `_shots.png` + `_card.png`.

```
<preamble>
TASK: the <WORLD> end-of-level boss "<NAME>" (<TITLE>): <concept: silhouette, materials, three firing parts -- name them:
e.g. Jaw, Furnace, Brow>. It is one 384x384 cell on screen at 3.3 world units; hit box is shared; keep the bulk inside the cell
with room for the aura.
1) <Key>.png 1920x1536 = 5 columns x 4 rows of 384x384 cells, ALL 20 non-empty, identical scale and registration in every
   cell: row 0 idle 0-3 (a 4-drawing loop: subtle) + hit; row 1 tell0 a,b / tell1 a,b / fire; row 2 tell2 a,b / death 0-2;
   row 3 death 3-4 / retreat 0-1 / portrait. Each TELL lights the part that is about to fire (tell0 = part 1, tell1 = part 2,
   tell2 = part 3); the FIRE cell shows the muzzle burst of attack 0. NO pixel with alpha > 24 within 6 px of a cell border: glows
   and smoke must taper off naturally inside the cell (a hard rectangular cut was a user complaint). The death cells may use
   up to ~90% of the cell only at the fireball peak.
2) <Key>_shots.png 1024x128 = 8 cells of 128: bolt 0-1, shard 2-3, lane telegraph 4, beam 5-6, muzzle charge 7 -- in <flash colour
   family>, NEVER red; the game draws hostile shots in hostile pink with a rim, so keep them bright and chunky.
3) <Key>_card.png 1024x288: the intro name card, "<NAME>" and "<TITLE>" in the world's neon, dark panel, readable.
Style: neon pixel art, rugged industrial, 1 px outline, rim light; <materials>; NOT cartoonish.
VERIFY: sizes, RGBA, 20 non-empty cells, per-cell alpha-24 border margin table, registration (centroid per idle cell).
```

Local check: `scripts/verify_boss_art.py <Bosses dir> <Key>`.

## 6b. Boss damage + fx + death (J9) -- `Art/Bosses/<Key>Damage~/`

Refs: `Resources/Bosses/{Space,Frost,Ember}_damage*.png`, `Ember_death.png`, the generator `Art/Bosses/SpaceDamage~/make_damage.py`.

```
<preamble>
TASK: battle-damage art for the World <N> (<WORLD>) boss. Bosses have 5 hearts; as it loses hearts it must look progressively
wrecked, smoking and shorting out. Done before for Space/Frost/Ember; the user's one correction: "make the last 2 frames way more
damaged" -- so make the late stages DRAMATICALLY wrecked from the start.
REFERENCES: ./Pause/Assets/Art/Resources/Bosses/<Key>.png (pristine; keep its design, silhouette, registration), the Space/Frost/
Ember _damage/_damage_fx for FORMAT and quality. Do NOT redesign the boss. Every damage cell must register EXACTLY with the
pristine idle cell: the alpha bbox of each damage cell lies within the pristine bbox grown by at most 6 px.
1) <Key>_damage.png 768x1536: 384 cells, 2 cols x 4 rows. Row r = damage stage r+1, CUMULATIVE; columns = 2-frame idle loop A,B
   (B differs slightly: glow flicker, a loose part shifting, a drip). stage 1 (4 hearts left): visible damage right away, NOT subtle
   (<concrete>); stage 2: clearly worse (<concrete>); stage 3 (2 left): DRAMATICALLY wrecked: a large hull breach showing the
   glowing core/machinery, a whole side cluster torn away or hanging, plates missing so the skeleton shows, one eye/lamp
   shattered; stage 4 (1 left): near-destroyed but still the recognisable boss inside the pristine footprint (never bigger):
   deep breaches front and back, mangled clusters, the core guttering and unstable. Each stage unmistakably different at game
   scale (the boss is ~190 px on screen). Proper painted pixel art (reshape armour edges, cut real holes) -- NOT decals. Do NOT
   bake smoke or electricity into these cells.
2) <Key>_damage_fx.png 2304x768: 384 cells, 6 x 2, transparent, drawn over ANY body frame at the same registration: row 0 SMOKE/
   STEAM loop (6 seamless frames; bold, clearly visible, heavy and opaque -- Space's was too faint -- jetting from breaches
   without covering the eyes/centre), row 1 ELECTRICAL loop (6 seamless frames; big crackling arcs, 2-3 px bright lines, some
   frames with a flash, erratic).
3) <Key>_death.png 2304x384: 6 cells at ~0.12 s each, registered to the pristine cell: 0 stage-4 wreck with a white-hot flash
   bursting through every breach; 1 hull splitting open along the breaches, core exposed, big plates blown out; 2 core overload:
   a huge fireball swallows the centre (parts may reach 90% of the cell); 3 fireball peak, a few dark chunks silhouetted; 4
   expanding shockwave ring and smoke, glowing chunks drifting; 5 fading smoke, drifting embers, a few settled hot chunks.
4) preview.png (pristine then the 4 stages with smoke + arcs composited at full strength, 2x on #0b0b1a) + preview.gif of
   stage 4 with overlays; <Key>_death_preview.png.
VERIFY: dimensions, RGBA, registration (bbox rule), stage-to-stage differences, no stray pixels across cell borders.
```

## 7. Elites (J8) -- `Art/Enemies/Elite/<World>/` (+ `src~/`)

Refs: `Resources/Elites/{Frost,Ember,Space}/*.png`, their `manifest.md` files, the world's enemy strips and boss.

```
<preamble, OUTPUT = ./Pause/Assets/Art/Enemies/Elite/<World>/>
TASK: draw <5-6> ELITE enemy ships for <WORLD>. They launch out of the ground sites of the world's backdrop (<site kinds>) and fly
top-down, nose toward the top, thrusters at the bottom. Compact: hull about 100-120 px wide in a 192 cell, >= 16 px clear margin.
Distinct silhouettes, personalities and attacks; they must read as different at ~1 world unit on screen:
 1. <w>_elite_<name> "<Display>": <silhouette>. (Attack idea: <...>.)   2. ...   (one block each; roles: herder, kiter, tender,
 tank/shield, interceptor, sniper -- mix them)
DELIVERABLES for EACH: <w>_elite_<name>.png = a 1344x192 RGBA flight strip of SEVEN 192x192 cells: 0 landed / engines off,
1 grounded idle (restrained glow), 2 lift-off ignition, 3 airborne straight hover, 4 bank left, 5 bank right, 6 second-heart damaged
(cracked, sparking). Same hull, same scale and anchor in every cell (hover/bank centroids within ~4 px), transparent, true alpha.
Also <name>_concept.png (one hero painting), a combined manifest_new_elites.md (cell table; approximate pixel positions at the hover
cell of engine nozzles and weapon muzzles, x from the left, y from the top; extra cells), contact_sheet.png (all strips stacked at 2x,
plus one existing elite for comparison) and preview.gif cycling cells 3,4,3,5.
Shot/heart colours are NOT drawn in the hull: hearts and shots will be wired in code (hostile pink); do not put the player's red anywhere.
Optional extra strips <key>_parked.png / _liftoff.png / _death.png (any number of square cells) -- ask first.
VERIFY: strip size, RGBA, margins >= 16, hover/bank scale and centroid numbers, distinct silhouettes (report bbox sizes).
```

## 8. Death sounds + screams (J10) -- `Pause/Assets/Audio/Resources/Audio/EnemyDeath/`, scripts in `Pause/Assets/Audio/EnemyDeathSrc~/`

```
You are a game SOUND DESIGNER building audio for "Pause" (Unity, vertical 2D shooter, phone). Work ONLY inside this git worktree;
do not edit any .cs/existing .meta file; do not commit. READ FIRST (the approved sounds and the exact toolkit/rules):
./Pause/Assets/Audio/EnemyDeathSrc~/build_space_death_sounds.py, build_space_screams.py, build_verdant_ember_sounds.py,
build_revision.py, sound_notes_*.md, and analyse the WAVs in ./Pause/Assets/Audio/Resources/Audio/EnemyDeath/. Reuse the toolkit
(copy it into build_<w>_sounds.py; keep style, loudness method, verification table). New WAVs go ONLY to the EnemyDeath folder;
scripts/notes/sheets/auditions to EnemyDeathSrc~ (build_<w>_sounds.py, sound_notes_<w>.md, sheet_<w>.png, audition_<w>.wav: variant 0
of each in order, 1.2 s gaps, each scream mixed over its death sound).
USER RULES (verbatim origin): "they have to make sense for the units, I don't want random beeping, that's annoying." Every sound is a
believable physical/organic event for what the unit IS, never a tone, chirp, blip, siren or coin. Not beepy (no sustained pure tones,
no sine sweeps above 1.5 kHz, no square chirps); round and low (centroid < 1.8 kHz, bigs < 900 Hz, < 12% energy above 4 kHz);
soft 2-8 ms attacks; layered (thump + material body + detail + 80-250 ms room); mono 44.1 kHz 16-bit WAV; numpy + wave only;
deterministic seeds; 3 round-robin variants named <key>_0/_1/_2.wav (cross-correlation peak < 0.6, length within +-20%).
LOUDNESS (a bug the user hit: "randomly some of them scream really loudly"): equalise PERCEIVED loudness, never peak: A-weighted
level of the loudest 50 ms of every death cue within +-1.5 dB of -15 dB (bigs/elites up to 2 dB louder), no ringing 1-3 kHz
resonance with a tonal run > 100 ms. Print the A-weighted table and fix until it passes.
DEATH CUES (x3): inspect every sprite (./Pause/Assets/Art/Resources/Enemies/<key>.png, elites in Resources/Elites/<World>/) and the names
in CodexCatalogue so the sound fits the drawn creature/machine: <one line per key: material event, ~duration>. Insect machines =
chitin crunch + small servo/wing buzz-burst (noise based, < 90 ms, masked); pilots' machines = servo/metal tear; creatures =
wet/organic burst; rocks = their material; mines = a concussive pop with the mine's contents.
SCREAMS (x3, <key>_scream_<0|1|2>.wav): ONLY for units with a living occupant or creatures: <list; justify each in the notes>. NO screams
for chasers, bigs, mines, rocks. A held painful, scared "ahhhh": 0.7-1.2 s (elites 1.0-1.3 s), quick attack, sustained vowel with natural
pitch contour (rise, 5-7 Hz vibrato and fear tremor, then a long falling pitch with growing breathiness), radio-filtered/muffled, cut by
the blast with a 20 ms fade, centroid < 1500 Hz (creatures < 1700), < 8% above 3.5 kHz, peak -9 dBFS, ~10-14 dB under its death cue in
game (the game plays screams at x0.5 with a 34-55 ms delay). Voices distinct per unit.
DO NOT make backdrop, ambient or music sounds: the user authors those himself.
VERIFY with the full per-file table (duration, peak, A-weighted loudest-50 ms, RMS, centroid, HF share, attack, longest tonal run,
clipping, variant differences, scream+death mix peak) and fix until it passes. Finish with a summary, caveats (you cannot listen: say what
protects against beepiness/harshness) and which sounds most need ear-tuning.
```

Revision prompt (use when the user gives feedback): "Rewrite WAVs IN PLACE (same names) ... USER FEEDBACK (verbatim): '<quote>' ...
TASKS: 1) screams longer/more present 2) death cues with no character (<keys>) get an identity layer 3) bring hot heavy cues down
~1.5-2 dB 4) re-run the verification table for every changed file; list what you changed."

## 9. Optional: a new explosion material (only if the world adds a `TargetExplosion.Kind`)

```
<preamble, OUTPUT = ./Pause/Assets/Art/Weapons/ExplosionsV3~/>
Extend ./Pause/Assets/Art/Resources/Weapons/Explosions.png (2048x768, 16 columns x 6 rows of 128 px: rows Metal, Rock, Mine, Ice, Spore,
Magma; cols 0-9 burst, row 0 cols 10-12 flash star x2 + ring) with ONE new row for <MATERIAL> in exactly the same style: compact soft
neon-pixel bursts, no outline, no red, core + debris <= 55-65% of the cell, a hot core that swells, breaks into glowing debris and fades.
Deliver a NEW file with the extra row(s) appended (2048 x (768 + 128 per row)); the existing rows must be byte-identical.
```

## What to look at when you critique (copy into your reply to the user)

- **Planet:** silhouette distinct? entry plasma wide? decks tile? cloud colour continues into the backdrop?
- **Backdrop:** do the four sets differ at a glance? sky/far as strong as mid? night side clearly night? where are the pipes/smoke/fire?
  do pieces sit sensibly (clusters, pinned, calm gaps), do smokes sit on stacks? real in-game frames, not a collage.
- **Enemies:** tones/boundaries numbers, anchor/scale drift, margins, stand-out on each tile, rocks that move, the weakest strip.
- **Deaths:** body continuity, readable at game scale, forbidden colours.
- **Boss:** stage 3-4 dramatic? smoke bold? clipped cells? attacks readable (tell before shot)?
- **Sounds:** table PASS; "most need ear-tuning: ...".
