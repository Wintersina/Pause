# Tide elite lights, launch sites, and damaged loops

This is an art-only staging folder. All PNGs are RGBA; the launch and review
assets are 768 px cells, while Tide elite strips use the installed 384 px cells.
The launch pads match the existing Tide site atlas scale and palette: Tide teal,
sea-blue, foam white, rust, brass, and restrained amber warning lamps. Launch
cells 2–5 include only faint, near-opening sea mist/steam in the revealed bays;
there are no launch flashes, flares, halos, or bursts.

## Source strip mapping

All five source strips are 3840 × 384 px, ten 384 × 384 cells. Their frame order
is `0 parked / engines off → 1 parked idle → 2 liftoff ignition → 3 straight
flight → 4 bank left → 5 bank right → 6 damaged hull → 7–9 flight loop`. The
defs have `flight: [3,7,8,9]`, `parked: 0`, `parkedIdle: 1`, `liftoff: 2`,
`damaged: 6`, and no separate tell/action/hit cells (`-1`).

| Overlay | Size / cells | Preview | GIF frames |
|---|---:|---|---|
| `tide_elite_riptide_lights.png` | 3840 × 384; 10 × 384 | `tide_elite_riptide_preview_lights.png` | 3,4,5,7,8,9 |
| `tide_elite_bathyscaphe_lights.png` | 3840 × 384; 10 × 384 | `tide_elite_bathyscaphe_preview_lights.png` | 3,4,5,7,8,9 |
| `tide_elite_trawler_lights.png` | 3840 × 384; 10 × 384 | `tide_elite_trawler_preview_lights.png` | 3,4,5,7,8,9 |
| `tide_elite_thunderfin_lights.png` | 3840 × 384; 10 × 384 | `tide_elite_thunderfin_preview_lights.png` | 3,4,5,7,8,9 |
| `tide_elite_pearl_warden_lights.png` | 3840 × 384; 10 × 384 | `tide_elite_pearl_warden_preview_lights.png` | 3,4,5,7,8,9 |

The light overlays are transparent and pixel-aligned to their source strips.
Parked cells are dim; flight cells chase across hull accent points; damaged
cell 6 is flickering and partly dead. The review GIFs are six frames at 130 ms.

## New Tide launch sites

Each base is 768 × 768. Each corrected launch strip is 4608 × 768 (six
768 × 768 cells). Cell order is exactly `0 closed → 1 closed + one small amber
warning lamp → 2 ~45% open → 3 ~80% open → 4 fully open, shaded bay interior
and depth → 5 ~50% open`. Cells 0 and 1 are exact copies of
the closed base (with only the lamp added in cell 1); cells 2, 3, and 5 move
the exact source panel pixels from cell 0, clipped beneath the door frame.
Opening cells use matching Tide rust, brass, teal-glass, foam, and sea-blue shading with
seams, rivets, edge highlights, water glint, and recessed depth details. Cells 2–5
also have 2–4 frame-varying, semi-transparent foam-white/teal-grey pixel mist
wisps rising from the bay; it is faint in cell 2, fuller in cells 3–4, and thins
in cell 5. Outside the documented door region and mist pixels, the base is copied
pixel-identically into every cell.

| Base / strip | Pad pixel `(x,y)` | Door mechanism / region |
|---|---:|---|
| `floating_carrier_pad.png` / `floating_carrier_pad_launch_v2.png` | `(384,376)`; x `0.500` | same base-painted riveted/hazard deck doors slide apart around `(384,376)`; lamp cue `(526,256)` |
| `lighthouse_silo.png` / `lighthouse_silo_launch_v2.png` | `(384,369)`; x `0.500` | base-painted dome iris splits into petals around `(384,369)` to reveal a broad shaded round shaft; lamp cue `(502,257)` |
| `submarine_pen.png` / `submarine_pen_launch_v2.png` | `(384,430)`; x `0.500` | `submarine_pen_closed.png` adds heavy riveted concrete-and-steel sea gates with seams and hazard paint; those exact gate pixels slide apart over the water channel; lamp cue `(534,336)` |
| `wreck_hull_bay.png` / `wreck_hull_bay_launch_v2.png` | `(384,430)`; x `0.500` | same base-painted cargo-bay doors split around `(384,430)`; lamp cue `(514,309)` |

## Damaged burn loops

`damaged/` contains one six-cell 2304 × 384 strip per elite, its transparent
light overlay, a composited preview strip, a six-frame 384 × 384 GIF, and the
combined `damaged/contact_sheet.png`. Each damaged strip restores the painted
engine bells from parked cell 0, then draws only weak ragged Tide-colour flames;
each engine uses an independent phase and the hull is unchanged across the six
loop frames.

Files:

- `damaged/tide_elite_riptide_damaged.png`, `_damaged_lights.png`, `_damaged_preview.png`, `_damaged.gif`
- `damaged/tide_elite_bathyscaphe_damaged.png`, `_damaged_lights.png`, `_damaged_preview.png`, `_damaged.gif`
- `damaged/tide_elite_trawler_damaged.png`, `_damaged_lights.png`, `_damaged_preview.png`, `_damaged.gif`
- `damaged/tide_elite_thunderfin_damaged.png`, `_damaged_lights.png`, `_damaged_preview.png`, `_damaged.gif`
- `damaged/tide_elite_pearl_warden_damaged.png`, `_damaged_lights.png`, `_damaged_preview.png`, `_damaged.gif`
- `damaged/contact_sheet.png` (three columns by two rows, six frames per ship)

## Builders and review

- `build_art.py` builds the five hull-light overlays and the final
  `contact_sheet.png` (four sites plus five lit ships).
- `build_launch_v2.py` builds the four corrected six-cell launch strips and
  `launch_v2_contact_sheet.png`; it also writes `submarine_pen_closed.png` and
  asserts six 768 × 768 cells, dimensions, no pink pixels, exact shifted
  panel-pixel copies for cells 2, 3, and 5 outside the mist mask, and
  non-decreasing cell-0 pixel differences through cells 2 → 3 → 4 with cell 4 largest.
- `build_damaged.py` builds the damaged loops, overlays, previews, GIFs, and
  `damaged/contact_sheet.png`.
- `contact_sheet.png` is 1440 × 900 and contains all four sites plus one lit
  flight cell for every Tide elite.

The generated site bases were normalized from transparent-background image
generation into this folder before deterministic door compositing. A pixel
validation pass found no pink/magenta pixels in the new site, overlay, damaged,
or contact-sheet outputs.
