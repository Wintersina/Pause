"""Generates the Saboteur Codex briefs: python3 mk_briefs.py <out dir>"""
import os, sys
out = sys.argv[1]
OUTDIR = './Pause/Assets/Art/Resources/ShipArt/Saboteur/src~/codex/'
HEAD = '''You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait, the player flies UP the screen). Work ONLY inside this directory tree; do not edit any .cs/.meta/.json or any existing file; do not delete anything; do not commit.
New files go ONLY to %s (the trailing ~ matters: Unity ignores it). Put helpers in its tools/ subfolder.
Read ./docs/art-style.md first (neon pixel art). ''' % OUTDIR

IDENT = '''The player ship "Saboteur": a violet/purple armoured gunship seen from directly above, nose pointing UP: two long upswept horn-like arms forming a deep V (rabbit-ear shape) with a small red tip cap at the top of each horn, a heavy octagonal armoured cradle/hub in the middle holding a glowing teal-cyan crystal core (the canopy), a broad shoulder plate under the hub with a small glowing red/white diamond light on each side, and one single small dark engine nozzle with an amber lip centred under the rear. Colour identity: VIOLET / PURPLE armour (ramp from near-black aubergine #2A1240 through #5E1A66 to a mid violet #8A38C8 and cool lavender-violet highlights #C9A0F5), teal-cyan core #6EF2EE, red tip caps, amber nozzle glow. The colour is clearly VIOLET-PURPLE (blue-leaning), NEVER pink, magenta or rose (pink is reserved for the Space world).'''

STYLE = '''STYLE RULES:
- Use IMAGE GENERATION for the painted content (generate several candidates, inspect, pick the best), then clean up with nearest-neighbour only (no blur, no smoothing). Procedural-only / filter-only results are rejected. Never just recolour or warp a reference with a script: redraw.
- Painted like Paragon: dense panel plating detail, rim lights on the edges facing up-left, deep shading under the horns, glowing neon core (cyan-teal, white-hot centre, a bright highlight), lit nozzle rim, saturated cores. Rich contrast, no flat fills.
- NO thick cartoon outline and no halo/border line around the ship: only the thin natural dark edge shading Paragon has where the hull meets the background.
- No pink or magenta anywhere. No green anywhere on the ship (the background #00FF00 will be keyed out), not even in highlights or glass.
- No flames or exhaust (drawn elsewhere); the nozzle ends cleanly at its lip.
- No shadows on the background, no text, no UI boxes, no grid lines.'''

J1 = HEAD + '''We are RESTYLING the player ship "Saboteur" from an old flat-cartoon look into the same art style as our premium ship "Paragon".

REFERENCES (attached images, in order):
 (1) paragon_ship_alone.png: the STYLE TARGET (look at HOW the hull is painted, not its shape).
 (2) paragon_damaged.png: how Paragon looks DAMAGED.
 (3) ref_old_state0.png: the OLD Saboteur, intact. Its SHAPE and identity are what you keep. ''' + IDENT + '''
 (4) ref_old_state1.png and (5) ref_old_state2.png: the old damaged and wreck states, only to show WHERE damage goes.

JOB J1: THE THREE KEY POSES. Make three files in the output folder, each ONE image of exactly 1024 x 1024 px on flat pure chroma green #00FF00, ship centred, nose up, filling the same extents as ref_old_state0.png (about 90% of the width and 95% of the height; the new silhouette must stay within about 3% of the old one's width and height, same V-horn silhouette family):
  key_state0.png  = Saboteur INTACT, redrawn in Paragon's painted style.
  key_state1.png  = DAMAGED. Everything of state 0 plus a FEW BOLD, DISTINCT damage features and nothing else: (a) the LEFT horn's mid panel blown off showing a dark cavity with exposed ribs and sparking amber/cyan wires, with soot streaks around it; (b) one blown plate with scorch on the RIGHT lower shoulder of the hub; (c) two or three hairline cracks on the hub plating beside the core. Everything else stays clean.
  key_state2.png  = HEAVILY DAMAGED / wreck. Everything of state 1 (identical there) plus a few more bold features: (d) the RIGHT horn's tip torn off and bent, jagged dark bites with a glowing orange rim inside the silhouette; (e) the hub's lower-right ring panel open showing exposed machinery with a blue-white arc; (f) the core crystal cracked (bone-white crack lines across the glass); (g) a char/scorch mark on the left horn tip; (h) a few glowing orange embers.
DAMAGE MUST STAY READABLE: bold distinct features, NOT an all-over lava / crack / noise texture. At least HALF of the hull area stays clean and intact even in state 2. No translucent ghost overlays, no flat brown sticker polygons: paint the damage as real plating cut away, scorched and exposed.
Damage is painted INSIDE the intact silhouette: the outer outline of all three images must be identical pixel for pixel (draw state 0 first, then derive 1 and 2 from it; the undamaged areas of states 1 and 2 must be pixel-identical to state 0).
''' + STYLE + '''
Also write tools/check_j1.py that checks the three files are 1024x1024, prints the bounding box of the non-green pixels of each, the % of silhouette pixels that differ between state 0 and states 1/2 (must be < 1.5%), and the % of pixels that differ in colour between state 0 and each damaged state (state 1 between 3% and 12%, state 2 between 8% and 20%, and state 2 at least 1.5x state 1). Fix and redraw until it passes, then print the table in your final message. Save preview_j1.png (the three images side by side on dark #0b0b1a).
Finish with a short summary of what you wrote and honest caveats.
'''

