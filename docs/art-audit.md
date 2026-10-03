# Art audit: current assets vs the Akira style

This list is classified against the **Audit rule** in [`art-style.md` §8](art-style.md#8-audit-rule-when-an-existing-asset-must-change).
Paths are relative to `Pause/Assets/` unless they start with `docs/` or `ProjectSettings/`.
"Used by" was checked by guid against scenes, prefabs, animations and materials, and by
`Resources.Load` paths in `Scripts/`.

| Verdict | Meaning |
|---|---|
| **PROTECTED** | The PAUSE logo and other marks. Never touch. ([§9](art-style.md#9-protected-assets-the-pause-logo)) |
| **REDRAW** | New SVG from scratch, same world size, pivot and frame count |
| **RESTYLE** | Edit the existing SVG source: palette, ink, chamfers, remove gradients and bloom |
| **KEEP** | Compliant, or not art |
| **UNUSED** | Not referenced anywhere; skip |
| **HOLD** | Product decision needed before anyone touches it |

Rule codes cited below: 1 = 3D shading, 2 = photo/painterly texture, 3 = glossy/bubbly, 4 = no ink,
5 = off-palette/role confusion, 6 = readability.

---

## Protected

| Asset | Path | Used by | Verdict |
|---|---|---|---|
| PAUSE title logo | `Art/pause_title_2.png` | `Scenes/startS4.unity` | **PROTECTED** |
| PAUSE title logo (README) | `docs/pause-title.png` | `README.md` | **PROTECTED** |
| HapticGate studio splash mark | `Art/HapticGate.png` | `Scenes/spashS7.unity` | **PROTECTED** |
| "PAUSED" wordmark | `Art/paused_1.png` | `Scenes/gameS1.unity`, `Scenes/tutorialS5.unity` | **Not protected** (user decision). **RESTYLED**: red title slab, BONE Orbitron, ink (`Art/UI/Pause/src~`) |
| Pause-glow bars | `Art/Resources/PauseGlow/pausedGlow_a.png`, `pausedGlow_b.png` | `Scripts/UI/moveStarsBackground.cs` | **Not protected** (user decision). **RESTYLED**: inked red slab bars + pop-in/glint flipbook (`PausedOverlayAnim`) |

## Player ships

**Converted.** All 15 roster hulls (Retro80s 1-7 and Originals 8-15, including Turtle) and the engine
exhaust are redrawn. Each ship is one flipbook sheet, `Art/Resources/ShipArt/Hulls/<ShipId key>.png`,
holding 6 idle drawings, bank left/right and a hit flash, each in intact/damaged/critical. The exhaust is
`Art/Resources/ShipArt/Exhaust/trail_strip.png`. The generator is `Art/Resources/ShipArt/Hulls/src~/`
(`build.py`, `exhaust.py`, `check.py`, `preview.py`). The old Retro80s, Originals, OriginalsIdle, prefab
Turtle and exhaust `.psd` files were removed, because only code loaded them. Still open: the legacy
`Art/Animation` sheets and the SourceStrips `player.png`/`ship.png`. Neither is shown in game any more,
because the roster art overrides both. `xenon2_ship.png` stays on HOLD.

| Asset | Path | Why | Verdict |
|---|---|---|---|
| Retro80s hulls 1-7 (Neon Comet, Volt Viper, Solar Fang, Crimson Halo, Ion Lancer, Jade Phantom, Gold Warden), each `intact/damaged/critical` + `_idle0-2` (84 files) | `Art/Resources/Prefabs/Ships/Retro80s/<Name>_<state>[_idle<n>].png` | pixel art with gradient shading, no ink, off-palette hues (blue, green, gold hulls) (1, 4, 5) | **REDRAW**. Keep each hull's identity colour as a stripe or canopy accent over a red/warm base; see open questions |
| Original hulls 8-15 (Lightning, Ligher, Paranoid, Ninja, Saboteur, UFO, Dove, Turtle) | `Art/Resources/ShipArt/Originals/*.png` (128×32 strips, Turtle 32×32) | shaded pixel art (1, 4, 5) | **REDRAW** |
| Original hull idles | `Art/Resources/ShipArt/OriginalsIdle/<Name>_idle0-2.png` (24 files) | same | **REDRAW** (3 frames, on 2s) |
| Legacy ship sheets (animated in-game prefabs) | `Art/Animation/{Dove,Ligher,Lightning,Ninja,Paranoid,Saboteur,UFO}.png` | used by `Resources/prefabs/Ships/*_0.prefab`, `inGameShips/ship*.prefab` (1, 4) | **REDRAW** (in step with the Originals) |
| Turtle (prefab art) | `Art/Resources/Prefabs/Ships/Turtle.png` | shaded pixel (1) | **REDRAW** |
| Source strips: player / ship | `Art/Retro80s/Ships/SourceStrips/player.png`, `ship.png` | `prefabs/Ships/player.prefab`, `ship.prefab`, title traffic in `startS4` (1, 4) | **REDRAW** |
| Source strip: xenon2 (also the **app icon**) | `Art/Retro80s/Ships/SourceStrips/xenon2_ship.png` | `ProjectSettings/ProjectSettings.asset` default icon | **HOLD**. Changing the store icon is a product call |
| Engine exhaust frames | `Art/Resources/ShipArt/Exhaust/Engine_exhaust1_frames.psd`, `Engine_exhaust2_frames.psd`, `Engine_exhaust3_frames.psd`, `Engine_exhaust3_frames_1.psd` | soft painted flame (1, 2) | **REDRAW** as the tail-light streak (`player_exhaust` sample) |
| Engine exhaust (duplicate) | `Art/Engine_exhaust/Engine_exhaust2_frames.psd` | duplicate of the above | **REDRAW** (or delete with the above) |
| Ship gun roster atlas | `Art/Resources/ShipArt/Guns/ship_gun_roster.png` | painted glossy guns (1, 3) | **REDRAW** |
| Shield (Amadeus) | `Art/ShipShields/Amadeus-Shild.png` | glossy crystal gradient (1, 3) | **REDRAW** as a hard-edged hex/chevron shield |
| Shield bubble | `Art/transparent-bubble.png` | used by all `inGameShips/ship*.prefab` and `Dove_1`; a literal glass bubble (3) | **REDRAW** as an angular energy shell (a cel hex lattice) |
| Shop images | `Art/Proteus-Shop-Image.png`, `Art/Engine_exhaust/Amadeus-Shop-Image.png` | not referenced | **UNUSED** |

## Enemies

| Asset | Path | Why | Verdict |
|---|---|---|---|
| Kenney fighters, 4 tiers × 5 (Black, Blue, Green, Red) | `Art/Resources/Prefabs/Enemies/Kenney/enemy{Black,Blue,Green,Red}{1-5}.png` | flat but no ink, rounded, the Red tier breaks the player=red rule (4, 5) | **DONE (replaced)**. Out of the spawn pool: each world now fields its own four fighter tiers from `EnemyRoster` (`Art/Enemies/src~`, strips in `Art/Resources/Enemies`). The Kenney prefabs/PNGs are unreferenced and await a delete decision |
| Chaser | `Art/Resources/Prefabs/Enemies/Kenney/enemyRed5.png` (via `Prefabs/Enemies/kn_enemyRed5`) | as above; currently red (5) | **DONE (replaced)**: per-world chasers (`<world>_chaser`) with a lunge tell |
| Alien | `Art/invader32x32x4.png` (`Resources/prefabs/alien1.prefab`) | shaded pixel, cute-round (1, 3, 4) | **DONE (replaced)**: per-world aliens (`<world>_alien`), still named `alien1` in play; the prefab is only the spawner's fallback |
| Rail mine / rail bomb (4 worlds × 4 frames) | `Art/Resources/Vfx/rail_bomb_themes_atlas.png` | painted glossy metal (1, 3) | **DONE**: Space keeps the approved design, restyled in flat ink; Frost (Geode), Verdant (Burr) and Ember (Crucible) are new, world-specific mines. The atlas is repacked from `<world>_mine` frames 0, 1, 4, 5 |
| Ember rail mine frames | `Art/Resources/Vfx/rail_mine_ember_1.png`, `rail_mine_ember_2.png` | same | **DONE (restyled)** from `ember_mine` frames 0 and 5; the game now plays the full `ember_mine` flipbook, so these two are no longer read at runtime |
| Legacy mine (animated) | `Art/Aestroids/1.png`, `Art/Aestroids/2.png` (`Art/Animation/mineAnime.anim`) | glossy red sphere with spikes (1, 3) | **REDRAW** if still spawned. Prefab `Prefabs/mine` was retired in code, so verify before working on it |

## Asteroids and hazards

| Asset | Path | Why | Verdict |
|---|---|---|---|
| Pixel asteroids | `Art/Aestroids/aestroid_brown.png`, `aestroid_brown_1.png`, `aestroid_dark.png`, `aestroid_dark_1.png`, `aestroid_gay_1.png`, `aestroid_gay_3.png`, `aestroid_gray_crooked_1.png`, `aestroid_gray_crooked_2.png` | sphere-shaded, glossy pits (1, 3, 4) | **DONE (replaced)**: Space spawns `space_rock_{crater,cluster,dark}`; these prefabs stay only as the scene-array fallback |
| Kenney meteors (brown + grey, 8 big files left) | `Art/Resources/Prefabs/Enemies/Kenney/meteor{Brown,Grey}_big1-4.png` | flat but no ink, blobby (3, 4) | tiny/small/medium **DELETED** (12 prefabs + PNGs). big1-4 **AMBIGUOUS**: kept but no longer spawned |
| Legacy asteroid | `Legacy/Art/Aestroids/asteroid6.png` | only used as the editor's default cursor | **UNUSED** (leave it alone) |

## Atoms and pickups

| Asset | Path | Why | Verdict |
|---|---|---|---|
| Blue shield atom | `Art/Atoms/atom3a.png` (`prefabs/atom3a.prefab`) | glossy bubble balls (1, 3) | **DONE (redrawn)**: pixel-art sibling of the green atom, teal/cyan hex cage + shield nucleus, idle + burst (art-style §5.1) |
| Red pause atom | `Art/Atoms/pauseAtom.png` (`prefabs/pauseAtom.prefab`) | glossy bubble balls (1, 3) | **DONE (redrawn)**: pixel-art sibling, crossed orbits + pause-bar nucleus, idle + burst |
| Green heal atom | `Art/Resources/Pickups/heal_atom_green.png` | the user's benchmark (pixel art) | **KEEP** (user decision): original pixels untouched; **animated** with overlay frames (glints, nucleus pulse) + burst |
| Star dust | `Art/Retro80s/Pickups/StarDustLarge.png`, `StarDustSmall.png` | confetti squares, off-palette (5) | **DONE (redrawn)**: pixel-art four-point star, twinkle/spin idle + burst |
| Gold stars (credits, menus, star pickups) | `Art/Stars/0.png`, `00.png`, `1.png`-`5.png` (`Art/Stars/starRotateAnime.anim`, `prefabs/LargeStar*`, `smStar*`, `superStar`, `CreditStar*`) | bevelled gold, 3D faceting with glow (1, 3) | **REDRAW** to match the star dust cell |
| Life heart | `Art/Resources/Vfx/lifeHeart.png` | flat pixel, round (3, 4) | **RESTYLE**: angular heart, `RED` + `BONE` kick + ink |

## FX and weapons

| Asset | Path | Why | Verdict |
|---|---|---|---|
| Red explosion (17 frames) | `Art/RedExplosion/1_0.png`-`1_16.png` (`redExplo.anim`, `halfRedAnime.anim`) | soft particle dots (1, 2) | **REDRAW** as cel explosion. Sample: `explosion` |
| Blue explosion (17 frames) | `Art/BlueEffects/1_0.png`-`1_16.png` (`blueExplosion.anim`) | soft particles (1, 2) | **REDRAW** (cold variant for enemies: `CYAN`/`BRUISE` cels) |
| Painted explosion sheet | `Art/Animation/explosion.png` (`prefabs/explosion_0.prefab`) | soft painted fireball (1, 2) | **REDRAW** |
| Pixel explosion strip | `Art/Animation/explode_1.png` (`prefabs/explode1_0.prefab`, `AnimationGo.prefab`) | dotted gradient (1) | **REDRAW** |
| Ultimate projectiles atlas | `Art/Resources/Vfx/ultimate_projectiles_atlas.png` | glossy painted bolts (1, 3) | **REDRAW** |
| Ship damage FX atlas | `Art/Resources/Vfx/ship_damage_fx_atlas.png` | painted sparks and smoke (1, 2) | **REDRAW** (cel sparks, angular smoke) |
| Teleport portal atlas | `Art/Resources/Vfx/teleport_portal_atlas.png` | swirly glossy vortex (1, 3) | **REDRAW** (hard-edged rings, tinted per world) |
| Kenney particle textures | `Art/Resources/Prefabs/Vfx/vfx_circle_05.png`, `vfx_light_02.png`, `vfx_muzzle_02.png`, `vfx_smoke_08.png`, `vfx_spark_05.png`, `vfx_star_08.png`, `vfx_trace_01.png` (`PowerFx`, `UltimateGun`, `ShipSpinWind`) | soft/photo particles (1, 2) | **REDRAW** as hard-shaped cel textures. `vfx_trace_01` becomes the light-trail streak |
| Code-drawn visuals | `Scripts/Worlds/WorldAtmosphere.cs`, `Scripts/Worlds/Portal.cs`, `Scripts/Gameplay/HealAtom.cs` (fallback), `Scripts/Gameplay/PowerReadyIndicator.cs`, `Scripts/Gameplay/UltimateGun.cs`, `Scripts/Ship/ShipShieldBubble.cs` | procedural soft textures / colours in code | **RESTYLE** in a code phase (palette constants, hard-edged shapes). Not part of art-only conversion |

## Worlds and backgrounds

| Asset | Path | Why | Verdict |
|---|---|---|---|
| Space: main-game starfield | `Art/Backgrounds/starFieldMainGame.png` (`starFieldMainGame.mat`, `starField_17.mat`, `pauseStarBackground.mat`) | painterly noise nebula (2) | **REDRAW**. Sample: `world_space` |
| Start menu starfield | `Art/Backgrounds/startMenuStartField.png` | painterly (2) | **REDRAW**. Must sit quietly under the protected logo |
| Shop backdrop | `Art/Backgrounds/shop.png` | painterly (2) | **REDRAW** |
| Credits backdrop | `Art/Backgrounds/credits.png` | painterly (2) | **REDRAW** |
| Space walls (pipes) | `Art/left.png`, `Art/right.png` (`left_1.mat`, `right_6.mat`, `right_7.mat`) | shaded thin pipes (1) | **REDRAW** (panelled wall, sodium lamps) |
| Frost backdrop + walls | `Art/Resources/Worlds/Frost/backdrop.png`, `wallLeft.png`, `wallRight.png` | noise texture, soft (2) | **REDRAW**. Sample: `world_frost` |
| Verdant backdrop + walls | `Art/Resources/Worlds/Verdant/backdrop.png`, `wallLeft.png`, `wallRight.png` | noise texture (2) | **REDRAW** |
| Ember backdrop + walls | `Art/Resources/Worlds/Ember/backdrop.png`, `wallLeft.png`, `wallRight.png` | noise texture (2) | **REDRAW** |

## UI

| Asset | Path | Why | Verdict |
|---|---|---|---|
| Quick-action icons (Replay, Home, Play glyph) | sources `Art/UI/Icons/src~/build_icons.py` → `icon_*.svg`; outputs `Art/Resources/QuickActions/QuickAction_{replay,home}[_glyph].png`, `QuickAction_play_glyph.png`, `Shine/*` | rounded plate, neon-tube bloom glyph, synthwave grid (3) | **DONE (restyled)**: chamfered plate, inked BONE glyph, red cel shadow; press squash (`CelPress`) and glint shimmer (`UiShimmer`) |
| Death panel sprites | sources `Art/UI/DeathPanel/src~/*.svg` (+ new `dp_bar`, `dp_slab`); outputs `Art/Resources/DeathPanel/*.png` | rounded corners, soft glow, wash gradient (1, 3) | **DONE (restyled)**, 9-slice borders kept; `DeathPanelView` on the palette, title slab, motion held on 2s, beat flashes |
| Space dock | sources `Art/UI/Dock/src~/*.svg`; outputs `Art/UI/Dock/Resources/Dock/*.png` | mostly flat and panelled already; rounded button, soft glows, cyan neon popup (3, 5) | **DONE (restyled)**: flat palette fills, ink contours, chamfers, hex badges, hard ring/shadow; dock code colours on `AkiraPalette`; popup button press |
| Dock shader | `Art/UI/Dock/Resources/Dock/DockSprite.shader` | not art | **KEEP** |
| HUD | `Scripts/UI/HudStyler.cs` + `Art/UI/Hud/src~` (`hud_panel`, `hud_meter`) | synthwave violet/magenta panel (5) | **DONE (restyled)**: chamfered night panel with red tab, CYAN/AMBER/RED read-outs, segmented pause meter, stepped punches and a low-pause blink |
| World banner | `Scripts/Worlds/WorldBanner.cs` | plain white text with a fade | **DONE (restyled)**: BONE type on the red title slab, flipbook in/out |
| Menu buttons (start, Options) | `Scripts/UI/MenuStyler.cs` (runtime; scenes untouched) | default text buttons | **DONE (restyled)**: BONE + ink + red cel drop, press squash, PLAY beat flash |
| Tutorial UI palette | `Art/UI/Tutorial/src~/palette.env`, `Scripts/Tutorial/TutorialPalette.cs` | near-guide colours | **DONE**: aligned to the guide hexes and re-rendered (art not redesigned) |
| In-game replay / menu buttons | `Art/redo-512.png`, `Art/taxes-menu-icon.png` (`gameS1`, `tutorialS5`) | flat white glyphs, no ink, round (3, 4) | **RESTYLE** (or replace with the new quick-action glyphs) |
| Login icon | `Art/loginIcon.png` | not referenced | **UNUSED** |
| Font | `Art/Orbitron/Orbitron-Bold.ttf` | fits the look | **KEEP** |
| Materials | `Art/Materials/*.mat`, `Art/Backgrounds/Materials/*.mat`, `Art/Black.mat` | not art | **KEEP** (repoint textures only) |

## Tutorial robot

| Asset | Path | Why | Verdict |
|---|---|---|---|
| Robot portrait | `Art/contra2.png` (sprite on `PlayerIcon` in `Scenes/tutorialS5.unity`) | detailed shaded pixel art, 3D chrome, red visor (1, 4) | **REDRAW** as a 3-frame talking portrait. Sample: `robot`, `robot_talk` |

---

### Suggested conversion order

1. Palette and ink pass on the vector UI (**RESTYLE**: Icons, DeathPanel, Dock). This is lowest risk, and the sources exist.
2. Player hull 1 + exhaust, enemies, pickups, asteroids (gameplay readability first).
3. Explosions and FX atlases.
4. World backdrops and walls (Space, Frost, Verdant, Ember), then the menu, shop and credits backdrops.
5. The remaining hulls 2-15 and their damage states.
6. Code-phase restyles (HudStyler, DeathPanelView colours, procedural FX).
