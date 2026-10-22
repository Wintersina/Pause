"""Generates the SolarFang briefs (j1, j1b, j2, skins) from the NeonComet ones. Usage: python3 mk_briefs.py"""
import os
HERE = os.path.dirname(os.path.abspath(__file__))
NC = os.path.join(HERE, "..", "..", "..", "neoncomet", "codex", "briefs")


def rd(n):
    return open(os.path.join(NC, n)).read()


def wr(n, t):
    open(os.path.join(HERE, n), "w").write(t)


def common(t):
    return t.replace("src~/neoncomet/codex/", "src~/solarfang/codex/").replace("NeonComet", "SolarFang")


# ---- J1 ----
j1 = rd("j1.md")
a = j1.index("(3) ref_old_state0.png")
b = j1.index("JOB J1:")
j1 = j1[:a] + (
    "(3) ref_old_state0.png: the OLD SolarFang, intact, flat-cartoon style (thick dark outline, flat fills). Its SHAPE and identity are what you keep: a broad solid-orange arrowhead / triangular delta seen from directly above, nose pointing UP, wide flat swept-back body whose rear edge is a jagged zigzag (sawtooth) trailing edge; a long narrow teal crystal canopy down the centre of the nose; in the middle of the hull a large gold OCTAGONAL reactor ring (octagon inside octagon, glowing) with a short dark line under it; a centre spine line; a diagonal seam line down each side; on each wing a long red slash light and at each rear corner a small red/white diamond tip light; and two tall narrow dark gunmetal FANG fins sticking up-and-back behind the wings on both sides (they stand slightly out from the hull, like fangs, one each side), plus one small amber nozzle lip at the centre of the rear edge.\n"
    " (4) ref_old_state1.png and (5) ref_old_state2.png: the old damaged and wreck states, only to show WHERE the damage goes.\n\n"
) + j1[b:]
j1 = j1.replace("a slim crimson delta-wing", "a broad orange arrowhead")
a = j1.index("  key_state0.png")
b = j1.index("Damage is painted INSIDE")
j1 = j1[:a] + (
    "  key_state0.png  = SolarFang INTACT, redrawn in Paragon's painted style.\n"
    "  key_state1.png  = DAMAGED (same ship, same pose, everything of state 0 plus): the left flank panel (left of the reactor ring, lower left of the hull) blown off showing a dark cavity with exposed frame ribs and sparking amber/cyan wires, the reactor ring cracked with a glowing fissure, scorch and soot streaks round it, two or three hairline cracks across the hull, the centre nozzle scorched and smoking.\n"
    "  key_state2.png  = HEAVILY DAMAGED / wreck (everything of state 1 plus): the left fang fin bent and cracked, the reactor housing split open and arcing with a glowing orange core, the right rear tip torn open with jagged dark bites and a glowing orange rim inside the outline, the spine cracked, the canopy cracked (bone-white crack lines across the glass), heavy char and soot over the plating, a few glowing orange embers.\n"
) + j1[b:]
a = j1.index("- Colour identity:")
b = j1.index("- No flames")
j1 = j1[:a] + (
    "- Colour identity: SOLAR ORANGE armour (ramp from near-black brown-maroon through #9A3E12 and #F07A1E to hot #FFC27A / pale yellow highlights), gold octagonal reactor ring with a hot white-yellow core glow, teal-cyan crystal canopy (white-hot core, bright highlight), red slash lights and red/white wingtip diamonds, gunmetal dark navy fang fins, amber nozzle glow. NO pink or magenta anywhere (pink is reserved for the Space world and for enemies). No green on the ship (the background is green and will be keyed out).\n"
) + j1[b:]
j1 = j1.replace("the engine pods", "the nozzle").replace("left engine nozzle scorched", "nozzle scorched")
j1 = j1.replace("lit cyan/amber nozzle rims", "a lit amber nozzle rim")
wr("j1.md", common(j1))

# ---- J1b ----
j = rd("j1b.md")
j = j.replace("the two cream/white slash lights on the wings and the two white-and-red diamond lights on the wingtips painted as unlit dark maroon/navy panel pixels, the gold star mark dimmed",
              "the two red slash lights on the wings and the two white-and-red diamond lights on the wingtips painted as unlit dark maroon/navy panel pixels, the gold octagonal reactor ring dimmed (its inner glow dark)")
j = j.replace("left wing and left engine pod visibly shorter", "left wing and left fang fin visibly shorter")
j = j.replace("(teal canopy, crimson armour, two engine pods with amber nozzle lips)", "")
j = j.replace("the right wing's outer side face", "the right wing's outer side face and fang fin")
wr("j1b.md", common(j))

# ---- J2 ----
j = rd("j2.md")
j = j.replace("NEW NeonComet hull (crimson armour, teal canopy, two engine pods with amber nozzle lips)",
              "NEW SolarFang hull (solar-orange armour, gold reactor ring, teal canopy, one centre nozzle at the rear)")