J1B = HEAD + '''The player ship "Saboteur" has just been redrawn in hi-res neon pixel art (same style as the premium ship "Paragon").

REFERENCES (attached images, in order): (1) key_state0.png, (2) key_state1.png, (3) key_state2.png: the APPROVED key poses (intact, damaged, wreck), 1024 x 1024 on flat #00FF00. Keep EXACTLY that ship, scale, palette, pixel density and damage; only the stated change differs. (4) ref_neoncomet_banks.png: ANOTHER ship's approved pose sheet (3 x 2 grid), shown ONLY to demonstrate how MODEST the bank cells (3 = bank left, 4 = bank right) must be: same hull, a subtle roll. Do not copy that ship.
RULES: use IMAGE GENERATION for the painted content (generate several candidates, inspect, pick the best), then nearest-neighbour clean-up only (no blur). Never just recolour, squash or warp a reference with a script: redraw/paint. Flat #00FF00 background, no shadows/text/grid. No pink or magenta. No green on the ship. No flames. No thick outline or halo.

JOB J1b: POSE SHEETS. Make three files hull_redo_state0.png, hull_redo_state1.png, hull_redo_state2.png (for key_state0 / 1 / 2 respectively). Each is a 3 x 2 grid of equal square cells (3 x 1024 wide, 2 x 1024 tall), flat #00FF00, ship centred at the SAME scale and position as its key. Cell order, row-major:
 0 LIGHTS OFF: the key pose with every running light DARK: the two red/white diamond lights on the shoulders and the red tip caps of the horns painted as unlit dark maroon/navy pixels, the core crystal slightly dimmer. Everything else identical.
 1 GLINT 1: the key pose with a bright white diagonal glint streak (3 to 4 pixel-steps wide, lower left to upper right) across the LEFT half of the teal core crystal plus a bone-white sparkle at its tip. Core only; hull unchanged.
 2 GLINT 2: the streak moved to the RIGHT half of the crystal, thinner and fainter; running lights dark like cell 0.
 3 BANK LEFT: a MODEST roll to the ship's left, like ref (4)'s cells: the SAME hull, SAME horn lengths and recognisable shape, lean only about 6 to 8 degrees. The whole silhouette width changes by at most about 20% versus the key (the left horn a little closer to the centre, the right horn a little wider), BOTH horns stay clearly visible at believable lengths and NEVER extend beyond the key pose's horn span; a hint of the far side's thickness; canopy core slightly skewed. NO twisted fuselage, NO blade-shaped or stretched horn, NO hatching patterns on the horns.
 4 BANK RIGHT: the same modest roll to the right, painted (not a flipped copy): in states 1 and 2 the damage stays on the SAME physical panels (moves with the part, never mirrored).
 5 leave completely empty (just the green).
For states 1 and 2 keep the damage exactly where the key has it.
Write tools/check_j1b.py that, for each file, prints: % of pixels differing from the key (threshold 60 on summed RGB) for cells 0-4; the % of core-crystal pixels that differ for cells 1-2; and for the banks the residual difference after the best integer translation of the key (must be >= 5%), the silhouette (non-green mask) difference (must be between 3% and 22%), the silhouette bounding-box width vs the key (must be within 80% to 100%, never wider than the key) and the lean angle estimate. Cell 1 must differ from the key in >= 0.8% of ALL pixels and cell 2 must differ from cell 1; cell 0 >= 0.4%. FIX AND REDRAW until every number passes; print the table in your final message. Also save preview_j1b.png (the three files side by side on dark #0b0b1a). If you cannot satisfy a threshold, say exactly which and why instead of weakening it.
'''

