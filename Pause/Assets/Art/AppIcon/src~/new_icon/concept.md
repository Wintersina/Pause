# Orbital Rail

The user chose **Orbital Rail** after viewing the first icon preview. The icon keeps the original gold GoldWarden Regent hull, its exhaust sprite, and the exact red PAUSE title from the main menu. The title artwork is resized with nearest-neighbour sampling; its lettering and colors are never recreated.

The earlier rail concept had a bright horizontal station bridge behind the ship, which competed with its wide wings. This version removes that bridge. Two mirrored crops of the game's `rail_space_wide_v1.png` frame the outer edges at their original 1:1 pixel scale, with their brightness and saturation lowered. The center is a dark launch lane. A small off-centre planet and violet nebula preserve the chosen concept's setting while keeping the ship the focal point and the wordmark the only large red element.

Three generated nebula backgrounds were inspected. `concept_rail_refined_base.png` is the selected background source; `concept_rail_planet_source.png` supplies the small planet. Both sources are included so `build_icon.py` can rebuild the deliverables. The adaptive background is a complete, opaque rail-and-space scene with no ship or title. It is composed separately so it reads on its own when a launcher omits the foreground. The adaptive foreground uses only the hull, exhaust, and title overlay, with their artwork fully inside the 132 px safe circle.

`preview.png` shows circle, squircle, and square treatments at 192, 96, and 48 px on dark and light tiles, for the master and adaptive compositions. Its bottom row compares the prior Chasm Breakout icon with Orbital Rail and shows the adaptive background alone.

Run `python3 build_icon.py && python3 verify_icon.py` from this directory. The build uses nearest-neighbour resizing for all art. At 48 px, the ship and red title remain recognizable, though the small planet and rail detail become subtle.
