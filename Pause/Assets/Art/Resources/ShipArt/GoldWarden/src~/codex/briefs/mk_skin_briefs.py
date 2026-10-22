import os, sys
out = sys.argv[1]
head = '''You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait, the player flies UP the screen). Work ONLY inside this directory tree; do not edit any .cs/.meta/.json or any existing file; do not delete anything; do not commit.
New files go ONLY to ./Pause/Assets/Art/Resources/ShipArt/GoldWarden/src~/codex/ (the trailing ~ matters: Unity ignores it). Put helpers in its tools/ subfolder.
Read ./docs/art-style.md first (neon pixel art). The player ship is "GoldWarden", redrawn in hi-res neon pixel art (warden-gold armour, teal canopy, tall gunmetal gun barrels on the wings, gunmetal engine pods, red triangle marks on the fuselage, red-and-white wingtip diamonds).

REFERENCES (attached images, in order): (1) skinref_state0.png, (2) skinref_state1.png, (3) skinref_state2.png: the FINAL approved flipbook, three 3 x 3 sheets (intact, damaged, wreck), 9 poses each in the order key pose / bob up / bob up lights off / glint 1 / glint 2 lights off / bob down / bank left / bank right / hit flash. Each is 1536 x 1536 (cells 512 px).
RULES: no pink or magenta anywhere. No flames. No outline or halo around the ship. Flat pure #00FF00 background. No green on the ship.
'''
body = '''
JOB SKIN-{name}: a COLOURWAY of the finished flipbook. Redraw each of the three sheets in the "{name}" colourway and write skin_{name}_state0.png, skin_{name}_state1.png, skin_{name}_state2.png (same 3 x 3 layout, each cell 1024 x 1024 i.e. 3072 x 3072 per file, flat #00FF00 background, every pose at the SAME position and scale as the reference sheet). THE DRAWING MUST STAY IDENTICAL cell for cell (same silhouette, panel lines, damage, cracks, glints, bank perspective, light dashes): ONLY COLOURS CHANGE:
{rules}
Everything else (canopy glass, engine bells and nozzle glow, ink edge, glints, and ALL damage effects: sparks, glowing orange tears, embers, soot) stays as in the reference. The hit-flash cell stays bone-white.
Use IMAGE GENERATION to repaint each sheet from the reference (several candidates, pick the truest to the reference), then clean with nearest-neighbour. Do NOT simply hue-shift with a script: the result must be a painted redraw with NEW shading in the new colours (new ramp, new rim lights, new highlights on the plates), but pixel-faithful to the drawing (same plating texture, panel lines and rim lights positions).
Write tools/check_skin_{name}.py that, for each of the three files and each of the 9 cells, prints: silhouette (non-green mask) difference vs the reference cell (must be < 1.5%), difference over the canopy/blue region (must be < 6%), and the fraction of all pixels that changed (must be > {chg}% so it is visibly a new colourway), and that no gold armour pixels (hue 35-55 deg, saturation > 0.5) remain in the armour except where stated. FIX AND REDRAW until every number passes; print the summary in your final message. Finish with honest caveats.
'''
skins = {
    'Night': ('''- the gold ARMOUR (all gold plating on fuselage and wings, with its darker shading and bright highlights) becomes deep midnight indigo-navy metal: base #2A2E6B, shadow #1A1F45, rim-light highlights bright cyan #6EF2EE along the upper-left edges of the plates (neon edge lighting) and a few pale blue-white highlights. Keep the gunmetal barrels and engines, the red triangle marks and the red wingtip diamonds exactly as they are.''', 12),
    'Neon': ('''- the gold ARMOUR becomes glowing neon TEAL-cyan enamel: base #1FB5B9, shadow #0F5E6A, highlights #6EF2EE and near-white mint on the upper-left plate edges, with deep teal-black panel seams. Make the CANOPY glass a clearly different, deeper BLUE (#2B6BFF core, white-hot highlight) so it does not blend with the teal armour. Keep the gunmetal barrels and engines and the red triangle marks / red wingtip diamonds as they are.''', 12),
    'Bone': ('''- the gold ARMOUR becomes warm BONE-white ceramic armour: base #F4EAD4, shadow #B9AE98, highlights near-white, with plate seams in a slightly darker warm grey. The two red triangle marks and the wingtip diamond lights stay bright RED (#D8232C with a hot #FF5B45 core) so they still read.''', 12),
}
for k, (rules, chg) in skins.items():
    open(os.path.join(out, 'skin_%s.md' % k), 'w').write(head + body.format(name=k, rules=rules, chg=chg))
