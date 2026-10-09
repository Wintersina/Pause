"""Verdant run C: painted-key, point-sampled atmosphere and light flipbooks."""

from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
N = Image.Resampling.NEAREST
ANCHOR = (128, 236)

# All colors are finite ramps. Bright red source fringe is mapped to orange or
# brown, so none of the exported FX can be mistaken for a player-red attack.
PALETTES = {
    "smoke_a": ["201d1b", "302b29", "413b37", "57514b", "6c655d", "82766c", "a07b58", "bd7441"],
    "smoke_b": ["34413f", "4e5b58", "67736f", "82908a", "a4aea6", "c4cbc1", "e0e0d3"],
    "flame_front": ["271b19", "43251c", "68311d", "994321", "c55a20", "ed832b", "ffb843", "ffe78b"],
    "flame_patch": ["251b19", "45271e", "6b351e", "a14920", "d06421", "f5922e", "ffc651", "fff0a0"],
    "wildsmoke_a": ["181817", "272320", "342b26", "493930", "5e4637", "77553c", "a46435", "da7928"],
    "wildsmoke_b": ["272b2a", "393c39", "50534f", "686c66", "828780", "a0a49b", "b9b9ab"],
    "steam_vent": ["33554b", "547769", "779989", "99baaa", "b9d9c8", "d8efdf", "edfaed"],
    "leak_sap": ["213b30", "335b37", "567e38", "80a641", "b5c34a", "e2d452", "f7ed91"],
    "ember_rain": ["3a2b21", "744025", "a55324", "d47727", "f7ac38", "ffe68b"],
    "spore_burst": ["255541", "3b7950", "66a761", "9bc868", "cde278", "eff1a3"],
    "beacon_lime": ["182b27", "355043", "6c6044", "99835a", "6cae49", "b8e65a", "f3ffb0"],
    "beacon_magenta": ["22222a", "493c46", "77515c", "ad577d", "df69a6", "fb9cce", "ffe4ee"],
    "window_lights": ["172824", "35463e", "675446", "87634b", "a88652", "d3a25b", "edd476", "f7eca9", "9ecf6b"],
    "strobe_white": ["1c2e2c", "405650", "819b91", "b7cbc2", "e5eee4", "ffffff"],
    "fireflies": ["234632", "46834b", "78b75c", "b9e875", "efffac"],
}
PALETTES = {k: np.array([tuple(bytes.fromhex(h)) for h in v], np.uint8)
            for k, v in PALETTES.items()}

# atlas, source sheet, source row, number of painted keys, frame count, painted
# size, peak alpha. All source keys are generated images retained in src~/.
SPEC = {
    "smoke_a": ("smoke", "smoke_keys.png", 0, 2, 8, (160, 183), 169),
    "smoke_b": ("smoke", "smoke_keys.png", 1, 2, 8, (135, 172), 167),
    "flame_front": ("wildfire", "wildfire_keys.png", 0, 2, 8, (216, 104), 232),
    "flame_patch": ("wildfire", "wildfire_keys.png", 1, 2, 8, (158, 139), 231),
    "wildsmoke_a": ("firesmoke", "firesmoke_keys.png", 0, 2, 8, (195, 207), 171),
    "wildsmoke_b": ("firesmoke", "firesmoke_keys.png", 1, 2, 8, (156, 177), 167),
    "steam_vent": ("leaks", "leaks_keys.png", 0, 1, 4, (118, 129), 169),
    "leak_sap": ("leaks", "leaks_keys.png", 0, 1, 4, (135, 110), 218),
    "ember_rain": ("leaks", "leaks_keys.png", 1, 1, 4, (106, 122), 207),
    "spore_burst": ("leaks", "leaks_keys.png", 1, 1, 4, (125, 117), 178),
    "beacon_lime": ("lights", "lights_keys.png", 0, 1, 4, (84, 107), 222),
    "beacon_magenta": ("lights", "lights_keys.png", 0, 1, 4, (84, 107), 222),
    "window_lights": ("lights", "lights_keys.png", 1, 1, 4, (128, 82), 222),
    "strobe_white": ("lights", "lights_keys.png", 1, 1, 4, (82, 103), 230),
    "fireflies": ("lights", "lights_keys.png", 1, 1, 4, (82, 74), 223),
}
SOURCE_COL = {"leak_sap": 1, "spore_burst": 1, "beacon_magenta": 1,
              "strobe_white": 1, "fireflies": 1}

