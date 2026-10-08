"""Assemble painted Frost run C key poses into seamless, point-sampled loops."""

from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from collections import deque

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 256
ANCHOR = (128, 236)  # Pixel coordinates from the top of an atlas cell.
R = Image.Resampling.NEAREST

SOURCES = {
    "smoke": ("smoke_keys.png", 4, 2),
    "steam": ("steam_keys.png", 4, 2),
    "fire_lights": ("fire_lights_keys.png", 4, 2),
    "beacons": ("beacons_keys.png", 4, 4),
    "aurora": ("aurora_keys.png", 4, 2),
}

PALETTES = {
    "smoke_a": ["172032", "272d43", "363b54", "4a4d68", "65647a", "807b8f", "9d91a0", "75506e", "a66583"],
    "smoke_b": ["2e3b54", "40516b", "596b81", "73869a", "91a5b6", "b5c7d1", "d7e1e6", "a3c4d0"],
    "steam_vent": ["27435a", "3e657c", "598aa4", "77b0c9", "9bd0df", "c1e9ed", "e0f4f3", "7ad5e1"],
    "geyser": ["163650", "245477", "307eaa", "51a8cb", "73cbe1", "a7e2ec", "d7f2f2", "f2faf5"],
    "flare": ["382138", "673253", "9b3d62", "ba5a45", "de7837", "fba43b", "ffd471", "fff3c2"],
    "searchlight": ["2b5870", "3e809a", "60aabd", "8bd0d8", "bce9e8", "e0f4f1"],
    "beacon_magenta": ["172638", "344057", "66466e", "985287", "ca6da8", "eea9d7", "fae4f3"],
    "beacon_cyan": ["172638", "304d65", "397d9b", "43add0", "71d8e9", "b3f0f4", "e3fbf8"],
    "strobe_white": ["172638", "344b61", "597d96", "94b9ca", "c2e1e8", "f5faf6"],
    "window_lights": ["172638", "35485d", "56738a", "658e9d", "88bfca", "c18c4d", "e8bc6a", "f5d9a0"],
    "aurora": ["10233e", "1a3856", "244969", "305a72", "376f79", "478882", "5a9e99", "397b8e", "544878", "6a6290", "4e4f82", "82aabd"],
}
PALETTES = {k: np.array([tuple(bytes.fromhex(c)) for c in colors], dtype=np.uint8)
            for k, colors in PALETTES.items()}

LOOPS = {
    "smoke_a": ("smoke", 0, 8, (166, 186), 147),
    "smoke_b": ("smoke", 1, 8, (141, 172), 132),
    "steam_vent": ("steam", 0, 8, (116, 108), 148),
    "geyser": ("steam", 1, 8, (123, 186), 167),
    "flare": ("fire_lights", 0, 8, (70, 110), 222),
    "beacon_magenta": ("beacons", 0, 4, (48, 77), 225),
    "beacon_cyan": ("beacons", 1, 4, (48, 77), 225),
    "strobe_white": ("beacons", 2, 4, (52, 82), 240),
    "window_lights": ("beacons", 3, 4, (105, 90), 194),
    "aurora": ("aurora", 0, 16, (228, 115), 39),
}


