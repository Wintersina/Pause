"""Rebuild only the ten Ember redo layers from new imagegen paintings.

The paired painted sources are kept alongside this script. Processing uses
nearest-neighbour resizing, a small discrete palette, and paired wrap joins.
"""

from __future__ import annotations

import hashlib
import json
import colorsys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from build import H, W, NAMES, phone, stats, wrap_blend


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
BACKUP = SRC / "originals_v1"
CHANGED = {1: ("sky", "far"), 2: ("sky", "far"),
           3: ("sky", "far", "mid", "flow"), 4: ("sky", "far")}
TARGET = {1: {"sky": .402, "far": .441},
          2: {"sky": .402, "far": .441},
          3: {"sky": .402, "far": .441, "mid": .475},
          4: {"sky": .322, "far": .359}}
SOURCE = {"sky": "a", "far": "b", "mid": "a"}
CONTRAST = {"sky": (.055, .095, .14, .23, .60),
            "far": (.05, .11, .17, .27, .68),
            "mid": (.045, .11, .175, .285, .76)}


def painted(variant: int, role: str) -> np.ndarray:
    path = SRC / f"v{variant}_redo_terrain_{SOURCE[role]}.png"
    im = Image.open(path).convert("RGB")
    width = min(im.width, im.height // 2)
    x = (im.width - width) // 2
    y = (im.height - 2 * width) // 2
    im = im.crop((x, y, x + width, y + 2 * width))
    return np.asarray(im.resize((W, H), Image.Resampling.NEAREST)).copy()


def hue_guard(rgb: np.ndarray, role: str) -> np.ndarray:
    hsv = np.asarray(Image.fromarray(rgb, "RGB").convert("HSV")).copy()
    hue = hsv[:, :, 0].astype(float) * 360 / 255
    sat = hsv[:, :, 1]
    val = hsv[:, :, 2]
    # Keep the painted amber, gold and rare warning lamps. Warm the dark
    # neutral stone with a violet-brown shadow tint, especially in sky.
    violet = (hue >= 275) & (hue <= 322)
    warm = (hue >= 26) & (hue <= 65)
    grey = sat < 12
    shadow = (val < 70) & (sat < 75)
    hsv[:, :, 0][shadow] = round(303 * 255 / 360)
    hsv[:, :, 1][shadow] = np.maximum(sat[shadow], 32 if role == "sky" else 23)
    replace = ~(violet | warm | grey | shadow)
    hsv[:, :, 0][replace] = round(34 * 255 / 360)
    hsv[:, :, 1][violet] = np.minimum(sat[violet], 90)
    scale = {"sky": .77, "far": .87, "mid": .96}[role]
    hsv[:, :, 1] = np.uint8(np.rint(hsv[:, :, 1] * scale))
    # At near-black values integer RGB rounding can turn a violet-brown tint
    # into a formally red hue. Those pixels have no visible chroma anyway.
    hsv[:, :, 1][val < 36] = 0
    return np.asarray(Image.fromarray(hsv, "HSV").convert("RGB")).copy()


def remap_value(rgb: np.ndarray, target: float, role: str) -> np.ndarray:
    hsv = np.asarray(Image.fromarray(rgb, "RGB").convert("HSV")).copy()
    v = hsv[:, :, 2].astype(np.float32) / 255
    q10, q50, q90, q99 = np.percentile(v, [10, 50, 90, 99])
    low, middle, shoulder, penumbra, high = CONTRAST[role]
    # The source paintings supply every visible shape; this transfer makes
    # darker depth planes while retaining hot individual fissures and lamps.
    q75, q85 = np.percentile(v, [75, 85])
    mapped = np.interp(v, [0, q10, q50, q75, q85, q90, q99, 1],
                       [.025, low, middle, shoulder, penumbra,
                        target, high, min(.86, high + .07)])
    hsv[:, :, 2] = np.uint8(np.rint(mapped * 255))
    return np.asarray(Image.fromarray(hsv, "HSV").convert("RGB")).copy()


def indexed(rgb: np.ndarray, colors: int) -> np.ndarray:
    return np.asarray(Image.fromarray(rgb, "RGB").quantize(
        colors=colors, method=Image.Quantize.MEDIANCUT,
        dither=Image.Dither.NONE).convert("RGB")).copy()


def finish_ground(variant: int, role: str) -> Image.Image:
    rgb = painted(variant, role)
    source_hsv = np.asarray(Image.fromarray(rgb, "RGB").convert("HSV"))
    source_hue = source_hsv[:, :, 0].astype(float) * 360 / 255
    warning_lamps = ((source_hue >= 275) & (source_hue <= 325) &
                     (source_hsv[:, :, 1] > 95) & (source_hsv[:, :, 2] > 70))
    if role == "sky":
        rgb = np.roll(rgb, (193, 37), axis=(0, 1))
        warning_lamps = np.roll(warning_lamps, (193, 37), axis=(0, 1))
    rgb = hue_guard(rgb, role)
    # Saturate only the hottest painted veins; neutral ash and rooftops stay
    # charcoal. This keeps the measured value while restoring lava colour.
    hsv = np.asarray(Image.fromarray(rgb, "RGB").convert("HSV")).copy()
    hot = (hsv[:, :, 2] >= round(TARGET[variant][role] * 255 * .76)) & (hsv[:, :, 1] > 45)
    hsv[:, :, 1][hot] = np.maximum(hsv[:, :, 1][hot], 158 if role != "sky" else 145)
    rgb = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB")).copy()
    rgb = remap_value(rgb, TARGET[variant][role], role)
    rgb = indexed(rgb, 56 if role == "sky" else 64)
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    rgba = wrap_blend(rgba, 78)
    rgb = indexed(rgba[:, :, :3], 64)
    # Quantization and edge blending shift p90 slightly; calibrate once.
    v = rgb.max(axis=2)
    p90 = np.percentile(v, 90) / 255
    factor = TARGET[variant][role] / p90
    rgb = np.uint8(np.rint(np.clip(rgb.astype(np.float32) * factor, 0, 255)))
    rgb = hue_guard(rgb, role)
    points = np.argwhere(warning_lamps)
    if len(points):
        points = points[np.linspace(0, len(points) - 1, min(16, len(points)),
                                    dtype=int)]
        rgb[points[:, 0], points[:, 1]] = (142, 59, 127)
    rgba[:, :, :3] = rgb
    rgba[:, -1] = rgba[:, 0]
    rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


def finish_flow() -> Image.Image:
    images = []
    for letter in "ab":
        im = Image.open(SRC / f"v3_redo_flow_{letter}.png").convert("RGBA")
        width = min(im.width, im.height // 2)
        x = (im.width - width) // 2
        y = (im.height - width * 2) // 2
        images.append(np.asarray(im.crop((x, y, x + width, y + 2 * width)).resize(
            (W, H), Image.Resampling.NEAREST)).copy())
    b = np.roll(images[1], (241, 77), axis=(0, 1))
    a = images[0]
    # Keep only painted opaque cores so the overlay stays a sparse ash gust.
    aa = np.where(a[:, :, 3] > 145, 100, 0)
    ba = np.where(b[:, :, 3] > 175, 76, 0)
    take_b = ba > aa
    alpha = np.maximum(aa, ba).astype(np.uint8)
    rgb = np.where(take_b[:, :, None], b[:, :, :3], a[:, :, :3])
    rgb = hue_guard(rgb, "far")
    rgb = indexed(rgb, 40)
    rgb[alpha == 0] = 0
    rgba = wrap_blend(np.dstack((rgb, alpha)), 70)
    levels = np.array([0, 48, 76, 100], dtype=np.uint8)
    rgba[:, :, 3] = levels[np.abs(rgba[:, :, 3, None].astype(np.int16)
                                - levels).argmin(axis=2)]
    active = rgba[:, :, 3] > 0
    p90 = np.percentile(rgba[:, :, :3].max(axis=2)[active], 90) / 255
    rgba[:, :, :3] = np.uint8(np.rint(np.clip(
        rgba[:, :, :3].astype(np.float32) * (.445 / p90), 0, .70 * 255)))
    rgba[:, :, :3][rgba[:, :, 3] == 0] = 0
    rgba[:, -1] = rgba[:, 0]
    rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


def preview() -> None:
    # Four 1080px columns. Original 1x layers at top, phone composites next,
    # then every changed layer paired at 1x before/after below.
    sheet = Image.new("RGB", (4320, 8650), (22, 17, 22))
    draw = ImageDraw.Draw(sheet)
    for variant in range(1, 5):
        x0 = (variant - 1) * 1080
        layers = {r: Image.open(ROOT / f"v{variant}" / f"{r}.png").convert("RGBA")
                  for r in ("sky", "far", "mid", "flow")}
        draw.text((x0 + 12, 9), f"V{variant}  {NAMES[variant]}", fill=(228, 190, 139))
        for role, x, y in (("sky", 0, 32), ("far", 542, 32),
                           ("mid", 0, 1065), ("flow", 542, 1065)):
            tile = Image.new("RGBA", (W, H), (33, 26, 33, 255))
            tile.alpha_composite(layers[role])
            sheet.paste(tile.convert("RGB"), (x0 + x, y))
            draw.text((x0 + x + 5, y + 5), role.upper(), fill=(240, 205, 152))
        sheet.paste(phone(layers).convert("RGB"), (x0, 2105))
        for index, role in enumerate(("sky", "far", "mid", "flow")):
            if role not in CHANGED[variant]:
                continue
            y = 4530 + index * 1030
            old = Image.open(BACKUP / f"v{variant}_{role}.png").convert("RGBA")
            for label, im, x in (("BEFORE", old, 0), ("AFTER", layers[role], 542)):
                tile = Image.new("RGBA", (W, H), (33, 26, 33, 255))
                tile.alpha_composite(im)
                sheet.paste(tile.convert("RGB"), (x0 + x, y))
                draw.text((x0 + x + 5, y + 5), f"{role.upper()} {label}",
                          fill=(245, 210, 157))
    sheet.save(ROOT / "preview_variants_v2.png", optimize=True)


def verify_exports(entries: list[dict], untouched: dict[str, str]) -> None:
    measured = {}
    for entry in entries:
        path = ROOT / entry["file"]
        im = Image.open(path)
        assert im.mode == "RGBA" and im.size == (W, H), path
        a = np.asarray(im)
        assert np.array_equal(a[:, 0], a[:, -1]), path
        assert np.array_equal(a[0], a[-1]), path
        active = a[:, :, 3] > 0
        p90 = float(np.percentile(a[:, :, :3].max(axis=2)[active] / 255, 90))
        lo, hi = (.32, .42) if entry["variant"] == "v4" else (.40, .50)
        assert lo <= p90 <= hi, (path, p90)
        assert abs(p90 - entry["p90_hsv_value_opaque"]) < .0002, path
        hsv = np.asarray(im.convert("RGB").convert("HSV"))
        hue = hsv[:, :, 0].astype(float) * 360 / 255
        sat = hsv[:, :, 1].astype(float) / 255
        forbidden = ((hue < 22) | (hue > 338) | ((hue > 65) & (hue < 270)))
        assert not (forbidden & (sat > .08) & active).any(), path
        player_hue = colorsys.rgb_to_hsv(1, 62 / 255, 78 / 255)[0] * 360
        hue_distance = np.abs((hue - player_hue + 180) % 360 - 180)
        assert not ((hue_distance < 22) & (sat > .08) & active).any(), path
        if entry["role"] == "flow":
            assert .005 < active.mean() < .25 and a[:, :, 3].max() < 255, path
        else:
            assert active.all(), path
        measured.setdefault(entry["variant"], {})[entry["role"]] = p90
    for variant, roles in measured.items():
        assert roles["sky"] < roles["far"] < roles["mid"], variant
    for path, digest in untouched.items():
        assert hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest, path


def main() -> None:
    untouched = [ROOT / f"v{v}" / f"{r}.png" for v in range(1, 5)
                 for r in ("sky", "far", "mid", "flow") if r not in CHANGED[v]]
    before = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in untouched}
    manifest_path = ROOT / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    redo_files = {}
    for variant, roles in CHANGED.items():
        for role in roles:
            im = finish_flow() if role == "flow" else finish_ground(variant, role)
            path = ROOT / f"v{variant}" / f"{role}.png"
            im.save(path, optimize=True)
            data = stats(im)
            redo_files[str(path.relative_to(ROOT))] = data
            for entry in manifest["files"]:
                if entry["file"] == str(path.relative_to(ROOT)):
                    entry.update(data)
                    entry["notes"] = {
                        "sky": "Deep dark basalt distance with violet-brown haze, fine lava veins and tiny forge lights.",
                        "far": "Distant dark volcanic ground with subdued hot amber channels and lit ruins.",
                        "mid": "Dark wind-carved ash dunes with narrow ember rivers, glowing vents and buried ruins.",
                        "flow": "Sparse transparent painted wind-blown ash streaks and glowing ember specks."
                    }[role]
            print(path.relative_to(ROOT), data)
    verify_exports(manifest["files"], before)
    manifest["preview"] = "preview_variants_v2.png"
    manifest["source_method"] = ("Original built-in imagegen backdrop set, with ten layers rebuilt "
                                 "from ten new built-in imagegen paintings; nearest-neighbour "
                                 "crop, discrete palette, depth values and two-axis wrap joins")
    manifest["redo"] = {
        "source_method": "Ten built-in imagegen paintings: two terrain candidates per variant and two transparent v3 flow candidates; nearest-neighbour resize, discrete palette, two-axis wrap blending",
        "builder": "src~/redo.py",
        "source_candidates": {f"v{v}": [f"src~/v{v}_redo_terrain_a.png",
                                        f"src~/v{v}_redo_terrain_b.png"] for v in range(1, 5)},
        "v3_flow_candidates": ["src~/v3_redo_flow_a.png", "src~/v3_redo_flow_b.png"],
        "originals": "src~/originals_v1/",
        "changed_files": list(redo_files),
        "measured_p90_per_file": {entry["file"]: entry["p90_hsv_value_opaque"]
                                  for entry in manifest["files"]},
        "untouched_sha256": {str(Path(p).relative_to(ROOT)): digest
                             for p, digest in before.items()},
    }
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    preview()


if __name__ == "__main__":
    main()
