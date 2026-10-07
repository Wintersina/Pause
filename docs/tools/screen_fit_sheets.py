#!/usr/bin/env python3
"""Contact sheets for the screen-fit suite.

    Unity -batchmode -quit -projectPath Pause -executeMethod ScreenFitSheets.Run
    python3 docs/tools/screen_fit_sheets.py <SCREEN_FIT_DIR>/<label>

Reads <dir>/results.json and <dir>/shots/*.png (written by ScreenFitSheets) and
writes, into <dir>/sheets/:

    <screen>.png   every device of the matrix side by side, with the safe area
                   (green), cutouts (red), rounded corners (red wedges), the
                   home indicator (white pill) and every failing rect (red box;
                   amber = waived) drawn on top
    matrix.md      screen x device: failing-check counts
    findings.md    every distinct finding and the devices it shows on
"""
import json
import math
import os
import re
import sys
from collections import OrderedDict, defaultdict

from PIL import Image, ImageDraw, ImageFont

CELL_H = 620
LABEL_H = 54
GAP = 14
COLUMNS = 11
BG = (18, 18, 24)


def font(size):
    for path in ("/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/System/Library/Fonts/Helvetica.ttc",
                 "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                pass
    return ImageFont.load_default()


def overlay(img, dev, shot):
    """Draws the device furniture and the findings on a frame (img is the scaled frame)."""
    k = img.height / dev["h"]
    w, h = img.size
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    def box(x, y, bw, bh):   # Unity bottom-left px -> image coords
        return [x * k, h - (y + bh) * k, (x + bw) * k, h - y * k]

    # rounded corners: the part of each corner square outside the arc
    r = dev["cornerRadius"] * k
    if r > 1:
        mask = Image.new("L", img.size, 0)
        md = ImageDraw.Draw(mask)
        md.rectangle([0, 0, w, h], fill=150)
        md.rounded_rectangle([0, 0, w - 1, h - 1], radius=r, fill=0)
        red = Image.new("RGBA", img.size, (255, 40, 60, 255))
        layer.paste(red, (0, 0), mask)

    # safe area
    sx, sy = dev["left"], dev["bottom"]
    sw, sh = dev["w"] - dev["left"] - dev["right"], dev["h"] - dev["top"] - dev["bottom"]
    if dev["top"] or dev["bottom"] or dev["left"] or dev["right"]:
        d.rectangle(box(sx, sy, sw, sh), outline=(60, 255, 120, 230), width=2)

    for c in dev["cutouts"]:
        b = box(c["x"], c["y"], c["width"], c["height"])
        rad = min(b[2] - b[0], b[3] - b[1]) / 2
        d.rounded_rectangle(b, radius=rad, fill=(255, 40, 60, 200), outline=(255, 255, 255, 255), width=1)

    if dev["homeBar"]:
        b = box(dev["homeX"], dev["homeY"], dev["homeW"], dev["homeH"])
        d.rounded_rectangle([b[0], b[1] - 1, b[2], b[3] + 1], radius=3, fill=(255, 255, 255, 235))

    for f in shot["findings"]:
        if f["w"] <= 0 or f["h"] <= 0:
            continue
        colour = (255, 190, 40, 255) if f.get("waived") else (255, 30, 30, 255)
        b = box(f["x"], f["y"], f["w"], f["h"])
        b = [max(-2, b[0]), max(-2, b[1]), min(w + 1, b[2]), min(h + 1, b[3])]
        if b[2] - b[0] < 2 or b[3] - b[1] < 2:
            continue
        d.rectangle(b, outline=colour, width=2)

    return Image.alpha_composite(img.convert("RGBA"), layer).convert("RGB")


def main():
    root = sys.argv[1]
    data = json.load(open(os.path.join(root, "results.json")))
    devices = OrderedDict((d["id"], d) for d in data["devices"])
    shots = defaultdict(dict)
    for s in data["shots"]:
        shots[s["screen"]][s["device"]] = s
    out = os.path.join(root, "sheets")
    os.makedirs(out, exist_ok=True)
    f_label, f_small, f_title = font(15), font(12), font(26)

    for screen in data["screens"]:
        sid = screen["id"]
        cells = []
        for did, dev in devices.items():
            shot = shots[sid].get(did)
            if shot is None or not shot.get("file"):
                continue
            img = Image.open(os.path.join(root, "shots", shot["file"])).convert("RGB")
            scale = CELL_H / img.height
            img = img.resize((max(1, round(img.width * scale)), CELL_H), Image.LANCZOS)
            cells.append((dev, shot, overlay(img, dev, shot)))
        if not cells:
            continue
        rows = [cells[i:i + COLUMNS] for i in range(0, len(cells), COLUMNS)]
        width = max(sum(max(c[2].width, 150) + GAP for c in row) for row in rows) + GAP
        height = 56 + len(rows) * (CELL_H + LABEL_H + GAP) + GAP
        sheet = Image.new("RGB", (width, height), BG)
        d = ImageDraw.Draw(sheet)
        d.text((GAP, 14), "%s  -  %s   [%s]" % (sid, screen["title"], data["label"]), fill=(240, 240, 240), font=f_title)
        y = 56
        for row in rows:
            x = GAP
            for dev, shot, img in row:
                cw = max(img.width, 150)
                sheet.paste(img, (x + (cw - img.width) // 2, y))
                fails = shot["failures"]
                d.text((x, y + CELL_H + 4), dev["name"], fill=(235, 235, 235), font=f_small)
                d.text((x, y + CELL_H + 19), "%dx%d  %s" % (dev["w"], dev["h"], dev["cutoutKind"]), fill=(160, 160, 170), font=f_small)
                d.text((x, y + CELL_H + 34), "PASS" if fails == 0 else "%d FAIL" % fails,
                       fill=(90, 230, 130) if fails == 0 else (255, 80, 80), font=f_label)
                x += cw + GAP
            y += CELL_H + LABEL_H + GAP
        sheet.save(os.path.join(out, sid + ".png"))

    # matrix
    with open(os.path.join(out, "matrix.md"), "w") as m:
        ids = list(devices.keys())
        m.write("| screen | " + " | ".join(ids) + " |\n|---|" + "---|" * len(ids) + "\n")
        for screen in data["screens"]:
            row = []
            for did in ids:
                s = shots[screen["id"]].get(did)
                row.append("-" if s is None else ("ok" if s["failures"] == 0 else str(s["failures"])))
            m.write("| " + screen["id"] + " | " + " | ".join(row) + " |\n")
        total = sum(s["failures"] for per in shots.values() for s in per.values())
        failing = sum(1 for per in shots.values() for s in per.values() if s["failures"])
        cells_n = sum(len(per) for per in shots.values())
        m.write("\n%d failing checks; %d of %d screen x device cells fail\n" % (total, failing, cells_n))

    # findings, grouped
    groups = OrderedDict()
    for screen in data["screens"]:
        for did in devices:
            s = shots[screen["id"]].get(did)
            if s is None:
                continue
            for f in s["findings"]:
                element = re.sub(r"\d+(\.\d+)?", "#", f["element"]) if f["kind"] in ("BARE",) else f["element"]
                key = (screen["id"], f["kind"], element, bool(f.get("waived")))
                groups.setdefault(key, {"devices": [], "detail": f["detail"], "waiver": f.get("waiver", "")})
                if did not in groups[key]["devices"]:
                    groups[key]["devices"].append(did)
    with open(os.path.join(out, "findings.md"), "w") as m:
        last = None
        for (sid, kind, element, waived), g in groups.items():
            if sid != last:
                m.write("\n## %s\n" % sid)
                last = sid
            m.write("- %s%s  `%s`  on %d: %s\n    e.g. %s%s\n" % (
                "(waived) " if waived else "", kind, element, len(g["devices"]), ", ".join(g["devices"]), g["detail"],
                ("\n    waived: " + g["waiver"]) if waived else ""))
    print("sheets ->", out)


if __name__ == "__main__":
    main()
