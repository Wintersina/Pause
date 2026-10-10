# Iron Kraken damage source paintings

The existing `Tide~/Tide.png` idle cell 0 was the edit reference. All painted
keys were generated with transparent backgrounds, then sampled to 384 px with
nearest-neighbour only. `build_damage.py` pins the eye and silhouette to the
pristine game-space cell; it paints the six arc timing frames and animates the
separately painted smoke key. No smoke or electricity is baked into the body.

Selected damage keys: `stage1_b.png`, `stage2_b.png`, `stage3_a.png`,
`stage4_a.png`. The sibling A/B files are the inspected alternate candidates.

- Stage 1: cracked barnacle-plated brow and shoulder, bent cannon, rust runoff,
  damaged eye.
- Stage 2: open dome and shoulder plate, exposed mint machinery and cables,
  split beak, crushed limb ends.
- Stage 3: major dome breach and visible reactor, broken eye and beak,
  detached or cable-hung tentacle ends.
- Stage 4: ruptured hull and cannon barrels, multiple mangled limb ends,
  fractured eye and exposed unstable mint machinery.

Death keys: `death_split.png`, `death_overload.png`, `death_peak.png`,
`death_shockwave.png`, `death_fade.png`. Death frame 0 adds a white-hot breach
flash to the selected stage-4 painting. Frames 1 through 5 use the painted
death keys. `smoke_key.png` is the painted FX source for the three plumes.

Painting prompt constraints repeated for every edit: preserve the front-on
radial Iron Kraken design, weathered brass/rust-brown iron and grey-green
barnacles, 1 px dark outlines, textured short tone ramps, mint core and rim
light, tiny blue-violet accents, actual broken armour geometry, transparent
background, no red or orange glow. The damage progression explicitly asked
for large structural failures in stages 3 and 4.

Build and check:

```sh
python3 Pause/Assets/Art/Bosses/TideDamage~/src~/build_damage.py
python3 Pause/Assets/Art/Bosses/TideDamage~/src~/verify_damage.py
```
