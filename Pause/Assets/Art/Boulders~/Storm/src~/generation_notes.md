# Storm cover-boulder generation

Mode: built-in image generation with transparent background. The selected source paintings are `thunderhead_source.png`, `hail_source.png` (the refinery island), and `island_source.png` (the hail cluster). The first candidate for each shape was inspected but not selected. `burst_source.png` is the generated storm flash. `build_storm.py` composes and pixel-cleans these sources; `build_04/` is the verified final candidate.

Selected variant prompt, common prefix:

> Use case: stylized-concept. Transparent isolated 2D game art. SECOND CANDIDATE for a Storm cover boulder in a portrait shooter. High oblique 3/4 top-down camera. Broad solid hiding-place silhouette; near 80% of canvas width. Painted at highest available native resolution with rich pixel-level detail and hand-placed-looking pixel texture, never flat or vector. Match a dark monochrome swirled cumulonimbus backdrop, graphite #15171C to muted slate #8A909C. Stay mostly in #252932 through #626977. 4-6 tone painterly ramps, shaded bottom-right, lit upper-left. Rugged hail-grey slate, pitted gunmetal, mineral crust, wind-carved ridges. Pixel-hard, dithered dark cloud skirt wrapping underside, fading to transparency. Natural unoutlined edges and no rim highlight, no halo, no cast shadow, no ground plane. Intact boulder, no luminous cracks. Absolutely no red, magenta, pink, violet, orange, amber, cyan, green, UI, text, weapons, thin spikes, circular mine shape.

Variant suffixes:

1. `Variant A: massive thunderhead rock; single anvil-shaped flat crown with a long broken slate overhang, thick downward craggy body. More cumulonimbus flow in the stone, visually related to storm-cloud backdrop.`
2. `Variant B: storm island, massive rough broad slate slab with a squat damaged refinery tower stump and sparse black railing, cloudbank clasping its base. The tower is tiny next to the wide rock body.`
3. `Variant C: exactly three fused huge pitted hailstones; near-round but irregular and wind-carved, overlapping into one heavy wide mass with a broad front face and a cloud skirt.`

Flash prompt:

> Use case: stylized-concept. Asset type: isolated transparent effects source for a pixel-art Storm boulder destruction animation. A compact electric storm-white lightning fracture burst, jagged branching white #F2F6FF forks with only tiny pale-yellow #FFF3A0 cores, several small detached slate-grey chips and puffs of dark graphite storm cloud. High resolution and very detailed, pixel-hard dithered edges, hand-placed-looking pixel-art texture. The flash branches outward from the center; no circular ring, no explosion fireball, no solid rock object, no background, no ground plane. Dark subdued monochrome graphite and blue-grey debris; one bright note only in the flash. No orange, red, magenta, pink, violet, cyan, green, text, UI, border, halo, or soft blur. Centered with large transparent margin.

The local verifier writes `manifest.json` although the art brief calls it `boulder.json`; the final `boulder.json` is an unedited copy of the verifier's manifest.
