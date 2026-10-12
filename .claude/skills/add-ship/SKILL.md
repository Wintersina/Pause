---
name: add-ship
description: Add a new PLAYER ship (hull) to Pause end to end -- roster id, hull flipbook sheet with damage rows, exhaust flames and nozzles, damage emitters, skins and their prices, ultimate weapon art + loadout, secret power, hearts / start speed, dock + codex + save data, tests -- as an ordered playbook, with Paragon (the premium real-money "cheat ship", id 16) as the worked example. Use when the user says "add a new ship", "new player ship", "premium / IAP ship", or when auditing that a ship missed a piece. Not for enemy ships (add-elite-ship) or worlds (add-world).
---

# Add a player ship

A Pause player ship is **data plus art, no prefab**. Every hull flies on the shared
`ship1` prefab (`spawnShips.PrefabPathFor`); its identity is its GameObject name
`ship<id>` and everything else is a table row indexed by that id. So "add a ship" =
one hull sheet + one flame strip + one row in each table below + tests. Unity project
root is `Pause/` (code in `Pause/Assets/Scripts`, art in `Pause/Assets/Art`).

Unity: `/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity`; run batch
jobs through `scripts/unity-batch.sh` (a machine-wide FIFO queue; light jobs wait behind a
heavy `AllTests.RunAll` from another worktree, sometimes an hour -- start early). If a batch
run hangs or reports compiler errors without any `error CS` line, kill the stale
`Unity.Licensing.Client` helper and rerun (memory: unity-licensing-hang).

## Hard rules

