# Tide rail mine beam source paintings

Three paintings were generated with the built-in image generation tool. The
reference Tide mine was used for pixel-art finish and world context. The Ember
atlas was used only for the cell map and quality benchmark.

## `candidate_jets.png`

Six independent tall upward pressurised water-gun jets on transparency.
Different pressure bands, foamy folds, ragged lateral spray, and bubbles in
each drawing. Dense white-water core, pale aqua-blue body, ocean-navy outline,
spiky neon-pink hostile rim and pink-white glints. Hard-edged 1990s arcade
pixel clusters. No flames, lasers, text, interface, background, or gradients.

## `candidate_splashes.png`

Seven independent water effects on transparency: four growing triangular
muzzle blast fans with ring ripples, then three far-rail splash crowns fading
into falling droplets. Asymmetric white foam, aqua-blue shadows, dark contour,
pink rim and glints. Crisp hand-placed-looking pixel clusters.

## `candidate_pilots.png`

Four growing water pressure bulbs on transparency: tiny bead, swelling bead,
tall rippled teardrop, unstable bulb with foam and bubbles. Dark ocean contour,
pale blue water, white highlights, spiky pink rim and glints.

## Assembly

`build_minebeam.py` crops distinct paintings, thresholds weak generated alpha,
maps their painted values to a seven-step Tide water ramp and pink ramp, then
places native 64 px cells. The final atlas is exported once with x2 NEAREST.
The beam's top spray caps and painted nozzle hardware were trimmed so the
vertical body can stretch across a game lane. Haze uses the painted splash
source at low opacity. Aim droplets and a few cast-off spray pixels are small
deterministic additions around the paintings.
