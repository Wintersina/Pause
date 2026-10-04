# Pause art style v2: neon pixel art

**Neon pixel art** is detailed, hand-placed-looking pixel art lit by neon. Solid materials are
painted with short ramps and texture, and saturated neon cores and lights glow past their 1 px
dark outlines. The colour sense is cyberpunk / *Akira*: dark bases, one electric hue per world,
and white-hot centres.

The benchmark is the **original rail-mine atlas**, which the user picked as "the theme I actually
want". It is no longer in the tree; view it with
`git show 18b5e5f^:Pause/Assets/Art/Resources/Vfx/rail_bomb_themes_atlas.png`. It has 4 worlds,
and each world has 4 key poses: dormant → waking → charging → burst. The second benchmark is the
green heal atom (`Pause/Assets/Art/Resources/Pickups/heal_atom_green.png`).

> **Superseded (v1).** The flat-cartoon cel "Akira" guide (bold flat fills, thick ink, no
> gradients) still governs **only the UI** (HUD, death panel, dock, quick actions, codex, menus)
> **and the player ships**. The ships stay unchanged for now. Everything else in gameplay
> converts to v2. The v1 text is in git history (`git log -- docs/art-style.md`), and its
> samples remain in `docs/art-samples/` (top level). The v2 samples are in
> `docs/art-samples/neon/`.

Defining traits (keep all of them, on every converted asset):

1. **1 px dark selective outline.** Ink on the shadow side and darkened local colour on the lit side.
2. **4–6 tone painterly material ramps** with texture: scratches, grain, facets, rivets, cracks.
3. **Rim light.** One pixel on the side away from the key light, usually in the world's neon colour.
4. **Saturated neon cores and lights.** Their glow and bloom spill past the outline.
5. **Elemental particle FX.** Sparks, snowflakes, leaves, energy arcs, embers.
6. **One elemental palette per world** on a dark base.
7. **Key-pose animation.** Clear frame sequences; the dormant → charge → burst pattern is the model.

### Rugged industrial rail pattern

The current gameplay direction is **rugged cyberpunk with a restrained steampunk layer**. This
applies to enemies, bosses, rail mines, rails and world landmarks; it does **not** change the
protected flat-cartoon UI or player ships.

- Start with weathered gunmetal or each world's local material, then add sparse brass/copper
  brackets, rivets, recoil collars, vent seams and exposed cable runs. Brass is a structural
  accent, never the dominant surface colour.
- Wear must be directional and readable: clipped paint scuffs on leading armour faces, small
  edge chips, soot around vents and heat stains near reactors. Do not scatter noise uniformly.
- Steam, smoke, frost vapour and spores are small secondary motion cues. They must never cover
  a core, telegraph or collision silhouette.
- Rails are heavy industrial infrastructure: layered dark metal, bolts, cable bundles, magenta
  status lamps and occasional cyan power conduits. Rail mines physically clamp to and ride the
  rails, so their clamps, pivots and emissive cores must visually align with the side structure.
- Bosses use the same vocabulary at a larger scale, but remain compact inside their atlas cells.
  The concept-art scale is not gameplay scale: body silhouettes stay within the documented
  boss-cell budget and tells/fire/death poses communicate through shape and light before detail.

This pattern keeps the four worlds distinct: Space is gunmetal/cyan-magenta, Frost is
icebreaker steel/cyan, Verdant is corroded plant-metal/lime, and Ember is basalt/brass/amber.

### Replacement policy: full raster rebuilds

When an existing gameplay asset is migrated to this direction, it is a **fresh raster redraw**.
Do not "upgrade" a legacy SVG or flat sprite by adding a few scratches, overlays or colour
shifts. Recreate every material plane, outline, light, glow and effect in the neon pixel-art
language above. The old asset may be used only to preserve the gameplay contract: its key,
transparent canvas, frame count, cell dimensions, pose order, silhouette budget and collision
read. Stage and review each completed sheet before replacing its live resource.

---

## 1. Palette