USE = {
    "smoke_a": (8, "refinery_00/01/02 and silo_00 stack mouths", .62),
    "smoke_b": (9, "refinery_00/01/02 auxiliary stacks", .64),
    "flame_front": (12, "fires.png burnfront_00..03 burning edge", .76),
    "flame_patch": (12, "fires.png burnpatch_*, coalbed_*, scorched_*", .74),
    "wildsmoke_a": (7, "fires.png burnfront_00..03, behind flame_front", .55),
    "wildsmoke_b": (8, "fires.png burnpatch_* smoke ribbon", .58),
    "steam_vent": (10, "pipes.png pipe flanges and pumphouse_00", .68),
    "leak_sap": (9, "pipes.png pipe_leak_00/01 rupture mouths", .72),
    "ember_rain": (8, "all fires.png burning pieces, sparse overlay", .56),
    "spore_burst": (8, "landmarks.png dome_00 and silo_00 foliage", .59),
    "beacon_lime": (4, "landmarks.png tower_*, relay_*, derrick_00", .73),
    "beacon_magenta": (4, "landmarks.png tower_*, relay_*, derrick_00", .73),
    "window_lights": (5, "landmarks.png refinery_* and dome_00", .71),
    "strobe_white": (4, "landmarks.png towers and relay antenna tops", .68),
    "fireflies": (6, "atmospheric foliage above v1/mid.png", .61),
}