- Work in your own jj workspace (`jj workspace add ../Pause-<name> -r master` + a
  `feature/<name>` bookmark). **Never** touch the main checkout (Codex's uncommitted edits).
  Load the `jj-sensei:wisdom` skill before any VCS command; no git commands; no
  Co-Authored-By / "Generated with" lines. Stop at a verified branch; merging to master needs
  the user's go-ahead.
- Copy the main checkout's `Library` into a fresh workspace with `cp -Rc` (APFS clone) from an idle worktree
  (`Pause/.claude/worktrees/*/Pause/Library`) or the first import takes ages.
- **Art is drawn by Codex** (`codex exec`), scripts only key / fit / slice / assemble / verify
  (user, 2026-10-12). Put Codex's raw outputs in `src~/.../codex/` (the `~` hides it from Unity),
  open them for the user, wire only what looks right. Max 2 `codex exec` at once; use
  `--skip-git-repo-check` (a jj workspace is not a git repo). Briefs below.
- Art direction: neon pixel art (docs/art-style.md), player ships keep a dark ink outline (it is the
  enemy-vs-player readability cue), friendly red is the player's alone, **no pink outside Space**.
- Never renumber or reorder ships: ids are the saved `boughtship<id>` / `spawnShip` values.
  New ships go at the END; `ShipId.keys`, `shopingShips.Roster / Prices`, every table below.

## Where a ship's data lives (the checklist; every row is indexed by id)

| What | File (Pause/Assets/Scripts/...) |
| --- | --- |
| id, art key, count | `Ship/ShipId.cs` (`keys`), `Shop/shopingShips.cs` (`shipTotal`, `Roster`, `Prices`) |
| hull sheet rect + pixels-per-unit | `Ship/ShipHullArt.cs` (`rects`) |
| nozzle points (where flames mount) | `Ship/ShipNozzles.cs` |
| damage emitters (sparks / smoke / arc / flame / leak / smolder per state) | `Ship/ShipDamageTable.cs` |
| exhaust theme row + atlas strips | `Ship/ShipExhaustStyle.cs` (`ExhaustKind`, `table`, `strips`) |
| skins (name, colours) | `Ship/ShipSkins.cs` (`table`) |
| heart orbit style | `Ship/ShipHeartStyles.cs` |
| hearts, start speed | `Ship/ShipLives.cs`, `Ship/SkinHearts.cs`, `Ship/ShipStartSpeed.cs` (by dock row / column, unless premium) |
| ultimate (attack) + secret power rows | `Gameplay/Weapons/ShipLoadout.cs` (`ShipAttack`, `SecretPower`, `PowerTrigger`, `Row(...)`) |
| ultimate runner | `Gameplay/Weapons/ShipAttacks.cs` (`StartRun` + `Step` cases); title screen mirror `UI/TitleScreenTraffic.Combat.cs` |
| ultimate look | `Gameplay/Weapons/WeaponStyleTable.cs` (+ `Resources/Weapons/<Key>.png`, `AttackFx.png` row, `SecretMeter.png` row, `ShipFxArt.Rows`) |
| gun pod that slides out before the ultimate | `Gameplay/Weapons/UltimateGun.cs` (tile in `ShipArt/Guns/ship_gun_roster.png`) |
| skin weapon upgrades | `Gameplay/Weapons/ShipWeaponUpgrades.cs` (4 steps per ship) |
| codex entry | `Codex/CodexCatalogue.cs` (`ShipLore`) |
| dock | `Shop/SpaceDock.cs` (berths sort by price, cheapest first), `DockPopup.cs`, `DockBay.cs` |
| ownership / cloud save | `Ship/ShipId.IsOwned` (`boughtship<id>`), `Core/CloudSave/ProgressSnapshot.cs` (follows `shipTotal`), `Core/DeveloperUnlocks.cs` |
| achievements | `Achievements/AchievementCatalog.cs` / `AchievementTracker.cs` (Fleet Admiral counts dust ships only) |

Baked data regenerates by itself (`ShieldSilhouetteBaker.Bake`: `hull_silhouettes.bytes`,
`hull_hitboxes.bytes`); run `-executeMethod ShieldSilhouetteBaker.Bake` after the art is final and commit them.

## Procedure

### 1. Decide the ship's economy

Standard ship: a dust price in `Prices` (it also sets the berth order, base hearts = dock row,
start speed = dock column, top-tier ultimate = the 3 priciest). **Premium ship** (real-money IAP):
see `PremiumShips.cs` -- no dust price (`CostFor` = `float.MaxValue`, sorts last), `TryPurchase`
refuses it, dock reads COMING SOON, own hearts / speed / timers by tier, excluded from the
all-skins set, Fleet Admiral and price tiers. Developer mode (`DeveloperUnlocks`) owns every
roster id, which is the debug grant. The IAP gate is `ShipId.IsOwned`; the future store flow calls
`PremiumShips.Grant(id)`. Grep `TODO(IAP)` for every place to touch.

### 2. Art (Codex), then assemble with scripts

Reference: the approved hero art; strip anything baked in that is a separate game piece (flames).
Briefs: see `Hulls/src~/paragon/codex/` (the prompts that worked are in the PR/branch notes:
common preamble + one JOB each). Always: green `#00FF00` chroma background, "redraw, never
filter-warp", IMAGE GENERATION for the painted content, nearest-neighbour clean-up, no pink,
write a `tools/check_*.py` and a `preview_*.png`.

- **J1 hull flipbook** -- 3 sheets (`hull_state0/1/2.png`), each 3x3 cells of 1024 px: idle key pose, bob up,
  bob up + lights off, canopy glint 1, glint 2 + lights off, bob down, bank L, bank R, hit flash (bone-white).
  State 1 = damaged (blown panel, sparking wires, scorch, hairline cracks), state 2 = wreck (adds a torn
  pod, glowing tears, cracked canopy, embers). Damage is painted INSIDE the intact silhouette.
- **J2 flame** -- `flame_cruise.png` / `flame_boost.png`: 7 cells (1:4), hot head at the top, only the five
  exhaust bands (outer / mid / core / dark / accent) so the skin remap shader finds them; frame 2 is the
  1-tick smear.
- **J3 ultimate** -- `charge_16.png` (4x4), `ready_shot_impact.png` (4x4), `muzzle_release_trail.png` (4x3),
  `fx_16.png` (4x4: attack loop, attack burst, power loop, power burst), `gun_tile.png`.
- Assemble: `Hulls/src~/paragon/build_paragon.py` (keys + fits the three sheets at 243 px wide, bank poses
  from the rest drawing via the same warp `ShipDamageTable.Posed` documents, damage rows take the intact
  alpha exactly, measures rect / nozzles / emitters -- nudging each emitter onto paint in all 27 cells --
  writes `data.json`, skins, exhaust atlas strips) then `patch_cs.py` (fills the `BEGIN/END PARAGON` blocks
  next to the generated blocks the vector pipeline owns). Weapons: `Weapons/src~/paragon_weapons.py`
  (scripted fallback + the meter row) then `ingest_paragon_codex.py`.
- **Skins**: palette-shift script on the finished sheet (`recolour`): hue-shift the armour's red only, keep
  blues, ink and alpha identical (the script asserts equal alpha). Written as PNG bytes to
  `Resources/ShipArt/Skins/<Key>_<Skin>.bytes`. 4 skins besides stock (`ShipSkins.PerShip = 5`,
  `ShipWeaponUpgrades.MaxLevel = 4` assume it).
- Import settings: hull sheet = copy a neighbour's `.meta` (bilinear, no mipmaps, 2048 max, `Resources/`);
  atlases keep their metas; Unity writes missing metas on first import, commit them.

### 3. Wire the tables (Paragon is the worked example)

1. `ShipId.keys` + `shopingShips` (`shipTotal`, `Roster`, `Prices`).
2. Run `build_paragon.py` + `patch_cs.py`: rect (+ppu = rect width / 0.34 world units), nozzles (3: big
   centre `scale 1.5`, two outboard `.55`), emitters, skins block.
3. Exhaust: new `ExhaustKind`, a `Row(...)` (its `outer` must be one of the hull's identity tones:
   Paragon's identity hue is its blue, not its red armour), two `Strip`s in free atlas space,
   `sprites` array sized for the new id.
4. Heart style, weapon style row (`ChargeStyle` / `ShotStyle` enum values + artKey), loadout row
   (new `ShipAttack` value goes BEFORE `ScreenClear`, which must stay last: title traffic sizes an array by it),
   `ShipWeaponUpgrades` track (4 steps), runner cases, title-traffic case, `ShipFxArt.Rows`,
   `UltimateGun` slot clamp, codex lore.
5. Premium extras: `PremiumShips` tiers (hearts / speed / missile cadence + count / shield cadence + seconds, one
   row per skin tier -- keep strictly increasing), `ParagonMissiles` (timed homing missiles through
   `ShipAttackRunner.FireMissiles`), `SecretPower.AegisShield` (timer-filled meter, `PowerTrigger.Always`,
   `collisionDetection.RaiseShield` = the blue-atom shield without its boost), `ShipDamageTable.StateFor`
   wrecks on the last two hearts of a 10+ heart hull.

### 4. Save data

Nothing new for a standard ship (`boughtship<id>`, `skinOwned<id>_<n>`, `shipSkin<id>` already sync;
`ProgressSnapshot.MaxShipIndex` follows `shipTotal`; old saves lack the new keys = unowned). Never add
required fields; additive only.

### 5. Tests

Add `PremiumShipTest`-style suite (register in `AllTests.Suites`): identity rows, the purchase gate,
tiers strictly escalating, skins priced and owned only with the ship, the economy it must not disturb,
timers firing at the right second (step the components with fixed `dt`), shield, barrage count.
Then run and fix the suites the roster length touches (each hard-codes "15 ships / 5 rows / prices"):
`RosterCleanupTest, ShipSelectionTest, ShopTest, RowHeartsTest, SkinHeartsTest, ShipLivesTest, ColumnSpeedTest,
ExhaustStyleTest, ShipNozzlesTest, ShipWeaponUpgradesTest, PowersReworkTest, CodexTest, AchievementCatalogTest,
UnusedAssetGuardTest, ShipHitboxTest` (use the real names in `AllTests.cs`). Final gate: `make test`.

### 6. Verify

`scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites A,B,C`, then
`BuildScript.BuildMac` (and `BuildAndroidDev` for the phone). To see the ship, `DockPreview` /
`CodexPreview` editor tools or the dev build (developer mode owns it).

## Pitfalls hit building Paragon

- The default jj workspace has no `Library`; `cp -Rc` one. The batch queue can hold you for an hour.
- Codex headless refuses a non-git dir unless `--skip-git-repo-check`; it writes finished PNGs to the
  folder you name, a half-written PNG ("truncated") means it is still working.
- Codex's bank poses barely differed from rest: bank from the rest drawing + the documented warp.
- `ShipLoadout` enums: adding after `ScreenClear` overflows `TitleScreenTraffic.AttacksFired`.
- `ShipLives.Rows` is derived from the roster count: a premium ship must not add a dock row's worth of
  hearts to every other ship (`PremiumShips.StandardCount`).
- Premium price sentinel: `CostFor` returning 0 would sort it first and make the dock sell it; return
  `float.MaxValue` and guard `IsTopTierByPrice`.
- The dev switch (`DeveloperUnlocks`) writes `boughtship<i>` for the whole roster: that is the intended debug grant, but
  a cloud snapshot must use `RealString` (it already does).
- Gun pods: `ship_gun_roster.png` tile 16 was empty and `UltimateGun` clamped the slot to 14.
