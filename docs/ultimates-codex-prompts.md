# Codex prompts: ultimates (boss / elite signature attack art)

Companion to `ultimates-art-brief.md` (audit + the spec table; read it for the why). Same conventions as `world-attacks-codex-prompts.md`:
slots, the common preamble, launch with `scripts/run_codex.sh <job> <prompt.md> [ref-image ...]` (absolute paths for `-i`; worktree `codex-<job>`, branch
`art/<job>`, max 2 jobs; `verify_attack_art.py`, open every image, critique, commit on the job's branch; **show the sheets to the user before wiring**).
Output goes to `./Pause/Assets/Art/Attacks/<World>~/` (shipped later to `Resources/Attacks/<World>/` as `<w>_attack_<slot>.png`, read by `AttackArt.PathOf`).
Codex is rate-limited until Oct 13 2026 22:53: queue, do not launch before.

Pre-launch lint: search the finished prompt for the other worlds' nouns (Space prompt must not say ice / lava / leaf / water; Frost no lava / leaf / water;
Verdant no ice / flame; Ember no ice; Tide no lava / ice) and for "red" outside "no red".

## Common preamble (start EVERY prompt with this: the world-attacks preamble plus the ultimates rules)

```
You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait). Work ONLY inside this git worktree; do not
edit any .cs / .meta / existing file; do not delete anything; do not commit. New files go ONLY to <OUT> (generators, prompts,
candidates in its src~/ subfolder; the trailing ~ matters: Unity ignores it).
Read ./docs/art-style.md, ./docs/world-attacks-design.md (0.1 and the world's section), ./docs/world-attacks-art.md and ./docs/ultimates-art-brief.md (sections 1, 4: sizes binding).
WORLD: <WORLD>. MATERIAL: <MATERIAL>. BODY COLOURS: <BODY_RAMP>.
THIS IS A BOSS / ELITE "ULTIMATE": the biggest, most visible hostile thing on screen. It must read as a PROPER ANIMATION: a charge-up tell, a sustained loop, an impact and a fade -- never a flat rectangle.
THE HOSTILE CUE: <PINK> (hue 312-326: #FF4FD8, #FF8AE6, #FFE0F8) is the core / fringe of everything, >= 30% of the opaque pixels pink-family or white-hot; the body material is secondary.
NEVER red (hue 345-15; red is the player), never a saturated <AVOID>. Rich tonal detail: 5-7 tone ramp per material, painterly neon pixel art, NOT posterised flat colour.
Transparent background; no baked halo, matte fringe or glow box (alpha steps down to 0 smoothly; the game adds the keyline). >= 6 px of alpha <= 24 margin round the content of every cell.
Every sustained loop changes >= 3% of the opaque pixels from frame to frame (rolling energy, licking flame, sparks); frames of a loop are seamless (last flows into first).
Scale: 128 px = 1 world unit; the hit core is the bright middle third, the glow around it is purely visual.
METHOD (mandatory): use IMAGE GENERATION for the painted content: several candidates, inspect, pick the best, then pixel-clean (64 px native, x2 nearest-neighbour ONLY, no blur,
rich per-material ramps). Procedural-only results are rejected.
SCALE CHECK: downscale to the game size (beams ~40 px wide, flares ~60 px) and check the pink core and silhouette over a dark tile and a bright tile.
VERIFY programmatically (verify.py, print the table): exact sizes, RGBA, cell non-empty (except reserved), border alpha >= 6 px, pink share per cell, pixels within 20 deg of 178/82/259/37, red band = 0,
frame-to-frame difference >= 3% per loop step, x2-nearest round trip, vertical seamless check (body tiles), loop seam. Write preview.png (dark #0b0b1a at 2x + game size over two backdrop tiles) and preview.gif of every loop.
Finish with a short summary, the numbers, and honest caveats.
```

## Slots (new ones in bold)

