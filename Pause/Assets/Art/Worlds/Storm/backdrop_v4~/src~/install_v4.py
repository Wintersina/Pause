"""Install the Storm v4 backdrop art into Art/Backgrounds/Resources/Worlds/Storm/Backdrop3.

    python3 install_v4.py <tiles dir (v1..v4/{sky,far,mid,flow}.png)> <recoloured atlas dir> <Backdrop3 dir>

* copies the 16 tiles (1024x2048) and the five recoloured atlases (JSON untouched),
* rewrites the tile importers: 170.67 pixels per unit (same world size as before), 2048 texture clamp on every
  platform (a 1024 clamp would silently halve the new art), point filter, ASTC 6x6 on the phone, full-rect sprite mesh
  for the sparse flow strips.
"""
import re
import shutil
import sys
from pathlib import Path

tiles, atlases, dest = (Path(a) for a in sys.argv[1:4])
for v in ("v1", "v2"):          # v3 keeps its original art, v4 is gone
    for role in ("sky", "far", "mid", "flow"):
        shutil.copyfile(tiles / v / f"{role}.png", dest / v / f"{role}.png")
        meta = dest / v / f"{role}.png.meta"
        t = meta.read_text()
        t = re.sub(r"spritePixelsToUnits: [0-9.]+", "spritePixelsToUnits: 170.66667", t)
        t = re.sub(r"(buildTarget: (?:DefaultTexturePlatform|Standalone|Android|iOS)\n\s+maxTextureSize: )\d+", r"\g<1>2048", t)
        if role == "flow":
            t = t.replace("spriteMeshType: 1", "spriteMeshType: 0")
        meta.write_text(t)
for name in ("landmarks", "sites", "weather", "lightning", "wx_ambient"):
    shutil.copyfile(atlases / f"{name}.png", dest / f"{name}.png")
print("installed")
