# Desert cover boulder source notes

Painted candidates were generated with the built-in image generation tool on
transparent backgrounds. The selected intact paintings are `mesa_a.png`,
`arch_b.png`, and `butte_b.png`; the matching generated breakup studies are
`mesa_burst.png`, `arch_burst.png`, and `butte_burst.png`. The other `*_a.png` or
`*_b.png` files in this folder are the inspected alternatives.

The final intact prompts requested, respectively:

1. A broad flat-topped canyon mesa with cracked rust-brown caprock, dark
   vertically stratified sandstone cliffs, mining bores, talus and sand,
   viewed from high oblique three-quarter top-down.
2. A heavy natural sandstone arch with a wide dark opening, a broad horizontal
   cap and thick piers, layered wind-scoured rock and fading sand/talus.
3. A rounded strip-mined butte with a broad cap, stepped terraces, rusted
   scaffold remains, drill scars and a fading talus skirt.

All prompts specified fine painterly pixel texture, muted Desert ochre/khaki
and umber, upper-left light, transparent isolation, and no outline, halo,
ground plane, red, pink, UI or weapon cues. Each breakup edit prompt asked the
image generator to split its matching selected painting into seven large
textured slabs with sun-gold seams. `assemble.py` downscales only, grades the
paintings toward the reference landmarks, cuts frame chunks from the selected
intact pixels, and uses the generated breakup studies to modulate the seam and
dust painting. The final sheets were verified at a 768 px cell size.
