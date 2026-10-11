# Unused / wasteful assets audit (read-only)

Branch `audit/unused-assets`, from master a7202e36. Nothing was deleted or changed except this file.
Constraints honoured: backdrops under `Assets/Art/Backgrounds` are never recompressed or downscaled; staged tilde-folder art for Tide, Storm and Desert is not listed; Resources art for Space, Frost, Verdant, Ember and Tide is treated as live.

## Headline

The project is clean at the "unreferenced file" level. Every file under every Resources folder is reachable through a name, folder or path builder (the 1,136 files were checked against C# string literals and path builders, scene/prefab/asset guids and the live enemy, elite and boss key sets). Only about 0.3 MB of strictly dead content turned up (section B). The real size is in formats and settings, not orphans:

| Rank | Item | Est. AAB saving | Risk |
|---|---|---|---|
| 1 | Ship skin sheets: 60 PNGs stored as TextAssets (20.9 MB, incompressible), quantize to 8-bit | 15 to 16.5 MB | needs visual check |
| 2 | Death/gate/shockwave SFX: 262 mono 44.1 kHz PCM wavs (17.8 MB), import as Vorbis or ADPCM | 10 to 13 MB | needs ear check |
| 3 | IL2CPP: Medium stripping and "Faster (smaller) builds" | 2 to 5 MB | needs check (reflection, JsonUtility) |
| 4 | Music: five Vorbis tracks at quality 1.0, world mp3s at 0.7 | 5 to 7 MB | user's call (audio) |
| 5 | Built-in modules and editor-only packages that pull modules in | 1 to 3 MB | needs check |
| 6 | R8 minify and the unused `play-services-nearby` dependency | 0.5 to 1.5 MB | needs check |
| 7 | Strictly dead assets (section B) | 0.5 to 0.7 MB | safe |
| | ARM64-only already committed (b105185a) | about 21.8 MB vs the earlier 158.8 MB AAB, if that figure still included armeabi-v7a | done |

Total estimated further saving: about 35 to 45 MB. Together with ARM64-only, the AAB should land near 95 to 105 MB (from 158.8 MB). Re-measure with `[SIZE]` before and after; no number here is a measured build.

## 1. Method and data sources

- No cached Android build report exists. `~/Library/Logs/Unity/Editor.log` holds an old Oct 7 desktop-style report (Resources only, old `Backdrop` layout, stale), the logs under `/tmp` have no `[SIZE]` lines, and `Pause/Builds/Android` holds only `Pause-dev.apk` (208 MB, Oct 10 15:19, built before the ARM64-only commit at 16:05).
- That APK was inspected as a size probe (`unzip -v`):
  - `lib/arm64-v8a/libil2cpp.so` 51.9 MB, 14.3 MB deflated
  - `lib/arm64-v8a/libunity.so` 19.8 MB, 8.8 MB deflated
  - `lib/armeabi-v7a/libil2cpp.so` + `libunity.so` 58 MB, 21.8 MB deflated (now gone)
  - `classes.dex` 3.2 MB, 1.25 MB deflated
  - `global-metadata.dat` 7.3 MB, 2.25 MB deflated
  - `unity default resources` 3.75 MB (fixed)
  - three Stored `.resource` audio streams of about 10.2 MB total
  - asset data is in hashed serialized files, so per-asset compressed sizes are not recoverable from it.
- The remaining sizes come from file inspection and measured compressibility (gzip on the wavs, a Pillow quantize test on the skins).
- Folders that ship regardless of references: `Assets/Resources`, `Art/Resources`, `Audio/Resources`, `Shaders/Resources`, `GooglePlayGames/Resources`, `Art/Backgrounds/Resources`, `Art/UI/Dock/Resources`. There is no StreamingAssets folder.
- Outside Resources, only assets reachable from the 7 scenes in build settings ship. Everything else in the project (`*/Staging`, `src~`, `*~` folders, `Art/Enemies/Elite/*`, `Art/AppIcon`, `Art/Worlds/Space/staging`, the Orbital Rail icon set, `Audio/EnemyDeathSrc~`) does not ship. The only referenced non-Resources payloads are the five `Audio/Music` wavs (22.9 MB source), `starFieldMainGame.png` (backdrop, exempt), `Splash/HapticGate_title_red.png`, `Title/pause_title_2.png`, the Orbitron font and small walls, animation and pickup sprites (27.8 MB in total).
- `UnusedAssetGuardTest` orphan `Assets/Art/UI/Splash/HapticGate.png` (14 KB): confirmed unreferenced, but it sits outside Resources, so it does not ship. Deleting it saves nothing in the build.
- Duplicate hashes inside the shipping set are negligible (section B). Other duplicates are source-vs-Resources copies, and the source copy does not ship.

## 2. Findings, ranked by saving

### 1. Ship skin sheets stored as PNG TextAssets (about 15 to 16.5 MB)
- Path: `Assets/Art/Resources/ShipArt/Skins/*.bytes` (60 files, 20.9 MB, 290 to 435 KB each, 2304x768 RGBA PNGs).
- Loaded by `ShipHullArt.DecodeSkin` (`LoadImage`, then GPU compress). All 15 ships x 4 skins are in the `ShipSkins` table and sold, so none are unused.
- Why it is waste: PNG inside a TextAsset is already deflated, so the AAB cannot shrink it (the report's "Other Assets 21.7 MB" is this). The sheets have only 3,100 to 4,500 unique colours. A 256-colour palette quantize with no dithering takes 376 KB to 78 KB; across a 10-file sample, 3.38 MB went to 0.71 MB (79% smaller), PSNR 41 to 43 dB, max channel diff 30. The user approved ASTC 4x4 at 44 to 49 dB, which is comparable.
- Zero code change: `LoadImage` reads palette PNGs. A better quantizer (pngquant, per-skin tuned palette) would improve the PSNR.
- Alternative: recolour at runtime from the stock hull using the existing `ShipSkins` hue tables, which saves all 20.9 MB at the price of code and tests.
- Risk: needs visual check (alpha edges, outlines).

### 2. Enemy death, gate and shockwave SFX imported as PCM (about 10 to 13 MB)
- Paths: `Assets/Audio/Resources/Audio/EnemyDeath/*.wav` (250 files, 16.7 MB, 194 s mono 44.1 kHz), `Audio/Gate/*.wav` (8 files, 0.64 MB), `Audio/Shockwave/*.wav` (3 files, 0.1 MB).
- All have `compressionFormat: 0` (PCM), `loadType: 0`. gzip -6 only reaches 17.8 MB to 15.4 MB, so the AAB carries about 15 MB for roughly 3.2 minutes of mono audio.
- The set is clean: all 250 clips match a live key (enemy, rock, mine, elite or boss `_death` key, or `boss_*`). Tide has no sounds yet. Screams are only on keys that do not borrow, and no clip exceeds 3 variants. So the issue is the format, not duplicates.
- Fix: import as Vorbis (quality about 0.4 to 0.5, mono, about 1.5 to 2.5 MB total) or ADPCM (about 5 MB, no pre-echo risk). Keep PCM for any clip where a short transient suffers.
- Risk: needs an ear check, since the death and scream design is tuned.

### 3. IL2CPP and stripping settings (about 2 to 5 MB, not measured)
Current values from `ProjectSettings.asset` (see section 4). Medium stripping and `il2cppCodeGeneration` = "Faster (smaller) builds" are untouched.
- Risk: needs check. Medium/High can strip fields of `JsonUtility` types and reflection used by Google Play Games. A `link.xml` (or `[Preserve]`) is required, and the CloudSave and Leaderboard JSON types and GPGS must be device-tested.

### 4. Music at quality 1.0 (about 5 to 7 MB)
- `Audio/Music/{MovingMusic,BoostingMusic}.wav` 7.1 MB each, `StoreMusic` 3.0, `Tutorial` 2.8, `MainMenuMusic` 2.8 MB (22.9 MB source). `compressionFormat: 1` (Vorbis) with `quality: 1`, which is the maximum and about 10.2 MB in the APK as three Stored `.resource` streams. Quality 0.5 to 0.6 should roughly halve that.
- `Audio/Resources/WorldMusic/Ember_Main.mp3` (5.1 MB) and `Frost_Main.mp3` (4.7 MB), Vorbis at 0.7, streaming. Quality 0.5 saves about 1.5 to 2 MB.
- All referenced (scene guids, `WorldMusic`, `musicControl`). This is the user's audio domain (size-diet.md already flagged it), so it is listed but not recommended without sign-off.

### 5. Built-in modules and packages (about 1 to 3 MB)
`Packages/manifest.json` still enables 29 built-in modules. The IAP, analytics and services packages are already gone (b105185a), but the dev APK's metadata still lists the old assemblies, so rebuild before measuring.
- Code that uses a module: UGUI (`ui`, `imgui`), `physics2d`, `audio`, `animation`, `jsonserialize`, `androidjni`, `imageconversion`, `unitywebrequest` (needed by the GPGS plugin), `particlesystem` and `physics` (only Physics casts in `EnemyRoster`; check before removing).
- No code or scene use found for: `terrain`, `terrainphysics`, `cloth`, `vehicles`, `wind`, `vr`, `xr`, `video`, `umbra`, `ai` (NavMesh), `unityanalytics`, `tilemap`, `screencapture`, `accessibility`, `adaptiveperformance`, `assetbundle`, `unitywebrequestassetbundle`/`audio`/`texture`/`www`, `vectorgraphics`, `director`, `uielements`. The managed assemblies for UIElements, Video, AI, Tilemap, Grid and SpriteShape appear in the dev build's metadata, so engine stripping does not remove them.
- Packages with no runtime use that keep modules alive: `com.unity.xr.legacyinputhelpers` (keeps vr/xr), `com.unity.timeline` (keeps director), `com.unity.2d.tilemap` (tilemap and uielements), `com.unity.multiplayer.center` (uielements), `com.unity.ai.navigation` (ai), `com.unity.collab-proxy`.
- The Unity MCP package (`com.coplaydev.unity-mcp`) has a runtime asmdef compiled for every platform and depends on uielements, screencapture and newtonsoft-json. Newtonsoft is in the dev build's metadata. Drop it from release builds (or gate it) if it is only an editor dev tool.
- Risk: needs check. Remove a package, rebuild, and confirm the asmdef-dependent tests pass.

### 6. R8 minify and `play-services-nearby` (about 0.5 to 1.5 MB)
- `AndroidMinifyRelease: 0`. `classes.dex` is 3.2 MB (1.25 MB compressed) and R8 would drop the unreferenced GPGS, Nearby and AndroidX code.
- `GooglePlayGamesPluginDependencies.xml` (and `mainTemplate.gradle`) pull `play-services-nearby:18.5.0`, which the game does not call (achievements and leaderboards only). Needs check: remove the dependency, then compile and run the GPGS sign-in on device.

## 3. B. Strictly dead or duplicate assets (safe, about 0.5 to 0.7 MB)

| Path | Size | Evidence | Saving |
|---|---|---|---|
| `Art/Backgrounds/Resources/Worlds/Space/Backdrop/anim.png` + `anim.json` | 1.59 MB source | `BackdropSet.LoadAnimAtlas` loads `anim_hires` first and falls back to `anim` only when it is missing; `anim_hires` exists. File removal only (no recompression); check `BackdropTests` first. | about 0.5 to 0.6 MB |
| `Resources/prefabs/explode1_0.prefab` (+ `Art/Animation/explode_1.png`, `explode1_0.controller`, `newAnime.anim` if nothing else uses them) | about 20 KB | no scene, prefab or code reference by guid or name | under 0.05 MB |
| Identical frames in `Art/Resources/Pickups/Atoms`: `cooldown_idle_{0,2,4,6,8}` equal `_{1,3,5,7,9}`; the four `*_burst_0` files are identical; `PauseGlowFx/pausedGlow_{a,b}_2` equal `PauseGlow/pausedGlow_{a,b}` | tens of KB | md5 identical | under 0.1 MB. Needs an animation-code change to share them, so skip unless free. |
| `Resources/prefabs/Ships/inGameShips/ship{2..6}.prefab` | 60 KB | selected via `ShipSpawn.PrefabPathFor` for ids 1 to 7 (`IsRetro`); likely all live | none |
| `Art/Resources/Elites/Tide` strips with no `Defs/*.json` | 0.6 MB | not unused: Tide elites ship in v1, the defs still need authoring | none |

Verified live (no action): all 60 achievement icons, 15 weapon and hull sheets, 6 Kenney Vfx files in `PowerFx`, boss `card`/`shots`/`damage`/`damage_fx`/`death` strips for all five worlds, `Attacks/*laser*`/`lash`/`log`/`shots`/`mineflame`, 5 `Planetfall` sets, all four sky variants and every `v1..v4` backdrop folder, all `Elites/*_death` strips.

## 4. Code stripping and build settings

| Setting | Value | Note |
|---|---|---|
| Scripting backend (Android) | IL2CPP (1) | |
| Managed stripping level | Low (1) | Medium (2) is usually safe; High (3) needs `link.xml` |
| Engine code stripping | on (`stripEngineCode: 1`) | only removes unreferenced engine code, not whole enabled modules |
| Target architectures | ARM64 only (2), committed in b105185a (Oct 10 16:05) | the dev APK at 15:19 predates it, so it still has armeabi-v7a |
| Android Min / Target SDK | 25 / 36 | |
| IL2CPP code generation | default (faster runtime) | switch to "Faster (smaller) builds" |
| IL2CPP compiler configuration | default (Release) | |
| App bundle / per-ABI split | AAB; `AndroidBuildApkPerCpuArchitecture: 0`; `androidSplitApplicationBinary: 0` | AAB gives Play per-ABI/density config APKs, so no change needed |
| R8 / minify | off (release and debug) | |
| Texture compression | ASTC 4x4 / 6x6 overrides via `AndroidTextureDiet` | backdrops excluded |
| Always-included shaders / Shader variant collections | defaults only, no preloaded shaders; user shaders total about 125 KB | fog and lightmap stripping off (a few KB at most) |

What could be tightened: items 3, 5 and 6 above, plus IL2CPP "Faster (smaller) builds" (est. 2 to 4 MB of the item-3 saving) and Medium stripping (est. 1 to 2 MB).

## 5. Install size vs AAB, and what else could help

- The 158.8 MB AAB figure (size-diet.md) may still include armeabi-v7a (about 21.8 MB deflated) and the dev APK confirms the pre-commit build had it. The ARM64-only commit should already take about 22 MB off the AAB; Play never delivers v7a to an arm64 phone anyway, so install size was less affected.
- Backdrops are the big remaining block (about 55 MB in the AAB per size-diet.md, Tide alone 43.6 MB of source PNG) and cannot be recompressed. The only lever that does not touch their pixels is moving Tide, Storm and Desert backdrops to Play Asset Delivery on-demand or fast-follow packs. It does not shrink the base module's 200 MB limit use for install-time packs, so it only helps if those worlds are not needed on first launch.
- Everything under `Resources` also ships to every device even when never loaded (for example Tide and the Ember/Frost mp3s), which is by design here.

## 6. Verification still needed after any change

1. Rebuild the AAB and read `[SIZE]`/`[SIZEDIR]` from the log for before/after numbers.
2. Grep the new `global-metadata.dat` for `Unity.Services`, `Purchasing` and `Newtonsoft` to confirm the package removals took effect.
3. Visual check of the quantized skin sheets (items to compare: outlines, alpha edges, the Special-skin patterns) and an ear check of the Vorbis/ADPCM death sounds.
4. On a device: GPGS sign-in, cloud save and leaderboards after raising stripping or dropping nearby.
