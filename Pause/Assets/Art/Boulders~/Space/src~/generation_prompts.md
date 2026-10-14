# Space cover boulder burst painting

Built-in image generation, transparent background, reference: `../boulder.png`.
Two candidates were generated and inspected for each variant. The chosen paintings are
`painted_burst_0.png` (first candidate), `painted_burst_1.png` (second), and
`painted_burst_2.png` (second). `build_break.py` composes these with the intact atlas.

## Chosen prompt: variant 0

Use case: precise-object-edit. Asset type: candidate breakup painting for variant 0 of a Unity 2D shooter cover asteroid. Input image: the first 512x512 asteroid cell of the supplied sheet is the exact subject and texture reference; ignore the other two. Paint one mid-burst candidate on a genuinely transparent background: that same broad cratered purple-grey asteroid torn into 6 large recognizable chunks, separated by narrow blazing magenta #FF55E8 seams with sparse #FFD0F6 cores, a few small rock chips and cyan-lit dust. Preserve its identifiable crater arrangement, cyan-blue rim on right/bottom, painterly detailed pixel-art texture and hard pixel edges. All lighting and chunks belong to that rock. Center composition with broad silhouette and margins. No weapons, mines, text, UI, red, blur, glow halo, background, or ground plane.

## Chosen prompt: variant 1

Use case: precise-object-edit. Asset type: second candidate for destruction burst of Space cover boulder variant 1, the MIDDLE asteroid in the supplied 3-cell sheet. Transparent sprite only. Maintain the middle asteroid's distinctive long slanted cratered purple-grey form, exact blue rim placement and crater patterns. Split into 6 to 7 LARGE recognizable pieces with 12-35 pixel gaps and thin magenta #FF55E8 seams, rare pale #FFD0F6 cores, little debris. Rock must stay heavy and detailed with short painterly 4-6 tone ramps. Crisp native-looking pixel art, no antialias/blur/halo, no backdrop, no text, no red.

## Chosen prompt: variant 2

Use case: precise-object-edit. Asset type: second candidate for destruction burst of Space cover boulder variant 2, the RIGHT asteroid in the supplied 3-cell sheet. Transparent sprite only. Maintain the right asteroid's distinctive central flaming crater, original cratered purple-grey texture and cyan-blue edge. Crack it into 6 to 7 LARGE pieces with 12-35 pixel gaps; center crater still recognizable as one piece. Thin magenta #FF55E8 illumination on newly exposed jagged rock edges, rare pale #FFD0F6 cores, sparse crumbs. Crisp hard-edged hand-drawn pixel art, no antialias/blur/halo, no backdrop, no text, no red.
