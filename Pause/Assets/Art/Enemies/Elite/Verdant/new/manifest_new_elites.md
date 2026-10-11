# Verdant elite additions — painted art handoff

Four new top-down elites for Verdant's hollow-log hangar, vine-tower dock,
spore silo and root bunker. The existing Resin Warden is the fifth row of
`contact_sheet.png` for fleet comparison. The four new rows are Timber Hauler,
Thornlash, Sporebloom and Leafblade, in that order.

Each `verdant_elite_<name>.png` is a transparent RGBA flight strip, 1344 × 192
pixels, seven 192 × 192 cells in left-to-right order. Every
`verdant_elite_<name>_death.png` is a 576 × 192 RGBA strip of three 192 × 192
cells. `preview.gif` loops flight cells 3, 4, 3, 5 for all four ships.

| Flight cell | Pose | Painted moving parts and light |
| --- | --- | --- |
| 0 | Landed | Main silhouette held; grabbers, legs, petals or blades parked; no exhaust. |
| 1 | Grounded idle | Same anchor; restrained crystal and nozzle glow; appendages begin to loosen. |
| 2 | Lift-off ignition | Bright core and longer emerald/amber thruster plume; appendages open. |
| 3 | Airborne straight hover | Short steady exhaust, active core; limbs or shutters flex to the hover pose. |
| 4 | Bank left | Slight body bank plus left-biased grabber, leg, petal or blade articulation. |
| 5 | Bank right | Mirrored bank action and exhaust sway. |
| 6 | Second-heart damaged | Cracked emerald seam, brass sparks, erratic appendages and reduced glow. |

| Elite / site / attack read | Hover engine nozzles `(x,y)` | Hover weapon muzzles `(x,y)` | Hover alpha bbox `(left,top,right,bottom)` / size |
| --- | --- | --- | --- |
| **Timber Hauler** / hollow-log hangar / roller-drum trunk throw | `(73,136)`, `(96,146)`, `(118,136)` | Grabber/drum ports `(62,65)`, `(130,65)` | `(32,42,160,155)` / **128 × 113** |
| **Thornlash** / vine-tower dock / telegraphed whip sweep | `(86,134)`, `(106,134)` | Coiled whip roots `(67,68)`, `(125,68)` | `(32,27,160,165)` / **128 × 138** |
| **Sporebloom** / spore silo / fanned spore burst | `(82,158)`, `(110,158)` | Side pollen vents `(49,89)`, `(143,89)`; central pod `(96,68)` | `(42,27,150,167)` / **108 × 140** |
| **Leafblade** / root bunker / dive and crescent slash | `(87,158)`, `(105,158)` | Nose `(96,29)`; blade tips near `(50,120)`, `(142,120)` | `(44,25,148,167)` / **104 × 142** |

Coordinates are local to one 192 × 192 cell, with `(0,0)` at its upper-left.
The nozzle and muzzle positions are approximate placement guides, not
definition JSON values. The hover bbox includes visible exhaust where present.

| Death cell | Beat |
| --- | --- |
| 0 | Emerald-white core flash inside the intact painted hull. |
| 1 | Armor ruptures into four large material planes with resin/brass sparks and sampled bark/metal chips. |
| 2 | Planes separate farther and shed more chips in each ship's own painted materials. |

## Measurement and source notes

| Elite | Smallest flight margin | Bank centroid drift from hover, left / right `(dx,dy)` | Fewest RGB colours in any flight cell | Hover red-band share | Hover hue shares near 82° / 178° / 259° / 37° |
| --- | ---: | --- | ---: | ---: | --- |
| Timber Hauler | 30 px | `(-0.11,+0.19)` / `(+0.11,+0.22)` px | 7,013 | 0.00% | 9.04% / 0.58% / 0.00% / 40.31% |
| Thornlash | 24 px | `(-0.05,+0.26)` / `(+0.35,+0.01)` px | 5,048 | 0.00% | 18.67% / 0.00% / 0.00% / 44.89% |
| Sporebloom | 18 px | `(-0.67,+0.15)` / `(+0.62,+0.13)` px | 8,208 | 0.00% | 8.28% / 0.04% / 0.00% / 38.43% |
| Leafblade | 18 px | `(-0.00,+0.68)` / `(+0.00,+0.72)` px | 6,021 | 0.00% | 8.41% / 0.00% / 0.00% / 36.03% |

Measurements use alpha > 15. Hue shares use visible chromatic texels
(HSV saturation ≥ 0.2, value ≥ 0.08) and ±20° of each named hue. Red-band
share uses visible texels with saturation > 0.5 and hue in 345°–15°. All 28
flight cells and 12 death cells have **zero** nontransparent pixels on the cell
border; the smallest death-cell margin is **17 px**. Saturated 62°–102° lime
occupies at most **0.23%** of hover texels after emerald correction.

The four full-resolution painted image-generation masters and the build script
are in `src~/`. Concept PNGs retain the full painted masters with the same
Verdant hue correction. Playable frames use only nearest-neighbour reduction
and nearest-neighbour rotation of painted appendages. The death flashes,
cracks and scattered debris are composited from those painted ships. These are
art assets only; no Unity elite definitions or attack behaviour are included.