def panel(atlas: str, row: int, col: int) -> Image.Image:
    name, cols, rows = SOURCES[atlas]
    im = Image.open(SRC / name).convert("RGBA")
    x0, x1 = round(col * im.width / cols), round((col + 1) * im.width / cols)
    y0, y1 = round(row * im.height / rows), round((row + 1) * im.height / rows)
    a = np.array(im.crop((x0, y0, x1, y1)))
    yy, xx = np.indices(a.shape[:2])
    edge = np.minimum.reduce((xx, yy, a.shape[1]-1-xx, a.shape[0]-1-yy))
    a[:, :, 3] = np.rint(a[:, :, 3] * np.clip(edge / 13, 0, 1)).astype(np.uint8)
    if atlas == "steam" and row == 1:
        # The generated tallest eruption reaches its source-panel boundary.
        # Taper its top into staggered icy wisps instead of a horizontal cut.
        stagger = 13*np.sin(xx*.075) + 8*np.sin(xx*.19)
        fade = np.clip((yy + stagger) / 77, 0, 1)
        a[:, :, 3] = np.rint(a[:, :, 3] * fade).astype(np.uint8)
    if atlas == "smoke" and row == 1:
        # Adjacent source paintings left isolated particles in this panel.
        # Keep the painted plume and its nearby vapor flecks.
        solid = a[:, :, 3] > 43
        seen = np.zeros(solid.shape, bool)
        largest: list[tuple[int, int]] = []
        for sy, sx in np.argwhere(solid):
            if seen[sy, sx]:
                continue
            q = deque([(int(sy), int(sx))])
            seen[sy, sx] = True
            component = []
            while q:
                py, px = q.popleft()
                component.append((py, px))
                for ny, nx in ((py-1,px),(py+1,px),(py,px-1),(py,px+1)):
                    if 0 <= ny < solid.shape[0] and 0 <= nx < solid.shape[1] and solid[ny,nx] and not seen[ny,nx]:
                        seen[ny,nx] = True
                        q.append((ny,nx))
            if len(component) > len(largest):
                largest = component
        connected = np.zeros(solid.shape, np.uint8)
        for py, px in largest:
            connected[py, px] = 255
        near = np.asarray(Image.fromarray(connected).filter(ImageFilter.MaxFilter(45))) > 0
        a[~near, 3] = 0
    a[a[:, :, 3] < 26, 3] = 0
    # Discard the almost invisible generated fringe before positioning.
    mask = a[:, :, 3] >= 48
    if not mask.any():
        raise ValueError(f"No painted content in {atlas}:{row},{col}")
    ys, xs = np.where(mask)
    return Image.fromarray(a, "RGBA").crop((max(0, xs.min()-3), max(0, ys.min()-3),
                                            min(a.shape[1], xs.max()+4), min(a.shape[0], ys.max()+4)))


