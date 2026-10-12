"""Cut the approved HapticGate gate-opening film into the splash's frame set.

Source: ~/Developer/HapticGate-brand/anim_v2/frames_portrait/frame_0000..0179.png
(Codex job "anim_v2": closed logo with charcoal smoke, the gate is forced open at
1.133 s, smoke / steam / warm haze spill out, the HapticGate wordmark flies out of
the door and lands at the bottom by 3.30 s; frames 100..179 are an identical hold).

Output: Assets/Resources/HapticGate/Film/gate_film_NNN.jpg (NNN = film frame at
30 fps), one file per KEPT frame (see GateFilm.Kept in Scripts/UI/GateFilm.cs):
  * rows 416..1520 of the 1080x1920 portrait (every non-black pixel of the whole
    film lies inside: x 9..1070, y 420..1514), 1080x1104,
  * scaled to 810x828 (0.75: indistinguishable from 1080 at phone size, about
    half the texture memory / APK bytes),
  * JPEG q95 4:4:4 (no chroma subsampling: the red neon edges stay sharp; Unity
    re-encodes to ASTC 6x6 on Android / iOS anyway), each well under 1 MiB.
Frames 1..33 equal frame 0 and 101..179 equal frame 100, so they are not stored.
In the near-static stretch 58..73 (only the tiny wordmark deep in the gap moves,
mean change 0.5/255 per frame) every second frame is kept.

Run from anywhere: python3 build_gate_film.py [source_dir]
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Resources", "HapticGate", "Film"))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.expanduser(
    "~/Developer/HapticGate-brand/anim_v2/frames_portrait")

CROP = (0, 416, 1080, 1520)
SIZE = (810, 828)
KEPT = [0] + list(range(34, 58)) + list(range(58, 74, 2)) + list(range(74, 101))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in KEPT:
        im = Image.open(os.path.join(SRC, "frame_%04d.png" % f)).convert("RGB")
        im = im.crop(CROP).resize(SIZE, Image.LANCZOS)
        path = os.path.join(OUT, "gate_film_%03d.jpg" % f)
        im.save(path, quality=95, subsampling=0, optimize=True)
        size = os.path.getsize(path)
        assert size < 1024 * 1024, path
    print("wrote", len(KEPT), "frames to", OUT)


if __name__ == "__main__":
    main()
