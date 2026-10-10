# Iron Kraken source notes

The four painted source images in this directory were made with image generation, then sampled with nearest-neighbour resizing only. `build.py` assembles all production images at native pixel resolution. Run it from anywhere with `python3 Pause/Assets/Art/Bosses/Tide~/src~/build.py`.

## Candidate review

| File | Observation |
| --- | --- |
| `candidate_01.png` | Strong hydraulic beak and cannon clusters; eye became a slit and the six broad arms did not clearly read as eight. |
| `candidate_02.png` | Clear eye and beak, attractive shell crust; six dominant arms left the eight-tentacle request ambiguous. |
| `hull_key.png` | **Selected.** Eight articulated arms, single round eye, broad symmetric silhouette, and restrained Tide palette. |

`maw_fire_key.png` is a targeted edit of the selected hull: its lower beak opens around a painted mint-white pressure jet. Only that lower-center region is revealed in the fire cell. `death_peak_key.png` is a targeted edit of the same hull with cracked iron, floating parts, and a mint-violet implosion. The death sequence reveals that edit, then recedes and fades it. The ordinary hull stays registered at the same scale.

## Prompt direction

Transparent square boss sprite, straight-on top/forward view, wide and roughly bilateral; riveted rust-brown iron dome, restrained brass, grey-green barnacles, crown pipes, one large bioluminescent mint eye, hydraulic beak, eight segmented pipe/cable tentacles with cannon and claw ends. Premium rugged cyberpunk and steampunk pixel art, thousands of tonal details, 1 px dark selective outlines, pale mint-white upper-left rim, localized mint-white cores and tiny blue-violet arcs. No red, orange fire, cyan ice fields, lime plants, scene, or text.

The source artwork contained small accidental warm-red texels. `build.py` shifts those to brass and violet while retaining value texture; brass/brown saturation is capped below 0.45. It does not quantize colours or smooth-resample the art.

## Verification

`verify_boss_art.py` is an unchanged copy of the repository boss-art checker. Run it with `python3 Pause/Assets/Art/Bosses/Tide~/src~/verify_boss_art.py Pause/Assets/Art/Bosses/Tide~ Tide --no-damage --no-death`. Run `audit.py` for per-cell margins, centroids, colour detail, red share, and measured contrast on all four Tide midground tiles.
