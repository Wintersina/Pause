# Storm enemy painted sources

All eleven candidate sheets were made with the built-in image-generation tool on
transparent backgrounds. Each sheet contains three separated concepts for one
enemy. The selected column is zero based. `build.py` crops the selected painting,
samples it only with nearest-neighbour, locally moves the named anatomy for idle
poses, and adds small storm-white tell, discharge, and hit marks. No existing
sprite or Unity metadata was used as a paint source.

## Common image-generation instructions

> Three different complete candidates, side by side on a genuinely transparent
> canvas, one isolated top-down orthographic sprite per column, facing downward,
> no labels or scenery. Detailed crisp-edged painterly pixel art for a phone
> portrait 2D shooter. 1 px selective dark outline, 2 px pale cold upper-left
> rim, dense rich 4–6 tone ramps per material, fine rivets, scratches and hail
> pitting. Weathered graphite, blue-slate hue 212–230, muted teal-grey metal,
> pale hail-grey wear. Storm-white #F2F6FF cores/eyes, tiny pale-yellow #FFF3A0
> lightning edges, sparse cold white-blue arcs. Strong separate silhouettes,
> complete sprites with generous transparent margins. No red, orange or amber
> glow, cyan fields, green, lime, mint, violet, magenta, brown-brass dominant
> hull, blur, background, interface, text, or shadows. Three concepts of the
> same subject, not animation frames.

The first fighter sheet was generated with the same content using the phrase
"three distinct complete candidates"; the next heavy/alien/drone batch used
"detailed crisp-edged painterly pixel art". The first Tempest Anvil generation
read as a narrow side view, so its candidate sheet was regenerated with an
explicit broad anvil and two coils.

| Sheet | Selected | Subject instruction |
| --- | ---: | --- |
| `storm_alien_candidates.png` | 1 | Static Mote: spherical ball lightning trapped inside a small gunmetal cage of curved rods; brilliantly white-hot plasma sphere at center edged pale yellow, short white-blue arcs joining rods. Fragile floating orb, not a ship. |
| `storm_chaser_candidates.png` | 1 | Cyclone Drone: compact central slate pod with one bright eye lens and four large, separate, strongly curved mechanical rotor blades around it; bolts, sharp blade ends, whirling wind-streak structure. |
| `storm_big_candidates.png` | 1 | Cloud Whale: huge armoured mechanical whale airship seen from above; riveted envelope of overlapping plates, large tail fin at top, side fins, rows of ballast gills, tiny cockpit lamp and downward arc-cannon snout. |
| `storm_fighter_1_candidates.png` | 0 | Stormray Pup: small manta drone with broad swept triangular jointed wings, short stinger tail, one bright storm-white eye and riveted slate plates. |
| `storm_fighter_2_candidates.png` | 1 | Stormray: larger wide manta gunship with ribbed swept wings, long thin tail, twin wing guns, sturdy layered gunmetal and a bright white eye. |
| `storm_fighter_3_candidates.png` | 1 | Thunderhead: chunky boxy gunship, big dorsal cloud-seed cannon drum, paired vent stacks, broad armour, storm-white charging nodes. |
| `storm_fighter_4_candidates.png` | 1 | Tempest Anvil: huge broad anvil-shaped dreadnought, squared slab shoulders, two large Tesla coils as left and right horns on pylons, heavy central prow and white-hot coil tips. |
| `storm_rock_hail_candidates.png` | 1 | Hailstone: irregular spherical hail-grey ore boulder, icy frost cracks, dark pitted craters, chipped facets and one small cold glint. |
| `storm_rock_ore_candidates.png` | 0 | Ore Shard: long jagged grey-blue crystal blade, uneven taper, bright frosted leading edge, translucent slate facets and hail pitting. |
| `storm_rock_slag_candidates.png` | 1 | Anvil Chunk: asymmetric blunt dark iron slag lump, charcoal mineral layers, scuffed edges and a small pale glint. |
| `storm_rock_islet_candidates.png` | 1 | Cloud Islet: upright floating storm-refinery fragment, jagged platform slab with small antenna and work lamp, hanging underside struts and lateral windswept vapour trails. |

`storm_big` is widened 1.3× at nearest-neighbour sampling to read as a heavy
envelope in its 256 px cell. The selected paint retains its fine tone variation.
