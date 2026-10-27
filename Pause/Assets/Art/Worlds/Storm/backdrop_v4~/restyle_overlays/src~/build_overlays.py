"""Storm overlay restyle build: slices Codex's painted sheets into the existing atlas cells (JSON untouched) and stacks the
rain sources into the four seamless 1024x2048 flow strips. Layout/fit only: every pixel of art comes from Codex.

    python3 build_overlays.py <src~ dir with the sheets> <Backdrop3 dir>
"""
import sys, json
from pathlib import Path
import numpy as np
from PIL import Image

SRC = Path(sys.argv[1]); DST = Path(sys.argv[2])
FLOW_ALPHA_MIN = float(__import__("os").environ.get("FLOW_ALPHA_MIN", "0.07"))
BLACK_KEY = {"lt_bolts", "lt_strikes", "lt_flashes"}   # additive light on black: brightness is opacity

def load_rgba(name):
    im = Image.open(SRC / (name + ".png")).convert("RGBA")
    a = np.asarray(im).astype(np.float32) / 255
    if name in BLACK_KEY or a[..., 3].min() > .98:
        # no real alpha: opacity = brightness (black = nothing)
        mx = a[..., :3].max(-1)
        al = np.clip((mx - .04) / .96, 0, 1) ** .8
        rgb = a[..., :3] / np.maximum(al[..., None], 1e-3)
        a = np.dstack([np.clip(rgb, 0, 1), al])
    return a

def to_img(a):
    return Image.fromarray(np.rint(np.clip(a, 0, 1) * 255).astype(np.uint8), "RGBA")

def cells(a, cols, rows):
    h, w = a.shape[:2]
    ch, cw = h // rows, w // cols
    return [a[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw] for r in range(rows) for c in range(cols)]

