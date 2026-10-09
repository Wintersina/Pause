"""Build Ember run C flipbooks from retained image-generated painted keys."""

from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
NEAREST = Image.Resampling.NEAREST
ANCHOR = (128, 236)  # local pixels from top in every 256px cell

PALETTES = {
    "smoke_a": "1d1a19 292321 38302b 4b3b31 604a38 79553a 9d6137 d28132",
    "smoke_b": "34302d 514b46 706a62 938a80 b3aaa0 d2c9b9 e8e2d3",
    "flare": "201816 41251a 703519 a94c1b db6d1b f99d28 ffcb54 fff2aa",
    "fountain": "201715 42241a 753418 aa491c d76a1f f79a2a ffca50 ffe9a0",
    "eruptsmoke_a": "191716 28201c 382a22 4c3527 68442b 87502b ad632a e18a32",
    "eruptsmoke_b": "282521 413a34 5d5349 78685b 95816c b39c80 d3b998",
    "steam_vent": "514b46 716b65 96918a b6b1a7 d1ccc1 e8e5d9 f9f7eb",
    "pipe_drip": "211816 482918 793b18 a94f1b d8751e f6a32d ffce56 fff0a2",
    "ember_rain": "38251b 77401c b85b17 e9811b ffaa2a ffd354 fff3a4",
    "lava_bubble": "1b1717 39211b 6e2b19 a84418 df6818 f99a25 ffcc54 ffe99b",
    "beacon_amber": "211b1a 493124 806044 bc7930 ef9a2e ffca53 fff1a1",
    "beacon_magenta": "241d26 493044 724060 a25086 ce6da8 f29acc ffe4ea",
    "window_lights": "1a1717 3c2b21 684528 a66b31 dca04a f9c970 ffe5a2 fffff0",
    "strobe_white": "26221e 574b3d 9a8e77 d0c9b5 ece7db fffff4",
}
PALETTES = {k: np.array([tuple(bytes.fromhex(c)) for c in v.split()], dtype=np.uint8)
            for k, v in PALETTES.items()}

# atlas, source sheet, source quadrant indices, count, maximum painted size, alpha cap
SPEC = {
    "smoke_a": ("smoke", "smoke_keys.png", (0, 1), 8, (185, 194), 170),
    "smoke_b": ("smoke", "smoke_keys.png", (2, 3), 8, (143, 181), 170),
    "flare": ("lavafire", "lavafire_keys.png", (0, 1), 8, (103, 177), 244),
    "fountain": ("lavafire", "lavafire_keys.png", (2, 3), 8, (180, 153), 246),
    "eruptsmoke_a": ("eruption", "eruption_keys.png", (0, 1), 8, (207, 214), 174),
    "eruptsmoke_b": ("eruption", "eruption_keys.png", (2, 3), 8, (130, 193), 170),
    "steam_vent": ("leaks", "leaks_keys.png", (0,), 4, (145, 157), 170),
    "pipe_drip": ("leaks", "leaks_keys.png", (1,), 4, (113, 162), 240),
    "ember_rain": ("leaks", "leaks_keys.png", (2,), 4, (145, 143), 240),
    "lava_bubble": ("leaks", "leaks_keys.png", (3,), 4, (171, 105), 238),
    "beacon_amber": ("lights", "lights_keys.png", (0,), 4, (97, 127), 246),
    "beacon_magenta": ("lights", "lights_keys.png", (1,), 4, (97, 127), 246),
    "window_lights": ("lights", "lights_keys.png", (2,), 4, (147, 99), 244),
    "strobe_white": ("lights", "lights_keys.png", (3,), 4, (96, 119), 250),
}

USE = {
    "smoke_a": (8, "refineries, forge towers, and stack mouths", .56),
    "smoke_b": (9, "cooling towers and refinery auxiliary stacks", .57),
    "flare": (12, "flarestack_* flame emitters", .83),
    "fountain": (11, "lavafountain_* and eruptionscar_* molten vents", .78),
    "eruptsmoke_a": (7, "lavafountain_* and eruptionscar_*; behind play objects", .48),
    "eruptsmoke_b": (8, "coalbed_* and scorched_* ground", .54),
    "steam_vent": (10, "pipe flanges, pumphouse, and coolingtower", .65),
    "pipe_drip": (10, "pipe_leak_* and pipe_lava_* rupture mouths", .82),
    "ember_rain": (9, "all lava grounds, sparse rising overlay", .83),
    "lava_bubble": (8, "lavapool_* and coalbed_* surfaces", .74),
    "beacon_amber": (4, "forge towers, derricks, and viaduct", .88),
    "beacon_magenta": (4, "forge towers, derricks, and viaduct", .85),
    "window_lights": (5, "foundries and refineries", .82),
    "strobe_white": (4, "tower warning lights", .80),
}