All hexes below are sampled from the reference atlas. Each cell was median-cut with Pillow, then
cleaned into even ramps. The values in `pixelkit.PAL` are the source of truth, and this table
mirrors them. Swatch: `docs/art-samples/neon/palette.png`.

**Shared**

| Role | Hex | Use |
|---|---|---|
| `INK` | `#05060c` | outline, deepest shadow, sockets |
| `PLAYER_RED` | `#d8232c` | **player only**; never on an enemy, rock, mine or boss |
| White-hot | `#f5fdfd` / `#fffbe0` | the single brightest pixel of a neon core (cool or warm worlds) |

**Space**: steel sphere, cyan core and lights.

| Ramp | Dark → light |
|---|---|
| metal | `#0b0f15` `#1a2129` `#2f343b` `#4b5059` `#7a7c82` `#b0b4b6` |
| trim (blue-steel) | `#0a1118` `#162e3c` `#2f4550` `#476d7b` |
| neon cyan | `#0b4f7a` `#0b91cc` `#0bd0f6` `#7af6fc` `#f5fdfd`, glow `#15d8fc` |
| background | `#03050a` `#070c16` `#0c1424` `#132036` `#1c2e48`, bg accent `#22668a` |

**Frost**: ice-crystal shell, snowflake core.

| Ramp | Dark → light |
|---|---|
| ice | `#0a1734` `#1c3c68` `#2a62ae` `#4a96e6` `#9fd2f2` `#e2fbff` |
| metal (blue-grey) | `#0c1020` `#1e2638` `#33405c` `#4f6188` `#7d93bd` |
| neon ice-blue | `#1569c8` `#1898f7` `#46ddfd` `#aaf2fb` `#f8fefc`, glow `#46c8fd` |
| background | `#03060e` `#07101f` `#0d1a31` `#142645` `#1d3558`, bg accent `#2e6c98` |

**Verdant**: vine-wrapped bark sphere, lime core, magenta thorns.

| Ramp | Dark → light |
|---|---|
| bark | `#0c0703` `#281b0a` `#3e2610` `#5a3a17` `#7d5426` `#a07a3c` |
| vine | `#0e1c03` `#1f3f04` `#306203` `#4dae03` `#a6d32a` |
| neon lime | `#3d8f02` `#70d804` `#b8f018` `#e6fc5e` `#fbffd8`, glow `#8ef014` |
| thorn magenta | `#3a0a2a` `#8c1462` `#e0309e` `#ff9ad6` |
| background | `#030603` `#060f07` `#0b1a0d` `#122615` `#1b351c`, bg accent `#3f7a22` |

**Ember**: basalt with magma cracks and an orange burst.

| Ramp | Dark → light |
|---|---|
| basalt | `#0e0808` `#1d1212` `#2e1d1a` `#47302b` `#6a4a40` `#957060` |
| magma | `#6a2404` `#c24a06` `#f77a0a` `#fcb809` `#fcee09` `#fdfa92` |
| neon amber | `#c24a06` `#fc7a08` `#fcb809` `#fdf06a` `#fffbe0`, glow `#fc8a10` |
| background | `#060303` `#100707` `#1a0c0a` `#26130f` `#341c16`, bg accent `#8f4a1c` |

**The enemy-never-red rule stays: red means friendly / the player.** The reference had two
breaches, and v2 fixes both.

- **Ember magma.** The reference cracks used `#cf0d03` and its burst used `#f91302`. Both are
  pure red. Ember's warm colours must keep a **hue of at least 18°** (orange to yellow). The
  darkest magma tone, `#6a2404`, is the reddest allowed.
- **Verdant thorns.** The reference thorns were crimson (`#6b1c26`, `#a11e18`). They are now
  **magenta**, at a hue of about 320°.

A converted asset may not use any other hue in the 345°–15° range at a saturation above 0.5.

Rules:

- Each asset uses **its own world's ramps, plus `INK`**. Mixing worlds is allowed only in
  set-piece moments, such as portals.
- A ramp is used whole or in part, but **never extended with in-between tones**. A new tone
  needs a palette change in `pixelkit.PAL` and in this table.
