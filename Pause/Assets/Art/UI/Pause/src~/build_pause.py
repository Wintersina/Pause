#!/usr/bin/env python3
"""The pause overlay: the PAUSED wordmark and the two pause-glow variants,
plus the overlay's flipbook frames. Writes the SVG sources here and
rasterises them with resvg.

  python3 build_pause.py

Outputs (file names and Resources paths are fixed by the game):
  Art/UI/Pause/paused_1.png                scene's initial sprite (PAUSED)
  Art/Resources/PauseGlow/pausedGlow_a.png variant A (moveStarsBackground
  Art/Resources/PauseGlow/pausedGlow_b.png variant B  picks one per pause)
  Art/Resources/PauseGlowFx/pausedGlow_<a|b>_<k>.png  flipbook frames that
      PausedOverlayAnim plays over the chosen variant: 0 squash, 1 stretch
      (the pop-in), 2..5 a hard BONE glint sweeping across the bars (idle).

Akira style (docs/art-style.md): RED slab bars, one RED_SH shadow tone, a
BONE kick, thick INK contour and a hard INK cel offset; no bloom, no glow.
"""
import os
import subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", "..", ".."))
FONT = os.path.join(ASSETS, "Art", "Orbitron", "Orbitron-Bold.ttf")

RED, RED_SH, RED_HI = "#D8232C", "#86121F", "#FF5B45"
INK, BONE, CYAN, AMBER = "#140C14", "#F4EAD4", "#6EF2EE", "#FFB43C"


def pts(p):
    return " ".join(f"{x:.1f},{y:.1f}" for x, y in p)


def bar(x, y, w, h, slant):
    """A slanted slab (italic, leaning right)."""
    return [(x + slant, y), (x + w + slant, y), (x + w, y + h), (x, y + h)]


def bars_body(variant):
    out = []
    shapes = [bar(36, 24, 22, 80, 8), bar(72, 24, 22, 80, 8)]
    # hard ink cel offset
    for s in shapes:
        out.append(f'<polygon points="{pts([(x + 5, y + 6) for x, y in s])}" fill="{INK}"/>')
    for s in shapes:
        (x0, y0), (x1, _), (x2, y2), (x3, _) = s
        out.append(f'<polygon points="{pts(s)}" fill="{RED}"/>')
        # shadow: the lower-right third, one hard tone
        out.append(f'<polygon points="{pts([(x1 - 7, y0), (x1, y0), (x2, y2), (x2 - 7, y2)])}" fill="{RED_SH}"/>')
        # BONE kick down the leading edge
        out.append(f'<polygon points="{pts([(x0 + 4, y0 + 5), (x0 + 8, y0 + 5), (x3 + 5, y2 - 14), (x3 + 1, y2 - 14)])}" fill="{BONE}"/>')
        out.append(f'<polygon points="{pts(s)}" fill="none" stroke="{INK}" stroke-width="5" stroke-linejoin="miter"/>')
    if variant == "b":
        # framing brackets: angular red corners, inked
        for path in ("M18 40 L18 16 L42 16", "M110 88 L110 112 L86 112"):
            out.append(f'<path d="{path}" fill="none" stroke="{INK}" stroke-width="11" stroke-linejoin="miter"/>')
            out.append(f'<path d="{path}" fill="none" stroke="{AMBER}" stroke-width="5" stroke-linejoin="miter"/>')
    else:
        # speed ticks: three short CYAN lines trailing off the left bar
        for k, y in enumerate((44, 62, 80)):
            out.append(f'<line x1="{14 + 3 * k}" y1="{y}" x2="{27 + 3 * k}" y2="{y}" stroke="{INK}" stroke-width="6"/>')
            out.append(f'<line x1="{15 + 3 * k}" y1="{y}" x2="{26 + 3 * k}" y2="{y}" stroke="{CYAN}" stroke-width="2.5"/>')
    return "\n  ".join(out)


def svg(body, w=128, h=128, comment=""):
    c = f"<!-- {comment} -->\n" if comment else ""
    return (f'<?xml version="1.0" encoding="UTF-8"?>\n{c}'
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">\n  {body}\n</svg>\n')


def squash(body, sx, sy):
    # scale about the bars' base so the pose lands on the ground line
    return f'<g transform="translate(64 104) scale({sx} {sy}) translate(-64 -104)">{body}</g>'


def shine(body, x):
    clip = (f'<clipPath id="bars"><polygon points="{pts(bar(36, 24, 22, 80, 8))}"/>'
            f'<polygon points="{pts(bar(72, 24, 22, 80, 8))}"/></clipPath>')
    band = (f'<g clip-path="url(#bars)"><polygon points="{x},128 {x + 10},128 {x + 52},0 {x + 42},0" fill="{BONE}"/>'
            f'<polygon points="{x + 16},128 {x + 19},128 {x + 61},0 {x + 58},0" fill="{BONE}"/></g>')
    return f"<defs>{clip}</defs>{body}{band}"


def wordmark():
    t = ('<text x="196" y="66" transform="skewX(-8)" font-family="Orbitron" font-weight="bold" font-size="54" '
         f'letter-spacing="6" text-anchor="middle" fill="{{fill}}" stroke="{{stroke}}" stroke-width="{{sw}}" '
         'stroke-linejoin="miter" paint-order="stroke">PAUSED</text>')
    out = [
        f'<polygon points="24,14 378,14 362,84 8,84" fill="{INK}"/>',
        f'<polygon points="20,8 372,8 356,78 4,78" fill="{RED}"/>',
        f'<polygon points="20,8 372,8 369,18 17,18" fill="{RED_HI}"/>',
        f'<polygon points="20,8 372,8 356,78 4,78" fill="none" stroke="{INK}" stroke-width="5" stroke-linejoin="miter"/>',
        t.format(fill=INK, stroke=INK, sw=8).replace('x="196" y="66"', 'x="200" y="70"'),
        t.format(fill=BONE, stroke=INK, sw=6),
    ]
    return "\n  ".join(out)


def render(svg_text, name, out_png, w=None, h=None):
    src = os.path.join(HERE, name)
    with open(src, "w") as fh:
        fh.write(svg_text)
    os.makedirs(os.path.dirname(out_png), exist_ok=True)
    cmd = ["resvg", "--use-font-file", FONT, src, out_png]
    if w:
        cmd[1:1] = ["-w", str(w), "-h", str(h)]
    subprocess.check_call(cmd)


def main():
    art = os.path.join(ASSETS, "Art")
    render(svg(wordmark(), 386, 94, "PAUSED wordmark: BONE italic Orbitron on the red title slab, INK contour and cel offset."),
           "paused_1.svg", os.path.join(art, "UI", "Pause", "paused_1.png"))
    fx = os.path.join(art, "Resources", "PauseGlowFx")
    # frame src SVGs are scratch: written to a temp name and kept out of git
    for v in ("a", "b"):
        body = bars_body(v)
        render(svg(body, comment=f"Pause glow variant {v.upper()}: two slanted RED slab bars, RED_SH shadow, BONE kick, INK contour and cel offset."),
               f"pausedGlow_{v}.svg", os.path.join(art, "Resources", "PauseGlow", f"pausedGlow_{v}.png"))
        frames = [squash(body, 1.22, 0.72), squash(body, 0.86, 1.16)]
        frames += [shine(body, x) for x in (-20, 14, 48, 82)]
        for k, fr in enumerate(frames):
            render(svg(fr), "_frame.svg", os.path.join(fx, f"pausedGlow_{v}_{k}.png"))
    os.remove(os.path.join(HERE, "_frame.svg"))


if __name__ == "__main__":
    main()
