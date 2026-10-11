# Android size diet

Audit AAB (debug-signed, `PAUSE_SKIP_APK=1`): **183.5 MB -> 158.8 MB** (Play limit 200 MB, goal 120 MB not reached by safe changes).
Build report: `Textures` 266.6 MB -> 204.3 MB uncompressed, total user assets 321 -> 259 MB.

## What changed
- `Assets/Editor/Importers/AndroidTextureDiet.cs`: explicit Android + iPhone ASTC override for every sizeable (>= 128 px), non-readable shipping texture.
  - ASTC 4x4 (hard outlines): ShipArt (hulls, guns, skins, exhaust, damage fx), Shield, Worlds rails, Weapons, Attacks, BossAttackFx, `*_shots.png`.
  - ASTC 6x6 (painted / soft): bosses, enemies, elites, pickups, vfx, gate art, title and splash art. Backdrops / planetfall keep their own importers (6x6 and 4x4 for fine fx and the planet sheet).
  - NPOT textures lose mipmaps (Unity otherwise silently keeps NPOT + mips as RGBA32: that was the 9.4 MB hull sheets and the 1000x1000 title).
  - Left uncompressed: launcher icon, UI under Art/UI except Title and Splash, `Splash/HapticGate.png` (tiny text logo), readable textures, anything < 128 px.
- Hull sheets 9.4 MB -> 1.8 MB; the stray Tide contact sheet lives in `src~` (a copy is still under `Resources/Elites/Tide` on master, see below).
- `BuildScript`: `[SIZE]` top-120 and `[SIZEDIR]` folder totals in the log; `PAUSE_SKIP_APK=1` builds only the AAB.
- Codex lists only live worlds (CodexTest fix). `AndroidTextureDiet.ReimportBackgroundsAndExit` re-imports backdrop textures after an importer change.

## Previews (`docs/release/size-diet-previews/`)
Each asset has `_zoom.png` (3x, dark, source | ASTC | diff x6), `_zoom_bright.png` (bright background) and `_game.png` (0.45x game scale). Produced by `TextureDietPreview.Run` on the Android-target Library (ASTC verified in the log: `format=ASTC_xxx`).
- Hulls Ninja / UFO / CrimsonHalo (4x4, PSNR 44-49 dB): 1 px dark outlines and alpha edges intact. OK.
- Exhaust atlas, Weapons AttackFx (4x4 after 6x6 visibly blocked its thin outline rings): OK at 4x4.
- Rail sheet, heal pickup, gate door / cracks / steam, Space and Ember bosses, Tide boss damage fx, enemies, elites, teleport atlas, title and splash (6x6): no visible degradation at 3x; diff noise only.
- Rejected: ASTC 8x8 on the backdrops (pixel art visibly blocky and soft, Worlds_Ember_Backdrop3_v1_mid_zoom.png): saved only 10 MB, reverted. ASTC 5x5 on hulls: looked fine but the AAB got 2 MB bigger (transparent area compresses to nothing under deflate), reverted.
- The user approved the previews.

## Why 120 MB is not reachable safely
The AAB is deflated per asset, so mostly transparent sheets cost little, and the remaining mass is spread thin: libil2cpp + libunity 19 MB, audio 28 MB (mp3 / wav, not recompressible), 107 MB uncompressed of backdrops (6x6 pixel art, ~55 MB in the AAB), 1,470 data entries (top 100 = 68 MB).
- Excluding Tide Resources folders (Backgrounds/Tide, Elites/Tide, Worlds/Tide, Bosses/Tide*): measured by a manual stash build, **143.0 MB** (-15.8 MB). NOT implemented: needs a build wrapper with try/finally restore plus crash recovery, and null-safety of every Tide Resources.Load at runtime cannot be verified without a device. The user's call.
- ASTC 8x8 backdrops: 148.6 MB, rejected (visuals). Both together would be about 133 MB.
- Other options: lower-bitrate audio (user's domain), drop unused hull skins from the build (every skin is sold), Play Asset Delivery install-time/fast-follow packs for Tide / later worlds (does not shrink the base AAB limit but removes it from the 200 MB base module).

## Known leftovers
- `Resources/Elites/Tide/tide_elite_contact_sheet.png` (+meta, 3.3 MB in the build) re-appeared from master and should be removed (`git rm`); the removal was blocked in this session.
- `UnusedAssetGuardTest` reports `Assets/Art/UI/Splash/HapticGate.png` as unreferenced; this file is untouched by the diet (pre-existing on master).