J2 = HEAD + '''We are restyling the player ship "Saboteur" in the style of our premium ship "Paragon".

REFERENCES (attached images, in order): (1) paragon_ship_alone.png: the style target (its jet flame: hard-stepped pixel bands, white-hot core, bright sparks). (2) key_state0.png: the NEW Saboteur hull (violet armour, one nozzle at the rear). (3) ref_old_flame.png: the OLD Saboteur exhaust, 8 frames on green: a stuttering gatling-style muzzle flash with smoke cels (orange burst, bone-white core, violet smoke).

JOB J2: EXHAUST FLIPBOOK (the plume the game attaches under Saboteur's single nozzle).
The game mounts one flame sprite under the nozzle, pointing straight DOWN the screen, hot head at the TOP of the sprite (attached to the nozzle), tail at the bottom. Redraw the old exhaust as a looping flipbook in the painted neon pixel style of Paragon's jet, keeping the identity: a stuttering orange-and-bone-white pulse train with violet smoke puffs trailing down.
Make two PNG files in the output folder:
 flame_cruise.png : 8 cells side by side in one row, each cell exactly 1 : 4 (wide : tall), e.g. 256 x 1024 each (2048 x 1024 total), flame head touching the top area (about 3% down), tail ending about 80% down, centred horizontally, flat #00FF00 background, NO backdrop rectangle or box of any other colour.
 flame_boost.png : same layout but clearly longer (tail reaching about 97% down), hotter and brighter: wider bone-white core, bigger orange burst, more sparks.
Frame behaviour (hold times in ticks 2,2,1,2,2,2,2,2): frames 0,1,3,4,5,6,7 are the normal stutter loop (pulses and smoke puffs moving down and fading, tail wavers a few pixels, a few spark pixels); frame 2 is the 1-tick burst frame: brightest and longest, near-solid white-orange streak.
Style: hard-stepped pixel bands, NO smooth gradients or blur, no outline. Use ONLY these five band colours plus the green key: violet #B83CC0 (outer flame / smoke), orange #F2862B (mid), bone-white #F4EAD4 (white-hot core), deep violet #5E1A66 (dark smoke edge), indigo #3A2A5C (accent / smoke shadow). NO pink fringes. Use IMAGE GENERATION for the painted content, then snap every pixel to the nearest of those five colours (nearest-neighbour only).
Write tools/check_j2.py verifying both files: 8 equal cells, only those 5 colours + key, no pixel rectangle backdrop, boost brighter and longer than cruise; prints a table; save preview_j2.png (both sheets on dark #0b0b1a).
Finish with a short summary and honest caveats.
'''

