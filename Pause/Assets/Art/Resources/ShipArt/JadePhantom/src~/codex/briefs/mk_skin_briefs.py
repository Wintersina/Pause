import os, sys
out = sys.argv[1]
head = '''You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait, the player flies UP the screen). Work ONLY inside this directory tree; do not edit any .cs/.meta/.json or any existing file; do not delete anything; do not commit.
New files go ONLY to ./Pause/Assets/Art/Resources/ShipArt/Hulls/src~/jadephantom/codex/ (the trailing ~ matters: Unity ignores it). Put helpers in its tools/ subfolder.
Read ./docs/art-style.md first (neon pixel art). The player ship is "JadePhantom", redrawn in hi-res neon pixel art (jade-green bat-wing armour, blue canopy, red-and-white wingtip diamonds, cream spine light bar, gold star mark, amber nozzle lips).

REFERENCES (attached images, in order): (1) skinref_state0.png, (2) skinref_state1.png, (3) skinref_state2.png: the FINAL approved flipbook, three 3 x 3 sheets (intact, damaged, wreck), 9 poses each in the order key pose / bob up / bob up lights off / glint 1 / glint 2 lights off / bob down / bank left / bank right / hit flash. Each is 1536 x 1536 (cells 512 px).
RULES: no pink or magenta anywhere. No flames. No outline or halo around the ship. Flat pure #00FF00 background. Every hull pixel must stay clearly different from pure #00FF00.
'''
body = '''
JOB SKIN-{name}: a COLOURWAY of the finished flipbook. Redraw each of the three sheets in the "{name}" colourway and write skin_{name}_state0.png, skin_{name}_state1.png, skin_{name}_state2.png (same 3 x 3 layout, each cell 1024 x 1024 i.e. 3072 x 3072 per file, flat #00FF00 background, every pose at the SAME position and scale as the reference sheet). THE DRAWING MUST STAY IDENTICAL cell for cell (same silhouette, panel lines, damage, cracks, glints, bank perspective, light dashes): ONLY COLOURS CHANGE:
{rules}
Everything else (canopy glass, engine nozzle glow, ink edge, glints, and ALL damage effects: sparks, glowing orange tears, embers, soot) stays as in the reference. The hit-flash cell stays bone-white.
Use IMAGE GENERATION to repaint each sheet from the reference (several candidates, pick the truest to the reference), then clean with nearest-neighbour. Do NOT simply hue-shift with a script: the result must be a painted redraw, but pixel-faithful to the drawing (keep the same plating texture, panel lines and rim lights, just in the new colours with proper ramp shading).
Write tools/check_skin_{name}.py that, for each of the three files and each of the 9 cells, prints: silhouette (non-green) mask difference vs the reference cell (must be < 1.5%), difference over the canopy/blue region (must be < 6%), and the fraction of all pixels that changed (must be > {chg}% so it is visibly a new colourway). FIX AND REDRAW until every number passes; print the summary in your final message. Finish with honest caveats.
'''
skins = {
    'Night': ('''- the jade ARMOUR (all green plating, with its darker shading and mint highlights) becomes deep midnight indigo-navy metal: base #2A2E6B, shadow #1A1F45, rim-light highlights bright cyan #6EF2EE along the upper-left edges of the plates (neon edge lighting) and a few pale blue-white highlights. Keep the cream spine light bar, the gold star mark and the red-and-white wingtip diamonds exactly as they are.''', 12),
    'Sodium': ('''- the jade ARMOUR becomes warm sodium-lamp ORANGE metal: base #F2862B, shadow #A9481A, highlights #FFB43C with pale yellow-white hot edges. Keep the cream spine light bar, the gold star mark and the wingtip diamonds as they are.''', 12),
    'Bone': ('''- the jade ARMOUR becomes warm BONE-white ceramic armour: base #F4EAD4, shadow #B9AE98, highlights near-white, with plate seams in a slightly darker warm grey. The cream spine light bar, the wingtip diamond lights and the gold star mark become bright RED (#D8232C with a hot #FF5B45 core) so they still read.''', 12),
    'Wraith': ('''- this is the special CHEVRON LIVERY: keep the jade armour exactly as it is, but paint three nested dark charcoal-violet chevron bands (V shapes pointing UP toward the nose, base #2C2D40, shadow #1A1A28, highlight edge #5A5C78) across the bat wings and the spine, centred on the spine, stacked one behind another from just behind the canopy to the wing notches, each chevron following the plate panels under it with a thin pale #5A5C78 rim on its upper edge. Nothing else on the ship changes colour (so the fraction changed is smaller here).''', 3),
}
for k, (rules, chg) in skins.items():
    open(os.path.join(out, 'skin_%s.md' % k), 'w').write(head + body.format(name=k, rules=rules, chg=chg))