j = j.replace("OLD NeonComet engine flame, 6 frames on green (a slim red/orange spike with a bone-white core, flickering)",
              "OLD SolarFang engine flame, 7 frames on green (three flaring solar flame tongues: a long centre fang and two shorter side tongues, orange with a bone-white core, flickering)")
j = j.replace("under each of NeonComet's two engine nozzles", "under SolarFang's single centre nozzle")
j = j.replace("one flame sprite under each nozzle", "one flame sprite under the nozzle")
j = j.replace("a red-and-orange comet-tail flame with a bone-white hot core and a few cyan sparks",
              "a solar flame: a long central tongue flanked by two shorter licking tongues, orange and amber with a bone-white hot core and a few orange sparks")
j = j.replace("6 cells", "7 cells").replace("(1536 x 1024 total)", "(1792 x 1024 total)")
j = j.replace("hold times in 24 fps ticks 2,2,1,2,2,2): frames 0,1,3,4,5 are", "hold times in 24 fps ticks 3,2,1,2,2,2,2): frames 0,1,3,4,5,6 are")
j = j.replace("tongues of flame lick", "the side tongues lick")
a = j.index("Use ONLY these five band colours")
b = j.index("Write tools/check_j2.py")
j = j[:a] + ("Use ONLY these five band colours plus the green key: deep burnt orange-brown #9A3E12 (outer edge / dark), orange #F07A1E (outer flame), amber #FFB43C (mid), warm bone-white #F4EAD4 (white-hot core), bright orange #F2862B (sparks / accent). NO pink, violet, magenta or blue fringes. Use IMAGE GENERATION for the painted content, then snap every pixel to the nearest of those five colours (nearest-neighbour only).\n") + j[b:]
j = j.replace("6 equal cells", "7 equal cells").replace("only those 5 colours", "only those 5 colours")
wr("j2.md", common(j))

# ---- skins ----
mk = rd("mk_skin_briefs.py")
head_a = mk.index("head = '''")
head_b = mk.index("body = '''")
head = mk[head_a:head_b]
head = head.replace("(crimson armour, teal canopy, gunmetal engine pods, cream wing slash lights, red-and-white wingtip diamonds)",
                    "(solar-orange armour, gold octagonal reactor ring, teal crystal canopy, gunmetal fang fins, red wing slash lights, red-and-white wingtip diamonds)")
body_b = mk.index("skins = {")
body = mk[head_b:body_b]
body = body.replace("the crimson ARMOUR", "the orange ARMOUR")
skins = '''skins = {
    'Night': ("""- the orange ARMOUR (all orange plating, with its darker shading and yellow highlights) becomes deep midnight indigo-navy metal: base #2A2E6B, shadow #1A1F45, rim-light highlights bright cyan #6EF2EE along the upper-left edges of the plates (neon edge lighting) and a few pale blue-white highlights. The gold reactor ring turns a cyan-white glow (#6EF2EE with a white core). Keep the red slash lights and the wingtip diamonds as they are.""", 12),
    'Neon': ("""- the orange ARMOUR becomes glowing neon TEAL metal: base #1FB5B9, shadow #0F5E6A, highlights #6EF2EE with white-cyan hot edges. The gold reactor ring stays warm gold/yellow so it contrasts. The crystal canopy becomes bright white-cyan so it still reads against the teal hull. Keep the red slash lights and wingtip diamonds as they are.""", 12),
    'Bone': ("""- the orange ARMOUR becomes warm BONE-white ceramic armour: base #F4EAD4, shadow #B9AE98, highlights near-white, with plate seams in a slightly darker warm grey. The red slash lights and the wingtip diamond lights stay bright red (#D8232C with a hot #FF5B45 core); the gold reactor ring stays gold.""", 12),
    'Eclipse': ("""- this is the special SPLIT LIVERY ("Eclipse"): keep the orange armour exactly as it is on the RIGHT half of the ship (right of the centre spine line), but paint the whole LEFT half of the armour (left of the centre spine, wing and flank plates) dark eclipse charcoal-violet metal: base #2C2D40, shadow #1A1A28, highlights #5A5C78, with a thin warm orange rim-light along the split line down the spine (the sun corona). The reactor ring, canopy, lights and fang fins stay unchanged; the damage on the left half keeps its scorch and glowing tears. Nothing else changes colour (so the fraction changed is smaller here).""", 3),
}
for k, (rules, chg) in skins.items():
    open(os.path.join(HERE, 'skin_%s.md' % k), 'w').write(common(head.replace("head = \\'\\'\\'", "")) if False else '')
'''
# simpler: build text directly
exec_ns = {}
exec(head.replace("head = '''", "H = '''", 1) + "\n" + body.replace("body = '''", "B = '''", 1), exec_ns)
H, B = exec_ns["H"], exec_ns["B"]
sk = {}
exec(skins.split("for k, (rules")[0], sk)
for k, (rules, chg) in sk["skins"].items():
    wr("skin_%s.md" % k, common(H + B.format(name=k, rules=rules, chg=chg)))
print("ok")
