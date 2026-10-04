# Art audit v2: neon pixel art conversion checklist

The rules are in [`art-style.md`](art-style.md) (v2, neon pixel art). The kit is
`Pause/Assets/Art/PixelKit/src~/pixelkit.py`, and the recipes are in `samples.py` in the same
folder. Paths below are relative to `Pause/Assets/` unless they start with `docs/`.

Every conversion must keep the asset's **file name, frame count, frame order and pivot**. Set the
loader's world size so that **1 game pixel = 1/64 u**: `FrameWorldSize = frame_px / 64` for
enemies, and the equivalent for bosses and backgrounds. Preview each asset at true scale on its
world background, with `bg_check` passing.

## CONVERT

### Enemies

The four worlds, Space, Frost, Verdant and Ember, are listed as `<w>`.

- [ ] **Fighters, 4 tiers.** Sprites: `Art/Resources/Enemies/<w>_fighter_{1..4}.png`. Frame:
  58–61 px.
- [ ] **Chasers.** Sprites: `Art/Resources/Enemies/<w>_chaser.png`. Frame: 61 px. Keep the
  lunge tell.
- [ ] **Aliens.** Sprites: `Art/Resources/Enemies/<w>_alien.png`. Frame: 44 px.
- [ ] **Heavies.** Sprites: `Art/Resources/Enemies/<w>_big.png`. Frame: 84 px.
- [ ] **Code to update for all four:**
  - Generators in `Art/Enemies/src~/` (`fighter.py`, `chaser.py`, `alien.py`, `big.py`,
    `build.py`). Rewrite them on PixelKit.
  - `Scripts/Gameplay/Enemies/EnemyRoster.cs`: `FrameWorldSize`, `IdleTicks`.
  - `Scripts/Gameplay/Enemies/EnemyPalette.cs`: hit and death colours, set to the v2 hexes.
  - `Art/Enemies/src~/palette.env`.

### Rocks

- [ ] **Space rocks.** Sprites: `Art/Resources/Enemies/space_rock_{crater,cluster,dark}.png`.
  Frame: 42–44 px.
- [ ] **Frost rocks.** Sprites: `Art/Resources/Enemies/frost_rock_{chunk,rime,shard}.png`.
- [ ] **Verdant rocks.** Sprites: `Art/Resources/Enemies/verdant_rock_{knot,pod,spore,vine}.png`.
- [ ] **Ember rocks.** Sprites: `Art/Resources/Enemies/ember_rock_{cinder,islet,magma,obsidian}.png`.
- [ ] **Generator:** `Art/Enemies/src~/rocks.py`.

### Mines

- [ ] **Rail mines.** Sprites: `Art/Resources/Enemies/<w>_mine.png`. Generator:
  `Art/Enemies/src~/mine.py`. Player: `Scripts/Gameplay/RailBombAnimator.cs`.
  - This is the **benchmark asset**. Follow the reference atlas
    (`git show 18b5e5f^:Pause/Assets/Art/Resources/Vfx/rail_bomb_themes_atlas.png`) closely.
  - Key poses: dormant → waking → charging → burst.
  - Space is already recreated in `samples.py: rail_mine()`. Generalise it to the other three
    worlds:
    - Frost: an ice shell with facets and a snowflake core.
    - Verdant: bark plus vines, a lime core and **magenta** thorns.
    - Ember: basalt with **orange** magma.

### Bosses

All four need a **full redesign**: new silhouettes, not a restyle of the current art.

- [ ] **Space boss.**
- [ ] **Frost boss.**
- [ ] **Verdant boss.**
- [ ] **Ember boss.**
- [ ] **Shared files for all four:**
  - Sheets: `Art/Resources/Bosses/<World>.png`.
  - Cards: `<World>_card.png`.
  - Projectiles: `<World>_shots.png`. Frame: 28–30 px.
  - Generator: `Art/Bosses/src~/bosses.py`. Rewrite it on PixelKit; the per-boss folders are
    `Art/Bosses/<World>/src~`.
  - Cell size: 208–212 px (`BossConfig.BossWorldSize` 3.3 u ≈ 211 px). Body: 128–192 px.
  - Code: `Scripts/Bosses/BossArt.cs`, `BossConfig.cs`.
- [ ] **Boss warning.** Sprite: `Art/Resources/Bosses/warning.png`. Source:
  `Art/Bosses/src~/warning.svg`.

### World backgrounds

Each world's parallax layers are in `Art/Resources/Worlds/<World>/Backdrop/`.

