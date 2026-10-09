# Steel Hound image generation

Built-in image generation edited `original.png` on a transparent background.
All four candidates are retained here. The original was saved before editing.

- `candidate_idle_a.png`: four fixed-scale idle poses; rotating turbines, flexing claws, copper ripple, visor scan, belly pulse.
- `candidate_idle_b.png`: alternate four-frame strip emphasizing turbine quarter-turns, 8 px-equivalent claw-tip movement, visor sweep, and stronger core pulse. Selected for idle 0–3.
- `candidate_tell_a.png`: rear with open claws, forward lunge with afterimages, localized hit flash and chips. Selected for tells and hit.
- `candidate_tell_b.png`: alternate restrained charge poses. Retained for comparison.

All prompts specified the original hound as the identity reference: steel and copper hull, twin magenta turbine pods, curved steel claws, cyan visor, magenta belly core, rich painterly pixel detail, crisp dark outlines, transparent background, no red, and one centered body per frame. Tell prompts required the same-scale hull, distinct rear/lunge/hit silhouettes, local white-magenta flash, and visible hull material.

`build_hound.py` crops the painted poses at fixed cell divisions, applies nearest-neighbour scaling and discrete alpha steps, remaps any stray saturated red to magenta or copper, and writes the live strip and previews. It does not synthesize the poses.