def quadrant(name: str, index: int) -> Image.Image:
    source = Image.open(SRC / SPEC[name][1]).convert("RGBA")
    w, h = source.size
    x = (index % 2) * w // 2
    y = (index // 2) * h // 2
    panel = np.array(source.crop((x, y, x + w // 2, y + h // 2)))
    yy, xx = np.indices(panel.shape[:2])
    border = np.minimum.reduce((xx, yy, panel.shape[1] - 1 - xx,
                                 panel.shape[0] - 1 - yy))
    panel[:, :, 3] = np.rint(panel[:, :, 3] * np.clip(border / 12, 0, 1)).astype(np.uint8)
    panel[panel[:, :, 3] < 30] = 0
    ys, xs = np.where(panel[:, :, 3] > 45)
    if not len(xs):
        raise ValueError(f"Empty generated key: {name}/{index}")
    return Image.fromarray(panel, "RGBA").crop((max(0, xs.min() - 3), max(0, ys.min() - 3),
             min(panel.shape[1], xs.max() + 4), min(panel.shape[0], ys.max() + 4)))


def clean(image: Image.Image, name: str, cap: int, scale_alpha: bool = True) -> Image.Image:
    a = np.array(image.convert("RGBA"))
    pal = PALETTES[name].astype(np.int32)
    rgb = a[:, :, :3].astype(np.int32)
    distance = ((rgb[:, :, None] - pal[None, None]) ** 2).sum(3)
    a[:, :, :3] = pal[distance.argmin(2)].astype(np.uint8)
    if scale_alpha:
        a[:, :, 3] = np.rint(a[:, :, 3].astype(float) * cap / 255 / 5).astype(np.uint8) * 5
    a[:, :, 3] = np.minimum(a[:, :, 3], cap)
    a[a[:, :, 3] < 10] = 0
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def painted_key(name: str, index: int) -> Image.Image:
    p = quadrant(name, index)
    tw, th = SPEC[name][4]
    factor = min(tw / p.width, th / p.height)
    p = p.resize((max(1, round(p.width * factor)), max(1, round(p.height * factor))), NEAREST)
    p = clean(p, name, SPEC[name][5])
    cell = Image.new("RGBA", (256, 256))
    cell.alpha_composite(p, (ANCHOR[0] - p.width // 2, ANCHOR[1] - p.height))
    return cell


def warp(image: Image.Image, phase: float, strength: float) -> Image.Image:
    """Periodic point-sampled deformation fades to zero at the fixed emitter."""
    a = np.asarray(image)
    yy, xx = np.indices((256, 256))
    altitude = np.clip((ANCHOR[1] - yy) / 205, 0, 1)
    dx = strength * altitude * (np.sin(phase + yy * .039) + .24 * np.sin(2 * phase + yy * .071))
    dy = strength * .40 * altitude * np.cos(phase + xx * .045)
    sx = np.clip(np.rint(xx - dx).astype(int), 0, 255)
    sy = np.clip(np.rint(yy - dy).astype(int), 0, 255)
    return Image.fromarray(a[sy, sx], "RGBA")


def blend(a: Image.Image, b: Image.Image, weight: float, name: str) -> Image.Image:
    aa = np.asarray(a, dtype=float)
    bb = np.asarray(b, dtype=float)
    a_alpha = aa[:, :, 3:4] / 255
    b_alpha = bb[:, :, 3:4] / 255
    alpha = a_alpha * (1 - weight) + b_alpha * weight
    rgb = (aa[:, :, :3] * a_alpha * (1 - weight) +
           bb[:, :, :3] * b_alpha * weight) / np.maximum(alpha, 1e-6)
    out = np.concatenate((rgb, alpha * 255), axis=2).clip(0, 255).astype(np.uint8)
    return clean(Image.fromarray(out, "RGBA"), name, SPEC[name][5], False)


def atmosphere(name: str) -> list[Image.Image]:
    count = SPEC[name][3]
    keys = [painted_key(name, q) for q in SPEC[name][2]]
    result = []
    for i in range(count):
        phase = 2 * math.pi * i / count
        strength = 5.2 if name in ("flare", "fountain", "pipe_drip") else 3.9
        if len(keys) == 2:
            frame = blend(warp(keys[0], phase, strength), warp(keys[1], phase, strength),
                          (1 - math.cos(phase)) / 2, name)
        else:
            frame = warp(keys[0], phase, strength)
            a = np.array(frame)
            if name in ("ember_rain", "pipe_drip", "lava_bubble"):
                # Bright painted pixels pulse while retaining the generated geometry.
                bright = a[:, :, :3].mean(2) > 135
                a[bright, 3] = np.rint(a[bright, 3] * (.83, 1, .91, .72)[i]).astype(np.uint8)
                frame = Image.fromarray(a, "RGBA")
        result.append(frame)
    return result


def lamp_frames(name: str) -> list[Image.Image]:
    key = np.array(painted_key(name, SPEC[name][2][0]))
    yy, xx = np.indices((256, 256))
    # The generated on-state is the source of every pixel of the mast, halo and windows.
    if name.startswith("beacon_"):
        # Bright source halo is above the mast; dim the painted mast in every state.
        halo = yy < 207
        mast = ~halo
        gains = (.012, .30, 1, .18)
        frames = []
        for gain in gains:
            a = key.copy()
            a[halo, 3] = np.rint(a[halo, 3] * gain).astype(np.uint8)
            a[mast, 3] = np.rint(a[mast, 3] * .32).astype(np.uint8)
            frames.append(Image.fromarray(a, "RGBA"))
        return frames
    if name == "window_lights":
        # Independently mask six painted windows on the retained facade.
        luminous = (key[:, :, :3].mean(2) > 110) & (key[:, :, 3] > 0)
        xlo, xhi = np.where(key[:, :, 3] > 50)[1].min(), np.where(key[:, :, 3] > 50)[1].max()
        ylo, yhi = np.where(key[:, :, 3] > 50)[0].min(), np.where(key[:, :, 3] > 50)[0].max()
        xs = np.linspace(xlo + 20, xhi - 20, 3)
        ys = np.linspace(ylo + 29, yhi - 18, 2)
        frames = []
        for selected in ((1,), (0, 3, 4), tuple(range(6)), (2, 5)):
            a = key.copy()
            a[:, :, 3] = np.rint(a[:, :, 3] * .025).astype(np.uint8)
            for j in selected:
                cx, cy = xs[j % 3], ys[j // 3]
                mask = (((xx - cx) / 13) ** 2 + ((yy - cy) / 12) ** 2 < 1) & luminous
                a[mask] = key[mask]
            frames.append(Image.fromarray(a, "RGBA"))
        return frames
    assert name == "strobe_white"
    frames = []
    for gain in (.012, 1, .04, .73):
        a = key.copy()
        a[:, :, 3] = np.rint(a[:, :, 3] * gain).astype(np.uint8)
        frames.append(Image.fromarray(a, "RGBA"))
    return frames


def brightness(image: Image.Image) -> int:
    a = np.asarray(image, dtype=float)
    return round((a[:, :, :3].mean(2) * a[:, :, 3] / 65025).sum())


def build_atlases(frames: dict[str, list[Image.Image]]) -> None:
    for atlas in ("smoke", "lavafire", "eruption", "leaks", "lights"):
        names = [name for name in SPEC if SPEC[name][0] == atlas]
        image = Image.new("RGBA", (1024, 1024))
        sprites = []
        index = 0
        for name in names:
            for frame_i, frame in enumerate(frames[name]):
                x, top = index % 4 * 256, index // 4 * 256
                image.alpha_composite(frame, (x, top))
                sprites.append({"n": f"{name}_{frame_i:02d}", "x": x,
                                "y": 1024 - top - 256, "w": 256, "h": 256})
                index += 1
        assert index == 16
        image.save(ROOT / f"{atlas}.png", optimize=True)
        (ROOT / f"{atlas}.json").write_text(json.dumps({"sprites": sprites}, indent=2) + "\n")


def append_manifest() -> None:
    path = ROOT / "manifest.json"
    data = json.loads(path.read_text())
    loops = []
    for name, (atlas, _, _, count, _, _) in SPEC.items():
        fps, use, alpha = USE[name]
        loops.append({"name": name, "atlas": f"{atlas}.png",
                      "frames": [f"{name}_{i:02d}" for i in range(count)],
                      "recommended_fps": fps, "anchor_px_from_top": list(ANCHOR),
                      "intended_use": use, "recommended_draw_alpha": alpha})
    data["run_c"] = {
        "build": "python3 src~/build_c.py", "verify": "python3 src~/verify_c.py",
        "preview": "preview_c.png", "animated_preview": "preview_c.gif",
        "source_key_paintings": [f"src~/{n}_keys.png" for n in
                                 ("smoke", "lavafire", "eruption", "leaks", "lights")],
        "atlas_grid": "Each 1024x1024 RGBA atlas has a 4x4 grid of 256x256 cells; JSON y is from image bottom.",
        "emitter_anchor": "Every ground-attached loop anchors at local (128,236) pixels from cell top; place on the corresponding run B emitter point.",
        "art_method": "Built-in image generation painted 2x2 transparent key sheets; inspected keys were palette cleaned with nearest-neighbor sampling and cyclic deformation/blending.",
        "loops": loops,
    }
    path.write_text(json.dumps(data, indent=2) + "\n")


def crop_piece(atlas: str, name: str) -> Image.Image:
    sprites = json.loads((ROOT / f"{atlas}.json").read_text())["sprites"]
    r = next(s for s in sprites if s["n"] == name)
    y = 1024 - r["y"] - r["h"]
    return Image.open(ROOT / f"{atlas}.png").convert("RGBA").crop((r["x"], y, r["x"] + r["w"], y + r["h"]))


def previews(frames: dict[str, list[Image.Image]]) -> None:
    ground = Image.open(ROOT / "v1/mid.png").convert("RGBA")
    bg = ground.crop((176, 432, 336, 592))
    bg = Image.blend(bg, Image.new("RGBA", bg.size, (25, 17, 13, 255)), .45)
    sheet = Image.new("RGB", (1536, len(SPEC) * 204), (24, 17, 14))
    draw = ImageDraw.Draw(sheet)
    for row, name in enumerate(SPEC):
        scores = [brightness(f) for f in frames[name]]
        caption = name
        if name in ("beacon_amber", "beacon_magenta", "window_lights", "strobe_white"):
            caption += f"  brightness {scores}  ratio {max(scores)/max(1,min(scores)):.2f}"
        draw.text((10, row * 204 + 3), caption, fill=(244, 218, 169))
        for i, frame in enumerate(frames[name]):
            patch = bg.copy()
            patch.alpha_composite(frame.resize((160, 160), NEAREST))
            sheet.paste(patch.convert("RGB"), (i * 188, row * 204 + 24))
            draw.text((i * 188 + 4, row * 204 + 186), f"{i:02d}", fill=(230, 203, 151))
    sheet.save(ROOT / "preview_c.png", optimize=True)

    base = ground.resize((600, 1300), NEAREST)
    fixed = [
        ("landmarks", "refinery_00", 25, 110, .65),
        ("landmarks", "forgetower_00", 310, 50, .66),
        ("landmarks", "coolingtower_00", 60, 480, .61),
        ("landmarks", "derrick_00", 365, 840, .63),
        ("pipes", "pipe_straight_00", 150, 360, .62),
        ("pipes", "pipe_leak_00", 285, 665, .61),
        ("pipes", "pumphouse_00", 35, 920, .61),
        ("fires", "lavapool_00", 20, 1080, .76),
        ("fires", "lavafountain_00", 335, 1085, .66),
    ]
    positions = {
        "smoke_a": (122, 175, .69), "smoke_b": (167, 523, .53),
        "flare": (400, 215, .53), "fountain": (430, 1153, .63),
        "eruptsmoke_a": (85, 1080, .74), "eruptsmoke_b": (475, 1000, .60),
        "steam_vent": (127, 954, .58), "pipe_drip": (410, 716, .55),
        "ember_rain": (266, 1093, .68), "lava_bubble": (144, 1200, .56),
        "beacon_amber": (404, 167, .48), "beacon_magenta": (450, 902, .46),
        "window_lights": (126, 278, .50), "strobe_white": (480, 115, .43),
    }
    result = []
    for tick in range(16):
        scene = base.copy()
        for atlas, name, x, y, scale in fixed:
            p = crop_piece(atlas, name)
            size = round(256 * scale)
            scene.alpha_composite(p.resize((size, size), NEAREST), (x, y))
        for name, (x, y, scale) in positions.items():
            p = frames[name][tick % len(frames[name])].copy()
            size = round(256 * scale)
            p = p.resize((size, size), NEAREST)
            scene.alpha_composite(p, (round(x - size / 2), round(y - 236 * scale)))
        result.append(scene.convert("RGB").quantize(colors=192, dither=Image.Dither.NONE))
    result[0].save(ROOT / "preview_c.gif", save_all=True, append_images=result[1:],
                   duration=110, loop=0, disposal=2, optimize=False)


if __name__ == "__main__":
    frames = {name: (lamp_frames(name) if SPEC[name][0] == "lights" else atmosphere(name))
              for name in SPEC}
    build_atlases(frames)
    append_manifest()
    previews(frames)
    for name in ("beacon_amber", "beacon_magenta", "window_lights", "strobe_white"):
        scores = [brightness(f) for f in frames[name]]
        print(f"{name}: {scores}, ratio {max(scores)/min(scores):.2f}")
