# Verdant mine beam painting sources

All four `candidate_*.png` files were made with the built-in image generation tool on a transparent background. The prompts asked for hand-placed, crisp pixel clusters, a dark forest-green ink contour, emerald and jade living vines (hue 120–145), a pale mint-white energy core, hooked thorns with neon pink tips, leaves, spores, and no scenery or text. They explicitly excluded flame shapes, lime, yellow, cyan, player red, blur, and gradients.

- `candidate_braid_a.png`: a tall narrow isolated bio-laser with two or three intertwined thick vines in a helical spiral, exposing the bright energy column and flinging small spores outward.
- `candidate_braid_b.png`: an independent, more aggressive corkscrew phase with alternating diagonal crossings and stronger side thorns. The painting contained a faint transparent glow that the builder thresholds away.
- `candidate_burst.png`: a square fan of thorned tendrils erupting from a bottom-center root, with torn leaves, pale mint energy, and pink spores. Used for the muzzle burst, far-rail impact, and dim haze.
- `candidate_pod.png`: a swollen upright thorny seed pod with overlapping leaf armor and cracks revealing a mint-white glow. Used for the four charge frames.

`build_minebeam.py` thresholds source alpha, uses nearest-neighbor sampling and a fixed Verdant-safe palette, composes all cells at 64 px, then exports the atlas once at x2 nearest. The seed-spore aim line and a few particles are compositional accents; all main forms come from the generated paintings.
