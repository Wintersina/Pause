# Where things live (`Pause/Assets`)

One home per kind of file. Anything under a folder named `Resources` is loaded
by path (`Resources.Load("<path inside Resources>")`), so moving a file between
two `Resources` roots is safe only if its path *inside* `Resources` stays the same.
Folders ending in `~` (`src~`) are ignored by Unity: they hold the Python/shell
generators and source art that render the PNGs next to them.

| Folder | What is in it |
| --- | --- |
| `Scenes/` | The 7 build scenes (`spashS7`, `startS4`, `gameS1`, `tutorialS5`, `shopS6`, `leaderboardS3`, `creditsS7`) and their lighting settings. |
| `Scripts/` | Runtime C#, by domain: `Bosses`, `Codex`, `Core` (+ `CloudSave`, `Leaderboards`, `Scoring`), `Credits`, `Gameplay` (+ `Elites`, `Enemies`, `Pickups`, `Spawning`, `Weapons`), `Menu`, `Ship`, `Shop`, `Tutorial`, `UI` (+ `Leaderboard`; no ad code, see `docs/release/no-ads.md`), `Worlds` (+ `Backdrop`), `Audio`. |
| `Editor/Build/` | `BuildScript` (the Makefile's `-executeMethod` targets) and the iOS post-process. |
| `Editor/Importers/` | Asset importers and bakers keyed on art folders: boss / boss-attack / weapon / ship / resume-fx importers, `EliteArtSync` (Codex's elite strips -> `Art/Resources/Elites`), `ShipHitboxBaker`, `ShieldSilhouetteBaker`. |
| `Editor/Tests/` | Editor test suites; every suite is registered in `AllTests.cs` (`AllTests.RunAll`). |
| `Editor/Tools/` | Batch tools (`AppIconSetter`, `PlayGamesSetup`, `WorldBackdropImport`, `WorldRailImport`, `EditorSceneLoader`, `ScreenFit/`); `Previews/` holds the `-executeMethod X.Run` review renders (`*Preview`, `*Render`, `WorldRailReview`). |
| `Shaders/Resources/` | Every project shader, at the Resources path its loader uses: `WorldRailRepeat`, `BackdropShaders/*`, `CodexSilhouette/*`, `Dock/DockSprite`, `ShipArt/Exhaust/ExhaustRemap` (+ `.cginc`), `TitleTraffic/TitleTrafficHaze`. |
| `Resources/` | Prefabs loaded by path (`prefabs/...`, ships in `prefabs/Ships/inGameShips/shipN`), `BillingMode.json` (Unity IAP), `HapticGate/` (splash textures). |
| `Art/Resources/` | Runtime art loaded by path: bosses, elites, enemies, pickups, ship hulls/skins/exhaust, weapons, codex, tutorial, HUD, `Vfx/` (incl. `Vfx/Kenney` particles), world rails (`Worlds/<World>/rail_*`). |
| `Art/Backgrounds/` | World backdrops (`Resources/Worlds/<World>/Backdrop/*`, loaded by `BackdropCatalog`) and the scenes' star-field materials (`Materials/`). |
| `Art/Worlds/` | Codex's world staging art and the backdrop generators (`src~`). Nothing here ships until promoted into `Art/Backgrounds/Resources`. |
| `Art/Enemies/Elite/` | Codex's elite source strips (copied into `Art/Resources/Elites` by `EliteArtSync`). |
| `Art/AppIcon/` | The app-icon candidates `AppIconSetter` switches between. |
| `Art/UI/`, `Art/Ships/`, `Art/Walls/`, `Art/Pickups/`, `Art/Animation/`, `Art/Fonts/`, `Art/PixelKit/`, `Art/Bosses/`, `Art/BossAttacks/`, `Art/Weapons/` | Scene-referenced art by domain, plus each family's generators in `src~`. `Art/Weapons/<Ship>/` are output folders for `weapons.py`. |
| `Audio/` | `Music/` and `Sfx/` (scene-referenced), `Resources/WorldMusic` + `Resources/Audio` (loaded by path). |
| `Plugins/` | Android Gradle templates / manifest lib, iOS Game Center bridge. |
| `GooglePlayGames/`, `ExternalDependencyManager/` | Third-party SDKs; leave in place (they locate their own files). |

Licences for bundled third-party art are in `docs/licenses/` (Kenney CC0 needs no attribution, so the credits screen lists only "Made by Sina Serati" and the Special thanks names).

`Pause/Library` is Unity's generated import cache (git-ignored). Deleting it is
safe but costs a full reimport on the next editor launch.

## Sound setting

Options has a `SOUND: ON / SOUND: OFF` row (`Menu/SoundOptions.cs`, y -33, or 147 on top of the developer stack in
developer builds). It drives `Audio/SoundSettings`, the one central switch: muted means `AudioListener.volume = 0`
(every source, including later/one-shot ones, goes silent while still playing, so nothing that waits on audio is
affected and un-muting resumes mid-track). PlayerPrefs int `soundMuted` (absent = on), applied before the first scene
and re-applied on scene load and focus change. Device-local: not in the cloud snapshot. Haptics are a separate matter
and unaffected (there is no haptics option). Un-muting plays a short confirmation click. Tests: `SoundMuteTest`,
ScreenFit screens `options-mute-on` / `options-mute-off`.

## Cloud save: what is synced

One JSON `ProgressSnapshot` (`Scripts/Core/CloudSave`), merged by `ProgressMerge`, uploaded debounced by `CloudSync`.
Synced: star dust, owned ships, spawn ship, best speed / best score, current / highest / start world, tutorial flag,
achievement counters and flags (`AchievementStore.SyncKeys`), hull skins, and the **Codex**: `codexSeen` (discovered
entries), `codexNew` (discovered but detail not yet opened = NEW dot) and `codexNewAck` (NEW entries whose tab was
opened, plus `ach:<id>` acknowledgements). Each is a sorted, de-duplicated id list, capped at 512 ids.
Codex merge: discoveries = union; NEW = both sides' NEW minus any entry the other side already knows and does not list
as NEW (cleared on either side stays cleared, so no dot is resurrected); acknowledged on either side stays acknowledged
(only while the entry is still NEW). Restoring on a fresh install therefore lights nothing: only entries the cloud itself
listed as NEW stay NEW. `Apply()` ends with `Codex.OnExternalChange()` so cached sets cannot overwrite the restore.
Schema stays at 1: the fields are additive (old saves load as empty; old clients ignore them, though an old client's
next upload drops them until a newer client uploads again). Device-local: settings, developer switches, cloud bookkeeping.
