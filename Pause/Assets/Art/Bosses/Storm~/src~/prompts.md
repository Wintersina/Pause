# Tempest Engine source notes

This staged boss was assembled from built-in image generation and native pixel edits. The generated keys remain in this folder. `build.py` resamples them with nearest-neighbour, keeps one selected hull in registration, and changes only the blade, coil, crown, and core regions for ordinary poses. It draws extra hard-edged arcs at native size. The card lettering and panel are drawn programmatically to keep the exact title readable.

## Candidate review

| File | Review |
| --- | --- |
| `candidate_a.png` | Strong structure and exhaust detail, but the central reactor dominates the drum and the coil/crown electricity occupies too much area. |
| `candidate_b.png` | Clear crown and broad flank blades; front drum looks flatter and its white reactor is less focused. |
| `hull_key.png` | Selected: distinct drum, twin wound coil towers, lightning-rod crown, and exposed flank turbines. Best read at 384 px. |

`fire_key.png` and `death_key.png` are generated edits of the selected hull, revealed only in their relevant poses. `shots_candidate_a.png` and `shots_candidate_b.png` are generated projectile studies; the sheet uses the best crop for each projectile type. Accidental source reds, purples, and strong blues are steered to desaturated Storm steel or pale-yellow electricity while retaining the source values and texture. No global color quantization is applied.

## Prompt direction

Transparent square sprite, symmetric floating weather-factory turbine, lower round armored drum with a white-hot reactor behind a grille, two separate Tesla coil pylons, a fan of long lightning rods, exposed flank turbine blades and exhaust. Riveted pitted gunmetal and slate, 4-6 material tones, rich tiny scratches and fasteners, upper-left pale rim, storm-white and pale-yellow compact electricity, cold blue-white arcs. No red, orange, magenta, green, violet, or cyan fields; no scene or text.

Fire edit: preserve the hull exactly; flare only the core and add a short three-prong downward arc fan. Death edit: preserve the machine identity; burst the drum into dark plated fragments surrounding a white-yellow electrical implosion. Projectile studies: separate bolt, hail spear, vertical beam, and charge-star effects on transparent background.

## Build and verification

Run `python3 Pause/Assets/Art/Bosses/Storm~/src~/build.py` from the worktree root. Then run `audit.py` for per-cell margins, colors, boundaries, registration, motion, red-band share, and contrast against both Storm cloud decks. `verify_boss_art.py` is an unchanged copy of the project checker; run it with `Pause/Assets/Art/Bosses/Storm~ Storm --no-damage --no-death`.
