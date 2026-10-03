# Pause art style: 80s anime / Akira

Status: **v1.1.** Converted so far: the UI (HUD, quick-action icons, death panel, space dock UI,
PAUSED overlay and pause glow, world banner, menu buttons via `MenuStyler`, tutorial palette) and the
pickups (the pixel-art atom family, §5.1). Everything else in [`art-audit.md`](art-audit.md) is still to do.
The sample sheet is [`art-samples/sample-sheet.png`](art-samples/sample-sheet.png), the before/after comparison is
[`art-samples/before_after.png`](art-samples/before_after.png), and the per-asset conversion list is
[`art-audit.md`](art-audit.md).

This guide is for the agents and artists converting the game's art. If a rule here conflicts with
something you like better, follow the rule and raise the idea separately.

---

## 0. The look in one paragraph

Flat, cartoony 2D cels in the manner of 80s TV anime, using the colours of *Akira* (1988): blue-black
night, Kaneda red, sodium-orange city glow, teal neon. Every solid object is a **bold flat colour shape
with a thick ink outline, one hard shadow tone and one highlight tone**. Shapes are **angular,
mechanical and punchy**, never round, cute or bubbly. Light is the only thing allowed to glow. Motion is
snappy and frame-by-frame, with anticipation, squash and stretch, smear frames, held key poses and the
long red tail-light streak. **Nothing should look 3D-rendered.**

---

## 1. Palette

All hex values live in code in [`art-samples/src/akira.py`](art-samples/src/akira.py). Use those names.
Swatches: [`art-samples/palette.png`](art-samples/palette.png).

### 1.1 Core roles

| Role | Name | Hex | Use |
|---|---|---|---|
| Night / sky | `NIGHT_0` | `#070A16` | deepest sky, top of backdrops |
| | `NIGHT_1` | `#0E1424` | default backdrop, UI panel fill |
| | `INDIGO_0` | `#1A1F45` | indigo, far structures, smoke core |
| | `INDIGO_1` | `#2A2E6B` | lit indigo (planets, far hulls) |
| | `DUSK` | `#3A2A5C` | horizon band, smoke cels |
| Hero | `RED` (Kaneda red) | `#D8232C` | player hull, hero UI, title slabs |
| | `RED_SH` | `#86121F` | its one shadow tone |
| | `RED_HI` | `#FF5B45` | warm kick on red, used sparingly; prefer `BONE` |
| City glow | `SODIUM` | `#F2862B` | sodium-orange lamps, exhaust mid, city windows |
| | `AMBER` | `#FFB43C` | hot amber, star dust, nav lights, rim kicks |
| | `SODIUM_SH` | `#A9481A` | shadow of amber/sodium forms |
| Neon | `TEAL` | `#1FB5B9` | canopy glass, neon signage, HUD speed |
| | `CYAN` | `#6EF2EE` | neon highlight, speed lines |
| | `TEAL_SH` | `#0F5E6A` | teal shadow |
| Accent (sparing) | `MAGENTA` | `#FF2E88` | enemy lights only, plus one accent per screen at most |
| | `MAGENTA_SH` | `#8E1450` | dimmed enemy light |
| Ink | `INK` | `#140C14` | every outline; warm near-black, **never `#000`** |
| Highlight | `BONE` | `#F4EAD4` | off-white kicks, type, racing stripes; **never `#FFF`** except 1-frame impact flashes |

### 1.2 Who gets which colours

The player and enemies must read as opposites at a glance, even in peripheral vision.

* **Player = red / warm.** `RED` hull, `BONE` stripes, `GUN` metal (`#2C2D40` / `#1A1A28` / `#5A5C78`),
  `TEAL` canopy, `AMBER`/`SODIUM` lights and exhaust. Allies (tutorial robot, friendly UI) share the warm side.
* **Enemies = cold, sickly or hostile.** No enemy may use `RED` as its body colour. The current Kenney
  "Red" tier is retired to the bruise family.
  * Steel: `STEEL #5A6A88`, `STEEL_SH #262D44`, `STEEL_HI #A3B4CC` (fighters, mechanical hulls)
  * Bruise: `BRUISE #74409A`, `BRUISE_SH #3A1E52`, `BRUISE_HI #A86CD0` (armour panels, mines)
  * Bile: `BILE #8FA84E`, `BILE_SH #3E5229`, `BILE_HI #D4E68E`, `BILE_LIGHT #C8FF3A` (aliens, organic)
  * Enemy lights are `MAGENTA` (machines) or `BILE_LIGHT` (organics), **never amber or red**.
