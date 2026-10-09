# Concept generation and selection

The three PNGs in `candidates/` are unmodified built-in image-generation outputs. Each used the current `tut_robot.png` as a *registration and silhouette reference*, with a request for a front-facing 64-pixel-style cyberpunk mentor, dark CRT visor, focused slitted eyes, segmented speech bar, gunmetal, cyan/magenta seams, red antenna, and a small brass remnant. The prompts deliberately rejected rounded eyes, smiles, painterly texture, and golden toy framing.

| Candidate | Direction | Review |
| --- | --- | --- |
| `head_a.png` | dark charcoal frame, cyan cable, close-set CRT scanlines | Selected for the shell material clusters and low brass ratio. Its inset expression is replaced by the runtime eye and mouth sprites. |
| `head_b.png` | restrained veteran mechanic, quiet face | Used as the expression reference. It has too much bright steel and brass for the selected shell. |
| `head_c.png` | more angular, brass gear and hazard marks | Informative for panel marks, but brass dominates and the gaze is more hostile. |

`build_kit.py` downsamples candidate A as an **actual material underpainting** on the old kit's binary silhouette. It then hand draws the new visor, plated rim, ports, cable, pipe, antenna, scratches, and hazard pixels on a 64×64 native grid. All other sprites are hand drawn on their native grids in the same limited palette. The old sprites contribute only dimensions and the head alpha silhouette; no old RGB pixels are reused. The script writes both native sources and exact 4× nearest-neighbour exports, plus `preview.png`.

Run `python3 build_kit.py` and `python3 verify_kit.py` from this directory to rebuild and audit. The output remains staged in `Tutorial_new~`; Unity ignores the folder until the kit is promoted in a later step.
