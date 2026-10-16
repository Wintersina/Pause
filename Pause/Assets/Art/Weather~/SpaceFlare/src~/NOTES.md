# SpaceFlare build notes

- `candidates/flare_wide_painted.png` and `candidates/flare_slim_painted.png` are the selected transparent image-generated painted source plates. The wide plate supplies flare A; the slimmer filament plate supplies flare B.
- `candidates/ambient_painted.png` is the selected image-generated particle board. Its painted sparks, motes, wisps, and mote variants are cropped and nearest-neighbour cleaned into the 16 ambient cells.
- `build_space_flare.py` performs nearest-neighbour resizing only, locks RGB to the Space ramp, adds hand-placed phase accents, wraps vertical paths, closes the one-pixel tile seam, and composes the requested atlas layout.
- `hazard.png` row 0 is the six-frame wide loop, row 1 the six-frame slim loop, and row 2 contains A warnings in cells 0–1 and B warnings in cells 2–3. Cells 4–5 are transparent.
- `ambient.png` is row-major by particle kind: four solar sparks, four plasma motes, four filament wisps, four tiny magenta motes.

The built-in verifier is the authoritative contract check. The preview is diagnostic only and is left beside the deliverables for review.
