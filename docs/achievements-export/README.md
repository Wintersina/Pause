# Store entry for the 60 achievements

Generated from the code (`Pause > Achievements > Export store CSV`, or
`scripts/unity-batch.sh -executeMethod AchievementStoreExport.Export`). Regenerate after any catalogue edit.

| File | Use |
|---|---|
| `play-console.csv` | Play Console > Play Games Services > Achievements: one row per achievement (Order, Name, Description, Points, Incremental steps, Initial state, Icon file). If bulk import rejects it, enter rows by hand in this order. |
| `game-center.csv` | App Store Connect > Game Center > Achievements: Reference name, Achievement ID (`me.hapticgate.pause.ach_<id>`), points, hidden, titles and descriptions, image file. |
| `android-ids.csv` | `internal_id,android_id` as the code sees it now (13 legacy ids, 47 `TODO_android_` placeholders). |

| `play-icons-512/<id>.png` | The 60 Google Play icons, 512x512 RGB, nearest-neighbour from the 1024 masters (committed). Upload one per row. |
| `gamecenter-1024/<id>.png` | The 60 App Store Connect images, 1024x1024 RGB, no alpha. Committed (about 1.8 MB). Regenerate with `Pause > Achievements > Export store icons` (or `scripts/unity-batch.sh -executeMethod AchievementStoreExport.ExportIcons`) from `Pause/Assets/Art/Achievements/src~`. |

Steps for the owner (see also `docs/achievements-store-setup.md`):

1. Play Console: if the game was never published, delete the 28 legacy achievements and create the 60 from `play-console.csv`
   (upload the 512 px icons; the 13 legacy ids already in the code are kept only if you keep those rows). If it was published, legacy
   rows cannot be deleted: add the new ones and check the 1000-point cap.
2. Copy each new row's `CgkI...` id into `Pause/Assets/Resources/AchievementStoreIds.csv` as `internal_id,CgkI...` (it overrides the code).
3. App Store Connect: create the 60 achievements with exactly the ids in `game-center.csv` and upload the 1024 px images, then set
   `AchievementIds.IosIdsConfirmed = true` in code.
4. Publish the Play Games configuration, add testers, and test one unlock on a device (sign in, earn `meta_first_flight`).
5. Score achievements are recalibrated (2,500 / 8,000 / 30,000): the exported descriptions already say so.