- Neon ramps are for **emissive pixels only**: cores, lights, energy and FX. Rim lights may
  borrow neon step 1. Materials never use neon tones for diffuse light.

---

## 2. Pixel technique

### 2.1 The game pixel

**1 game pixel = 1/64 world unit (PPU 64).** Every enemy, rock, mine, boss, projectile, FX and
background shares this pixel, so pixel size matches across the whole playfield.

Measured against the current game:

- **Screen.** It is 10 u tall, which is 640 game px. Its width is about 5.7 u, which is about
  365 game px.
- **Phone.** One world unit is about 190 px on a 1080-wide phone, so **1 game pixel is about
  3 screen px**. That is chunky enough to read as pixel art and fine enough for "HD" detail.
- **Export.** Every sprite is drawn at native game-pixel size, then **upscaled ×4 with
  nearest-neighbour**, so the texture PPU is 256. The ×4 copy keeps edges clean when sprites
  rotate, sway or scale (enemy sway, boss tilts), which raw 64-PPU texels would not.
- **Loader rule.** `EnemyArt` sets `ppu = tex.height / FrameWorldSize`. Converted enemy art must
  therefore use `FrameWorldSize = frame_px / 64`, where `frame_px` is the native frame height.
  Any other value changes the pixel size. Bosses (`BossConfig.BossWorldSize`) and backgrounds
  follow the same rule.

| Asset class | Gameplay size today | Native frame (game px) | Silhouette inside the frame |
|---|---|---|---|
| Rocks | frame 0.66 u | **42–44** | 30–36 |
| Aliens | frame 0.67 u | **44** | 30–36 |
| Mines | frame 0.85 u | **54–64** | 44–52 (the recreation uses 64 = 1.0 u to leave room for the burst) |
| Fighters, chasers | frame 0.9–0.95 u | **58–61** | 46–52 |
| Heavies ("Big") | frame 1.3 u, about 1.1 u drawn | **84** | 64–72 |
| Enemy and boss projectiles | 0.42–0.46 u | **28–30** | 12–20 core |
| Bosses | cell 3.3 u | **208–212** | 128–192 body |
| Walls | as now (e.g. 64×448 tex) | 1 game px = 1/64 u | – |
| Backgrounds | full screen and up | **365 × 640** per screen (tiles 128–256) | – |
| Player ships, atoms, UI | **unchanged** | – | – |

The heal atom family is denser, at about 168 px per unit. It is a protected exception.

### 2.2 Import settings (Unity)

- **Filter mode: Point.** No mipmaps. Wrap mode: Clamp.
- **Compression: None** (RGBA32). No lossy compression on sprites; ASTC or ETC smears pixel
  edges and neon gradients. Backgrounds may use RGBA32 or, if memory forces it, RGB24.
- **Sprite pivot:** centre, as now. **Pixels Per Unit:** 256 for ×4 exports, or set by the
  loader as above.
- Never resample in Unity. Never draw a sprite at a non-integer multiple of another sprite's
  pixel. Scale a gameplay object only through `FrameWorldSize`.

### 2.3 Outline

- **Exactly 1 game pixel** of outer outline, on every solid object, using 4-connectivity.
- **Selective outline (sel-out).** On the shadow side (bottom-right) the outline is `INK`. On
  the lit side (top-left) it is the neighbouring local colour, darkened to about 25% of its
  value and mixed with ink.
- **Every overlapping part gets its own outline.** Pods over a sphere, a clamp over a body, a
  wing over a hull: the ink line is what separates parts.
- **Interior lines:** panel seams and grooves are 1 px, drawn **two ramp steps darker** than
  the surface, not black. Use black only for sockets and deep cuts.
- Glow, particles and energy arcs are **never outlined**.

### 2.4 Shading

- **Key light from the top-left**, toward the viewer: `(-0.55, -0.65, 0.52)`.
- **Ramps:** 4–6 tones per material, lit with hard bands. Most of an object sits in the middle
  three tones. The top tone is a hot spot of a few pixels; the bottom tone sits on the
  bottom-right edge.