def recolor(im: Image.Image, name: str, cap: int) -> Image.Image:
    a = np.array(im.convert("RGBA"))
    pal = PALETTES[name]
    rgb = a[:, :, :3].astype(np.int16)
    # A fixed 6-12 tone loop palette keeps the painted value clusters but
    # eliminates generated near-duplicates and soft color gradients.
    dist = ((rgb[:, :, None, :] - pal[None, None, :, :].astype(np.int16)) ** 2).sum(axis=3)
    a[:, :, :3] = pal[dist.argmin(axis=2)]
    alpha = a[:, :, 3].astype(np.float32) * (cap / 255)
    alpha = (np.rint(alpha / 5) * 5).clip(0, cap).astype(np.uint8)
    a[:, :, 3] = np.where(alpha < 10, 0, alpha)
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def quantize_blend(im: Image.Image, name: str) -> Image.Image:
    a = np.array(im)
    pal = PALETTES[name]
    rgb = a[:, :, :3].astype(np.int16)
    dist = ((rgb[:, :, None, :] - pal[None, None, :, :].astype(np.int16)) ** 2).sum(axis=3)
    a[:, :, :3] = pal[dist.argmin(axis=2)]
    a[:, :, 3] = (np.rint(a[:, :, 3] / 5) * 5).clip(0, 255).astype(np.uint8)
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def prepared_keys(name: str) -> list[Image.Image]:
    atlas, row, _, target, cap = LOOPS[name]
    raw = [panel(atlas, row, i) for i in range(4)]
    maxw, maxh = max(p.width for p in raw), max(p.height for p in raw)
    scale = min(target[0] / maxw, target[1] / maxh)
    out = []
    for key_index, p in enumerate(raw):
        local_scale = scale
        if name == "smoke_a":
            local_scale = min(target[0]/p.width, (103, 127, 147, 119)[key_index]/p.height)
        elif name == "smoke_b":
            local_scale = min(target[0]/p.width, (94, 111, 128, 106)[key_index]/p.height)
        size = (max(1, round(p.width*local_scale)), max(1, round(p.height*local_scale)))
        p = recolor(p.resize(size, R), name, cap)
        cell = Image.new("RGBA", (CELL, CELL))
        base_alpha = np.asarray(p.getchannel("A"))[-min(17,p.height):]
        by, bx = np.where(base_alpha > 35)
        base_x = round(float(bx.mean())) if len(bx) else p.width // 2
        x = ANCHOR[0] - base_x if name != "aurora" else (CELL-p.width)//2
        y = (ANCHOR[1] - p.height if name != "aurora" else (CELL-p.height)//2)
        cell.alpha_composite(p, (x, y))
        if name == "geyser" and key_index == 2:
            a = np.array(cell)
            ys, xs = np.where(a[:, :, 3] > 0)
            top = int(ys.min())
            gy, gx = np.indices((CELL, CELL))
            taper = (gy < top+30) & (np.abs(gx-128) > 7+(gy-top)*1.2)
            a[taper] = 0
            cell = Image.fromarray(a, "RGBA")
        out.append(cell)
    return out


def shifted(im: Image.Image, dx: int, dy: int) -> Image.Image:
    out = Image.new("RGBA", im.size)
    out.alpha_composite(im, (dx, dy))
    return out


def premul_mix(a: Image.Image, b: Image.Image, t: float) -> Image.Image:
    aa = np.asarray(a).astype(np.float32)
    bb = np.asarray(b).astype(np.float32)
    am = aa[:, :, 3:4] / 255
    bm = bb[:, :, 3:4] / 255
    out_a = am * (1-t) + bm * t
    out_rgb = (aa[:, :, :3] * am * (1-t) + bb[:, :, :3] * bm * t) / np.maximum(out_a, 1e-7)
    rgba = np.concatenate((out_rgb, out_a*255), axis=2).clip(0, 255).astype(np.uint8)
    rgba[rgba[:, :, 3] < 8] = 0
    return Image.fromarray(rgba, "RGBA")


def atmospheric_loop(name: str) -> list[Image.Image]:
    keys = prepared_keys(name)
    count = LOOPS[name][2]
    frames = []
    for i in range(count):
        k = i // 2
        if i % 2 == 0:
            frames.append(keys[k])
        else:
            # Each painted key shifts 1-2 pixels toward the next key; the
            # cross-dissolve changes the billow and internal texture together.
            dx = 2 if name in ("smoke_a", "aurora") else -2 if name == "smoke_b" else 1
            if i == count-1 and name.startswith("smoke"):
                # Finish the large plume's dissipation close to its compact
                # opening pose, so the wrap is no harsher than another beat.
                frames.append(quantize_blend(premul_mix(keys[k], keys[0], .9 if name == "smoke_b" else .5), name))
            else:
                a = shifted(keys[k], dx, -1 if name != "aurora" else 0)
                b = shifted(keys[(k+1) % len(keys)], -dx, 1 if name != "aurora" else 0)
                frames.append(quantize_blend(premul_mix(a, b, .5), name))
    return frames


def beacon_loop(name: str) -> list[Image.Image]:
    keys = prepared_keys(name)
    if name == "window_lights":
        # One painted compound silhouette stays fixed; windows/lamps flicker.
        base = np.array(keys[0])
        frames = []
        for i, gain in enumerate((.68, 1.0, .83, .94)):
            a = base.copy()
            a[:, :, :3] = np.rint(a[:, :, :3] * .64).astype(np.uint8)
            yy, xx = np.indices(a.shape[:2])
            warm = (a[:, :, 0] > a[:, :, 2] * 1.17) & (a[:, :, 3] > 0)
            cool = (a[:, :, 2] > a[:, :, 0] * 1.2) & (a[:, :, 3] > 0)
            flicker = np.where(((xx//7 + yy//5 + i) % 3 == 0), .54, 1)
            a[:, :, 3] = np.rint(a[:, :, 3] * np.where(warm | cool, gain*flicker, 1)).astype(np.uint8)
            frame = Image.fromarray(a, "RGBA")
            d = ImageDraw.Draw(frame, "RGBA")
            lamps = [(110, 207, (240, 179, 85)), (119, 213, (94, 185, 200)),
                     (136, 216, (235, 174, 79)), (147, 205, (85, 179, 203)),
                     (132, 194, (231, 168, 72))]
            for j, (lx, ly, color) in enumerate(lamps):
                strength = (53, 158, 99, 134)[(i+j)%4]
                d.rectangle((lx-1, ly, lx+1, ly+1), fill=(*color, strength))
            frames.append(frame)
        return frames
    if name == "strobe_white":
        # Painted source frames 0/2 are quiet, 1/3 contain the double flash.
        for i in (0, 2):
            a = np.array(keys[i])
            a[:, :, :3] = np.rint(a[:, :, :3] * .6).astype(np.uint8)
            a[:, :, 3] = np.rint(a[:, :, 3] * .65).astype(np.uint8)
            keys[i] = Image.fromarray(a, "RGBA")
        return keys
    # Stable painted fixture with four stepped neon lamp strengths.
    base = np.array(keys[0])
    base[:, :, :3] = np.rint(base[:, :, :3] * .53).astype(np.uint8)
    base[:, :, 3] = np.rint(base[:, :, 3] * .78).astype(np.uint8)
    fixture = Image.fromarray(base, "RGBA")
    bounds = fixture.getchannel("A").getbbox()
    cy = bounds[1] + 12
    color = (224, 83, 171) if name == "beacon_magenta" else (72, 204, 229)
    frames = []
    for amount in (24, 115, 224, 80):
        glow = Image.new("RGBA", (CELL, CELL))
        d = ImageDraw.Draw(glow, "RGBA")
        d.rectangle((118, cy-10, 138, cy+10), fill=(*color, round(amount*.12)))
        d.rectangle((123, cy-5, 133, cy+5), fill=(*color, round(amount*.27)))
        d.rectangle((126, cy-2, 130, cy+2), fill=(*color, round(amount*.65)))
        glow.alpha_composite(fixture)
        d = ImageDraw.Draw(glow, "RGBA")
        d.rectangle((127, cy-1, 129, cy+1), fill=(*color, amount))
        frames.append(quantize_blend(glow, name))
    return frames


def searchlight_loop() -> list[Image.Image]:
    # The four painted beams are resampled into an overhead perspective sweep.
    # The azimuth spans 360 degrees while ground-plane projection keeps the
    # emitter at (128,236) and all beam tips inside the cell.
    source = [panel("fire_lights", 1, i) for i in range(4)]
    tex = [np.asarray(recolor(p.resize((116, 150), R), "searchlight", 42)) for p in source]
    frames = []
    yy, xx = np.indices((CELL, CELL))
    for i in range(8):
        theta = math.tau * i / 8
        endx = 128 + 75 * math.sin(theta)
        endy = 139 - 72 * math.cos(theta)
        vx, vy = endx-128, endy-236
        length = math.hypot(vx, vy)
        ux, uy = vx/length, vy/length
        along = (xx-128)*ux + (yy-236)*uy
        across = (xx-128)*(-uy) + (yy-236)*ux
        t = np.clip(along / length, 0, 1)
        spread = 4 + 36*t
        sx = np.rint(58 + across / spread * 55).astype(int).clip(0, 115)
        sy = np.rint(149 - t*148).astype(int).clip(0, 149)
        pixels = tex[(i//2) % 4][sy, sx].copy()
        valid = (along >= 0) & (along <= length) & (np.abs(across) <= spread)
        pixels[~valid] = 0
        # Projected rearward angles are shorter and fainter, as on a surface.
        pixels[:, :, 3] = np.rint(pixels[:, :, 3] * (.73 + .17*math.cos(theta))).astype(np.uint8)
        pixels[pixels[:, :, 3] < 5] = 0
        frame = Image.fromarray(pixels, "RGBA")
        d = ImageDraw.Draw(frame, "RGBA")
        d.rectangle((126, 232, 130, 236), fill=(71, 132, 154, 118))
        d.point((128, 233), fill=(157, 220, 222, 174))
        frames.append(frame)
    return frames


def make_atlas(family: str, loops: dict[str, list[Image.Image]]) -> Image.Image:
    names = [f"{name}_{i:02d}" for name, frames in loops.items() for i in range(len(frames))]
    assert len(names) == 16
    atlas = Image.new("RGBA", (1024, 1024))
    rects = []
    all_frames = [frame for frames in loops.values() for frame in frames]
    for i, (name, frame) in enumerate(zip(names, all_frames)):
        row, col = divmod(i, 4)
        atlas.alpha_composite(frame, (col*CELL, row*CELL))
        rects.append({"n": name, "x": col*CELL, "y": 1024-(row+1)*CELL, "w": CELL, "h": CELL})
    atlas.save(ROOT / f"{family}.png", optimize=True)
    (ROOT / f"{family}.json").write_text(json.dumps({"sprites": rects}, indent=2) + "\n")
    return atlas


def contact_sheet(loops: dict[str, list[Image.Image]]) -> None:
    rows = sum(math.ceil(len(v)/8) for v in loops.values())
    out = Image.new("RGB", (8*256, rows*282), (8, 17, 29))
    d = ImageDraw.Draw(out)
    yrow = 0
    for name, frames in loops.items():
        for j, frame in enumerate(frames):
            row, col = divmod(j, 8)
            x, y = col*256, (yrow+row)*282
            out.paste(frame, (x, y), frame)
            d.text((x+12, y+256), f"{name}_{j:02d}", fill=(151, 188, 203))
        yrow += math.ceil(len(frames)/8)
    out.save(ROOT / "preview_c.png", optimize=True)


def make_gif(loops: dict[str, list[Image.Image]]) -> None:
    mid = Image.open(ROOT / "mid.png").convert("RGBA")
    ground = Image.new("RGBA", (600, 1300), (5, 11, 20, 255))
    for y in range(0, 1300, 600):
        for x in range(0, 600, 600):
            ground.alpha_composite(mid.resize((600, 600), R), (x, y))
    landmarks = Image.open(ROOT / "landmarks.png").convert("RGBA")
    def landmark(index: int, x: int, y: int, w: int) -> None:
        row, col = divmod(index, 4)
        piece = landmarks.crop((col*256, row*256, (col+1)*256, (row+1)*256))
        piece = piece.resize((w, w), R)
        ground.alpha_composite(piece, (x, y))
    landmark(0, 62, 127, 160)   # rig
    landmark(4, 315, 319, 155)  # refinery
    landmark(6, 88, 610, 145)   # relay
    landmark(13, 342, 829, 170) # derrick
    landmark(3, 200, 1030, 154) # platform
    places = {
        "smoke_a": (387, 365, .43), "smoke_b": (460, 379, .38),
        "steam_vent": (265, 1075, .40), "geyser": (180, 713, .48),
        "flare": (150, 225, .38), "searchlight": (157, 1118, .46),
        "beacon_magenta": (112, 213, .45), "beacon_cyan": (149, 690, .45),
        "strobe_white": (422, 897, .44), "window_lights": (356, 396, .46),
        "aurora": (285, 70, 1.18),
    }
    frames = []
    for tick in range(16):
        scene = ground.copy()
        for name, (px, py, size) in places.items():
            seq = loops[name]
            index = tick % len(seq) if name != "aurora" else tick
            piece = seq[index]
            w = round(256*size)
            piece = piece.resize((w, w), R)
            x = round(px-w/2)
            y = round(py-size*236) if name != "aurora" else round(py-w/2)
            scene.alpha_composite(piece, (x, y))
        # Palette GIF keeps the phone mock manageable without blurring sprites.
        frames.append(scene.convert("RGB").quantize(colors=96, dither=Image.Dither.NONE))
    frames[0].save(ROOT / "preview_c.gif", save_all=True, append_images=frames[1:],
                   duration=125, loop=0, optimize=False, disposal=2)


def append_manifest() -> None:
    path = ROOT / "manifest.json"
    original = path.read_text()
    if '"run_c"' in original:
        return
    uses = {
        "smoke_a": (8, "refinery and industrial stacks", .68),
        "smoke_b": (8, "refinery and platform chimneys", .72),
        "steam_vent": (10, "ice vents and coastal shelves", .75),
        "geyser": (8, "ice shelves and cryo vents", .70),
        "flare": (12, "offshore rigs and derricks", .78),
        "searchlight": (8, "platforms and hangars", .52),
        "beacon_magenta": (4, "rigs and causeways", .70),
        "beacon_cyan": (4, "relays and causeways", .68),
        "strobe_white": (8, "rigs and relay towers", .55),
        "window_lights": (5, "refinery compounds and platforms", .60),
        "aurora": (3, "night variants behind ground activity", .46),
    }
    run = {"build": "python3 src~/build_c.py", "preview": "preview_c.png",
           "animated_preview": "preview_c.gif", "atlas_grid": "4x4 256x256 cells; Unity rect y measured from image bottom",
           "source_key_paintings": [f"src~/{v[0]}" for v in SOURCES.values()],
           "loops": [
               {"name": name, "atlas": f"{('smoke' if name.startswith('smoke') else 'steam' if name in ('steam_vent','geyser') else 'fire_lights' if name in ('flare','searchlight') else 'aurora' if name == 'aurora' else 'beacons')}.png",
                "frames": [f"{name}_{i:02d}" for i in range(LOOPS[name][2] if name in LOOPS else 8)],
                "recommended_fps": uses[name][0],
                "anchor_px_from_top": [128, 128] if name == "aurora" else list(ANCHOR),
                "intended_use": uses[name][1], "recommended_draw_alpha": uses[name][2]}
               for name in uses]}
    pos = original.rfind("\n}")
    if pos < 0:
        raise ValueError("Unexpected manifest formatting")
    insertion = ',\n  "run_c": ' + json.dumps(run, indent=2).replace("\n", "\n  ")
    path.write_text(original[:pos] + insertion + original[pos:])


def main() -> None:
    loops = {name: atmospheric_loop(name) for name in ("smoke_a", "smoke_b", "steam_vent", "geyser", "flare")}
    loops["searchlight"] = searchlight_loop()
    loops.update({name: beacon_loop(name) for name in ("beacon_magenta", "beacon_cyan", "strobe_white", "window_lights")})
    # Eight painted aurora keys, interleaved with deformed cross-dissolves.
    keys = prepared_keys("aurora")
    extra = [panel("aurora", 1, i) for i in range(4)]
    maxw, maxh = max(p.width for p in extra), max(p.height for p in extra)
    scale = min(228/maxw, 115/maxh)
    for p in extra:
        p = recolor(p.resize((round(p.width*scale), round(p.height*scale)), R), "aurora", 39)
        cell = Image.new("RGBA", (CELL, CELL))
        cell.alpha_composite(p, ((CELL-p.width)//2, (CELL-p.height)//2))
        keys.append(cell)
    aurora = []
    for i in range(16):
        k = i//2
        aurora.append(keys[k] if i%2 == 0 else quantize_blend(premul_mix(shifted(keys[k], 1, 0), shifted(keys[(k+1)%8], -1, 0), .5), "aurora"))
    loops["aurora"] = aurora
    make_atlas("smoke", {k: loops[k] for k in ("smoke_a", "smoke_b")})
    make_atlas("steam", {k: loops[k] for k in ("steam_vent", "geyser")})
    make_atlas("fire_lights", {k: loops[k] for k in ("flare", "searchlight")})
    make_atlas("beacons", {k: loops[k] for k in ("beacon_magenta", "beacon_cyan", "strobe_white", "window_lights")})
    make_atlas("aurora", {"aurora": aurora})
    contact_sheet(loops)
    make_gif(loops)
    append_manifest()


if __name__ == "__main__":
    main()
