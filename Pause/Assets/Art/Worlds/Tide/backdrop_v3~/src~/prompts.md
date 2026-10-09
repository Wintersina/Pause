# Tide backdrop v3 image-generation prompts

Built-in image generation produced two painted candidates for each variant. Candidate numbers match `candidate_01.png` through `candidate_08.png`. The selected mid sources are 01, 03, 06, 07; selected far sources are 02, 04, 05, 08. Sky is a shifted and darkened version of the corresponding far repaint. All generated art and seam-repaint outputs are retained in `src~`.

## Original candidate prompts

### Candidate 01

Use case: stylized-concept. Asset type: painted source for vertical phone game tile. Straight-down orthographic aerial view beneath storm clouds, no horizon, no sky or space. OPEN SEA on drowned industrial Tide planet: almost-black abyssal teal-green heaving ocean, long sinuous diagonal swell bands with very thin cool white-teal foam, kelp rafts, iridescent oil sheen, a handful of tiny weathered rust-brown buoys. Dense hand-painted pixel-art texture with short 4-6 tone ramps, rugged dark detail and sparse mint-green glints. Frame filled edge to edge with homogeneous world terrain, no central hero subject. Dark, rich, high contrast, tiny bright accents. No text, red, pink, orange glow, cyan fields, lime, stars, clouds, land, ships. Portrait 1:2.

### Candidate 02

Use case: stylized-concept. Asset type: painted source for vertical phone game tile. Straight-down orthographic aerial view beneath storm clouds, no horizon, no sky or space. OPEN SEA alternative composition: deep black-green choppy ocean with concentric, curling tidal current and jagged slender whitecap threads, kelp slicks, scattered rusted buoy crowns, broken dark oil sheen. Neon pixel art, extremely intricate hand-placed looking pixels, selective dark edges, teal material ramps and small mint bioluminescent traces. Dense detail to all edges; no single central vignette or large blank area. No red, orange, cyan field, lime, stars, text, smoke. Portrait 1:2.

### Candidate 03

Use case: stylized-concept. Asset type: painted source for vertical phone game tile. Straight-down orthographic overhead view. RIG FIELD: dense archipelago of rusted offshore industrial platforms, concentric barnacled deck rings, cranes, flare stacks seen from directly above, tiny dark catwalks and pipe bridges joining many irregular platforms. Inky teal ocean visible in channels, thin foam around pylons; sparse mint-green static work lights. Rugged cyberpunk steampunk pixel art, dense intricate machinery, dark teal-black base, restrained low-saturation brown rust, short painterly material ramps, no perspective horizon, structures extend across all edges. No red, orange flare glow, yellow, cyan fields, stars, text. Portrait 1:2.

### Candidate 04

Use case: stylized-concept. Asset type: painted source for vertical phone game tile. Straight-down overhead view of a DIFFERENT RIG FIELD layout: dozens of little off-shore oil-city islands on dark sea, long crisscrossing pipe causeways and gantries, circular tanks and cranes, weathered deep brown metal, thin mint work lights, foam lapping decks. Fine dense pixel-art detail, dramatic dark cyberpunk game ground tile. Distributed architecture across entire frame, no isolated central subject, no horizon, no sky, no smoke or flame, no red/pink/lime/orange/cyan fields, no lettering. Portrait 1:2.

### Candidate 05

Use case: stylized-concept. Asset type: painted source for vertical phone game tile. Strict straight-down orthographic view through shallow dark teal seawater onto a SUNKEN INDUSTRIAL CITY. Submerged illuminated street grid, submerged dark tower roof tops and factory blocks, some spires break the surface with fine white foam collars. Green-mint lamp lines and quiet blue-violet depth shadows, dark abyssal teal water veiling structures, delicate caustic ripple lines. Detailed neon pixel art, rugged weathered structures, many distinct little streets, overhead map-like but organic flooded ruin. No land, skyline, horizon, sky, text, red, cyan fields, bright large lights. Portrait 1:2.

### Candidate 06

Use case: stylized-concept. Asset type: painted source for vertical phone game tile. Straight-down overhead view of a sprawling flooded industrial city under shallow nearly black green sea; dark underwater street blocks, rectangular rooftops and grid of pipe roads glow faint mint through water, broken tower crowns pierce surface and cause fine curling whitecap rings. Dense, painterly neon pixel art; dark contrast with restrained light and subtle blue-violet shadow. Full frame continuous terrain, no isolated scene. No perspective buildings, horizon, sky, red, lime, orange, bright cyan fields, text. Portrait 1:2.

### Candidate 07

Use case: stylized-concept. Asset type: painted source for vertical phone game tile. Straight-down aerial view of NIGHT SIDE ocean: almost black blue-green water, many tiny bioluminescent mint plankton blooms and large spiraling current swirls, scattered distant rusted rig light clusters and beacon dots, subtle quiet blue-violet undertones, storm-lit wave edges. Dense finely painted pixel-art terrain with organic eddies everywhere, varied scales of little glints but overall very dark. No sky, stars, planet, horizon, clouds, red/pink/lime/orange/cyan fields, text. Portrait 1:2.

### Candidate 08

Use case: stylized-concept. Asset type: painted source for vertical phone game tile. Straight-down overhead view of black ocean at night, luminous bioluminescent mint swarms flowing like branching river deltas and cellular blooms, dark wave ridges and a sparse few small offshore rig lantern rings. Detailed moody neon pixel art, intricate texture with selective black edges and stepped mint glow, subtle deep blue-violet shadow. Evenly distributed continuous top-down ground texture. No stars, sky, horizon, planet, magenta, red, lime, orange, bright cyan sheets, text. Portrait 1:2.

## Seam repaint prompt

Each selected mid and far image was offset, then passed back to built-in image generation as an edit target with this instruction, customized to its subject:

Edit this painted orthographic pixel-art game ground tile. Repaint the obvious hard vertical and horizontal joins crossing the exact centre of the image so all terrain detail continues naturally across them. Preserve the existing composition, dark teal palette, overhead camera, individual crisp pixels and high density of detail. Do not add a new subject. No horizon, sky, stars, lettering, red, orange glow or large cyan fields.

The city mid edit used an additional instruction to continue underwater streets, pipes, rooftops, ripples and tiny mint lights across the join. `build.py` retains the original toroidal border, selects painted repaint pixels through a narrow stippled transition, calibrates depth brightness, removes forbidden hues, and exports all 16 tiles. Nearest-neighbour resampling is used throughout.