- **Texture:** quantise it to the ramp. Add value noise and grain at 0.10–0.16 of the ramp
  range. Add 1 px scratches one tone up, rivets as single top-tone pixels, and facets or cracks
  as one-tone-down lines. Texture lives in the mid-tones and never breaks the silhouette.
- **Rim light:** 1 px on the bottom-right edge, the edge facing away from the key light. Use
  neon step 1 for the "lit by its own core" look, or ramp step 3 for plain parts. The rim stops
  where another part covers the edge.

### 2.5 Dithering

- **The default is no dithering.** Bands are hard.
- **Ordered (Bayer 4×4) dithering** is allowed only on band borders of large, slow surfaces:
  rocks, boss armour, background gradients. Keep it at strength 0.3–0.6
  (`shade(dither=...)`), and only between adjacent ramp tones.
- **Never dither** on sprites smaller than 32 px, on neon, or on animated pixels. Dither
  flickers when it moves.

### 2.6 Anti-aliasing

- **No soft anti-aliasing.** No bilinear, no supersample-and-shrink, no semi-transparent edge
  pixels on solid objects. Solid pixels have alpha 255.
- **Hand AA only.** One intermediate ramp tone at the steps of a curve's stair (the kit's
  ramp quantisation does this naturally).
- **Glow layers are the only partial alpha**, and they are stepped (see §3).

---

## 3. Glow and bloom

- **Separate layer.** Glow is a separate layer built from the emissive pixels: cores, lights,
  energy and magma. It is composited **additively**, behind the sprite for halos and over it for
  flashes and spikes. In the art it is baked into the flipbook frame, because the kit's `glow()`
  output sits in the same frame. A runtime emissive or additive sprite on top is allowed if a
  later code phase wants it. No camera post-process bloom is required.
- **Hard core, pixel halo.** The emissive pixels themselves are opaque neon ramp tones; the
  brightest pixel is white-hot. The halo is a blur quantised into **3–5 hard alpha steps**, so
  it reads as pixel rings. It is capped at **62% alpha**, and the halo colour is the world's
  glow hex.

**Spill limits** (measured from the outline):

| State | Max spill |
|---|---|
| Idle / dormant | 2 game px |
| Waking, tells | 3 px |
| Charging | 4 px, plus energy arcs out to 8 px |
| Burst / explosion | up to 25% of the sprite's size, and the halo must fit inside the frame |
| Boss idle | 6 px |
| Boss attack | 12 px |

- **Glow density.** No more than about 15% of an enemy's silhouette may be emissive. The
  material must read first and the neon second, except on burst frames.
- **Pause.** The game's mechanic is the pause, and **glow freezes with time**. Because glow is
  part of the frame, a paused enemy holds its current frame and halo exactly. Any shader or
  code-driven pulse must run on scaled game time (`Time.time` / `deltaTime`), never on
  unscaled time. Don't dim, desaturate or "breathe" glow during pause unless a pause-FX design
  says so.

---

## 4. Animation

Timing is authored in **ticks of 1/24 s**, as `IdleTicks` in `EnemyRoster` already does.

| Action | Frames | Holds (ticks) | Notes |
|---|---|---|---|
| Idle (fighter, chaser, alien) | 4 | 4 4 4 4 | 1 px bob, engine flicker, light pulse, eye blink on the last frame |
| Rock idle | 4 | 5 3 2 3 | a facet glint travels; silhouette fixed |
| Mine: dormant → waking → charging → burst | 4 keys | **12 / 4 / 4 / 8** | loop the dormant frame while idle (with a 2-frame light pulse); play waking and charging as the tell; burst then hand off to the explosion |
| Tell / wind-up (any attacker) | 2–3 | 3 2 2 | the neon ramp climbs one step per frame; spill grows per §3 |
| Hit flash | 1 | **2** | every non-ink pixel becomes neon step 4 (white-hot), outline stays `INK`; then back |
| Enemy explosion | 6–8 | 1 2 2 3 3 4… | the core flashes white, the shell cracks into ramp-coloured shards, world particles, a stepped halo that shrinks |
| Boss idle | 4–6 | 4–6 each | slow; plates shift a pixel, lights breathe in 2 steps |
| Boss attack | key poses: anticipation → charge → release → recover | 6 / 4 / 2 / 8 | |