def panel(name: str, col: int) -> Image.Image:
    _, file, row, _, _, _, _ = SPEC[name]
    source = Image.open(SRC / file).convert("RGBA")
    w, h = source.size
    # The image generator painted four independent keys in 2x2 panels.
    a = np.array(source.crop((col*w//2, row*h//2, (col+1)*w//2,
                              (row+1)*h//2)))
    yy, xx = np.indices(a.shape[:2])
    edge = np.minimum.reduce((xx, yy, a.shape[1]-1-xx, a.shape[0]-1-yy))
    a[:, :, 3] = np.rint(a[:, :, 3].astype(float) *
                        np.clip(edge / 14, 0, 1)).astype(np.uint8)
    if name == "flame_front":
        # The key sheet lets sparks from the lower patch enter the upper
        # quadrant. They are unrelated to the painted burn-edge silhouette.
        a[510:, :, 3] = 0
    if name == "wildsmoke_b":
        # A narrow remnant of the large top-row plume crosses this panel.
        a[:76, :, 3] = 0
    a[a[:, :, 3] < 24, 3] = 0
    ys, xs = np.where(a[:, :, 3] > 45)
    if not len(xs):
        raise ValueError((name, col, "empty source key"))
    return Image.fromarray(a, "RGBA").crop((max(0, int(xs.min())-3),
        max(0, int(ys.min())-3), min(a.shape[1], int(xs.max())+4),
        min(a.shape[0], int(ys.max())+4)))


def palette_clean(im: Image.Image, name: str, cap: int,
                  normalize_alpha: bool = True) -> Image.Image:
    a = np.array(im.convert("RGBA"))
    pal = PALETTES[name]
    rgb = a[:, :, :3].astype(np.int32)
    d = ((rgb[:, :, None, :] - pal[None, None, :, :].astype(np.int32))**2).sum(3)
    a[:, :, :3] = pal[d.argmin(2)]
    alpha_scale = cap/255 if normalize_alpha else 1.0
    a[:, :, 3] = np.minimum(cap, np.rint(a[:, :, 3].astype(float)*alpha_scale / 6)*6).astype(np.uint8)
    a[a[:, :, 3] < 12] = 0
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def source_key(name: str, col: int) -> Image.Image:
    p = panel(name, col)
    target = SPEC[name][5]
    scale = min(target[0]/p.width, target[1]/p.height)
    p = p.resize((round(p.width*scale), round(p.height*scale)), N)
    p = palette_clean(p, name, SPEC[name][6])
    cell = Image.new("RGBA", (256, 256))
    x = 128 - p.width//2
    y = ANCHOR[1] - p.height
    cell.alpha_composite(p, (x, y))
    return cell


def warp(im: Image.Image, phase: float, strength: float, anchor_y: int = 236) -> Image.Image:
    """Periodic nearest-neighbour flow; the emitter remains stationary."""
    a = np.array(im)
    yy, xx = np.indices(a.shape[:2])
    altitude = np.clip((anchor_y - yy) / 205, 0, 1)
    dx = strength * altitude * (np.sin(phase + yy*.038) + .30*np.sin(phase*2 + yy*.071))
    dy = strength*.46*altitude*np.cos(phase + xx*.047)
    sx = np.clip(np.rint(xx - dx).astype(int), 0, 255)
    sy = np.clip(np.rint(yy - dy).astype(int), 0, 255)
    return Image.fromarray(a[sy, sx], "RGBA")


def mix(a: Image.Image, b: Image.Image, weight: float, name: str) -> Image.Image:
    aa = np.array(a).astype(float)
    bb = np.array(b).astype(float)
    alpha_a = aa[:, :, 3:4] / 255
    alpha_b = bb[:, :, 3:4] / 255
    alpha = alpha_a*(1-weight) + alpha_b*weight
    rgb = (aa[:, :, :3]*alpha_a*(1-weight) + bb[:, :, :3]*alpha_b*weight) / np.maximum(alpha, 1e-6)
    out = np.concatenate([rgb, alpha*255], 2).clip(0,255).astype(np.uint8)
    return palette_clean(Image.fromarray(out, "RGBA"), name, SPEC[name][6], False)


def atmospheric(name: str) -> list[Image.Image]:
    count = SPEC[name][4]
    keys = [source_key(name, c) for c in range(SOURCE_COL.get(name, 0),
              SOURCE_COL.get(name, 0)+SPEC[name][3])]
    frames = []
    for i in range(count):
        phase = 2*math.pi*i/count
        if len(keys) == 2:
            weight = (1-math.cos(phase))/2
            strength = 5.5 if name.startswith("flame") else 4.2
            a = warp(keys[0], phase, strength)
            b = warp(keys[1], phase, strength)
            frame = mix(a,b,weight,name)
        else:
            strength = 4.7 if name in ("steam_vent", "spore_burst") else 5.6
            frame = warp(keys[0], phase, strength)
            frame = palette_clean(frame, name, SPEC[name][6], False)
            # A periodic, stepped emissive pulse animates the same painted
            # jet/spore/ember clusters without inventing new source geometry.
            if name in ("leak_sap", "ember_rain", "spore_burst"):
                a = np.array(frame)
                lum = a[:, :, :3].mean(2)
                hot = (lum > np.percentile(lum[a[:, :, 3]>0], 65)) & (a[:, :, 3]>0)
                gain = (0.82, 1.0, 0.94, 0.76)[i]
                a[hot, 3] = np.minimum(SPEC[name][6], np.rint(a[hot, 3]*gain)).astype(np.uint8)
                frame = Image.fromarray(a, "RGBA")
        frames.append(frame)
    return frames


def lights(name: str) -> list[Image.Image]:
    key = source_key(name, SOURCE_COL.get(name, 0))
    base = np.array(key)
    yy, xx = np.indices(base.shape[:2])
    if name.startswith("beacon"):
        # Separate the painted rusted mast from its painted colored bloom.
        # State 0 is an actually dark lamp; state 2 reveals the painted halo.
        center = np.hypot(xx-128, yy-171)
        top = yy < 191
        lamp = top & (center < 7)
        halo = top & (center >= 7)
        states = ((.05,.02), (.45,.27), (1.,1.), (.30,.12))
        frames = []
        for lamp_gain, halo_gain in states:
            a = base.copy()
            a[halo, 3] = np.rint(a[halo, 3]*halo_gain).astype(np.uint8)
            a[lamp, 3] = np.rint(a[lamp, 3]*lamp_gain).astype(np.uint8)
            # The mast is quiet and subordinate at background depth.
            a[~top, 3] = np.rint(a[~top, 3]*.43).astype(np.uint8)
            frames.append(Image.fromarray(a, "RGBA"))
        return frames
    if name == "window_lights":
        # Six source-painted windows occupy two rows. Turn different subsets
        # fully on; retain the low-contrast industrial wall behind them.
        frames = []
        for on in ((1,), (0,3,4), tuple(range(6)), (2,5)):
            a = base.copy()
            green = (a[:, :, 1] > a[:, :, 0]*1.12) & (a[:, :, 1]>90)
            warm = (a[:, :, 0] > a[:, :, 2]*1.4) & (a[:, :, 0]>110)
            luminous = (green|warm) & (a[:, :, 3]>0)
            a[luminous, 3] = np.rint(a[luminous, 3]*.015).astype(np.uint8)
            a[~luminous, 3] = np.rint(a[~luminous, 3]*.055).astype(np.uint8)
            for j in on:
                cx = (99,128,157,99,128,157)[j]
                cy = (194,194,194,217,217,217)[j]
                mask = ((xx-cx)**2/12**2+(yy-cy)**2/11**2 < 1.0) & luminous
                a[mask] = base[mask]
            frames.append(Image.fromarray(a,"RGBA"))
        return frames
    if name == "strobe_white":
        # One painted white strobe is shown dim, flashing, dim, flashing:
        # the second flash is shorter and less bright.
        frames = []
        for gain in (.045, 1., .07, .68):
            a = base.copy()
            light = (a[:, :, :3].mean(2)>115) & (a[:, :, 3]>0)
            a[light, 3] = np.rint(a[light, 3]*gain).astype(np.uint8)
            a[~light, 3] = np.rint(a[~light, 3]*.25).astype(np.uint8)
            frames.append(Image.fromarray(a,"RGBA"))
        return frames
    # Fireflies come from the three source-painted lime points beside the
    # strobe. Mask away the mast and independently pulse/drift these points.
    assert name == "fireflies"
    a = base.copy()
    green = (a[:, :, 1] > a[:, :, 0]*1.12) & (a[:, :, 1]>80) & (xx>130)
    a[~green] = 0
    key = Image.fromarray(a,"RGBA")
    frames = []
    for i,gain in enumerate((.28,.88,1.,.52)):
        piece = warp(key, 2*math.pi*i/4, 8)
        b = np.array(piece)
        b[:, :, 3] = np.rint(b[:, :, 3]*gain).astype(np.uint8)
        frames.append(Image.fromarray(b,"RGBA"))
    return frames


def brightness(im: Image.Image) -> int:
    a = np.asarray(im, dtype=np.float64)
    return round((a[:, :, :3].mean(2)*a[:, :, 3]/65025).sum())


def build() -> dict[str, list[Image.Image]]:
    all_frames = {name: (lights(name) if SPEC[name][0]=="lights" else atmospheric(name))
                  for name in SPEC}
    for atlas in ("smoke", "wildfire", "firesmoke", "leaks", "lights"):
        names = [n for n in SPEC if SPEC[n][0]==atlas]
        im = Image.new("RGBA", (1024,1024))
        sprites = []
        offset = 0
        for name in names:
            for i, frame in enumerate(all_frames[name]):
                if atlas == "lights" and name in ("strobe_white", "fireflies"):
                    # 20 named light frames cannot all occupy 256px cells in
                    # one 1024px atlas. Their genuinely tiny source paintings
                    # occupy eight 128px non-overlapping subcells instead.
                    j = (0 if name == "strobe_white" else 4)+i
                    x,y,w,h = (j*128, 768, 128, 128)
                    content = frame.crop((64, 115, 192, 243))
                else:
                    x,y,w,h = (offset%4*256, offset//4*256, 256, 256)
                    content = frame
                    offset += 1
                im.alpha_composite(content, (x,y))
                sprites.append({"n":f"{name}_{i:02d}","x":x,"y":1024-y-h,"w":w,"h":h})
        im.save(ROOT/f"{atlas}.png",optimize=True)
        (ROOT/f"{atlas}.json").write_text(json.dumps({"sprites":sprites},indent=2)+"\n")
    return all_frames


def append_manifest() -> None:
    path = ROOT/"manifest.json"
    data = json.loads(path.read_text())
    loops = []
    for name, (atlas, _, _, _, count, _, _) in SPEC.items():
        fps,use,alpha = USE[name]
        small = name in ("strobe_white","fireflies")
        loops.append({"name":name,"atlas":f"{atlas}.png",
                      "frames":[f"{name}_{i:02d}" for i in range(count)],
                      "recommended_fps":fps,
                      "anchor_px_from_top":[64,121] if small else list(ANCHOR),
                      "intended_use":use,"recommended_draw_alpha":alpha})
    data["run_c"] = {
        "build":"python3 src~/build_c.py",
        "preview":"preview_c.png","animated_preview":"preview_c.gif",
        "source_key_paintings":[f"src~/{x}_keys.png" for x in
                                ("smoke","wildfire","firesmoke","leaks","lights")],
        "atlas_grid":"1024x1024 RGBA, 4x4 grid of 256x256 cells; JSON y from bottom",
        "lights_packing_exception":"Five four-frame loops make 20 sprites. The final lights row packs strobe_white and fireflies into eight nonoverlapping 128x128 subcells; all other sprite rects are 256x256.",
        "emitter_anchor":"Ground-attached loops use local pixel (128,236) from top; strobe_white uses (64,121) in its half-size rect; fireflies are atmospheric.",
        "loops":loops,
    }
    path.write_text(json.dumps(data,indent=2)+"\n")


def crop_piece(atlas: str, name: str) -> Image.Image:
    entries = json.loads((ROOT/f"{atlas}.json").read_text())["sprites"]
    r = next(r for r in entries if r["n"]==name)
    return Image.open(ROOT/f"{atlas}.png").convert("RGBA").crop(
        (r["x"], 1024-r["y"]-r["h"], r["x"]+r["w"], 1024-r["y"]))


def previews(frames: dict[str,list[Image.Image]]) -> None:
    # The contact sheet retains every authored state at a readable 160px cell.
    ground = Image.open(ROOT/"v1/mid.png").convert("RGBA")
    bg = ground.crop((ground.width//2-80,ground.height//2-80,
                      ground.width//2+80,ground.height//2+80))
    bg = Image.blend(bg, Image.new("RGBA",bg.size,(7,22,19,255)), .53)
    sheet = Image.new("RGB",(1536, 15*206),(10,24,21))
    d = ImageDraw.Draw(sheet)
    for row,name in enumerate(SPEC):
        scores = [brightness(f) for f in frames[name]]
        caption = name + (f"  brightness {scores}  ratio {max(scores)/max(1,min(scores)):.2f}" if name in
                  ("beacon_lime","beacon_magenta","window_lights","strobe_white") else "")
        d.text((10,row*206+3),caption,fill=(221,224,198))
        for i,f in enumerate(frames[name]):
            backdrop = bg.copy()
            size = 160
            content = f.resize((size,size),N)
            backdrop.alpha_composite(content)
            sheet.paste(backdrop.convert("RGB"),(i*188,row*206+24))
            d.text((i*188+5,row*206+186),f"{i:02d}",fill=(226,223,188))
    sheet.save(ROOT/"preview_c.png",optimize=True)

    # Tall game-scale compositing proof, with run B assets and all 15 loops.
    base = Image.open(ROOT/"v1/mid.png").convert("RGBA").resize((600,1300),N)
    fixed = [("landmarks","refinery_00",(36,75),.67),
             ("landmarks","tower_00",(350,75),.56),
             ("landmarks","dome_00",(90,505),.64),
             ("landmarks","derrick_00",(380,885),.57),
             ("pipes","pipe_straight_00",(185,405),.58),
             ("pipes","pipe_leak_00",(325,680),.64),
             ("pipes","pumphouse_00",(60,925),.63),
             ("fires","burnfront_00",(25,1020),.83),
             ("fires","burnpatch_00",(345,1145),.65)]
    positions = {
        "smoke_a":(118,174,.63), "smoke_b":(180,230,.48),
        "flame_front":(170,1125,.58), "flame_patch":(420,1200,.53),
        "wildsmoke_a":(125,980,.62), "wildsmoke_b":(460,1100,.56),
        "steam_vent":(130,925,.56), "leak_sap":(414,730,.58),
        "ember_rain":(272,1110,.64), "spore_burst":(190,603,.54),
        "beacon_lime":(460,145,.55), "beacon_magenta":(404,966,.57),
        "window_lights":(138,291,.52), "strobe_white":(477,205,.53),
        "fireflies":(355,590,.68),
    }
    scene_frames = []
    for tick in range(16):
        scene = base.copy()
        for atlas,name,(x,y),scale in fixed:
            p = crop_piece(atlas,name)
            side = round(256*scale)
            scene.alpha_composite(p.resize((side,side),N),(x,y))
        for name,(x,y,scale) in positions.items():
            p = frames[name][tick%len(frames[name])]
            side = round(256*scale)
            p = p.resize((side,side),N)
            if name in ("smoke_a","smoke_b","wildsmoke_a","wildsmoke_b"):
                # Place the transparent smoke behind active play geometry.
                p.putalpha(p.getchannel("A").point(lambda a: round(a*.78)))
            scene.alpha_composite(p,(round(x-side/2),round(y-236*scale)))
        scene_frames.append(scene.convert("RGB").quantize(colors=192,dither=Image.Dither.NONE))
    scene_frames[0].save(ROOT/"preview_c.gif",save_all=True,
        append_images=scene_frames[1:],duration=110,loop=0,disposal=2,optimize=False)


if __name__ == "__main__":
    result = build()
    append_manifest()
    previews(result)
    for name in ("beacon_lime","beacon_magenta","window_lights","strobe_white"):
        scores = [brightness(f) for f in result[name]]
        print(f"{name}: {scores}, ratio {max(scores)/min(scores):.2f}")
