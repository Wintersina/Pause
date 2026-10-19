# Frost elite lights and new launch sites

Staging folder: `new_docks_lights~` (the trailing `~` keeps Unity from importing
these review assets until they are wired). All PNGs are RGBA; overlays are
transparent and additive, and all art uses pale cyan / ice white / cold steel
with restrained amber warning lamps. There are no space objects and no pink.

## Hull-light overlays

The five source strips are the existing `frost_elite_<name>.png` files, each
3840 × 384 px: ten pixel-aligned 384 × 384 cells. The source frame order used
by this pass is:

`0 parked / engines off → 1 grounded idle → 2 lift-off ignition → 3–8 flight
and burn loop (straight/bank/hold poses) → 9 second-heart damaged flight hull`.

| File | Size / cells | Review output |
| --- | --- | --- |
| `frost_elite_rimebreaker_lights.png` | 3840 × 384; 10 × 384 | `frost_elite_rimebreaker_preview_lights.png`, `frost_elite_rimebreaker_lights.gif` |
| `frost_elite_floe_harrower_lights.png` | 3840 × 384; 10 × 384 | `frost_elite_floe_harrower_preview_lights.png`, `frost_elite_floe_harrower_lights.gif` |
| `frost_elite_cryo_siren_lights.png` | 3840 × 384; 10 × 384 | `frost_elite_cryo_siren_preview_lights.png`, `frost_elite_cryo_siren_lights.gif` |
| `frost_elite_glacier_tender_lights.png` | 3840 × 384; 10 × 384 | `frost_elite_glacier_tender_preview_lights.png`, `frost_elite_glacier_tender_lights.gif` |
| `frost_elite_whiteout_sentinel_lights.png` | 3840 × 384; 10 × 384 | `frost_elite_whiteout_sentinel_preview_lights.png`, `frost_elite_whiteout_sentinel_lights.gif` |

The overlay derives points from each ship's own bright accent pixels. Cores are
1–4 px with a 1–2 px hard-edged glow. Parked/grounded cells are dim, ignition
and burn cells pulse/chase across nav lights and emitters, and cell 9 is
irregular and partly dead. The GIFs show the six burn cells in order, looped at
130 ms per frame. `build_art.py` is the deterministic source.

## New Frost launch sites

Each base is a transparent top-down HD pixel-art set piece, normalized to a
maximum long side of 768 px. Each launch strip has six same-size cells in this
exact order: `0 idle → 1 one small amber warning lamp blinks → 2 doors open →
3 open → 4 open hold → 5 closing`. There is no flash, flare, halo, or steam
burst. Pad points are pixel coordinates in the normalized base PNG where the
elite emerges.

| Base | Base size | Pad pixel | Launch strip |
| --- | ---: | ---: | --- |
| `ice_locked_offshore_rig.png` | 768 × 768 | (384, 384) | `ice_locked_offshore_rig_launch.png`, 4608 × 768; 6 × 768 |
| `cryo_silo.png` | 768 × 768 | (384, 399) | `cryo_silo_launch.png`, 4608 × 768; 6 × 768 |
| `glacier_cave_hangar.png` | 768 × 768 | (384, 336) | `glacier_cave_hangar_launch.png`, 4608 × 768; 6 × 768 |
| `icebreaker_deck.png` | 768 × 768 | (384, 395) | `icebreaker_deck_launch.png`, 4608 × 768; 6 × 768 |

Site concepts: offshore rig on stilts in a frozen lead; squat armored cryo
silo with sliding roof segments; glacier-ridge cave hangar with lamps; and a
large icebreaker trapped in the ice with a deck launch pad. The four sites are
intended to sit beside the existing Frost `hangar`, `rigbay`, `padring`,
`crawlerbay`, and `hatch` art in scale and density.

## Review

`contact_sheet.png` is the final review sheet: the four sites across the top
and one representative lit burn cell from each of the five ships below.