Key-pose rules:

- **Readable holds.** The pose that carries meaning (dormant, full charge) gets the longest hold.
- **Silhouette first.** Between frames, change light and FX before you change silhouette. A
  silhouette moves at most 1–2 px on idle loops.
- **Pattern.** Every threat escalates through **dormant → charge → burst**, with neon brightness
  stepping up the ramp each pose. Players learn to read the neon level as danger.

---

## 5. Readability versus backgrounds

Backgrounds are neon pixel art too, but **darker, lower in contrast, and lower in neon density**
than anything you can collide with. These limits are checked by `pixelkit.bg_check()`:

| Metric (HSV) | Background limit | Gameplay objects |
|---|---|---|
| 95th-percentile value | **≤ 0.35** | material mid-tones at 0.25–0.70 |
| Brightest pixel | **≤ 0.60** | neon cores at **≥ 0.90** |
| Saturation of bright pixels (value > 0.3) | **≤ 0.80** | neon at 0.6–1.0 |
| Accent coverage (value > 0.40) | **≤ 3%** of pixels | emissive up to 15% of a silhouette |
| Outline | none; background shapes are edged with a lighter `bg` tone | 1 px `INK` / sel-out, always |
| Glow | none, or ≤ 1 step at 25% alpha | per §3 |

- Backgrounds use only the world's `bg` ramp plus its `bg accent`. They never use the gameplay
  neon ramps or white.
- **Parallax layers** step up the `bg` ramp toward the viewer (far = `bg[1–2]`,
  near = `bg[3–4]`). Ordered dither is allowed on large gradients.
- **Background particles** (snow, embers, dust, twinkles) use `bg[4]` or the bg accent, at
  1–3 px.
- **Walls** sit between the background and gameplay. They may use the material ramps, but
  only the lower 4 tones and no neon brighter than step 1.
- **Check:** the frost vignette scores p95 value 0.27, max 0.60, saturation 0.70 and 0.25%
  accents (`samples.py` asserts this). Then composite an enemy and the player ship on it at
  true scale, as in `vignette_frost.png`.

---

## 6. Protected assets (unchanged)

| Asset | Path | Used by |
|---|---|---|
| PAUSE title logo | `Pause/Assets/Art/pause_title_2.png` (guid `a2e075ab7763def46a6d6d3587b47678`) | `Scenes/startS4.unity` |
| PAUSE logo (README) | `docs/pause-title.png` | `README.md` |
| HapticGate splash mark | `Pause/Assets/Art/HapticGate.png` (guid `4f7e65d7e0d48dd48a9a0f0b5c456d93`) | `Scenes/spashS7.unity` |

- Don't redraw, recolour, filter or re-export these.
- Nothing glowing may sit inside the logo's bounding box.
- `ArtRestyleTest` still guards them.

---

## 7. Production pipeline: PixelKit

Agents can't hand-place pixels, so the look comes from a **reproducible procedural pipeline**:
`Pause/Assets/Art/PixelKit/src~/pixelkit.py`, which needs only numpy and Pillow. Unity ignores
`src~`. Recipes and samples are in `samples.py` in the same folder. Run `python3 samples.py` to
rebuild `docs/art-samples/neon/`.

Order of work (each step is a kit call):

1. **Shapes:** `disc`, `ellipse`, `ring`, `rect`, `round_rect`, `poly`, `line`, `capsule`,
   `star`. These are boolean masks with no AA. Mask ops: `dilate`, `erode`, `edge`, `shift`,
   `distance`.
2. **Heights:** `height_sphere`, `height_dome` (pillow, any silhouette) and `height_bevel`
   (plates). Facet tilt from `facets()` can be added for crystal or rock.
