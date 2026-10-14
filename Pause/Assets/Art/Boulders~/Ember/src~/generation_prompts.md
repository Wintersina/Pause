# Ember boulder image-generation prompts

The final paintings were made with the built-in image-generation tool at
1254 × 1254 RGBA. `candidate_B0.png` supplies the spire,
`candidate_B1.png` the slab, and `candidate_A2.png` the columns. Other
candidate paintings are retained in this folder.

Shared direction: one high-resolution transparent game sprite viewed from an
oblique high three-quarter angle, matching a near-black volcanic pixel-art
backdrop. A huge solid cover stone with a broad face and natural unoutlined
silhouette. Dark warm basalt (`#14120F` to `#3A3026`), cooled slag, black
obsidian facets, sparse thin old-gold amber (`#D8962C`) veins, soot and a
little embedded metal. Upper-left light, bottom-right shadow, fine hand-placed
looking grain and stepped painterly planes, muted atmospheric contrast. Rock
occupies about 80% of the square canvas with a fading ash and slag base.
Transparent empty margins. No outline, halo, pale edge, red, pink, magenta,
purple, UI, text, weapon, hazard, or scenery.

- Spire: “A giant wide-based obsidian spire with two blunt split tips, deep
  vertical flutes, one broad continuous opaque front mass, stepped caldera
  shelves at the base.”
- Slab: “A giant lava-veined slab: thick asymmetric monolithic volcanic plate,
  broad upright trapezoid silhouette, flat chipped oblique top and vast dark
  face, only a few delicate amber lines.”
- Columns: “A basalt column cluster: five fused chunky hexagonal black-basalt
  columns of staggered heights, a large continuous broad footprint and
  interlocking stone at the base, each column with an oblique visible top.”

`compose.py` grades those paintings into the backdrop's range, cuts the
destruction chunks from their actual painted pixels at source resolution, then
downsamples into the 768 px cells.
