from pathlib import Path
from PIL import Image
import sys
sys.path.insert(0, str(Path(__file__).parent))
from spec import SHIPS
HERE = Path(__file__).parent
RES = HERE.parents[2] / "Resources" / "Enemies"
for name, cell, *_ in SHIPS:
    im = Image.open(RES / f"{name}.png").convert("RGBA").crop((0, 0, cell, cell))
    im.resize((cell * 3, cell * 3), Image.NEAREST).save(HERE / "in" / f"{name}_in.png")
print("ok")