- [ ] **Space.** Layers: `sky.png`, `anim.png` + `anim.json`, `fx.png` + `fx.json`.
- [ ] **Frost.** Layers: `sky`, `far`, `mid`, `flow`, `anim`, `fx`.
- [ ] **Verdant.** Layers: as in its folder.
- [ ] **Ember.** Layers: as in its folder.
- [ ] **Shared for all four:**
  - Generators: `Art/Worlds/src~/` (`bgkit.py`, `frost.py`, `ember.py`, `palette.py`,
    `altitude.py`) and `Art/Worlds/<World>/src~`. Port them to PixelKit `bg` ramps.
  - Limits: each layer must pass `pixelkit.bg_check`.
  - Screen: 365 × 640 game px.
- [ ] **Menu, shop and credits starfields.** Files: `Art/Backgrounds/startMenuStartField.png`,
  `shop.png`, `credits.png`, `starFieldMainGame.png`. The start-menu field sits under the
  protected logo, so it gets nothing glowing in the logo's bounds.

### Walls

- [ ] **Frost, Verdant and Ember walls.** Files:
  `Art/Resources/Worlds/{Frost,Verdant,Ember}/wallLeft.png`, `wallRight.png`. Loader:
  `Scripts/Worlds/WorldPainter.cs`, which applies `theme.tint`; check that the tint doesn't
  muddy the ramps.
- [ ] **Space walls.** Files: `Art/left.png`, `Art/right.png` (`left_1.mat`, `right_6.mat`,
  `right_7.mat`).
- [ ] **Rules:** material ramps, lower 4 tones, neon at step 1 or below.

### Enemy explosions and hit FX

- [ ] **Target explosions.** Sheet: `Art/Resources/Weapons/Explosions.png`, generator
  `Art/Weapons/src~`. Script: `Scripts/Gameplay/TargetExplosion.cs`, `WeaponArt.cs`.
  - Convert the kind rows: Metal, Rock, Mine, Ice, Spore, Magma. Use 6–8 frames with stepped
    halos and world particles.
  - The weapon-colour flash and ring cells (row 0, columns 10–12) belong to the player's
    weapon. Redraw them at the same pixel density, but **keep the weapon colours**.
- [ ] **Hit flash.** 2 ticks, all non-ink pixels at neon step 4. Code:
  `Scripts/Gameplay/Enemies/EnemyFlipbook.cs` or `EnemyPalette.cs`.
- [ ] **Legacy explosion frames.**
  - `Art/RedExplosion/1_*.png` (`halfRedAnime.anim`)
  - `Art/BlueEffects/1_4..1_11.png` (`halfBlueAnime`)
  - `Art/Animation/explosion.png` (`prefabs/explosion_0`)
  - `Art/Animation/explode_1.png` (`prefabs/explode1_0`)

  Convert each one that is still shown, and delete the rest.
- [ ] **Teleport portal.** Atlas: `Art/Resources/Vfx/teleport_portal_atlas.png`. Loader:
  `Scripts/Worlds/TeleportPortalSprites.cs`.

## KEEP (don't touch in this conversion)

| What | Paths | Why |
|---|---|---|
| Player ships and exhaust | `Art/Resources/ShipArt/Hulls/*.png` + `src~`, `Art/Resources/ShipArt/Exhaust/`, `Art/Resources/Weapons/<Ship>.png`, `AttackFx.png`, `SecretMeter.png` | User: "leave the ship designs alone for now". They stay flat cel (v1) |
| Ship damage FX | `Art/Resources/Vfx/ship_damage_fx_atlas.png` | player-side |
| Atoms / pickups | `Art/Resources/Pickups/heal_atom_green.png`, `Art/Resources/Pickups/Atoms/*` | already pixel art; the green atom is a benchmark |
| Life heart | `Art/Resources/Vfx/lifeHeart.png` | player-side |
| UI (flat cel) | `Art/Resources/{DeathPanel,Hud,QuickActions,Codex,Tutorial,PauseGlow,PauseGlowFx,Shield}/`, `Art/UI/**` | v1 cel style still governs the UI |
| PAUSE logo | `Art/pause_title_2.png`, `docs/pause-title.png` | protected |
| Splash mark | `Art/HapticGate.png` | protected |
| App icon | `Art/Retro80s/Ships/SourceStrips/xenon2_ship.png` | on HOLD (product call) |
| Font, materials, audio | `Art/Orbitron/`, `Art/Materials/` | not art |

## Suggested order

1. Mines, in all four worlds. They are the benchmark, and the recipe exists.
2. Fighters, chasers, aliens and heavies, one world at a time.
3. Rocks.
4. Explosions and hit flash.
5. Backgrounds and walls, then the menu, shop and credits fields.
6. Bosses, starting with Space; see `boss_space_bust.png`.