| Slot | Meaning |
| --- | --- |
| `<WORLD>` `<w>` `<MATERIAL>` `<BODY_RAMP>` `<PINK>` `<AVOID>` `<REFS>` `<OUT>` | as in `world-attacks-codex-prompts.md` section 1; world values in its section 4 |
| **`<LASER>`** | what this world's boss beam is made of (brief 4.1 table, "Beam" + "Notes") |
| **`<ROOT>`** | what the muzzle of that boss looks like when it charges (the Archon's engine pod, the Leviathan's eye, the Queen's cannon, the Drake's brow gem, the Kraken's beak) |
| **`<FLOOR>`** | what the beam does where it ends (rail spark, frost spikes, acid splash, magma drip, splash crown) |

World value for Space (not in the older doc):

```
<WORLD>=Space  <w>=space  <OUT>=./Pause/Assets/Art/Attacks/Space~/
<MATERIAL>=neon plasma, ionised gas and arc-light of a capital carrier's engine pods; steel and brass hardware around it
<BODY_RAMP>=white-hot #FFF4FB core, hot pink #FF4FD8 / #FF8AE6, magenta #C02AA8, deep plum #5A1858, steel #B6BCD0 / #6E7488 / #2A2E44 only on hardware, ink #0B0B1A
<PINK>=the beam IS pink: #FFE0F8 centre, #FF8AE6 mid, #FF4FD8 body, #C02AA8 outer glow
<AVOID>=cyan 178, green 82, amber 37 (no orange sparks), violet 259 (keep the outer glow magenta 300-326, never blue-violet), any red
<REFS>=Pause/Assets/Art/Resources/Bosses/Space.png (cell 28-34 are the pod tells), Space_shots.png, a Space backdrop tile
```

## Queue (ordered; one agent at a time per job; each entry is launch-ready: preamble + TASK + verification list)

### U1. Space boss laser kit (the user's complaint) -- priority 1

```
TASK: three files in <OUT>:
(a) space_attack_laser.png, exactly 1024 x 384 RGBA, 8 x 3 cells of 128 x 128.
  Row 0 WINDUP 1-8: the engine pod's nozzle gathering energy over 0.95 s (12 fps): 1-2 a few pink sparks drawn in, 3-5 a pink-white core swells with orbiting arc motes, 6-7 overbright
  with a lens cross, 8 white-out (ignition). Content centred, growing from 12 px to 100 px across. The pod hardware itself is NOT drawn (the boss sprite shows it); only the energy.
  Row 1 MUZZLE a-d (4-frame loop, 12 fps, the bloom at the beam's root while firing: white-hot disc with rolling plasma, short crackling spikes, 80 px) then FADE 1-4 (the root collapsing to a point).
  Row 2 IMPACT 1-4 (the beam meeting a rail or the floor: a pink-white burst that grows to ~90% of the cell by frame 3 and dissolves by 4; sparks flying UP-and-out) then SPARK a-d (a 4-frame loop of sparks and plasma flecks spitting off a rail, 70 px).
(b) space_attack_laserbody.png, exactly 512 x 256 RGBA, 4 frames of 128 x 256 (12 fps loop). THE BEAM, vertical, root at the TOP, tileable vertically (top row of the cell continues the bottom row exactly) and
  stretchable. <LASER>: a white-hot core 16-20 px wide, a hot-pink body ~44 px, a soft magenta outer glow fading to alpha 0 by 80 px, arc-sparks crawling along the edges, rolling bright pulses travelling DOWN the core
  from frame to frame (>= 8% change), faint plasma strands. NOT a flat strip: the core must show tonal variation along its length. Content <= 100 px wide.
(c) space_attack_lasertell.png, exactly 512 x 128 RGBA: sight a,b (128 x 128 tileable aim line: a dotted line of pink motes, 4 px core, soft, a/b shifted half a period) and lock a,b (a 96 px blinking reticle: thin pink
  bracket corners + a centre cross, for the end of the aim line).
References (-i): <REFS>. Match the pink of the existing boss shots, but richer: this is the finale.
```

### U2-U5. Frost, Verdant, Ember, Tide boss laser kits -- priority 2 (same TASK as U1; replace the content lines by the world's, change only these)

```
TASK: same three files as U1 with <w>_ names (frost_attack_laser.png ... ). Differences: the energy is <LASER>; the windup gathers at <ROOT> (drawn as energy only); the impact is <FLOOR>.
Frost  : cryo ray -- white-lilac body, crystal sparkles along its edges, pink-white core, frost mist at the root, ice spikes at impact.            <ROOT>=the Leviathan's glaring eye (two beams: draw one)
Verdant: acid hose -- DARK leaf-green body (value <= .55), sap drips, bubbling edge, pink-white core, spray at impact; no lime.                   <ROOT>=a flank cannon mouth swelling
Ember  : fire laser -- white-hot core, orange body (hue 22-30), heat ripple edge, magma drips and ember sparks at the rail; no amber 37.            <ROOT>=the brow gem flaring
Tide   : pressure jet -- white-water body, spray beads, foam edge, pink-white core, splash crown at impact; never saturated cyan.                  <ROOT>=the beak opening round a bubble
Use the world's slot sheet from world-attacks-codex-prompts.md section 4 for <MATERIAL>, <BODY_RAMP>, <PINK>, <AVOID>, <REFS>.
```

### U6. Space themed set: ion arcs + scan line -- priority 5 (not fighting yet)

```
TASK: (a) space_attack_arc.png, exactly 768 x 384 RGBA. Row 0: bolt 1-6 (six 128 x 256 frames, 12 fps: a vertical LIGHTNING LANCE from the pod, root at the TOP centre, a zig-zag white-hot core with side forks,
re-rolled in shape each frame, pink body, magenta glow, 10-14 px core, content <= 100 px wide). Row 1 (128 px cells): nozzle 1-3 (growing flare through the tell) and tip 1-3 (arc sparks dying at the far end).
(b) space_attack_scan.png, exactly 512 x 480 RGBA: rows 0-3 four 512 x 96 SCAN-LINE bands (a horizontal neon scan bar: bright leading edge, pixel-grid noise, moire, pink core line 3 px, seamless left-right,
12 fps loop, hit thickness is the middle 51 px); row 4 (96 px cells): capL, capR, gapMarker a,b (an arrow-and-bracket glyph).
```

### U7. Strike / jet / ring tell + end frames (all worlds with themed hazards) -- priority 3

```
TASK: (a) <w>_attack_strike_fx.png, exactly 768 x 256 RGBA (two rows of six 128 x 128 cells): row 0 TELL 1-6 (the lane marker building over 1 s at 12 fps: the dotted ring closes, <MATERIAL> gathers -- mist / embers /
water motes / static -- and the last frame flashes white-pink); row 1 END 1-4 (the column's impact dissolving: debris, steam, spray, afterimage) + 2 reserved.
(b) <w>_attack_jet_fx.png, exactly 768 x 256 RGBA: row 0 NOZZLE 1-6 (the muzzle flare growing across the 0.9-1 s tell), row 1 END 1-4 (the jet tearing off at the nozzle and shredding to sparks) + 2 reserved.
Worlds: Frost (icicle drop, cold-blast ring: add frost_attack_ring_fx.png 1024 x 256: ring TELL 1-4 + ring FADE 1-4 of the 128 px snowflake glyph), Ember (flame sweep jet, eruption strike), Tide (water jet, thunder strike), Space (ion lance = U6).
```

### U8. Elite signature kits -- priority 4 (rift rail, ward curtain, floe cast, frost bloom first)

```
TASK (Space): space_attack_rail.png 1024 x 384: sight a,b (128 aim line, tileable) | slug a,b (railgun slug: a hot pink-white lance head with a 3-frame sonic ring around it) | kick 1-4 (recoil flash at the muzzle) |
trail 1-4 (the rail of light left in the lane: four 128 x 384 vertical frames, tileable, fading downward, 12 fps).   space_attack_curtain.png 1024 x 256: slab a,b (128 x 96 neon shield slab, tileable left-right) | capL | capR | gap a,b (arrow glyph) | spark 1-2.
space_attack_sling.png 1024 x 256: orb a-d (a spiked pink gravity orb, 4-frame spin) | ring 1-4 (the closing gravity ring, 4 frames, 240 px content) | lens a,b (space-warp streaks).   space_attack_crescent.png 1024 x 128: shard a-d (crescent blade, 4-frame spin) | lock a,b | puff a,b.
TASK (Frost): frost_attack_sweep.png 1024 x 384: stream 1-6 (a sweeping bolt-stream skin, 128 x 256, 6 frames) | ram 1-4 (two-shard spray) | plate 1-4 (an armour plate shattering) | charge a,b (an enraged dash trail 128 x 256).
TASK (Ember): ember_attack_siege.png 1024 x 384: muzzle 1-4 (cannon blast) | shell a-d (fireball with a long tail) | crater 1-4 (impact) | heat 1-4 (dash heat trail, 128 x 256 tile) | dive a,b (claw-dive streaks).
TASK (Verdant): verdant_attack_resin.png 1024 x 256: glob a-d (amber-brown sap, sat <= .6, pink glint, wobble) | splat 1-4 | pool a-d (sticky pool 1 u across, bubbling loop).
Each file is its own job; use the world's slot sheet. Hostile shapes are pointed or angular, never a smooth disc.
```

## Verification list (paste at the end of every prompt)

```
verify.py prints per file: size OK / RGBA / reserved cells empty / border alpha >= 6 px OK / pink share per cell (>= 30%) / hue audit (20 deg of 178,82,259,37; red band = 0) /
loop frame difference >= 3% per step / x2 round trip / vertical-seamless (laserbody, trail) / loop seam (every loop) / content width limits (beam <= 100 px).
```

## What to look at when you critique

* Does the windup visibly GATHER (not just appear)? Is the last frame a clear ignition?
* Does the beam body show tonal variation along its length, a bright core, a soft glow and moving energy -- or is it a flat strip?
* Does the impact have a start, a peak and a fade? Do sparks leave the rail?
* Pink first, material second, at game size over a dark and a bright tile; no red anywhere.