* **Hazards (asteroids, debris):** `ROCK #605878`, `ROCK_SH #2C2638`, `ROCK_HI #958AA4`, with an `AMBER`
  rim kick on the lit edge (reflected city glow) so they separate from the sky.
* **Pickups:** a pixel-art family (see §5.1), keeping the colour coding players already know: the
  green heal atom keeps its own greens (`#29A805`, `#7EE702`, `#B4F246`, white kick, ink `#00021B`), the
  blue shield atom is `TEAL_SH`/`TEAL`/`CYAN`, the red pause atom `RED_SH`/`RED`/`RED_HI`, and star dust
  (money; warm reads as "good") `SODIUM_SH`/`SODIUM`/`AMBER`. Each ramp ends in a `BONE` kick and sits on
  `INK`. (The earlier `HEAL #2EE6A6` hex-cell heal sample is superseded.)

### 1.3 Per-world variations (all inside the Akira family)

Worlds are `WorldManager.Worlds`: **Space (0), Frost (1), Verdant (2), Ember (3)**. Only the backdrop,
walls, atmosphere particles and the portal tint change. Gameplay sprites keep the core palette in every
world, which is what keeps them readable.

| World | Sky top to bottom | Structure tones | Rim / lamp | Particles | Portal |
|---|---|---|---|---|---|
| Space | `#070A16` > `#0E1424` > `#1A1F45` > `#2A1E48` | `#1A1C3A`, planet `#2A2E6B` / `#141838` | `SODIUM` city lights, `CYAN` planet rim | `CYAN` dust, 35% | `CYAN` |
| Frost | `#04080F` > `#0A1A2A` > `#123248` | towers `#0F2134`, crags `#16324A` | ice rim `#9FE8F0`, `AMBER` windows (sparse) | `CYAN` ice flecks | `#9FE8F0` |
| Verdant | `#05070F` > `#0A0F20` > `#10183A` (indigo night) | pines far `#0F3A32`, near `#185038`, kick `#2E7A52` on indigo shadow `#0A1A2E` | `TEAL`/`CYAN` river and glyphs, `SODIUM` lanterns, 2 px `RED` lantern dots only | `AMBER` fireflies, `CYAN` spores (flipbooks) | `#7FAF6A` |
| Ember | `#120608` > `#24090E` > `#3E1016` | basalt `#2A1416`, `#5A1A1A` | lava `SODIUM` / `AMBER`, hard-edged | embers `SODIUM` | `AMBER` |

Ember is the only world where a warm colour dominates the backdrop. To keep the red player readable
there, Ember backdrops stay **at or below 30% value** and their lava is sodium-orange, never `RED`, and
the player keeps its `BONE` stripes and `INK` outline.

---

## 2. Rendering rules: flat cartoon, not 3D

1. **Flat colour shapes.** Each form gets at most **three tones: base, one hard shadow, one highlight.**
   No gradients on objects. Gradients are allowed only on sky backdrops (`L0`) and inside glow layers.
2. **Hard-edged shadow shapes.** Shadows are polygons with sharp edges, placed as a cel painter would:
   light comes from the upper left, so the shadow falls on the lower right side, undersides and trailing
   edges. No ambient occlusion, no soft falloff, no rim gradients.