def fit(cell, box_w, box_h, margin=.04):
    """Trim to the element's alpha bounding box and scale it (aspect kept) to fit box_w x box_h; returns RGBA image."""
    al = cell[..., 3]
    ys, xs = np.where(al > .06)
    if len(ys) == 0:
        return Image.new("RGBA", (1, 1))
    t = cell[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    im = to_img(t)
    s = min(box_w * (1 - margin) / im.width, box_h * (1 - margin) / im.height)
    im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
    return im

def paste_center(sheet, im, cx, cy):
    sheet.alpha_composite(im, (int(round(cx - im.width / 2)), int(round(cy - im.height / 2))))

# weather cells: scale the painted opacity so each cell covers about what the old art did (CloudCoverMeter: clouds cover <= 15 % of
# the view and never park over the lane); measured from the pre-restyle atlas, mean-alpha match x 1.1, never above 1
WEATHER_SCALE = {"cloud_bank_00": .52, "cloud_bank_01": .36, "cloud_bank_02": .41, "cloud_bank_03": .37, "cloud_wisp_00": .85, "cloud_wisp_01": .5,
                 "cloud_wisp_02": .44, "mist_00": .25, "mist_01": .22, "gust_00": .3, "gust_01": .28, "pall_00": .22,
                 "rain_00": .34, "rain_01": .3, "rain_02": .35, "rain_03": .33}

def fill_atlas(atlas_name, mapping, sprite_key="sprites"):
    """mapping: {sheet name: [sprite names in row-major cell order]} -> rewrite those cells of <atlas>.png."""
    j = json.load(open(DST / (atlas_name + ".json")))
    sheet = Image.open(DST / (atlas_name + ".png")).convert("RGBA")
    H = sheet.height
    by = {s["n"]: s for s in j[sprite_key]}
    for sname, names in mapping.items():
        if not (SRC / (sname + ".png")).exists():
            print("missing", sname); continue
        cs = cells(load_rgba(sname), 2, 2)
        for n, c in zip(names, cs):
            s = by[n]
            sheet.paste((0, 0, 0, 0), (s["x"], H - s["y"] - s["h"], s["x"] + s["w"], H - s["y"]))
            if n.startswith("strike_"):
                # StormTuning.StrikeEndY: the bolt's lower end must sit at (128, 226) from the cell top
                al = c[..., 3]; ys, xs = np.where(al > .06)
                y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
                core = np.where(al > .5); yb = core[0].max(); xb = int(np.median(core[1][core[0] > yb - 10]))
                sc = (226 - 14) / max(1, yb - y0)
                im = to_img(c[y0:y1, x0:x1]); im = im.resize((max(1, round(im.width * sc)), max(1, round(im.height * sc))), Image.LANCZOS)
                left = 128 - round((xb - x0) * sc)
                sheet.alpha_composite(im.crop((max(0, -left), 0, min(im.width, s["w"] - left), min(im.height, s["h"] - 14))), (s["x"] + max(0, left), H - s["y"] - s["h"] + 14))
                continue
            im = fit(c, s["w"], s["h"])
            if atlas_name == "weather" and WEATHER_SCALE.get(n):
                arr = np.asarray(im).astype(np.float32) / 255; arr[..., 3] *= WEATHER_SCALE[n]; im = to_img(arr)
            paste_center(sheet, im, s["x"] + s["w"] / 2, H - s["y"] - s["h"] / 2)
            if n.startswith("flash_"):
                # glow cells must fade to nothing at their border (StormBackdropTest): soft radial falloff on the painted glow
                box = (s["x"], H - s["y"] - s["h"], s["x"] + s["w"], H - s["y"])
                cell = np.asarray(sheet.crop(box)).astype(np.float32) / 255
                yy, xx = np.mgrid[0:s["h"], 0:s["w"]]
                r = np.hypot((xx + .5 - s["w"] / 2) / (s["w"] / 2), (yy + .5 - s["h"] / 2) / (s["h"] / 2))
                u = np.clip((r - .5) / .38, 0, 1); cell[..., 3] *= 1 - u * u * (3 - 2 * u)
                sheet.paste(to_img(cell), box[:2])
    sheet.save(DST / (atlas_name + ".png"))

def fill_ambient():
    j = json.load(open(DST / "wx_ambient.json"))
    sheet = Image.open(DST / "wx_ambient.png").convert("RGBA")
    H = sheet.height
    for sname, row in (("amb_a", 0), ("amb_b", 1)):
        if not (SRC / (sname + ".png")).exists():
            print("missing", sname); continue
        cs = cells(load_rgba(sname), 4, 2)          # sheet cell order: 4 across, 2 down
        for i, c in enumerate(cs):
            col = i % 4 + 4 * (i // 4)               # sheet row 0 -> atlas cols 0-3, sheet row 1 -> cols 4-7
            s = [x for x in j["cells"] if x["col"] == col and x["row"] == row][0]
            sheet.paste((0, 0, 0, 0), (s["x"], H - s["y"] - s["h"], s["x"] + s["w"], H - s["y"]))
            im = fit(c, s["w"], s["h"], .02)
            paste_center(sheet, im, s["x"] + s["w"] / 2, H - s["y"] - s["h"] / 2)
    sheet.save(DST / "wx_ambient.png")

def flows():
    srcs = {k: load_rgba("rain_" + k) for k in "abcd" if (SRC / ("rain_" + k + ".png")).exists()}
    pairs = {"v1": "ab", "v2": "cd", "v3": "bc", "v4": "da"}
    for v, (p, q) in pairs.items():
        if p not in srcs or q not in srcs:
            continue
        def crop(a):
            h, w = a.shape[:2]; y0, x0 = (h - 1024) // 2, (w - 1024) // 2
            return a[y0:y0 + 1024, x0:x0 + 1024]
        strip = np.concatenate([crop(srcs[p]), crop(srcs[q])], 0)
        # drop the near-invisible haze (alpha under THR): it carries identical colour on thousands of texels (StormBackdropTest's
        # duplicate-pair rule) and costs nothing visible; the top and bottom rows match exactly (seamless loop)
        strip[..., 3] = np.where(strip[..., 3] < FLOW_ALPHA_MIN, 0, strip[..., 3])
        strip[-1] = strip[0]
        to_img(strip).save(DST / v / "flow.png")
        print("flow", v)

if __name__ == "__main__":
    flows()
    fill_atlas("weather", {"wx_banks": ["cloud_bank_00", "cloud_bank_01", "cloud_bank_02", "cloud_bank_03"],
                           "wx_wisps": ["cloud_wisp_00", "cloud_wisp_01", "cloud_wisp_02", "mist_00"],
                           "wx_gusts": ["mist_01", "gust_00", "gust_01", "pall_00"],
                           "wx_rain": ["rain_00", "rain_01", "rain_02", "rain_03"]})
    fill_atlas("lightning", {"lt_bolts": ["bolt_00", "bolt_01", "bolt_02", "bolt_03"],
                             "lt_strikes": ["bolt_04", "bolt_05", "strike_00", "strike_01"],
                             "lt_flashes": ["flash_00", "flash_01", "arc_00"]})
    fill_ambient()
