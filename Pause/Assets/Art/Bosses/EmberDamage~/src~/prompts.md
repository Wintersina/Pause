# Ember damage generation notes

All candidates in this directory were generated with `ref_idle_cell.png` as the edit target or registration reference, with transparent background requested. `build.py` selects and composites them with nearest-neighbour sampling. The source PNGs are retained here for review.

## Body candidates

- `stage1_a`: Edit the transparent Ember boss cell as the same volcanic warship, keeping its centered pose and footprint. Redraw the upper right shoulder as cracked, scorched and dented; chip the brass rim, add blast scoring and one dim eye. Structural damage, crisp neon pixel art, basalt and brass, amber fractures, no smoke, lightning or background.
- `stage1_b`: Alternative stage 1. Preserve exact warship head silhouette, alignment and pixel art palette. Structural cracked and scorched wing shoulder, chipped brass support, dented plate, one dim eye, a few amber fractures. Damage obvious at half size. Transparent, no smoke, electricity or blur.
- `stage2_a`: Same pose and registration. Retain shoulder damage; add large jagged orange white magma cracks, a sheared exhaust stack, torn plating with visible machinery pipes and leaking magenta pressure tubes, broken fang and hanging brackets. No smoke or electricity.
- `stage2_b`: Alternative stage 2. Retain early shoulder damage. Sheared top exhaust, broad torn wing gap exposing black machinery, severed magenta tube, hanging brass bracket and glowing cracks spreading toward the furnace. Structural pixel damage on transparency.
- `stage3_a`: Dramatically wrecked Ember warship in the same footprint. Huge right hull breach exposing orange white magma reactor and mechanical ribs, missing plates, ripped side weapon cluster, hanging cables, shattered dark eye, broken chimney, extensive heat cracks. Recognizable face, transparent, no effects.
- `stage3_b`: Alternative stage 3. Left wing and cannon cluster torn and hanging on cables, molten core visible through hollow hull, missing front plates, broken chimney and jaw, dark eye. Exact pose and location, transparent pixel art.
- `stage4_a`: Near destroyed one heart warship in the original footprint. Both side clusters mangled, multiple deep breaches and skeletal ribs, molten core, hanging cables and slag, shattered eye, broken chimneys, half detached jaw, charred cracked plates, unstable furnace. No baked effects.
- `stage4_b`: Alternative stage 4. Asymmetric structural failure with both shoulders blown open, wing armor dangling by cables, machinery and magma exposed, busted stacks, shattered eye, crooked jaw and orange fissures. Same registered pose, transparent.

## FX candidates

- `smoke_a`: Transparent overlay only. Thick billowing sooty grey black smoke from three top/shoulder sources, near white heat steam, orange embers and glowing bases. Chunky hard edged pixel clusters; leave eyes and jaw clear. No body.
- `smoke_b`: Alternative smoke overlay only. Three heavy asymmetric coal dark smoke columns and pale hot steam, orange cinders and heat glow, hard pixel edges and true alpha. No body.
- `electric_a`: Transparent overlay only. Long white hot jagged arcs fringed molten orange and magenta between shoulder breaches and across upper hull, several spark bursts. Crisp pixel lines, no body or smoke.
- `electric_b`: Alternative electrical overlay only. Dense branching white hot arcs with amber and magenta fringes around shoulders and upper hull, spark pinwheels. Transparent, no body.

## Death candidates

- `death_b`: A single transition keyframe. The molten orange white core bursts from the torn face and wings; angular basalt and brass plates split outward in a smoky orange blast. Same boss identity, hard pixel edges, transparent.
- `death_a`: A catastrophic peak reference. White orange furnace fireball where the face and core were, dark basalt and brass hull chunks silhouetted around the blast, orange slag fragments. Rugged neon pixel art, transparent.
- `death_peak`: The boss fully obliterated. Rough white hot yellow orange fireball fills most of the cell, irregular flame lobes and dark angular hull chunks at the rim; no intact face, hard pixels, transparent.
- `death_ring`: Aftermath. Broken expanding orange shockwave ring, sooty grey smoke and glowing molten slag, mostly hollow transparent center, no intact ship.
- `death_ash`: Final aftermath. Fading grey wisps, sparse dim orange embers and settled hot basalt/brass chunks low in the cell, mostly empty transparent center and top.

The painted candidates are square AI outputs; `build.py` resizes with nearest neighbour and constrains the final body alpha to the pristine structural footprint plus six pixels. It adds small pixel corrections and loops the effects with local shifts and sparks.
