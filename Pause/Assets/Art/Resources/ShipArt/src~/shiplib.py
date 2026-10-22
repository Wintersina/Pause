"""Shared helpers for the player-ship pipeline: where a ship's files live, and its data file.

Every ship has ONE folder, Assets/Art/Resources/ShipArt/<Key>/ (Key = the art key in
Scripts/Ship/ShipId.cs: NeonComet, VoltViper, ...), holding

    <Key>.png                hull sheet (9 columns x 3 damage rows of 256 px cells)
    <Key>_<Skin>.bytes       skin sheets (PNG bytes)
    <Key>_flame.png          flame strips (layout in the data file)
    <Key>_shield.bytes       baked shield silhouette   (Unity: ShieldSilhouetteBaker)
    <Key>_hitbox.bytes       baked hit zones           (Unity: ShipHitboxBaker)
    <Key>_data.json          rect/ppu, hue, nozzles, damage emitters, skins, exhaust, painted
    src~/                    this ship's sources (Codex paintings, per-ship tools, restyle.json)

The shared tools live in ShipArt/src~/ (this folder). Everything per ship is addressed by ship
NAME only (any case: `NeonComet`, `neoncomet`); adding a ship edits no shared file.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SHIPART = os.path.normpath(os.path.join(HERE, ".."))                 # Assets/Art/Resources/ShipArt
REPO = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", ".."))
SHIP_ID_CS = os.path.join(REPO, "Pause", "Assets", "Scripts", "Ship", "ShipId.cs")

# fixed key order of the data file (stable diffs)
DATA_ORDER = ["id", "key", "painted", "rect", "hue", "nozzles", "damageNote", "emitters", "skins", "exhaust"]


def all_keys():
    """Art keys in roster order (ShipId.cs `keys`): the ship registry, id = position."""
    src = open(SHIP_ID_CS).read()
    block = src[src.index("static readonly string[] keys"):]
    return re.findall(r'"(\w+)"', block[:block.index("};")])


def resolve(name):
    """'neoncomet' / 'NeonComet' -> 'NeonComet'. Exits with a hint for an unknown ship."""
    for k in all_keys():
        if k.lower() == name.lower():
            return k
    sys.exit("unknown ship %r; known: %s" % (name, ", ".join(all_keys())))


def ship_id(key):
    return all_keys().index(key) + 1


def ship_dir(key):
    return os.path.join(SHIPART, key)


def src_dir(key):
    return os.path.join(ship_dir(key), "src~")


def hull_path(key):
    return os.path.join(ship_dir(key), key + ".png")


def skin_path(key, skin):
    return os.path.join(ship_dir(key), "%s_%s.bytes" % (key, skin))


def flame_path(key):
    return os.path.join(ship_dir(key), key + "_flame.png")


def data_path(key):
    return os.path.join(ship_dir(key), key + "_data.json")


def load_data(key):
    p = data_path(key)
    return json.load(open(p)) if os.path.exists(p) else {"id": ship_id(key), "key": key, "painted": False}


def save_data(key, data):
    ordered = {k: data[k] for k in DATA_ORDER if k in data}
    ordered.update({k: v for k, v in data.items() if k not in ordered})
    os.makedirs(ship_dir(key), exist_ok=True)
    with open(data_path(key), "w") as fh:
        json.dump(ordered, fh, indent=2)
        fh.write("\n")
    return ordered


def update_data(key, **fields):
    """Merge fields into <Key>_data.json (creating it if absent). Returns the new dict."""
    data = load_data(key)
    data.update(fields)
    return save_data(key, data)


def is_painted(key):
    p = data_path(key)
    return os.path.exists(p) and bool(json.load(open(p)).get("painted"))


def painted_keys():
    """Ships drawn as hi-res neon pixel art: derived from the per-ship data files."""
    return {k for k in all_keys() if is_painted(k)}


def need_unity_meta(path, like):
    """New file next to `like` (an existing sibling asset): give it a .meta cloned from the sibling's
    with a fresh deterministic GUID, if it has none yet. Unity makes one itself on first import; this
    keeps the commit complete."""
    import hashlib
    if os.path.exists(path + ".meta") or not os.path.exists(like + ".meta"):
        return
    guid = hashlib.md5(("shipart-file:" + os.path.relpath(path, REPO)).encode()).hexdigest()
    meta = re.sub(r"guid: \w+", "guid: " + guid, open(like + ".meta").read(), count=1)
    open(path + ".meta", "w").write(meta)


# ---------------------------------------------------------------- flame sheets
GAP = 2


def read_strips(key):
    """{layer: [PIL RGBA frame images]} for every strip in the ship's flame sheet, in strip order."""
    from PIL import Image
    data = load_data(key)
    sheet = Image.open(flame_path(key)).convert("RGBA")
    out = {}
    for s in data["exhaust"]["strips"]:
        top = sheet.height - s["y"] - s["h"]
        out[s["layer"]] = [sheet.crop((s["x"] + f * s["stride"], top, s["x"] + f * s["stride"] + s["w"], top + s["h"]))
                           for f in range(s["frames"])]
    return out


def write_flame_sheet(key, strips, layout=None):
    """Writes <Key>_flame.png from {layer: [frames]} and records the strip rectangles in the data
    file. `layout`: {layer: (w, h, stride)}; defaults to the strips already in the data file, else
    plume strips (48 x 192, stride 50). Strips are stacked with a 2 px clear gap, x = 0."""
    from PIL import Image
    data = load_data(key)
    old = {s["layer"]: s for s in data.get("exhaust", {}).get("strips", [])}
    order = [l for l in ("Plume", "PlumeBoost", "Ring", "RingBoost", "Wake", "WakeBoost") if l in strips]
    specs = []
    for layer in order:
        frames = strips[layer]
        if layout and layer in layout:
            w, h, stride = layout[layer]
        elif layer in old:
            w, h, stride = old[layer]["w"], old[layer]["h"], old[layer]["stride"]
        else:
            w, h, stride = 48, 192, 50
        specs.append((layer, frames, w, h, stride))
    width = max((len(f) - 1) * st + w for _, f, w, _, st in specs)
    height = sum(h for _, _, _, h, _ in specs) + GAP * (len(specs) - 1)
    sheet = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    top, rows = 0, []
    for layer, frames, w, h, stride in specs:
        for i, fr in enumerate(frames):
            sheet.paste(fr, (i * stride, top))
        rows.append(dict(layer=layer, x=0, y=height - top - h, w=w, h=h, frames=len(frames), stride=stride))
        top += h + GAP
    sheet.save(flame_path(key), optimize=True)
    data.setdefault("exhaust", {})["strips"] = rows
    save_data(key, data)
    # a first flame sheet of a new ship needs a meta: clone a sibling's
    for other in all_keys():
        if other != key and os.path.exists(flame_path(other)):
            need_unity_meta(flame_path(key), flame_path(other))
            break
    return rows