3. **Shading:** `shade(mask, hf, ramp, strength≈radius, tex=..., tex_amt=.1–.16, spec=.03,
   dither=0|.3–.6, bias=...)`. It does Lambert lighting, then texture, then quantises to the
   ramp.
   - Use `part(...)` for shade + rim + outline of one part, and build sprites back to front
     with `over()`.
4. **Detail:**
   - `tone(img, mask, ramp, ±n)` steps pixels along the ramp. Use it for seams (−2),
     scratches (+1) and crater lips.
   - `scratches`, `cracks`, `facets` / `facet_lines`, `value_noise`, `grain`.
   - `highlight` for rivets.
   - `fill` for flat colours.
5. **Rim and outline:** `rim(img, mask, color, dx=1, dy=1)`, then `outline(img)` (sel-out).
6. **Neon:** paint emissive pixels with `fill()` from the neon ramp on a separate layer.
7. **Glow:** `glow(emissive, glow_hex, radius, strength, steps)` or `halo(...)` for bursts.
   Composite with `add()` behind the sprite, then `over()` the sprite, then `over()` the
   emissive layer.
8. **Particles:** `twinkle`, `spark`, `snowflake`, `leaf`, `ember`, `arc` (jagged energy),
   `orbit_arc`, and `scatter(kind=...)`.
9. **Export:** `strip`, `sheet`, `upscale(img, 4)`, `save`, and `save_gif(frames, path, ticks)`.
   `hit_flash(img)` makes the 2-tick hit frame. `palette_swatch`, `on_bg` and `bg_check` help
   with previews and limits.

Converter conventions:

- Put each asset family's generator in its own `src~` (for example `Art/Enemies/src~`), and
  `import pixelkit` from `Art/PixelKit/src~`, for example via `sys.path`.
- Generators are deterministic: fixed seeds, no randomness without a seed.
- Write the ×4 strip into `Art/Resources/...` with the same name and frame count as the asset
  it replaces. Update `FrameWorldSize` so it equals `frame_px / 64`.
- Preview every asset at true scale on its world background before committing.

---

## 8. Do / don't

| Do | Don't |
|---|---|
| Draw at native 1/64 u pixels, upscale ×4 nearest | Draw big and downscale, or resample in Unity |
| 1 px sel-out outline per part | Thick ink (v1), no outline, or outlined glow |
| 4–6 tone ramps with texture in the mid-tones | Smooth gradients, airbrush, photo texture |
| Rim light on the bottom-right | Rim on every edge (that reads as a sticker outline) |
| Neon cores with a white-hot centre and a stepped halo | Soft full-opacity bloom blobs, or glow wider than §3 allows |
| One world palette per asset plus `INK` | Mixed world hues, or invented in-between tones |
| Cold, sickly, electric or orange enemies; magenta thorns | **Red enemies**, red magma, crimson thorns |
| Dark, quiet, low-neon backgrounds that pass `bg_check` | Bright neon cityscapes behind the playfield |
| Key poses with clear holds: dormant → charge → burst | Smooth tweened motion, or silhouette jitter on idle |
| Glow that freezes with the pause | Pulses on unscaled time |
| Leave the ships, UI, atoms, PAUSE logo and HapticGate alone | Restyle protected or kept assets in this conversion |

---

## 9. Reference samples (`docs/art-samples/neon/`)

| Sample | File |
|---|---|
| **Space rail mine, PixelKit recreation vs the original row** | `mine_space_vs_original.png` |
| Mine strip (4 key poses, ×4) / GIF | `mine_space_strip.png`, `mine_space_burst.gif` |
| Fighter, alien and rock for Frost and Ember | `enemies_frost_ember.png`, `{frost,ember}_{fighter,alien,rock}.png` |
| Enemy idle (Ember fighter, 4 frames on 4s) | `ember_fighter_idle_strip.png`, `ember_fighter_idle.gif` |
| Boss concept bust (Space, 176×144 game px) | `boss_space_bust.png` |
| Background vignette, Frost, with a fighter, a rock and the unchanged player ship at true scale | `vignette_frost.png` |
| Palettes | `palette.png` |
| Everything on one sheet | `sample-sheet.png` |
