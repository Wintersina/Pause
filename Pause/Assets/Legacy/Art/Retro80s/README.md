# Retro 80s Art

- `Ships/` — master player-ship sprites. Each hull has `intact`, `damaged`,
  `critical`, and three `_idle0..2` frames.
- `Ships/SourceStrips/` — the compact supplied sprite-strip references used
  for the small arcade-style player hulls.
- `../../Resources/Prefabs/Ships/Retro80s/` — runtime copies loaded by the
  dock and player. Keep this folder's names synchronized with `Ships/`.
- `Asteroids/Reference/` — the three chosen compact asteroid designs.
- `Enemies/` and `Pickups/` — standalone retro gameplay art.

The existing `Art/Aestroids/` paths are retained for the asteroid prefab GUIDs;
their active textures have been replaced with the compact reference designs.