3. **Highlights are "kicks":** thin hard slivers of `BONE` (or the family's `_HI`) on leading edges and
   glass. Use one or two per form. No glossy specular balls and no round "shine dots".
4. **Thick, confident ink.** `INK` outline on every solid object. Stroke weights, in authoring units
   (one sprite canvas = 128 u):

   | Asset size (longest edge in game) | Outer contour | Interior panel lines | Small detail |
   |---|---|---|---|
   | Pickups (about 0.3 world units, 64 u canvas) | 3.2 u | 2 u | 1.2 u |
   | Ships, enemies, hazards (0.5 to 0.9 world units, 128 u canvas) | 4 u | 2 u | 1.5 u |
   | Bosses or large set pieces (over 1.2 world units) | 5 u | 2.5 u | 1.6 u |
   | UI plates and panels (canvas units) | 4 to 5 u | 2 u | 1.5 u |
   | Background structures | 1.5 to 3 u, colour `#0A0C1C` (bg ink) | 1.2 u | none |

   The rule of thumb is outer contour ≈ 3% of the sprite's longest edge, which must still be ≥ 2 px
   at in-game texture resolution. Use `stroke-linejoin="round"` on contours, and `miter` on UI glyphs.
5. **Angular, mechanical, panel-lined.** Build silhouettes from straight segments and chamfers. Curves
   are allowed only as large arcs (planets, rings). Add panel lines, vents, stripes and bolts as ink
   lines. No blobs, no pill shapes, no rounded-rectangle UI, no bubbles.
6. **Simplify and exaggerate.** Push proportions so they read at a glance (bigger nose cones, longer
   prongs, chunkier engines). Think 80s TV cels, not film-grade painted detail.
7. **Glow belongs to lights only:** engine mouths, nav lights, eyes, visors, cores, exhaust, portals
   and city windows. Glow is a `feGaussianBlur` copy of the light's shape in the `glow`/`glow-back`
   layer, at 35 to 70% opacity. Solid hulls never glow, and glows never carry an ink outline.
8. **Grain:** skip it on sprites and UI. Backdrops may carry a fractal-noise grain at **≤ 6% opacity**
   (see `scenes.py`, layer `L6-grain`). The grain should be felt rather than seen.
9. **No 3D tells:** no bevel/emboss, no inner shadow, no Fresnel rim, no metallic gradient, no photo
   texture, no normal-map lighting, no soft drop shadows. A cel drop shadow (hard offset copy, e.g. the
   red offset under UI glyphs) is fine.

---

## 3. Motion language

All timing is in **ticks at 24 fps** (1 tick ≈ 42 ms). Character-like motion runs **on 2s or 3s**
(each drawing held 2 to 3 ticks). Parallax scrolling and camera moves stay smooth, at engine framerate.

| Device | Rule | Sample |
|---|---|---|
| **Key pose holds** | Every loop starts and rests on a held key pose (4 to 6 ticks). Stillness gives the action its punch. | fighter f0 (6 ticks), mine f0 (6) |
| **Anticipation** | 1 to 2 ticks of the opposite motion before an action: squash down, pull in, dim the light. | fighter f1, alien f1, mine f1, heal f1 |
| **Squash and stretch** | Squash on anticipation and impact, stretch on release. Keep the area roughly constant (sx·sy ≈ 1). Rigid hulls flex by at most 6%; organics and FX can go to 15% or more. | alien f1/f2, heal f1/f2 |
| **Smear frames** | 1 tick only. Stretch the shape along the motion, add ghosted copies or speed lines, and drop detail. | exhaust f2, star dust f2 (edge-on cut) |
| **Light trails** | The Akira tail-light: a long red streak with an amber middle, a `BONE` core and a hard shock diamond riding on it. Its length pulses frame to frame, and at speed it smears and adds `CYAN` speed lines. | `player_exhaust` |
| **Speed lines** | Thin, straight vertical `CYAN`/`BONE` lines at 10 to 20% opacity in backdrops, and up to 80% on smear frames. | worlds L4, exhaust f2 |
| **Impact frames** | 1 tick of flat `BONE` (or pure white) shape with a `RED` ink outline, ideally with a 1-tick full-screen red or white flash in code, then a hard-shaped burst. | explosion f0 |
| **Explosions** | Built from hard-edged cel shapes with ink outlines: burst star, lobed fireball (3 flat tones), torn shards plus a 1-line shockwave ring, then angular `DUSK`/`INDIGO` smoke cels and `SODIUM` embers. **No soft particle sprites.** | `explosion` (ticks 1,2,2,2,3,3) |
| **Talking** | 3 mouth drawings (closed, mid, open) on 2s/3s, never lerped. A blink can be added as a separate overlay. | `robot_talk` |

Unity playback: author flipbooks as separate frames and import as a sprite sheet. Drive them from an
AnimationClip with constant sprite keys (no interpolation), or from a script stepping frames with the
tick table. Don't tween scale or rotation in code to fake squash; draw it.

---

## 4. Readability

1. **Backgrounds are darker and less saturated than anything you can touch.** Backdrop layers stay at
   **HSV value ≤ 35%** and **saturation ≤ 60%**. The only exceptions are point lights (city windows,
   lamps), which are tiny. Gameplay sprites must contain at least one tone at **value ≥ 70%**
   (a `BONE` kick, a light, or an `AMBER` rim).
2. **Contrast minimum** (WCAG relative-luminance ratio, measured against the world's *lane colour*,
   the backdrop tone behind the play lane: Space `#0E1424`, Frost `#0A1A2A`, Verdant `#0B1F1C`,
   Ember `#24090E`):
   * the sprite's largest-area tone (its body base) must be **≥ 2.5:1**,
   * and its brightest tone (kick, light or rim) must be **≥ 7:1**.

   The ink outline is not counted, because it separates shapes but doesn't make them read on a dark sky.
   Reference values on Space: `RED` 3.7, `STEEL` 3.4, `BRUISE` 2.6, `ROCK` 2.8, `BILE` 6.9, `AMBER` 10.4,
   `BONE` 15.4. Larger far-backdrop elements (the Space planet, lit towers) may sit behind sprites at
   up to `INDIGO_1`, where bodies drop to about 2:1. That's acceptable only because every sprite carries
   a ≥ 7:1 kick. If a body fails, add a rim kick (`AMBER` for hazards, `_HI` for enemies) rather than
   brightening the whole thing.
3. **Silhouette test:** fill the sprite solid black at **true phone size** (ship ≈ 110 px tall on a
   1080 px wide screen; a pickup ≈ 55 px). It must still read as its class: dart (player), claw
   (fighter), crowned bug (alien), spiked hub (mine), lump (rock), four-point star (dust), hex (heal).
4. **Hue separation:** player red/warm against enemy cold, everywhere. If a world's backdrop drifts
   warm (Ember), drop its value further instead of shifting the player.
5. **One accent per screen:** `MAGENTA` appears only on enemy lights plus at most one UI accent.

---

## 5. Sizes, PPU and fitting the existing gameplay

The camera is orthographic size 5 (10 world units tall, about 5.7 wide on a 9:19.5 phone, widened by
`CameraFit` to a half-width of at least 2.85). One world unit is about 190 px on a 1080 px wide screen.
**Keep each replacement's world-space size and pivot (centre) identical** to what it replaces. Prefab
colliders are `BoxCollider2D` in local units, so matching the world size keeps every collider valid.
Keep the silhouette's mass filling the collider box (Kenney colliders are about 82% of sprite bounds).

| Asset class | Currently | World size | Author canvas | Export | PPU | Notes |
|---|---|---|---|---|---|---|
| Player hulls (Retro80s 1-7) | 64×64 png, cropped by hard-coded rects in `shopingShips.LoadRuntimeSprite`, PPU 100 | normalised to 0.58 u longest edge (`ReferenceHullSize`) | 128 u | 256×256 | any (normalised) | The art must fill the canvas. The hard-coded crop rects must be removed in the conversion PR (a code change), or the new hulls must be authored into those exact rects. **Decision needed.** |
| Player hulls (Originals 8-15) | 128×32 strips / 32×32 idles, PPU 100 | 0.58 u (normalised) | 128 u | 256×256 per frame | any | 3 idle frames + intact/damaged/critical, as now |
| Exhaust / trails | psd sprite frames | about 0.3 × 0.6 u | 128×256 u (ship space) | 256×512 | 440 | trail only. Hide the hull layers on export |
| Kenney enemies | 82-104 × 84 px, PPU 100, prefab scale about 0.89 | about 0.83 u | match aspect (e.g. 128×104 u) | **2× the old px** (e.g. 186×168) | **200** | 2× pixels at 2× PPU gives the same world size, so colliders stay valid |
| Alien (invader) | 32×32 ×4, PPU 100, prefab scale 2 | 0.64 u | 128 u | 128×128 | 400 | or 64×64 at PPU 200 |
| Rail mine | 4×4 atlas of 313 px cells, PPU 180, scale 0.46 | about 0.8 u | 128 u | 4×4 atlas, 313 px cells | 180 | keep the atlas layout: row = world, column = frame |
| Asteroids (Aestroids) | 18-32 px, PPU 100, prefab scale 1.8 | 0.32-0.58 u | 128 u | 2× old px (64×64 / 36×36) | 200 | |
| Kenney meteors | 16-120 px, PPU 100 | as now | 128 u | 2× old px | 200 | |
| Atoms / pickups | pixel art, see §5.1 | 0.28 u (`HealAtom.TargetDiameter`); star dust 0.256 u / 0.064 u | 49-cell grid | 196×196 (x4 nearest) | 700 | heal atom: the original 1254 px art, untouched |
| Explosions / FX | 256 px frames, PPU 100 | about 1-2.5 u | 128 u | 256 per frame | 100-200 | sheet in a single row |
| World backdrop | 1024×4096, seamless vertical tile | full screen | 512×2048 u | 1024×4096 | 100 | seamless top/bottom. Layers L0-L4 in one texture |
| World walls | 64×448, seamless | quad 1.43 u wide, inner ~0.36 u on screen | 64×448 px, crisp | 64×448 | 100 | flat cel walls from `Art/Worlds/src~/walls.py` (Space writes `Art/left.png`/`right.png`); face, lights and spikes in the inner 20 px; right = mirror of left |
| Quick-action icons | 256×256, PPU 256 | UI | 128 u | 256×256 (`render.sh --glyph` variant too) | 256 | |
| Death panel | `dp_*` at 2× zoom, PPU 200 | UI | canvas units | 2× | 200 | keep the 9-slice borders listed in each SVG header |
| Dock | SVG at 100 u = 1 world unit, 3× zoom | world | as now | 3× | 300 | `DockArt.PixelsPerUnit` |
| Tutorial robot | `contra2.png` 110×107 | UI portrait | 128×124 u | 256×248 | match the old on-screen size | 3 mouth frames |

### 5.1 Pickups: the pixel-art atom family (overrides the 64 u SVG spec)

The user picked the **green heal atom** (`Art/Resources/Pickups/heal_atom_green.png`) and the pixel-art
rail mine as the look for pickups, and the green atom as the quality bar. Where that conflicts with the
flat-SVG rules above, the pickups follow the green atom:

* **Pixel art, not vector.** Draw on a coarse cell grid with crisp cells only (no anti-aliasing), then
  upscale with nearest-neighbour and import with **Point** filtering, **no mipmaps, uncompressed**.
  Density matches the green atom: about 47 cells across 0.28 world units, so the siblings use a
  **49-cell grid ×4 = 196 px at PPU 700** (exactly 0.28 u). Star dust: 45 cells (180 px, PPU 70.3125,
  prefab scale 0.1 → 0.256 u) and 11 cells (44 px, PPU 34.375, scale 0.05 → 0.064 u).
* **The green atom's shading is the model:** a dark ink outline that thickens to the lower right,
  balls shaded with a shadow, a base and a light tone plus a hard square white kick (more tones than §2's
  "one shadow, one highlight" — the user's preference wins for pickups).
* **Distinct shape and colour per pickup:** heal = three elliptical orbits around a "+" nucleus (green);
  shield = a hexagonal orbit cage around a split heater-shield nucleus with three cube electrons
  (teal/cyan); pause = two orbits crossed in an X around an octagon-cut red ball with `BONE` pause bars;
  star dust = a cel-faceted four-point star (sodium/amber).
* **The green atom itself is never redrawn.** Its frame 0 is the original pixels. Its idle animation is
  a flipbook of light overlays on a child renderer above it: electron glints in sequence with 1-tick
  smears between them, then a nucleus pop and a shock ring (ticks 8,2,1,2,1,2,1,2,2,2,3).
* **Animation:** every pickup has an idle loop (atoms: rest 6, ten travelling drawings on 2s, then
  anticipation, pop, settle; star dust: rest, squash, stretch, spin through an edge-on smear, rest on the
  flip side) and a 6-frame pickup burst (1-tick white impact, burst star, a ring breaking into shards and
  per-kind confetti: `+`, pause bars, hex chips, sparks). Idle loops run on game time, so they freeze when
  the world freezes.
* Sources: `Art/Atoms/src~/pixel_atoms.py` (writes every frame, the metas and the previews in
  [`art-samples/atoms/`](art-samples/atoms/)); playback: `PickupArt`, `PickupFlipbook`, `PickupBurst`.

---

## 6. SVG authoring conventions

* **Sources live in a `src~/` folder** next to the asset's output, e.g. `Art/<Area>/src~/`. The `~` makes
  Unity ignore the folder. Each folder has a `render.sh` that rasterises with **resvg**
  (`brew install resvg`), following `Art/UI/Icons/src~/render.sh`, `Art/UI/Dock/src~/render.sh` and
  `Art/UI/DeathPanel/src~/rasterize.sh`. The script writes PNGs straight into the `Resources/` path the
  game loads from. PNGs are committed, and SVGs are the source of truth.
* **Fonts:** Orbitron Bold (`Art/Orbitron/Orbitron-Bold.ttf`). Pass it to resvg with
  `--use-font-file`. Type is italic (`skewX(-8)`) with an `INK` stroke under the fill
  (`paint-order="stroke"`).
* **Layer stack.** Every sprite has these top-level groups, in this order:

  ```xml
  <g id="glow-back">  <!-- optional: blurred light that sits behind the object (exhaust, aura) -->
  <g id="base">       <!-- flat base colours, no strokes -->
  <g id="shadow">     <!-- ONE hard shadow tone per form -->
  <g id="highlight">  <!-- ONE highlight tone per form: kicks, stripes -->
  <g id="ink">        <!-- outer contour + panel lines, INK only -->
  <g id="glow">       <!-- lights: flat light colour + optional blurred copy; no ink -->
  ```

  This order lets a pipeline recolour a family (swap `base`/`shadow`/`highlight`), strip glows for a
  silhouette check, or export the trail without its hull.
* **Parametric flipbooks.** Write a frame table (one row per drawing: pose parameters and hold ticks)
  and generate one SVG per frame: `<asset>_<frame>.svg`. See `FIGHTER_FRAMES`/`FIGHTER_TIMING` in
  [`sprites.py`](art-samples/src/sprites.py). Store the hold ticks in each SVG's header comment. Export
  a horizontal strip (frames butted, no gap) for Unity's sprite editor, plus a GIF preview.
* **Coordinates:** a 128-unit canvas for sprites, origin top-left, pivot at the centre. Player art
  faces **up**, enemy art faces **down**. Light always comes from the upper left.
* **Hand-authored SVGs are welcome.** The generator is a convenience, not a requirement. Keep to the
  layer stack and palette names (`<!-- RED -->`-style comments help) either way.
* **No raster in SVGs** (`<image>` is banned) and no external references.
* The pipeline for these samples is `python3 build.py && ./render.sh && python3 compose.py` in
  `docs/art-samples/src/` (needs resvg and Pillow).

---

## 7. Do / Don't

| Do | Don't |
|---|---|
| Flat fills, one hard shadow, one highlight | Gradients, airbrush or soft shading on objects |
| Thick warm-black `INK` contour on every solid | Pure `#000` outlines, or no outline at all |
| Chamfers, wedges, prongs, panel lines | Round blobs, pills, rounded-rect buttons, bubbles |
| `BONE` kicks on leading edges | Glossy round specular dots, "wet" shine |
| Glow only on lights, eyes, engines, cores | Glowing hulls, neon-tube outlines around everything |
| Player red/warm, enemies cold/sickly/magenta | Red enemies, amber enemy lights |
| Backdrops dark, low-saturation, thin bg ink | Bright, busy, high-contrast backdrops |
| Draw squash, smear and holds frame by frame | Tweening scale in code to fake squash |
| Explosions as inked cel shapes | Soft particle puffs, photo smoke |
| Smoke as angular `DUSK`/`INDIGO` cels | Grey photographic smoke |
| Pixel-snapped thick lines that survive at ≤ 64 px | Hairline detail that vanishes on a phone |
| Complement the PAUSE logo's red | Restyle, redraw or recolour the PAUSE logo |

---

## 8. Audit rule: when an existing asset must change

The conversion agents use this rule to classify every asset in [`art-audit.md`](art-audit.md). An asset is
**non-compliant** if **any** of these is true:

1. **3D shading:** gradient-shaded volume, airbrushed or soft shadows, ambient occlusion, bevel/emboss,
   metallic gradients, sphere-like shading (e.g. the glossy atoms, the shaded pixel asteroids, the
   painted gun and projectile atlases).
2. **Photo or painterly texture:** noise-painted backdrops, photographic rock or smoke, realistic
   particle sprites.
3. **Glossy or bubbly:** specular "shine" dots, glass bubbles, rounded-pill or rounded-rectangle forms,
   neon-tube outlines with soft bloom used as the whole look (the current synthwave UI).
4. **No ink:** a solid gameplay object without a dark outline.
5. **Off-palette, or role confusion:** colours outside §1, a red or warm enemy, cold player art.
6. **Readability fail:** breaks §4 (bright backdrop, low-contrast sprite, unreadable silhouette).

The verdicts:

* **KEEP:** already compliant, or not art (fonts, materials). No work needed.
* **RESTYLE:** the shapes and layout are right but the rendering is not. Adjust the existing source
  (usually an SVG in a `src~/`) by recolouring to the palette, adding ink, replacing rounded corners
  with chamfers and removing gradients and bloom. The geometry and the 9-slice layout stay.
* **REDRAW:** the asset fails rules 1 to 3, or has no vector source. Author a new SVG from scratch
  under this guide, at the same world size, pivot and frame count.
* **PROTECTED:** never touch it (see §9).
* **UNUSED:** not referenced by any scene, prefab, animation or `Resources.Load`. Don't spend time on it.

---

## 9. Protected assets: the PAUSE logo

The PAUSE title/logo is **exempt from the restyle**. Don't redraw, recolour, filter, re-export,
re-compress or re-crop it, and don't move it in its scenes. New UI that sits around it must
**complement it, not compete with it**: keep headings near the logo smaller, use `RED` slabs and `BONE`
type that echo its red, and put nothing glowing within its bounding box. `ArtRestyleTest` checks these
files are byte-identical to master.

| Asset | Path | Used by |
|---|---|---|
| PAUSE title logo (in-game texture) | `Pause/Assets/Art/pause_title_2.png` (+ `.meta`, guid `a2e075ab7763def46a6d6d3587b47678`) | `Pause/Assets/Scenes/startS4.unity` (start screen title) |
| PAUSE title logo (repo/README) | `docs/pause-title.png` | `README.md` header |
| Studio splash mark (treated as protected, since it is also a logo) | `Pause/Assets/Art/HapticGate.png` (guid `4f7e65d7e0d48dd48a9a0f0b5c456d93`) | `Pause/Assets/Scenes/spashS7.unity` (splash) |

**Not protected (decided by the user):** the "PAUSED" wordmark (`Art/paused_1.png`) and the pause-glow
bars (`Art/Resources/PauseGlow/pausedGlow_a.png`, `pausedGlow_b.png`). Both have been restyled (sources in
`Art/UI/Pause/src~/build_pause.py`), and the overlay now pops in and glints (`PausedOverlayAnim`, frames in
`Art/Resources/PauseGlowFx/`).

Notes:

* The splash scene `spashS7` contains no PAUSE logo sprite (it shows the HapticGate mark and the text
  "A … Game"). The start screen `startS4` is where the logo appears.
* **App icon:** `ProjectSettings/ProjectSettings.asset` sets the default icon to
  `Pause/Assets/Art/Retro80s/Ships/SourceStrips/xenon2_ship.png`, a ship strip, not the logo. So it
  isn't protected under the logo rule. Changing the store icon is a product decision; the audit lists it
  as *hold*.

---

## 10. Reference samples

| What | File |
|---|---|
| Full sheet | `art-samples/sample-sheet.png` |
| Before → after | `art-samples/before_after.png` |
| Palette | `art-samples/palette.png` |
| Player hull / exhaust flipbook | `player_ship.png`, `player_exhaust_strip.png`, `player_exhaust.gif` |
| Enemy fighter / alien / rail mine | `enemy_fighter*.png/.gif`, `enemy_alien*`, `enemy_mine*` |
| Asteroid, explosion | `asteroid.png`, `explosion_strip.png`, `explosion.gif` |
| Pickups | the pixel-art family in `art-samples/atoms/` (`atom_family.png`, `*_idle_strip.png`/`.gif`, `*_burst_strip.png`/`.gif`); the older SVG `pickup_stardust*` / `pickup_heal*` samples are superseded |
| Worlds | `world_space.png`, `world_frost.png` |
| UI | `ui_hud.png`, `ui_icon_replay.png`, `ui_icon_home.png`, `ui_death_panel.png` |
| Robot | `robot.png`, `robot_talk_strip.png`, `robot_talk.gif` |
| Every rendered frame | `art-samples/frames/` |
| Sources | `art-samples/src/` (`akira.py` palette + helpers, `sprites.py`, `scenes.py`, `ui.py`, `build.py`, `render.sh`, `compose.py`, one `.svg` per frame) |
