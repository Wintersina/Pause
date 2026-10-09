# Verdant run C painted sources

Generated with the built-in image generation tool, then copied into this
directory as five retained key sheets. Each source used a genuine transparent
background and a 2×2 layout. `build_c.py` crops the painted quadrants,
point-scales them, maps them into short material palettes, and adds cyclic
nearest-neighbour flow between keys. The exported atlases are not solely
procedural drawings.

| Selected source | Painted key request |
| --- | --- |
| `wildfire_keys.png` | Four overhead jungle wildfire poses: two long burn-edge flame lines and two round burning patches, gold cores, amber tongues, dark char, flying sparks; no vivid red. |
| `smoke_keys.png` | Two thick brown-grey industrial stack plumes with ember-lit bases and two thin white-grey refinery steam columns, all drifting and curling. |
| `firesmoke_keys.png` | Two huge black-brown wildfire columns widening upward from glowing bases and two thinner wind-leaning grey smoke ribbons. |
| `leaks_keys.png` | Corroded copper pipe flange venting white-green steam; burst pipe jetting green-gold sap; sparse orange embers; luminous lime spore puff from a plant pod. |
| `lights_keys.png` | Painted rusted lime and magenta beacon masts, a refinery wall with six amber/lime windows, and a white warning strobe accompanied by lime fireflies. |

Common prompt constraints: crisp hand-painted pixel clusters, limited Verdant
palette, overhead 2D game view, separated silhouettes with generous margins,
no labels, no grid lines, no blur or smooth gradients, no space imagery. The
full-size source PNGs preserve the generated painted keys for later revision.

Light states were corrected after extraction: the two beacons and white
strobe expose the painted bright halo at the flash and dim it at rest; the
window mask selects different painted windows per frame. Measured
alpha-weighted sums are printed by `verify_c.py` and on `preview_c.png`.
