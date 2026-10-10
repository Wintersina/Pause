# Tide death painting prompts

Mode: built-in imagegen, using each approved `tide_enemies/tide_*_key.png` as
the image reference. Each source is a transparent two-panel painting. The left
panel paints rupture; the right paints dispersal. Final strips preserve idle
cell 3 for the first pose and recell these paintings with nearest-neighbour
resampling only.

Shared prompt:

> Use case: stylized-concept. Asset type: painted key frames for a Tide enemy
> death flipbook in a vertical 2D shooter. Input image is the approved intact
> enemy. Create a transparent horizontal two-panel sprite sheet, equal square
> panels, no labels or grid. Left: same anatomy and top-down orientation, body
> cracked and half dissolved, dark and desaturated, radial mint-white discharge
> cracks, chips and underwater spray. Right: no intact body, only matching
> shell/brass/stone chips, drifting spray and bubbles, sparse mint glints and
> tiny blue-violet sparks. Dense painterly pixel art, crisp hard pixel edges,
> selective 1-pixel dark outline, rich 4-6 tone ramps, pale mint rim upper left.
> Materials: dark gunmetal, barnacle grey-green, weathered brown brass, bone and
> pearl. Dominant emission: bioluminescent mint #7CF2C0. Center each effect
> with generous transparent margins. No red, lime, cyan fields, orange glow,
> text, backdrop or blur.

Subject additions used in each prompt:

| Source | Rupture direction |
| --- | --- |
| `tide_alien_painted_death_source.png` | Jelly bell bursts into a ring of mint fluid droplets and torn tendrils. |
| `tide_chaser_painted_death_source.png` | Eel snaps into separated S-shaped iron-brass segments and sparking joints. |
| `tide_big_painted_death_source.png` | Nautilus spiral shell opens in plates, a pearl burst pierces the center, tentacle pipes tear, large plume. |
| `tide_fighter_1_painted_death_source.png` | Remora suction disc splits along ribs; fins and tail shear off. |
| `tide_fighter_2_painted_death_source.png` | Needlefish beak cannon and dorsal blade break into sections. |
| `tide_fighter_3_painted_death_source.png` | Angler lure stalk and bulb shatter; toothed jaw and armored head split. |
| `tide_fighter_4_painted_death_source.png` | Hammerhead splits through the transverse head; tip cannons and gill plates tear away. |
| `tide_rock_brain_painted_death_source.png` | Maze-fold brain coral breaks into irregular slabs, barnacle pits and mint polyps. |
| `tide_rock_staghorn_painted_death_source.png` | Upright reef branches snap into angular coral and brass-capped tips. |
| `tide_rock_urchin_painted_death_source.png` | Bone-white iron spines blast outward as the central shell cracks. |
| `tide_rock_islet_painted_death_source.png` | Upright floating slab fractures; kelp cap breaks and roots fray. |
| `tide_mine_painted_death_source.png` | Horned limpet mine breaks into horn and shell plates around the mint core; brass clamp and water spray remain. |

The mine death painting used the finished four-pose limpet sheet as its image
reference. Its left panel shows rupture and its right panel shows drifting
clamp, pearl, and brass chips.
