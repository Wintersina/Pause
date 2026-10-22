"""Bank-left stopgap: Codex's painted bank-left tilted the left pod like a yaw (rotated, detached look), while its
bank-right is a clean roll. The intact Paranoid is left/right symmetric, so bank-left = horizontal mirror of
bank-right (cell 4 -> cell 3) in hull_redo_state0.png. Original saved as hull_redo_state0_codex.png.
Damage is NOT mirrored: the damaged rows are produced later from the idle damaged cell (damage_overlay.py)."""
import os
import shutil
from PIL import Image, ImageOps

d = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
p = os.path.join(d, "hull_redo_state0.png")
bak = os.path.join(d, "hull_redo_state0_codex.png")
if not os.path.exists(bak):
    shutil.copy(p, bak)
im = Image.open(bak).convert("RGB")
n = im.width // 3
c4 = im.crop((n, n, 2 * n, 2 * n))                    # cell 4 = row 1, col 1
c3 = ImageOps.mirror(c4)
im.paste(c3, (0, n))                                   # cell 3 = row 1, col 0
im.save(p)
print("bank-left replaced by the mirror of bank-right")
