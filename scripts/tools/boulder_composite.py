#!/usr/bin/env python3
"""Composite each boulder variant over each of the world's backdrop variants at in-game scale (2x of a 512 px-wide backdrop viewport).

    python3 scripts/tools/boulder_composite.py <World> <boulderDir> <out.png> [cell=768]

Rows = backdrop variants v1..v4, columns = the three boulders (at their table widths, placed a little off-centre) so the
art can be judged against the ground it drifts over.  Never any mock UI."""
import sys, os, json, re
from PIL import Image
w, bdir, out = sys.argv[1:4]
cell = int(sys.argv[4]) if len(sys.argv) > 4 else 768
R = "Pause/Assets/Art/Backgrounds/Resources/Worlds/%s/Backdrop3/" % w
src = open("Pause/Assets/Scripts/Gameplay/Cover/CoverBoulderTable.cs").read()
m = re.search(r'world = "%s".*?widths = new\[\] \{ ([^}]*) \}' % w, src, re.S)
widths = [float(x.strip().rstrip("f")) for x in m.group(1).split(",")]
tm = re.search(r'world = "%s"(?:(?!world = ).)*?tint = new\[\] \{ ([^}]*)\},' % w, src, re.S)
tints = [(1, 1, 1)] * 4
if tm and tm.group(1):
    parts = re.findall(r'Color\.white|C\(([^)]*)\)', tm.group(1))
    tints = [(1, 1, 1) if not q else tuple(float(x.strip().rstrip("f")) for x in q.split(",")) for q in parts]
man = json.load(open(bdir + "/manifest.json"))
sheet = Image.open(bdir + "/boulder.png").convert("RGBA")
VW, VH = 1024, 1820            # 2 x (512 x 910): 7.43 u wide, 13.2 u tall -> 138 px/u
PPU = VW / 7.43
tiles = []
vs = sorted(d for d in os.listdir(R) if re.fullmatch(r"v\d", d) and os.path.isdir(R + d))
for r, v in enumerate(vs):
    row = []
    for c in range(3):
        bg = Image.new("RGBA", (512, 1024), (0, 0, 0, 255))
        for layer in ("sky", "far", "mid", "flow"):
            p = "%s%s/%s.png" % (R, v, layer)
            if os.path.exists(p):
                bg.alpha_composite(Image.open(p).convert("RGBA"))
        if os.environ.get('FLASH'):
            from PIL import ImageEnhance
            bg = ImageEnhance.Brightness(bg.convert('RGB')).enhance(float(os.environ['FLASH'])).convert('RGBA')
        bg = bg.crop((0, 40 + 60 * r, 512, 40 + 60 * r + 910)).resize((VW, VH), Image.NEAREST)
        jv = man["variants"][c]
        art = sheet.crop((c * cell, 0, (c + 1) * cell, cell))
        s = widths[c] * PPU / (jv["x1"] - jv["x0"])
        art = art.resize((int(cell * s), int(cell * s)), Image.LANCZOS)
        tr, tg, tb = tints[r] if r < len(tints) else (1, 1, 1)
        if (tr, tg, tb) != (1, 1, 1):
            ch = list(art.split()); ch[0] = ch[0].point(lambda v: int(v * tr)); ch[1] = ch[1].point(lambda v: int(v * tg)); ch[2] = ch[2].point(lambda v: int(v * tb)); art = Image.merge("RGBA", ch)
        bg.alpha_composite(art, (int(VW * (.34 + .16 * (c % 2)) - art.width / 2), int(VH * (.30 + .12 * c) - art.height / 2)))
        row.append(bg.convert("RGB").resize((VW // 2, VH // 2), Image.LANCZOS))
    tiles.append(row)
sheetout = Image.new("RGB", (3 * VW // 2, len(vs) * VH // 2))
for r, row in enumerate(tiles):
    for c, t in enumerate(row):
        sheetout.paste(t, (c * VW // 2, r * VH // 2))
sheetout.save(out)
print("wrote", out, sheetout.size)
