Desert SALT FLAT ground (variant 3), hi-res PIXEL ART tiles made from Codex (gpt-5.6-luna image generation) paintings.

- src~/mid_a.png, mid_b.png: the two 1254 px paintings the shipped tile is cut from (A over B). mid_c.png, mid_d.png: an unshipped alternate pair (crystal garden / dunes on the pan). prompt_*.md + common.md: the exact prompts. refs/: the reference images passed to Codex (OLD_* is the replaced art, passed as "do not copy").
- src~/build_tiles.py v3 <src dir> <out dir>: min-error-cut join of the two paintings (vertical and horizontal wrap), grade (p90 .465 / far .415 / sky .365, forbidden pink / purple / red hues rotated to ochre, calm central lane), then the PIXEL PASS: unsharp, block-average to a 4 px cell grid (256 x 512 pixels), snap to a 96-colour palette, nearest-neighbour upscale. far / sky are the same painting shifted / mirrored and hazier. The tile is point-filtered, no mipmaps.
- src~/build_flow.py salt <out>: the transparent wind-grit and glint overlay.
- ../src~/ground_masks.py --only 3 (CELL 32): affinity mask; teal brine pools (cells over 20% pool) are class C so docks and structures never land on water.
