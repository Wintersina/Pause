"""Engine plume flipbook: the Akira tail-light streak (docs/art-samples
player_exhaust), one plume per nozzle.

    python3 exhaust.py   ->  ../../Exhaust/trail_strip.png  (6 frames, 64x256 px each)

Canvas 32 x 128 u per frame, rendered at 2x. The flame head (where it leaves
the nozzle) is at y = HEAD; ShipExhaust pivots the sprite there. Frame table
and holds are the sample's, so the plume plays on 2s with a 1-tick smear.
"""
import os
import subprocess

from PIL import Image

from hullkit import *  # noqa: F401,F403

OUT = os.path.abspath(os.path.join(HERE, "../../Exhaust"))
BUILD = os.path.join(HERE, "build")
W, H, ZOOM, HEAD = 32, 128, 2, 3.0

# (length, width, smear, flare) -- docs/art-samples/src/sprites.py EXHAUST_FRAMES
FRAMES = [
    (74, 1.00, False, 0.6),
    (92, 1.05, False, 0.8),
    (122, 0.72, True, 1.0),
    (104, 0.92, False, 0.9),
    (80, 1.10, False, 0.7),
    (88, 1.00, False, 0.75),
]
TIMING = [2, 2, 1, 2, 2, 2]


def plume(i):
    L, wm, smear, flare = FRAMES[i]
    cx, top = W / 2, HEAD
    w = 9.0 * wm
    outer = [(cx - w, top), (cx + w, top), (cx + w * 0.55, top + L * 0.55), (cx, top + L), (cx - w * 0.55, top + L * 0.55)]
    mid = [(cx - w * 0.62, top), (cx + w * 0.62, top), (cx + w * 0.3, top + L * 0.5), (cx, top + L * 0.78),
           (cx - w * 0.3, top + L * 0.5)]
    core = [(cx - w * 0.3, top), (cx + w * 0.3, top), (cx, top + L * 0.45)]
    back = poly(outer, RED, f'opacity="{0.5 * flare}" filter="url(#glowS)"')
    body = poly(outer, RED) + poly(mid, SODIUM) + poly(core, BONE)
    dy = top + L * (0.22 if i % 2 == 0 else 0.3)
    body += poly([(cx, dy - 5), (cx + 3, dy), (cx, dy + 5), (cx - 3, dy)], BONE)   # hard shock diamond
    if smear:
        for k, x in enumerate([cx - 11, cx + 11, cx - 13.5, cx + 13.5]):
            y0 = top + 12 + k * 8
            body += line([(x, y0), (x, y0 + 50 + k * 8)], 1.4, CYAN, 'opacity="0.8"')
        body += poly([(cx - 2.4, top + 22), (cx + 2.4, top + 22), (cx, top + L + 4)], RED_HI, 'opacity="0.85"')
    return f'<g id="glow-back">{back}</g><g id="trail">{body}</g>'


def main():
    os.makedirs(BUILD, exist_ok=True)
    cw, ch = W * ZOOM, H * ZOOM
    strip = Image.new("RGBA", (cw * len(FRAMES), ch), (0, 0, 0, 0))
    for i in range(len(FRAMES)):
        p = os.path.join(BUILD, f"exhaust_{i}.svg")
        with open(p, "w") as fh:
            fh.write(svg(W, H, plume(i), f"exhaust frame {i}: hold {TIMING[i]} ticks"))
        png = p[:-4] + ".png"
        subprocess.run(["resvg", "--zoom", str(ZOOM), p, png], check=True)
        strip.alpha_composite(Image.open(png).convert("RGBA"), (i * cw, 0))
    os.makedirs(OUT, exist_ok=True)
    strip.save(os.path.join(OUT, "trail_strip.png"), optimize=True)
    print("exhaust strip", strip.size, "head pivot y =", 1 - HEAD / H)


if __name__ == "__main__":
    main()
