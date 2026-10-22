"""Derives skin_<Name>_damage.md (redo of the damaged + wreck sheets only) from skin_<Name>.md."""
import os, sys
here = os.path.dirname(os.path.abspath(__file__))
for n in ("Night", "Sodium", "Bone", "Lance"):
    t = open(os.path.join(here, "skin_%s.md" % n)).read()
    t = t.replace(
        "REFERENCES (attached images, in order): (1) skinref_state0.png, (2) skinref_state1.png, (3) skinref_state2.png: the FINAL approved flipbook, three 3 x 3 sheets (intact, damaged, wreck)",
        "REFERENCES (attached images, in order): (1) skinref_state1.png, (2) skinref_state2.png: the FINAL approved DAMAGED and WRECK flipbook sheets (the wreck has destruction all over both wings, fuselage and pods), (3) skin_%s_state0.png: the already finished %s sheet of the INTACT ship, only the colour reference (3072 x 3072). The reference sheets are" % (n, n))
    t = t.replace("(the wreck has destruction all over both wings, fuselage and pods)",
                  "(a few bold damage features on both sides, the rest of the hull clean: paint exactly those features, add no extra texture or noise)")
    t = t.replace("Each is 1536 x 1536 (cells 512 px).", "Reference sheets 1 and 2 are 1536 x 1536 (cells 512 px).")
    t = t.replace("Redraw each of the three sheets", "Redraw ONLY the damaged and wreck sheets (do NOT touch skin_%s_state0.png)" % n)
    t = t.replace("write skin_%s_state0.png, skin_%s_state1.png, skin_%s_state2.png" % (n, n, n),
                  "write skin_%s_state1_v2.png and skin_%s_state2_v2.png" % (n, n))
    t = t.replace("each cell 1024 x 1024 i.e. 3072 x 3072 per file", "each cell 1024 x 1024 i.e. 3072 x 3072 per file")
    t = t.replace("for each of the three files", "for each of the two files")
    t = t.replace("Everything else (canopy glass",
                  "The bank cells (7 and 8) must stay clean perspective banks exactly like the reference: not folded, crumpled or doubled; all painted as solid opaque pixels, no translucent or ghost patches. Everything else (canopy glass")
    open(os.path.join(here, "skin_%s_damage.md" % n), "w").write(t)
