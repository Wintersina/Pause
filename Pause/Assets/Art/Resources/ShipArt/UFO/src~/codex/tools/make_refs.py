"""Reference images for the UFO briefs: the LEGACY (pre-restyle) cell 0 of each damage row, 4x nearest on green.
Run once from a tree where ShipArt/UFO/UFO.png is still the old flat-cartoon sheet (git show the master version)."""
import os, shutil, sys
from PIL import Image
CODEX = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SHIPART = os.path.normpath(os.path.join(CODEX, "..", "..", ".."))
im = Image.open(sys.argv[1] if len(sys.argv) > 1 else os.path.join(SHIPART, "UFO", "UFO.png")).convert("RGBA")
for s in range(3):
    c = im.crop((0, s * 256, 256, s * 256 + 256)).resize((1024, 1024), Image.NEAREST)
    bg = Image.new("RGBA", (1024, 1024), (0, 255, 0, 255))
    bg.alpha_composite(c)
    bg.convert("RGB").save(os.path.join(CODEX, f"ref_old_state{s}.png"))
shutil.copy(os.path.join(SHIPART, "Paragon", "src~", "ship_alone.png"), os.path.join(CODEX, "paragon_ship_alone.png"))
print("ok")
