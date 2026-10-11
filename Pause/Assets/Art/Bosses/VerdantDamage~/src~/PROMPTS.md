# Verdant damage image generation

Reference for every body candidate: the supplied 384×384 `Verdant_idle.png` crop.
All generations used transparent-background edit mode. Each stage received two
independent candidate prompts. Candidate numbers match `candidate_STAGE_VARIANT.png`.

Shared body prompt: preserve the exact Bloom Queen pose, camera, scale and
identity: dark leaf-plate petals, glowing lime seed core, brass twin cannons,
two green bulb stalks, and dangling vine roots. Keep surviving forms aligned
to the reference. Native-looking hard-edged pixel art, dark selective outline,
short green/brass ramps. Transparent alpha; no smoke, electrical FX, blur,
background or red.

| Stage | Variant 1 | Variant 2 | Selected |
| --- | --- | --- | --- |
| 1 | Torn right leaves and upper left petal, exposed inner machinery, chipped bands, bent right bulb stalk, dented right cannon. | Torn left lower and upper right leaves, scorched splits, bent left bulb stalk, dented left cannon. | 2 |
| 2 | Large upper left and right mid-petal splits with leaking seed light, sheared root, sap pipes, split right barrel. | Large upper right and left lower splits, exposed seed light, split left muzzle, sheared vine, sap leaking from brass tubes. | 2 |
| 3 | Huge upper right and lower left breaches with transparent missing plates, one shattered bulb, hanging root cables, torn cannon. | Ripped upper left and lower right plates, exposed industrial ribs and core, one broken bulb, roots on cables. | 1 |
| 4 | Near-destroyed front and rear breaches, shredded petals, mangled cannons, broken bulb, frayed cables, cracked guttering seed core. | Wider leaf breaches, dark brass mechanisms, mangled cannons and bulb stalks, patchy white-lime core. | 1 |

`fx_candidate_1.png` and `fx_candidate_2.png` were separate smoke-overlay
generations: thick dark olive/green-grey pixel smoke and spore puffs jetting
from shoulder breaches, emerald embers at the base, center core clear, no boss
body. `fx_candidate_3.png` and `fx_candidate_4.png` were separate electrical
overlay generations: bright white-green jagged arcs with emerald fringe and
spark bursts, no background. The latter two included some unwanted boss pixels;
`build.py` extracts only thin, bright spark fragments and connects them with
crisp 2 px arcs. The smoke candidates are mixed across six loop frames.

`build.py` registers the chosen painted candidates to the pristine solid-alpha
envelope using nearest-neighbour resampling, adds two-frame flicker/shard/sap
motion, and exports the two atlases and previews. The candidate PNGs remain in
this folder for inspection and future repainting.
