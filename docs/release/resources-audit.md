# Resources audit — October 22, 2026

Baseline: `Pause/Builds/Android/Pause-dev.apk`, built October 21, 312,171,290 bytes.
All MB below are decimal. No Android rebuild was performed for this pass.

## Completed cleanup

* The credits gate's frames 63–68 are byte-identical to frame 62 (SHA-256
  `0f06aa463efb9c05c28049dbef662d87eb6c440ca2e99ed458890711c4468d64`).
  `GateOpenFrames.Load` now reuses sprite 62 for those six timeline entries.
  The 69-frame, 15 fps timeline and 4.6-second duration are preserved, as are
  image resolution and pixels. The six PNGs and metas are preserved under
  `Art/Resources/HapticGate/GateOpen/archive~`, which Unity excludes from import.
* `Art/Resources/Vfx/eliteHeart.png` has no code path or serialized GUID consumer.
  `EliteHearts.SpritePath` delegates to `ShipLivesIndicator.HeartSpritePath`,
  which uses `Vfx/lifeHeart`; bosses use the same path. The old image and meta
  are preserved under `Art/Resources/Vfx/archive~`.
* Removed entries occupy **420,081 compressed bytes** in the baseline APK
  (734,812 serialized bytes). This is the baseline contribution excluded by
  the change, not a measured new APK delta. Build layout can affect the delta.
* `CreditsTest` verifies the shared hold sprites and duration.
  `UnusedAssetGuardTest` prevents the retired live paths from returning.
  Both suites passed in Unity, zero failures.

## Reproducible inventory

Run `python3 scripts/tools/resources_audit.py`. It produces
`Pause/Builds/resources-audit.csv` and a folder summary. Options `--apk` and
`--csv` accept alternate input/output files.

The CSV lists each current imported Resources file, its load key, source
size, SHA-256, and compressed contribution from GUID-named entries in the
specified APK. A missing APK match is **unknown**, not proof of zero cost;
scene-packed dependencies, changed GUIDs and newly added assets may not map.
Archived files are excluded, so the inventory describes the current source
tree against the older APK. PNG/JSON atlas pairs intentionally share load keys.
Literal filename hints are search aids only: concatenated paths, formatted
numbers and data-driven selection need manual review. The tool never deletes.

## Largest Resources groups checked

| Group | Attributed baseline APK MB | Usage evidence / disposition |
|---|---:|---|
| World backdrops / planetfall / weather | 107.62 | Catalog layers, directors, ambient tables and variant selection load by constructed paths. Retain. |
| World and boss music | 33.04 | Theme/music tables and boss music systems select tracks at runtime. Retain. |
| Boulders | 28.55 | `CoverBoulderArt` loads manifest, intact sprites, break strips and fx by world. Retain. |
| Boss impact / damaged poses | 25.07 | Runtime-selected attack/damage states; no removal established in this pass. |
| Bosses | 15.89 | World/pose-derived paths. Retain. |
| ShipArt | 15.01 | Includes PNG `.bytes` skin sheets selected by ship and skin. Retain. |
| Splash film | 12.44 | `GateFilm.Kept` explicitly selects the 60 installed images. No unused frames established. |
| Weapons | 10.82 | Includes dynamically named powers; no removal established. |
| Enemies / elites | 16.62 | World/roster/state-derived paths. No large unused group established. |

These figures are attributable ZIP entry sizes, not runtime memory or installed
storage. They are not predicted savings.

## Candidates needing further work

1. **Space low-resolution `anim` fallback.** `BackdropSet.LoadAnimAtlas`
   prefers `anim_hires` while the older `anim.png` / `anim.json` still ship.
   However, `WorldBackdropTest` directly Resources-loads the older atlas;
   `SpacePlanetSheetTest` reads its files and compares both resolutions;
   `BackdropCellClipTest` also names it. Excluding it requires retaining an
   editor reference and updating those consumers/tests. It is not a safe
   delete based solely on the runtime preference, and offers only a small gain.
2. **Remaining exact duplicates.** PauseGlow/PauseGlowFx and some pickup
   frames share source hashes. Their loaders use different names and some
   sprites have different metadata. Sharing them requires checking pivots,
   filtering, slicing and pixels-per-unit, not just source bytes. Small savings.
3. **Paragon skins.** Four skin `.bytes` entries contribute approximately
   6.43 MB, but `ShipId` explicitly retains art keys for the premium ship even
   when hidden. Visibility is not evidence that the assets are unused.
4. **Resources reachability guard limitation.** `EveryAssetIsReachable` makes
   every Resources asset a root. It catches unrelated orphan assets but
   cannot prove a Resources file has a runtime consumer. A filename search
   cannot close this gap either. Continue with explicit per-loader contracts
   and exercised runtime load inventories before removing large dynamic sets.

No large unused Resources collection has been proven by this pass. The measured
safe cleanup is small; achieving a 112 MB reduction will require further asset
sharing/encoding work or changing content delivery.
