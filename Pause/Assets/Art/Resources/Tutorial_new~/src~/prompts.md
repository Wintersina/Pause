# Image generation sources

Built-in image generation was used for the painted head, bubble, eye, and mouth.
The saved candidates are the unmodified tool outputs. `build_kit.py` selects
`head_candidate_b.png` and fits the sources to the original sprite geometry.

## Head candidate A

> Redraw the supplied squat floating robot head as a rustic steampunk neon pixel
> art mentor. Preserve its centered silhouette, visor, antenna, lamp and side
> sockets. Add brass, iron, copper pipes, rivets, scuffs, steam vent and small
> gear. Keep a blank navy glass face panel, transparent background, hard pixels.

## Head candidate B (selected)

> Transform the supplied head while preserving its exact outer footprint and
> anchor: bent antenna, wide short head, deep blank central screen, side modules
> and chin. Use hammered brass bezel, charcoal iron, copper tubes, four tiny
> rivets, a steam vent and half gear. Add a one pixel dark contour, five tone
> ramps, amber highlights, cyan reflections and a red lamp spot. Friendly,
> rounded, transparent pixel art; no background or text.

## Bubble

> Repaint the supplied square dialogue panel as a transparent, nine slice,
> riveted brass speech box. Preserve the same bounds and large unbroken dark
> glass text field. Keep edge centers continuous for stretching. Use a one pixel
> dark outline, limited ramps, amber/cyan rim glints and hard pixel edges.

## Eye and mouth

> Repaint the supplied tiny eye wedge as a warm amber vintage terminal display
> with a pale yellow highlight and cyan edge accent, at the same anchor and
> scale. Repaint the supplied open A mouth as a friendly amber nixie display
> with dark interior, copper lower lip and tiny cyan glints. Keep hard pixels,
> transparent alpha and no additional objects.

The final open eye is softened into a rounded display in `build_kit.py` for a
friendlier expression; small painted clusters from the eye candidate remain.
