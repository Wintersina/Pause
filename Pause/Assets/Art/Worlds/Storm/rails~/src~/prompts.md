# Storm rail image edits

Built-in image generation tool, with the existing Tide rail as the edit target.
`candidate_a.png` and `candidate_b.png` were generated as independent edits;
`candidate_b.png` was selected. `seam_edit.png` is a further built-in edit of
`candidate_b.png`. The final assembly uses its end strips and restores the
selected candidate's middle 90%, then applies the Tide structural alpha mask.

## Candidate A

Use case: style-transfer. Asset type: production 2D pixel-art scrolling side
rail texture for Pause's Storm world. Image 1 is the EDIT TARGET, not merely
inspiration. Repaint this exact rail in place. Preserve the tall 725x2170
canvas, vertical full-height cut-off framing, narrow rail thickness and
x-position, precise silhouette, exterior transparency and real transparent
gaps in the vent slots and between machinery, and the sequence of riveted
plates, brackets, ribbed hoses, vents, right-hand lamp column. Straight front
orthographic view, crisp densely detailed rugged pixel art with 1-pixel dark
outlines, stepped 4-6-tone material ramps, directional upper-left cold rim
light; avoid painterly smoothing. Storm variant: dark wind-scoured gunmetal
and bruised slate steel (blue-grey hue 212-230, low saturation), matte-black
recesses, pale hail-grey pitting and short diagonal rime streaks, muted
teal-grey only as a material trace. Brackets must be graphite steel, not
copper brown. Replace all mint/green and violet coils and lamps with bright
storm-white #F2F6FF cores, restrained cold white-blue edge glow around hue
205 with saturation <= .5, and a few pale-yellow #FFF3A0 inner glints; two
or three tiny hot-pink status pixels only. Add a few tiny lightning-rod stubs
integrated into the structure, without changing the outer silhouette. Keep
dark heavy machinery with sparse luminous tubes. No red, orange, amber glow,
cyan fields, green, lime, violet or magenta areas, brown-brass hull, soot,
flames, lightning outside the rail, clouds, scene, text, shadow halo, or
opaque background. The upper and lower rail cross-sections must continue
straight through the canvas edges for vertical tiling, without end caps.

## Candidate B (selected)

Use case: style-transfer. Asset type: production Storm-world 2D pixel-art
scrolling side rail texture. Image 1 is the precise EDIT TARGET. Repaint
exactly in place, preserving its 725x2170 narrow vertical rail silhouette,
placement, all machinery layouts and proportions, exterior transparent
margins, transparent vent slots and gaps, and straight orthographic front
view. In particular preserve the familiar repeated plate/bracket/vent/hose/
lamp rhythm. Reimagine metal for a gas giant's violent atmosphere: dark
bruised slate gunmetal and graphite steel, matte black recesses, silver-grey
hail pitting, sparse short horizontal wind-shear scuffs and small rime
streaks. This must read STORM and wind-scoured industrial refinery, not Frost
ice: no icicles, snowflakes, crystals or heavy blue ice. Minimal tiny
lightning-rod stubs fit inside the current outline. Replace ALL green and
purple lamps and coils with storm-white #F2F6FF cores edged in restrained
cold white-blue around hue 205, saturation under .5; include only a few
pale-yellow #FFF3A0 glints and at most two tiny hot-pink warning lamps.
Lamps should be bright enough to pop against a dark cloud deck, while
armored panels remain dark. Rugged dense crisp stepped pixel art, 1 pixel
dark selective outlines, rich 4-6 tone ramps per material, tiny upper-left
cold rim highlights, no blur or global posterization. Keep true alpha
between pieces, no dark halo. No red, orange or amber glow, cyan fields,
green or lime, violet or magenta areas, mint, brown brass, painted clouds,
giant lightning arcs, background, text or shadow. Continuous straight rail
cut flush at top and bottom with matching cross-sections for vertical
repetition; no end caps.

## Seam refinement

Use case: precise-object-edit. Asset type: production vertically repeating
2D pixel-art Storm rail. Image 1 is the EDIT TARGET. Fix ONLY its vertical
tiling seam. Keep the rail's entire middle 90% unchanged, including all
individual pixels, plates, tubes, hoses, lamp colors, texture, silhouette,
and transparent cutouts. Modify only narrow strips within the top 5% and
bottom 5% of the tall image so its last scanline can join its first scanline
without a discontinuity. Align matching top and bottom cross-sections for
the dark slate steel backbone, silver ribbed hoses, luminous white-blue
tube column, and transparent exterior. Rail must run flush through both
canvas edges with no caps or fading, no new top/bottom details, no scene.
Preserve transparent background and gaps. No blur, no red, green, cyan
fields, amber glow, violet, text or shadow halo. Keep 725 px width and tall
portrait format, straight front orthographic detailed pixel art.
