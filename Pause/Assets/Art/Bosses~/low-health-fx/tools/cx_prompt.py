import sys
# cx_prompt.py Key pose stage outpath -> prompt text on stdout
k, pose, stage, out = sys.argv[1:5]
WORLD = {
 'Frost': ('the Hoarfrost Leviathan, an ice-forged steampunk war-fortress (steel, brass bands, cyan glowing tubes, ice crystals)',
           'ice shards, frost vapor and exposed cyan glowing cores; NO fire, NO pink'),
 'Verdant': ('the Bloom Queen, an overgrown living-wood and brass jungle battle-flower (bark, vines, glowing green-gold sap and spore pods)',
             'torn bark, snapped vines, leaking glowing green-gold sap and spores; NO fire, NO pink'),
 'Storm': ('the Tempest Engine, a storm-forged brass and steel turbine war machine with crackling blue-white electric cores',
           'bent plates, sparking blue-white electric cores, steam; NO fire, NO pink'),
 'Desert': ('the Dune-Eater, a sand-worn brass-and-bone burrowing war machine with sun-gold glowing parts',
            'cracked plates, sand spilling out, exposed warm sun-gold cores; NO pink'),
}[k]
print(f"""You are making game art for a mobile shoot-em-up (Unity vertical scroller), neon HD pixel art. Use your image generation tool ONCE and save the PNG to the output path at the bottom.

REFERENCES (attached): image 1 = the CLEAN, undamaged boss {WORLD[0]} in its '{pose}' attack pose. image 2 = the SAME boss in its idle pose at damage stage {stage} of 4 (stage 4 = nearly wrecked): this is the DAMAGE LOOK to apply.

TASK: draw ONE image: the boss of image 1 in EXACTLY its image-1 pose (same parts in the same places, same silhouette envelope, same lit/glowing attack parts, same scale, same centring, same canvas size and square ratio), but carrying the SAME battle damage as image 2: the same kinds of broken-off chunks along the outline, missing and bent plates, cracks, scorching, dangling bands and wires, and {WORLD[1]}. Match image 2's damage density, palette, contour treatment and level of detail, so that when the game swaps from image 2 (idle) to this image (attack) only the POSE changes, never the look of the hull or its damage.

HOW TO WORK (strict): produce the picture ONLY with your built-in image generation tool, using the two attached images as references. Do NOT search the filesystem for other images, do NOT open, copy, resize, crop, recolour or composite any other image file (especially anything that looks like a previous or damaged version of this sprite), and do NOT draw the art with Python/PIL. If the image tool fails or is unavailable, STOP and write no file at all, and say so.

HARD RULES:
- ONE single clean drawing of the boss. NOT a blend, NOT a double exposure, NO ghost copy, NO second contour, NO semi-transparent overlay of another hull. One crisp contour.
- Crisp hand-placed pixel-art edges, no blur, no airbrush, no anti-aliased soft halo, no glow field around the sprite, no cast shadow, no text, no frame.
- Keep the attack glow / telegraph parts of image 1 (they are the readable cue) but you may show them partly damaged.
- Transparent background: real alpha channel, fully transparent outside the boss (no colour backdrop, no checkerboard).
- No pink / magenta anywhere unless it exists in image 1.
DELIVER: square PNG at least 1024x1024. Save to the output path, print its size and mode. Do not write other files.

OUTPUT PATH: {out}
""")
