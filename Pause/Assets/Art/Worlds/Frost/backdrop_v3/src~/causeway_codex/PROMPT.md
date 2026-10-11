# Codex brief: Frost causeway_00 with landfall ends

Context: Unity 2D portrait shooter "Pause", Frost world backdrop (neon pixel art: HD hand-placed
pixel clusters, 1 px dark outline, 4-6 tone ramps, dark navy / blue-violet / gunmetal base, ice
blue-white, sparse cyan and magenta lamps). The ground pieces are drawn on 256 x 256 transparent
cells in landmarks.png and placed on the scrolling ice-ocean ground.

Problem: the current rail causeway sprite (causeway_00_old.png in this folder) is a FRAGMENT: the
viaduct deck is sliced off flat at both ends (vertical cuts at x~22 and x~240), so wherever it is
placed it looks like a random rail stub sitting on the ice. The game now places it only across an
open-water lead with land under both deck ends, so the drawing must END on land.

Task: paint a replacement causeway_00.png (exactly 256 x 256, RGBA, transparent background) in the
SAME style, palette, lighting, view angle and pixel scale as causeway_00_old.png, ref_cliff_00.png
and ref_rig_01.png (all in this folder; read them first and match them closely):
  - the same rail viaduct (dark steel deck with rails, ice-crusted trestle piers with icicles,
    cyan lamp posts and one magenta lamp) running on the SAME diagonal: from the upper-left down to
    the lower-right (deck ends near (30, 70) and (226, 150) in the cell; keep the centre of the
    deck near the cell centre).
  - BOTH ends must terminate properly: each end comes down onto a snow-capped stone / ice
    ABUTMENT (a squat embankment block with a short sloped ramp) standing on a small shelf of
    snow-covered shore ice. No flat slice anywhere; the deck reads as a complete short bridge.
  - under the middle spans: dark open water (deep navy) with a few small white-blue floes, like
    the old drawing, so it reads as crossing a lead between two shores.
  - everything inside the cell with at least 16 px of fully transparent margin on every side; no
    stray pixels or fragments in the margin; no text, no drop shadow box, no background fill.
  - nearest-neighbour crisp pixels, no blur, no anti-aliased soft edges, no photorealism.
Deliver:
  - causeway_00.png  (256 x 256 RGBA) in this folder
  - causeway_00_preview.png (the cell scaled x3 nearest-neighbour over a mid-grey background)
Do not modify any other file.
