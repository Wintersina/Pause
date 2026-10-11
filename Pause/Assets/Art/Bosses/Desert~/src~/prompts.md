# Desert boss image generation

Built-in image generation, transparent background. The painted master is `candidate_c.png`; `candidate_a.png` and `candidate_b.png` were alternate hulls. `death_key.png` supplies painted wreckage and shockwave detail. All four are kept here as generated source. The build script uses nearest-neighbour resampling and pointwise colour correction; it does not blur or quantise the images.

## Hull candidate A

One isolated feral bucket-wheel excavator boss, top-down with slight front elevation, faces down. Broad riveted oxidised-copper chassis, paired crawler skirts, front rotary worm-maw wheel with bone buckets and tiny sun-white throat, segmented rear lens dish, articulated right drill boom and vent stack. Rugged detailed industrial pixel art on transparent background. Matte copper, pale desaturated bone-gold, dark indigo recesses, sun-white light only. No red, pink, violet, cyan glow, green fields, orange or amber glow, fire, scenery, or text.

## Hull candidate B

Same boss, with a low mirror-petal array and a more distinct lattice drill boom. Compact boss-wide silhouette, 1 px selective outlines, scratches, rivets, five-tone ramps, dark structural base and transparent padding. Same palette exclusions.

## Hull candidate C (selected)

Same boss, with a huge upper dish and front bucket wheel, right articulated boom, flank crawlers, bone-pale scoop teeth and tiny sun-white lens and throat. Rich detailed rugged game pixel art, matte copper under 60% value, bone-gold at low saturation, indigo only in deep recesses. No background or text. Selected because all three firing parts remain distinct after scaling to a 384 px cell.

## Death key

The excavator in a white-gold core overload, shattered lens, exposed bucket wheel, blown riveted plates, sand shockwave arcs, chunky debris. White-hot #FFF6DC core and #FFF1C8 rays only; matte copper and pale dust. Transparent background, effects taper inside square. No orange, amber, red, pink, violet or cyan flame.

## Shot key

Two separate transparent projectile paintings: a fat industrial sand-slug of bone-gold grit, ochre bands and sun-white seam, and a long fractured pale glass shard with a white reflected centre. Both point down. Detailed rugged pixel art with hard tonal ramps and selective dark outline; pale light only, no forbidden coloured glow. The lane, beam and charge shapes are composed in code around these painted projectile keys.
