# Frost industrial pixel-background staging (v2)

The v2 sky, snow-flow strip and aurora atlas were promoted byte-for-byte into
`Backgrounds/Resources/Worlds/Frost/Backdrop` (`sky.png`, `flow.png`,
`anim.png` / `anim.json`), so their staged copies (and the superseded v1 sky)
were removed. What is left here is still review-only:

- `frost_far_industrial_pixel_v2.png` — distant icy relay mountains (differs from the live `far.png`).
- `frost_mid_relay_pixel_v2.png` — nearer frozen catwalk and infrastructure layer (differs from the live `mid.png`).
- `frost_fx_industrial_pixel_v2.png` / `.json` — 4×4 effects and landmark atlas; the PNG matches the live
  `fx.png`, but the live `fx.json` names more cells than this manifest.
- `frost_relay_platform_pixel_concept_v1.png` — relay-platform landmark concept.

The staging files already use the current runtime dimensions (512×1024 tiles and 1024×1024 atlases).
Promotion still needs an art-import/review pass and the backdrop checks.