SKHEAD = HEAD + '''The player ship is "Saboteur", redrawn in hi-res neon pixel art (violet armour, teal core crystal, red tip caps, shoulder diamond lights, one amber nozzle).

REFERENCES (attached images, in order): (1) skinref_state0.png, (2) skinref_state1.png, (3) skinref_state2.png: the FINAL approved flipbook, three 3 x 3 sheets (intact, damaged, wreck), 9 poses each in the order key pose / bob up / bob up lights off / glint 1 / glint 2 lights off / bob down / bank left / bank right / hit flash. Each is 1536 x 1536 (cells 512 px).
RULES: no pink or magenta anywhere. No flames. No outline or halo around the ship. Flat pure #00FF00 background. No green on the ship.
'''
SKBODY = '''
JOB SKIN-{name}: a COLOURWAY of the finished flipbook. Redraw each of the three sheets in the "{name}" colourway and write skin_{name}_state0.png, skin_{name}_state1.png, skin_{name}_state2.png (same 3 x 3 layout, each cell 1024 x 1024 i.e. 3072 x 3072 per file, flat #00FF00 background, every pose at the SAME position and scale as the reference sheet). THE DRAWING MUST STAY IDENTICAL cell for cell (same silhouette, panel lines, damage on the same panels, cracks, glints, bank perspective, light dashes): ONLY COLOURS CHANGE:
{rules}
Everything else (core crystal glass, nozzle glow, ink edge, glints, and ALL damage effects: sparks, glowing orange tears, embers, soot) stays as in the reference. The hit-flash cell stays bone-white.
Use IMAGE GENERATION to repaint each sheet from the reference (several candidates, pick the truest), then clean with nearest-neighbour. Do NOT simply hue-shift with a script: the result must be a painted redraw with new ramp shading for the new colours, but pixel-faithful to the drawing (same plating texture, panel lines and rim lights).
Write tools/check_skin_{name}.py that, for each of the three files and each of the 9 cells, prints: silhouette (non-green mask) difference vs the reference cell (must be < 1.5%), difference over the teal core region (must be < 6%), and the fraction of all pixels that changed (must be > {chg}%). FIX AND REDRAW until every number passes; print the summary in your final message. Finish with honest caveats.
'''
skins = {
    'Night': ('''- the violet ARMOUR (all purple plating with its darker shading and highlights) becomes deep midnight indigo-navy metal: base #2A2E6B, shadow #1A1F45, rim-light highlights bright cyan #6EF2EE along the upper-left edges of plates (neon edge lighting) and a few pale blue-white highlights. Keep the red tip caps and shoulder diamond lights as they are.''', 12),
    'Sodium': ('''- the violet ARMOUR becomes warm sodium-lamp ORANGE metal: base #F2862B, shadow #A9481A, highlights #FFB43C with pale yellow-white hot edges. Keep the red tip caps and shoulder diamond lights as they are.''', 12),
    'Bone': ('''- the violet ARMOUR becomes warm BONE-white ceramic armour: base #F4EAD4, shadow #B9AE98, highlights near-white, plate seams slightly darker warm grey. The red tip caps and shoulder diamond lights stay bright red (#D8232C with a hot #FF5B45 core) so they still read.''', 12),
    'Jester': ('''- this is the special JESTER LIVERY (chevrons): keep the violet armour exactly as it is and paint a bold cyan-teal (#1FB5B9, shadow #0F5E6A, highlight #6EF2EE) stack of THREE downward-pointing chevrons (V-shapes, each a thick band about 6 pixel-steps wide) on the hub and shoulder plate: the chevron stack is centred on the ship's centre line below the core crystal, symmetric left-right, the tips pointing DOWN toward the nozzle, with the three chevrons spaced evenly one above the other; plus one matching cyan chevron band across the base of EACH horn (a short V stripe following the horn's width). The shapes are crisp, clean, evenly spaced and mirror-symmetric, never mottled patches or noise. Nothing else changes colour (so the fraction changed is smaller here).''', 3),
}
for name, src in (('j1', J1), ('j1b', J1B), ('j2', J2)):
    open(os.path.join(out, name + '.md'), 'w').write(src)
for k, (rules, chg) in skins.items():
    open(os.path.join(out, 'skin_%s.md' % k), 'w').write(SKHEAD + SKBODY.format(name=k, rules=rules, chg=chg))
