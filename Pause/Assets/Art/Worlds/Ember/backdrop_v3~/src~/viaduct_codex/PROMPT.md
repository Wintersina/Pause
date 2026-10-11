# Codex brief: Ember viaduct_00 with landfall ends

Context: Unity 2D portrait shooter "Pause", Ember (forge / lava) world backdrop: neon pixel art --
HD hand-placed pixel clusters, 1 px dark outline, 4-6 tone ramps, near-black basalt crust, rusted
dark-bronze iron, molten orange-gold lava glow. Ground pieces are drawn on 256 x 256 transparent
cells in landmarks.png and placed on the scrolling lava-field ground.

Problem: the current lava viaduct (viaduct_00_old.png in this folder) is a FRAGMENT: its deck and
its crust base are sliced off flat at both ends (vertical cuts at x~14 and x~241), so wherever it
is placed it reads as a random stub of bridge. The game now lays it only across a lava channel
with crust under both ends, so the drawing must END on land at both ends.

Task: paint a replacement viaduct_00.png (exactly 256 x 256, RGBA, transparent background) in the
SAME style, palette, lighting, view angle and pixel scale as viaduct_00_old.png, ref_lavafall_00.png
and ref_refinery_01.png (all in this folder; read them first and match them closely):
  - the same iron-girder viaduct with its arched trestle piers, running on the SAME diagonal, from
    the lower-left up to the upper-right (deck ends near (40, 150) and (216, 95) in the cell; keep
    the middle of the deck near the cell centre);
  - BOTH ends terminate properly: each comes down onto a squat basalt / iron ABUTMENT block with a
    short ramp, standing on a small shelf of dark crust rock. No flat slice anywhere: the viaduct
    reads as a complete short bridge from bank to bank;
  - under the middle spans: a glowing molten lava channel (orange-gold, like the old drawing)
    flowing between the two crust shelves;
  - everything inside the cell with at least 16 px of fully transparent margin on every side; no
    stray pixels or fragments in the margin; no text, no background fill, no drop-shadow box;
  - crisp nearest-neighbour pixels, no blur, no soft anti-aliasing, no photorealism, no pink.
Deliver:
  - viaduct_00.png  (256 x 256 RGBA) in this folder
  - viaduct_00_preview.png (the cell scaled x3 nearest-neighbour over a mid-grey background)
Do not modify any other file.
