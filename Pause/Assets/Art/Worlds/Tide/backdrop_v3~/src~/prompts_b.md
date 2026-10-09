# Tide Run B painted source notes

All five atlas families were painted with the built-in image-generation tool on
transparent backgrounds. The reference image was `run_b_reference_montage.png`:
Tide ground v1–v4, the approved Tide Planetfall limb and cloud deck, and Ember
and Verdant Backdrop3 tile and atlas samples. Each family got an initial sheet
(`candidate_b_*_a.png`) and a second distinct candidate (`*_b.png`).

Shared direction in both calls: straight-down orthographic view from low
atmosphere; a drowned industrial ocean planet; dark abyssal teal, desaturated
rust, small mint bioluminescent and blue-violet accents; dense crisp rugged
steampunk/cyberpunk pixel detail, hard tonal ramps and rim light; transparent
separate objects, small contact foam, broad clear grid gutters. Explicitly
exclude red, orange/lava, lime, large cyan fields, magenta, stars, planets,
space imagery, horizons, lettering, grids and blur.

| Sheet | Row-major first-candidate subject prompt | Selection |
|---|---|---|
| landmarks | Three different oil rigs; two refinery rigs; crane gantry; tanker; two trawler barges; lighthouse; two sunken city crown clusters; pump platform; wind turbine rig; mooring buoys; bridge on pilings. Visible stack mouths, no painted smoke. | A except city clusters 10–11 from B for smaller water bases. |
| pipes | Horizontal and vertical straights; two bends; tee; cross; two manifolds; two pipe bridges on pilings; pump house; two ruptured seam leaks; barnacled intake; two tangles. Visible connectors and leak emitter bases. | B cells 2, 3, 8, 9, 13; A otherwise. |
| fires | Two whirlpools, two oil slicks, two reef heads, three partly submerged wreck hulls, two bubbling vents, two flare stacks with open mouths, two plankton blooms, salvage crawler. No lava or orange fire. | B cells 2, 3, 6, 7, 8, 15; A otherwise. |
| weather | Four broad storm ceiling cloud banks matching Tide cloud deck; four wisps; three sea mists; three salt-spray gusts; one smoke pall and rain. Low-alpha slate grey-teal with mint-lit crests. | B cells 1, 3, 5, 7, 10, 12, 14; A otherwise. |
| sites | Matched closed/open pairs for trench hatch, rig bay, reef dock, vent stack and wreck bay; navigation lamps off/on. First three source rows filled, last blank. Deep centred opening in each open state. | B for all 12. |

The second-candidate prompts retained the same ordered subjects and explicitly
requested varied structures, angles, wear and silhouettes. Each painted cell
was then independently cropped, nearest-neighbour resized and remapped to a
small Tide palette by `build_b.py`. `verify_b.py` checks the output contract.

The weather subject list in the request totals **17** names but the specified
1024×1024 atlas has **16** non-overlapping 256×256 cells. The current sheet
keeps `rain_00` and omits `pall_01`; the second painted pall candidate remains
in `candidate_b_weather_b.png` if a different selection is requested.
