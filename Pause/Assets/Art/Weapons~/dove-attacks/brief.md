Redo ONE player-ship secret-power effect for a top-down vertical shooter (hi-res neon pixel art: crisp hard pixels, NO anti-aliased blur, 1-3 px strokes, a limited palette with a darker shade pixel line). The current art is a PLAIN procedural thin circle outline (ref_current_ring.png) that the user finds ugly. The user already picked this direction on three ships (see ref_approved_example.png: NeonComet double ring with lattice ticks, VoltViper coiling viper ring, SolarFang corona with flare loops): thin, ring/coil/corona-shaped, detailed, NOT spiky blobs. Match that finish. ref_style_strip.png shows the strip layout.

DELIVER ONE PNG named B_<name>.png (name given below): 4096x256 RGBA, transparent background, 16 cells of 256x256 side by side. Cells 0-7 = LOOP state (8 frames, seamless), cells 8-15 = BURST state (8 frames, one-shot, frame 8 the strongest, fading to nearly empty by cell 15). Everything stays inside its cell; the effect is centred on the cell centre = the ship centre. 256 px = 1 world unit; the hull (ref_hull_in_cell.png, ~148 px tall, nose up) sits at the centre, so wrap AROUND it: the inside ~55% of the cell stays fully transparent so the hull is completely visible. Take the palette from the SHIP'S OWN hull colours in ref_hull_in_cell.png (its main hue + its glow colour + cream highlights). The sprite is scaled up (often 1.5x-4x) at runtime, so keep strokes 2-3 px and shapes bold-simple. Make the 1 px wire detail at least 2 px so it reads.

Restraint rules (user): thin, elegant, LOW glow, no big bright blobs, no wide soft halos; the game applies ~0.85 alpha. No pink/magenta/purple unless it is the ship's own hull colour. No outlines/borders.

Also write preview.png (the strip's 16 cells in a row over a dark #14162A backdrop with the hull composited at each cell centre). Generate real pixel art with image generation; assemble/clean with Python PIL (python3 -m venv if PIL missing) but do not just draw plain circles or lines procedurally. Write files into the current directory.

SHIP: Dove
FILE: B_mending.png
POWER: MENDING: repairs one heart. Loop (0-7): faint ring of tiny drifting feathers/sparkles round the hull. Burst (8-15): a gentle heal ring: a thin soft-green (#7DFFA0 style heal green) ring with feathers lifting off and drifting up, small plus-sparkles, fading.
(Runtime size: the ring is shown about 1.5 world units across.)
