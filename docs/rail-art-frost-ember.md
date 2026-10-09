# Reinforced Frost and Ember rails

Installed with the built-in image generation tool, using the approved Forest
rail as Frost's edit target and the supplied clean Space rail as Ember's target.
Both use WorldPainter's reinforced rail path, mirrored on the right, with the
Frost visible thickness (each world compensates for its transparent padding).
Importing preserves full resolution, transparency,
point filtering and vertical repeat. Legacy wall textures remain available.
All four worlds use a transparent unlit scrolling shader with a 3% premultiplied
overlap and a near-black matte cutout. Residual exterior padding reveals the
backdrop. RailFit preserves texture proportions through vertical repeats.

Assets:
- `Pause/Assets/Art/Resources/Worlds/Frost/rail_frost_wide_v1.png`
- `Pause/Assets/Art/Resources/Worlds/Ember/rail_ember_wide_v1.png`

Validation: `WorldRailReview.Run` checks live bindings, imports, silhouettes,
scrolling-wrap transitions and switching back to Space. It renders the new
rails over their world backdrops into `Pause/Builds/RailPreview/`.
`WorldBackdropTest` validates the reinforced art instead of unused legacy walls.

## Frost prompt

Use case: style-transfer. Asset type: production scrolling side rail texture for a pixel-art sci-fi game. Edit target: supplied forest rail. Make a FROST world variant of this exact rail. Preserve the exact image dimensions, vertical full-height framing, rail thickness, silhouette and position, black/transparent exterior margins, straight front orthographic view, densely detailed rugged pixel art, riveted metal armor plates, copper brackets, ribbed hoses, vents and structural rhythm. Replace ALL green leaves, vines and roots with small angular ice crystals, frost crust and icy buildup following the same areas. Weathered steel becomes cold blue-grey gunmetal, copper braces remain weathered copper. Replace magenta lamps and coils with bright icy cyan-blue cryogenic lamps; existing cyan can become pale ice-white blue. Small icicles clinging to brackets, no sprawling extra silhouette. Keep dark heavy mechanical structure, crisp stepped pixel shading and luminous lamp cores, no painterly smoothing. Continuous vertical rail cut off flush at top and bottom, suitable for vertical repetition: top and bottom cross sections must match, no end caps. Genuine transparent background outside rail, no text, no scene, no watermark. Preserve the mechanical design and comparable visual density.

## Frost seam refinement

Frost seam refinement (built-in edit): Use case: precise-object-edit. Edit target: supplied FROST pixel-art industrial side rail. Fix ONLY the vertical tiling seam. Preserve exact 725 x 2169 canvas, transparency, thickness, position, ice details, cyan lighting, mechanical structure and all center artwork. Modify narrow strips near top and bottom so the last row joins the first row continuously as a seamless vertical repeating game texture: exact matching cross-sections for the main dark blue steel backbone, copper and silver pipes, cyan lamp column and transparent silhouette. Match the first and last rows precisely. No abrupt metal plate or pipe truncation mismatch at the wrap, no end caps. The image remains one full-height cut-off industrial rail on transparent background, same crisp pixel art. Leave the middle 90 percent unchanged. No text or watermark.
## Ember prompt

Use case: style-transfer. Asset type: production scrolling side rail texture for a pixel-art sci-fi game. Edit target: supplied clean SPACE rail. Create EMBER world variant. Preserve exact canvas dimensions, full-height vertical framing, rail position and thickness, silhouette, transparent exterior margins, front orthographic view, mechanical design: rugged riveted armor plates, copper clamps, ribbed hoses, vents and dense pixel detail. Cold grey plates become scorched charcoal and dark heat-stained gunmetal, copper braces are burnt bronze with chipped orange edges. Replace all magenta and cyan lamps and coils with lava orange, fiery red and small hot amber-white cores. Add restrained soot, ash deposits and thin molten fissures embedded within armor, preserving recognizable machinery. No leaves or vines, no icicles. Keep crisp stepped pixel shading and heavy dark structure with sparse bright furnace lamps. No large flames, no bright full-surface orange wash, no painterly softness. Continuous vertical rail flush cut at top and bottom, matching cross sections suitable for repeated scrolling without end caps. Genuine transparent background outside rail, no text, no scene, no watermark.

## Frost rail match to Space (Oct 8)

"Frost rails are too wide out, match the width and visibility to Space." Measured with `RailMatchTest` (rails alone
through the 9:21 game camera, and against each world's own backdrop): inner edge |x| 2.69-2.70 u, outer 3.64-3.65 u,
width 0.95-0.96 u and the footprint on a bright field (2.549..3.665) are the same in all four worlds, so the lane
bounds the mines, ship clamp and spawns read were never different. What differed was visibility: the 60% dim
(`c7abc9e6`) made Frost's dark rails twice as heavy against its pale sky as Space's against black (rail-vs-lane
contrast 0.276 vs 0.133). `WorldPainter.FrostRailBrightness` is back to 1 (0.155, 1.17x Space, asserted within 20%).
Verdant is geometrically identical but its rails are quieter (contrast 0.087, 0.65x Space); Ember matches (0.118).
No new art needed.
