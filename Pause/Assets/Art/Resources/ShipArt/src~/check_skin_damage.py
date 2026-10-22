"""Regenerates every hull sheet in memory -- with all per-skin damage modules
(skin_damage_*.py) loaded together, as a full build_skins.py run does -- and
asserts each one matches the committed file:

    python3 check_skin_damage.py            # all 15 ships: 15 stock + 60 skin sheets
    python3 check_skin_damage.py Ninja UFO  # just these ships
    python3 check_skin_damage.py --out DIR  # also write the regenerated sheets to DIR
    python3 check_skin_damage.py --reverse  # load the skin_damage_* modules in reverse order

Skin sheets (../../Skins/<Key>_<Skin>.bytes) must match byte for byte; stock
sheets (../<Key>.png, written by build.py) must match pixel for pixel. It also
imports damage a second time under another module name and checks the hooks
were not installed twice and SKIN_D holds the same objects. Nothing in the project is
written (the intermediate SVG/PNG cells go to a temp dir). Exit 1 on any
mismatch.
"""
import glob
import importlib.util
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
if "--reverse" in sys.argv:
    # damage.py reuses modules already in sys.modules, so pre-loading them
    # here, last first, reverses the order their hooks wrap hullkit in
    for _p in sorted(glob.glob(os.path.join(HERE, "skin_damage_*.py")), reverse=True):
        _name = os.path.splitext(os.path.basename(_p))[0]
        _spec = importlib.util.spec_from_file_location(_name, _p)
        sys.modules[_name] = importlib.util.module_from_spec(_spec)
        _spec.loader.exec_module(sys.modules[_name])
        print("loaded", _name)

from PIL import Image, ImageChops  # noqa: E402

import build_skins  # noqa: E402
import damage  # noqa: E402
import hullkit  # noqa: E402
from ships import ORDER  # noqa: E402
from skins import SKINS  # noqa: E402


def reload_check():
    """Loading damage again (as a second module object) must not re-wrap the
    renderer hooks or change the per-skin tables -- it must reuse the same
    skin_damage_* modules, so the tables hold the very objects (style
    classes, seeds) the installed hooks recognise."""
    hooks = (hullkit.damage_draw, hullkit.damage_holes, hullkit.render)
    spec = importlib.util.spec_from_file_location("damage_again", damage.__file__)
    again = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(again)
    ok = True
    if (hullkit.damage_draw, hullkit.damage_holes, hullkit.render) != hooks:
        print("FAIL: importing damage twice re-wrapped the hullkit hooks")
        ok = False
    if again.SKIN_D.keys() != damage.SKIN_D.keys() or any(
            again.SKIN_D[k] is not v for k, v in damage.SKIN_D.items()):
        print("FAIL: importing damage twice reloaded the per-skin modules (new SKIN_D objects)")
        ok = False
    return ok


def main():
    args = [a for a in sys.argv[1:] if a != "--reverse"]
    out = None
    if "--out" in args:
        i = args.index("--out")
        out = args[i + 1]
        del args[i:i + 2]
        os.makedirs(out, exist_ok=True)
    keys = [k for k in ORDER if not args or k in args]
    bad = 0 if reload_check() else 1
    with tempfile.TemporaryDirectory() as tmp:
        build_skins.SKIN_BUILD = tmp
        for key in keys:
            for index, skin in enumerate(SKINS[key]):
                _, cells = build_skins.render_cells(key, index)
                sheet = build_skins.compose(cells)
                if index == 0:
                    diff = ImageChops.difference(sheet, build_skins.stock(key)).getbbox()
                    ok, what = diff is None, "pixels " + ("identical" if diff is None else f"DIFFER at {diff}")
                    data = None
                else:
                    data = build_skins.png_bytes(sheet)
                    path = os.path.join(build_skins.SKIN_DIR, f"{key}_{skin.name}.bytes")
                    with open(path, "rb") as fh:
                        ok = fh.read() == data
                    if ok:
                        what = "bytes identical"
                    else:
                        diff = ImageChops.difference(sheet, Image.open(path).convert("RGBA")).getbbox()
                        what = "bytes DIFFER, pixels " + ("identical" if diff is None else f"DIFFER at {diff}")
                if out:
                    name = f"{key}_{skin.name}.png"
                    if data is None:
                        sheet.save(os.path.join(out, name))
                    else:
                        with open(os.path.join(out, name), "wb") as fh:
                            fh.write(data)
                print(f"{key:12s} {skin.name:10s} {what}", flush=True)
                bad += not ok
    print("all sheets match" if not bad else f"{bad} MISMATCH(ES)")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
