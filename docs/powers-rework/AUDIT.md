# Ship powers audit (read-only)

Paths are relative to `/Users/sina/Developer/Pause/Pause/Assets/` unless noted. W = `Scripts/Gameplay/Weapons/`.

## Vocabulary
The code calls them **attack** (`ShipAttack`, what the player means by "ultimate") and **secret power** (`SecretPower`, the "secondary power").
- Attack: fires itself when a 30-60 s countdown ends, `ShipPowerController.cs:28`. Dust and atoms shave time off it, `:55-59`. The tuning struct is `ShipLoadout.cs:71`.
- Only the 3 priciest hulls (Ion Lancer, Jade Phantom, Gold Warden) get `ScreenClear`. That is the cinematic homing volley at everything on screen (`ShipPowerController.cs:355-455`, slow motion at 0.06x, `:99`).
- Secret power: a separate 100-point meter, `SecretPowers.cs:20`.
  - Fills from small dust +3, large dust +6, kill +5, `:23-25`.
  - When full it waits for its trigger (`ShipLoadout.cs:57-69`) and fires itself. There is no input.
  - Hit-avoidance powers also fire from `InterceptHit()` (`:220`) the instant a hit lands.

## 1. Ship table

All 15 hulls. Ships are not tied to worlds. Ownership is `boughtship<id>`, `ShipId.cs`. Price is in star dust, `Shop/shopingShips.cs:114`. Ship 1 (starter) is always owned. Developer mode (`Core/DeveloperUnlocks.cs`) owns everything.

Attack and power rows come from `ShipLoadout.cs:120-195`. Weapon upgrade steps are `W/ShipWeaponUpgrades.cs:53-73`: 4 steps per ship, bought as colourway skins. Top tier gets only -10% cooldown steps.

| id | Ship (key) | Price | Attack (stats) | Secret power (trigger) | Power mechanics (`SecretPowers.cs`) |
|---|---|---|---|---|---|
| 1 | Neon Comet (NeonComet) | 0 (starter) | Comet Streak: 2 straight streaks, 0.16 s apart, speed 13, range 11, stop at first hit | Shield Pulse (ImminentHit) | `:252`: 0.75 s invulnerability (`pulseInvulnerable`), kills hazards within 1.4, burst art 3.2 units |
| 2 | Volt Viper | 700 | Chain Lightning: 5 hops, first reach 4.6, then 2.5, one hop per 60 ms | EMP (5+ enemies on screen) | `:258`, `Stun()` `:401`: disables movers on every `Enimey`-tagged object for 3 s via `HazardMovers.Disable` (moveEnimes, straight-line, ChaserEnemy). Does not stop their shots. |
| 3 | Solar Fang | 1700 | Solar Fireball: slow shot (speed 4.6), burst radius 1.7, range 3.6 | Solar Nova (3+ hazards within 2.3) | `:263`: kills within radius 2.1, 0.06 s hit-stop, camera kick |
| 4 | Crimson Halo | 2650 | Rail Gun: instant piercing line, half-width 0.42, to top of screen | Capacitor Dump (3+ targets while charge < 65%) | `:269`: `RechargeNow()` sets the attack timer to about 1.2 s. Shows a 1.6 burst. |
| 5 | Ion Lancer | 3850 (top tier) | Ion Barrage: cinematic homing volley (`ScreenClear`) | Ion Battery (pauses <= 1) | `:277`: `score.incromentPause()` adds 2 pauses. Code comment says +2, UI says "Adds two", so they agree. |
| 6 | Jade Phantom | 5300 (top tier) | Wisp Storm: cinematic volley | Phase Cloak (ImminentHit) | `:282`, `BeginPhaseCloak` `:352`: 4 s real invulnerability. Burst plus a 1.6-unit aura loop on the hull. |
| 7 | Gold Warden | 7000 (top tier) | Warden Salvo: cinematic volley | Gold Magnet (4+ dust in sight) | `:286`, `TickEffects` `:485`: 5 s, pickups pulled at 7 u/s to the ship. 3.2-unit loop art. |
| 8 | Lightning | 950 | Sky Javelins: 3 lanes from above, 0.08 s apart, lane spacing 1.2, speed 26 | Thunderclap (2+ hazards within 2.0) | `:291`, `Clap` `:429`: pushes hazards within 2.6 up 2.4 units over 0.25 s. Never moves the boss. Does not kill. |
| 9 | Ligher | 1200 | Blowtorch: flame cone range 2.7, half-angle 24 deg, 1.4 s held, 0.2 s boss ticks | Flare Decoy (chaser within 4.5, or 2+ hazards within 2.4) | `:296`: a decoy 1.2 units to the side and 0.9 up. `ChaserEnemy` hunts `ShipDecoy.Position` for 2.5 s, then pops (radius 1.5 kills). |
| 10 | Paranoid | 1450 | Seeker Eyes: 3 homing eyes, speed 7.5, one target each | Time Bubble (3+ hazards within 2.6) | `:307`: `WorldTimeFx.BubbleScale = .45` for 2.5 s. 3.6-unit loop art. |
| 11 | Ninja | 1950 | Ricochet Star: bounces on 5 hops, reach 3.1, speed 11 | Blink Dash (ImminentHit) | `:313`, `Dash` `:445`: teleports sideways 1.25 away from the threat, 0.6 s cloak, offset eased over 0.7 s |
| 12 | Saboteur | 2150 | Gatling: 14 shells over 1.1 s, cone half-width 7 deg, speed 16 | Black Hole (6+ enemies on screen) | `:317`, `StepHole` `:542`: hole 2.6 units ahead for 2.6 s. Pulls within 2.7 and kills within 0.45. Bosses take a trickle (0.34/s). |
| 13 | UFO | 2400 | Tractor Beam: held 1.5 s, half-width 0.38, to top of screen | Star Shower (calm: <= 1 hazard) | `:327`, `Shower` `:463`: 10 `smStar_1` dust pickups in an arc 2.2-3.3 above. This is a pure currency payout. |
| 14 | Dove | 2900 | Feather Fan: 5 darts in a 56 deg fan, speed 12 | Mending (hull damaged) | `:332`: `lifeCounter--`, heals one heart |
| 15 | Turtle | 3600 | Shell Disc: orbits at radius 1.05 for 5 s, 320 deg/s, hit radius 0.42 | Hard Shell (hazard in lane within 3.4) | `:337`: shield for up to 8 s that eats the next hit. Uses `ShipHitbox` shield zone. |

Hit rules used by every attack and power (`W/ShipAttackTarget.cs:36`):
- Ordinary hazards die from any hit.
- A boss (`IShipAttackTarget`: `BossActor`, `EliteShip`, `EliteShots`, `TeleportFx`) takes a weighted hit. One firing is capped at 1.0 total (the `budget`). Powers that touch the boss do half weight, `SecretPowers.cs:391`.

### Art and VFX each uses
**Secret-power art** (the thing to rework):
- All powers share one atlas, `Art/Resources/Weapons/AttackFx.png`, 2048x1920, 128 px cells, 16 cols x 15 rows (one row per ship). `ShipFxArt.cs:22-35`.
  - Cols 8-11 are a 4-frame power loop. Cols 12-15 are a 4-frame power burst.
  - Hold times: loop {3,2,3,2} ticks, burst {1,2,2,3} ticks at 24 fps, `ShipFxArt.cs:30-31`.
  - Cols 0-7 are the attack loop and burst, also 4 frames each.
- Texture import: PPU 100, filterMode 1 (Bilinear), max size 2048, `AttackFx.png.meta:34-51`.
- Sprites are sliced at runtime with `Sprite.Create(..., ppu = cell width)`, so one world unit equals 128 px at scale 1.
- Drawn through the pooled `AttackSprite` (`W/ShipAttacks.cs:725`, sources AttackLoop/AttackBurst/PowerLoop/PowerBurst/Shot).
- Meter badge: `SecretMeter.png`, 1024x960, 64 px cells. Cols 0-9 fill, 10-13 ready loop, 14 flash, 15 icon. The badge is `W/SecretMeter.cs`.
- Source is `Art/Weapons/src~/fx.py` (`power_loop` `:214`, `power_burst` `:285`, `icon` `:395`). It writes SVG and renders with resvg. `weapons.py` is the orchestrator.
  - **The style is the old flat Akira cel look** (docstring `weapons.py:3-12`), not the current neon pixel art direction.
- Upscaling: a power burst is drawn at e.g. 6 world units for EMP (`SecretPowers.cs:259`), and `ShieldPulse` at about 3.2.
  - On a 1080-wide phone that is roughly 190 px per unit, so a 128 px cell is stretched about 6x, with bilinear filtering. It will look soft.
- Other FX: `PowerFx.cs` is the older Kenney particle path (`Resources/Vfx/Kenney/vfx_*.png`). It is used for the ultimate-clear ring, `ShipPowerController.cs:445`, and legacy helpers (Laser, Ring, Aura, Burst, Missiles) that the new powers no longer call.
- `TeleportKillFx` is **not** a ship power. It is the pause-teleport kill: `Resources/Vfx/teleport_kill_atlas.png`, 8 frames of 256 px, PPU 256. It is the only neon-pixel-art FX in this area, and a good style bar. Used from `Ship/TeleportFx.cs:141`.

**Attack (ultimate) art:**
- Per-ship weapon atlases `Art/Resources/Weapons/<Key>.png`, 2048x384, 128 px cells, 16x3.
  - Contents: charge x16, ready x4, shot x6, impact x6, muzzle x4, release x4, trail x2. `WeaponArt.cs:7-100`.
  - Also the same old cel style.
- Charge indicator in front of the hull: `W/ChargeIndicator.cs`, styled by `WeaponStyleTable.cs:5-111` (15 charge styles, 15 shot styles).
- Shot behaviour for the top tier: `WeaponFx.cs` (`WeaponShot`, `FlipbookFx`).
- The slide-out barrel on the hull's left side is `W/UltimateGun.cs`.
- Explosions: shared `Explosions.png`, 2048x768, `WeaponArt.cs:115`.

### Usefulness judgement
Rough meter economy: 100 points at +3 per small dust, +6 per large dust, +5 per kill. Fill time depends on spawn density. Powers fire only when their trigger is met, so strict triggers mean a full meter can sit unused.

- **Genuinely useful:**
  - Shield Pulse, Phase Cloak, Blink Dash, Hard Shell: they negate a lethal hit, and `InterceptHit` makes them always spend on the hit. Mending (a life) is also strong.
  - Phase Cloak (4 s, no movement) is the strongest of these. Blink Dash is the most skill-expressive but needs a clear side.
  - Shield Pulse is the weakest of the group: 0.75 s.
- **Strong but trigger-gated:**
  - Black Hole (6+ enemies on screen) is rarely satisfied.
  - EMP (5+ enemies) is likewise rare, and it only stops movement, not enemy shots.
  - Solar Nova (3+ in 2.3) only fires in a clump.
- **Weak or low-visibility:**
  - Capacitor Dump: it sets the timer to about 1.2 s but fires only while charge < 65%. The benefit is invisible: another attack comes sooner.
  - Ion Battery: +2 pauses, but only when pauses <= 1. It is a good survival effect, but nothing happens on screen.
  - Gold Magnet: a currency convenience, near-zero danger value.
  - Star Shower: spawns 10 dust, purely a currency bonus, fires only in calm.
  - Time Bubble: 0.45x world speed for 2.5 s. It is actually good, but has no readable visual beyond a 128 px loop.
  - Thunderclap: pushes hazards up 2.4. It does not kill, and bounces back via world scroll.
  - Flare Decoy: mixed. It only matters against chasers.
- **Redundancy:**
  - Shield Pulse, Phase Cloak, Blink Dash and Hard Shell are all "avoid one hit" with different costumes.
  - Gold Magnet and Star Shower are both "currency".
- **Attacks:**
  - Top tier (Ion/Jade/Gold) is distinct only in art. The three attacks are mechanically identical; only the shot sprite differs (`WeaponStyleTable.cs:100-102`).
  - Gatling, Rail and Javelins are the strongest direct damage.
  - Tractor Beam (UFO) and Feather Fan (Dove) are basically a forward line or cone. Their name and art do not suggest "tractor", since it does not pull.

## 2. The Space Dock screen (where the preview goes)

- Scene `Scenes/shopS6.unity` keeps only camera, starfield, HUD canvases and UIManager scripts. The dock is **procedural**, built in code from `ShopSceneExtender.Build()` (`Shop/ShopSceneExtender.cs:12`) into `SpaceDock.Build()`. `Construct()` is at `Shop/SpaceDock.cs:202`.
- Files in `Scripts/Shop/`:
  - `SpaceDock.cs`: berths, selection, `Select` `:487`, `ShowPopup` `:521`, `Launch`, `Buy`.
  - `DockBay.cs`: each berth with its hull sprite, `HullSize = .64`.
  - `DockPopup.cs` (794 lines): the LAUNCH/BUY card.
  - `DockLayout.cs`: rack math.
  - `DockArt.cs`: UI sprites.
  - `DockTween.cs`, `DockLaunchFlame.cs`.
- **Popup construction:** `DockPopup.Create` (`:134`) makes a `~DockPopup` GameObject with its own **world-space uGUI Canvas** (`RenderMode.WorldSpace`, `sortingOrder = 60`, `CanvasScaler` with `dynamicPixelsPerUnit = 4`, `GraphicRaycaster`), `:160-171`.
  - All children are built in code with `Rect/Place/Label/Chip` helpers: `Panel` (sliced `DockArt "popup"`), title, skinName, status, lives badge, close button, `Action` Button, a 5-chip skin row, a weapon-level row, START SPEED line.
  - It is a legacy `UnityEngine.UI` (uGUI) `Image` + `Text` stack. No UI Toolkit, no prefab.
- **Sizing:** layout is in canvas units, 1 unit = 1 pt/dp. `PanelWidth = 300`, `BaseHeight = Pad + HeaderHeight + 10 + ButtonHeight + Pad` (about 126, locked ship), `OwnedHeight = BaseHeight + 50 + 20 + 18` (about 214). `Resize()` at `:529` sets `sizeDelta`.
  - `Follow()` anchors the card above (or below, if flipped) the ship, with a tail, clamped to `safeView`. If it fits neither side the dock slides the rack with `onNudge`.
  - `SetDensity` shrinks the unit so `OwnedHeight + TailUnits` fits `safeView.height`, `:117`.
  - **The card is drawn at `PopupScale = .85`.** It is the same card for owned and unowned ships.
- **How to add a preview region above the popup:**
  - Add a child `RectTransform` (e.g. `Preview`, about 300 x 120 units) anchored top of `panel`, inside the card above the title. Push the existing offsets (`Place(... -Pad ...)`, `SkinRowTop`, `-Pad - 24f`, etc.) down by a constant `PreviewHeight`, and add it to `BaseHeight` / `OwnedHeight`.
  - Alternatively make the preview a separate sibling card above the panel and extend `size` in `Follow()`.
  - Re-check the `safeView` fit: the card is already near the height limit on short phones. The `DockPopupScaleTest` and `DockPreview.RunPopup` cover this.
  - Drive it from `Show()` / `ShowSkins()` (they know ship index and owned state) by assigning an animated sprite sequence to an `Image`/`RawImage`. This works for owned and locked ships (a preview before buy is the point).
  - Use `Time.unscaledTime`, as the popup does.
- **Video viability:**
  - **Unity VideoPlayer is not used anywhere.** `grep VideoPlayer` over Assets/Packages/ProjectSettings returns nothing, and `Packages/manifest.json` has no `com.unity.modules.video`.
  - It is technically available on Android (MediaCodec) and iOS (AVFoundation), but:
    - You would need to enable the video module.
    - You would need per-platform codec settings (H.264 mp4), and 15 clips stored in the build.
    - Clips would be non-pixel-crisp compressed (banding on neon glow).
    - You would need a RenderTexture on a world-space canvas, with a startup stall per clip on mobile.
    - Alpha is unsupported, so the clips must be baked over a background.
  - **Recommended: baked looping frame-sequence atlases.**
    - The project already does this everywhere: `AttackFx`, `SecretMeter`, `teleport_kill_atlas`. Slicing and flipbook players exist (`ShipFxArt.Slice`, `WeaponArt.FrameAt`, `AttackSprite`).
    - Pixel-art friendly (Point filter, no codec artifacts), supports alpha, small, and testable in edit mode.
    - A 15-clip atlas at e.g. 256x128 px per frame x 24 frames x 15 ships is manageable: about 6144x384 per ship, or one PNG per ship.
    - Offscreen RT frame capture can be generated in-editor, see section 3.

## 3. Recording / harness tooling that exists

- **Unity Recorder:** not installed. `Packages/manifest.json` has no `com.unity.recorder`, and nothing references `UnityEditor.Recorder`. Present: `com.unity.modules.screencapture`, `com.unity.test-framework 1.6.0`, `com.coplaydev.unity-mcp`.
- **Offscreen render-to-PNG pattern exists:** `Editor/Tools/Previews/DockPreview.cs` is batchmode: `Unity -batchmode -projectPath Pause -executeMethod DockPreview.Run|RunMix|RunWeapons|RunPopup`. It:
  - Opens `shopS6`, builds the dock, selects a ship, and renders the camera to a `RenderTexture`, then `ReadPixels` and `EncodeToPNG` (`:105-150`).
  - It can open an arbitrary popup state via the `after` callback, e.g. `TapSwatch`.
  - Other previews in `Editor/Tools/Previews/`: `BossDamagePreview`, `AttackCoresDPreview`, `ShipReachRender`, `LiftoffPreview`, etc. Same pattern.
- **Deterministic ship-power harness (edit-mode tests):** `Editor/Tests/ShipAttacksTest.cs` and `Editor/Tests/TestHarness.cs` (`TestHarness.Sandbox`).
  - They build a scene, spawn a ship with `ShipPowerController`, then call `Runner.Fire()` (`:191`), `Secret.SetMeter(SecretPowerController.Full)` then `Secret.Step(dt)` (`:321-331`), or invoke `ShipPowerController.Fire` by reflection (`:168`).
  - `Step(dt)` is public on `ShipPowerController`, `ShipAttackRunner` and `SecretPowerController` for exactly this purpose.
  - Other relevant tests: `CloakShieldTest`, `RedAtomFreeShotTest`, `ShipWeaponUpgradesTest`, `TitleScreenCombatTest`.
- **So a frame-sequence bake is feasible:** an Editor script (new, modelled on `DockPreview.Render`) that, per ship id, loads an empty scene, spawns the hull with `ShipPowerController.Setup`, places a few `Enimey`-tagged dummy targets, steps `Step(1/24)` per frame and calls `camera.Render()` into a RenderTexture to write PNG frames. This is **not present today**, but every ingredient is.
  - Caveat: `AttackPool`, `WeaponFx` and `TargetExplosion` use `Time` / coroutines. Some effects (`PowerFxRunner` coroutines, `AttackSprite` clocks) may not advance in edit mode without being driven. Play mode (the `-executeMethod` plus `EditorApplication.isPlaying`, or a PlayMode test) is safer.
- **Mac build flags:** `Editor/Build/BuildScript.cs` provides `BuildMac`, `BuildAndroid`, `BuildAndroidDev` (per memory `pause-build-run.md`). **There are no runtime command-line flags.** `grep GetCommandLineArgs` returns nothing, so a built game cannot be told to spawn ship N and trigger its power.
  - The dev build (`PAUSE_DEV`, `DeveloperUnlocks.Available`) unlocks all ships, so capture by hand in-game is possible but not scripted.
- Android screen recording: `adb shell screenrecord` works on the phone (adb serial in memory).

## 4. Highest-impact rework opportunities per ship

Constraints: HD neon pixel art (`art-direction-akira.md`, hi-res Space planet set as style bar); pink only in the Space world (`pink-only-in-space.md`), so a power effect in a non-Space world must use the ship's own colour or the world cue colour (power art is currently driven by the shared PALETTES row of each ship, so keep to ship palette; no magenta except Space). The player owns red. Keep outlines pale and readable (`enemy-visibility-add-border.md`).

**Cross-cutting (biggest wins):**
1. Redraw `AttackFx.png` and `SecretMeter.png` as neon pixel art at higher resolution (e.g. 256 px cells, Point filter), keeping the layout in `ShipFxArt.cs` so no code changes are needed. Currently 128 px cells blown up to 3-6 world units, bilinear, in the old cel style.
2. Raise power frame counts from 4 loop / 4 burst to 8-12 so the preview clips read as animation, and make the layout in `ShipFxArt` per-ship configurable.
3. Give the "invisible" powers an on-screen consequence (see below), so the dock preview has something to show.
4. Differentiate the four "avoid one hit" powers mechanically (see below), or the ship roster feels same-y.
5. Add power info to the popup. Currently the dock never shows the attack or power (`grep Loadout Shop/` returns nothing); only the Codex does (`Codex/CodexCatalogue.cs:378`). The preview region is a natural place for the name plus line plus looping clip.

**Per ship:**
- **1 Neon Comet / Shield Pulse:** 0.75 s is short. Make it a visible expanding shield-ring that also destroys projectiles (it kills hazards within 1.4 already), and extend invulnerability to about 1.2 s. Starter ship, so it should be the cleanest tutorial of "power fires on its own".
- **2 Volt Viper / EMP:** show the stun: arcing sparks on each frozen enemy (per-enemy loop exists, `SecretPowers.cs:412`). Add "also cancels incoming enemy shots" so it is useful when 5 enemies are not on screen (lower threshold to 3).
- **3 Solar Fang / Solar Nova:** a dramatic expanding radial flare in amber, bigger kill radius (2.1 is small), plus burn residue.
- **4 Crimson Halo / Capacitor Dump:** weakest and least visible. Rework so the visible result is an immediate rail shot (fire the attack on trigger instead of resetting its timer). Show capacitor ring discharge to the gun. Drop the "charge < 65%" requirement.
- **5 Ion Lancer / Ion Battery:** show a visible battery-pack pickup floating to the pause counter, and also grant a short slow-time. Consider trigger at <= 2 pauses.
- **6 Jade Phantom / Phase Cloak:** the strongest; make the visual unmistakable (a ghost afterimage trail, ship goes translucent) and consider letting the ship pass through hazards.
- **7 Gold Warden / Gold Magnet:** add a secondary effect (e.g. magnet also converts nearby hazards' debris to dust or briefly doubles dust value) so it is not purely currency; show golden field lines pulling dust.
- **8 Lightning / Thunderclap:** shoves hazards up but they come back. Add damage (lightning strikes the shoved hazards) so it kills, not only displaces.
- **9 Ligher / Flare Decoy:** only helps against chasers. Make the flare also distract shooters, and show a bright pulsing lure with sparks.
- **10 Paranoid / Time Bubble:** keep, but add a visible dome with distortion rings and a distinct "slow" tint. Currently a 128 px loop.
- **11 Ninja / Blink Dash:** add a visible afterimage line and impact smoke at both ends. Also let it work as a proactive dodge, not only as an interceptor.
- **12 Saboteur / Black Hole:** threshold of 6 enemies rarely met. Lower to 3-4 and show a spiral accretion disc with debris. Consider a neon-purple (not pink) rim outside Space.
- **13 UFO / Star Shower:** pure currency. Make it a tractor beam pulling enemy debris plus dust, or tie to the ship's tractor-themed attack (the Tractor Beam does not actually pull anything).
- **14 Dove / Mending:** keep. Add a visible green heal feather burst and a heart restore animation. The preview should show the hull cracking then being mended.
- **15 Turtle / Hard Shell:** the 8 s shell with hex plates matches the ship's charge style; make the shell visible on the hull for its full duration and show the break animation when it absorbs a hit (currently only a 2.2 burst, `SecretPowers.cs:230`).

**Attacks (ultimates):**
- Top tier trio: give each a distinct mechanic. Today only the sprite differs (`WeaponStyleTable.cs:100-102`).
  - E.g. Ion Lancer: piercing lances. Jade Phantom: bouncing wisps. Gold Warden: area missiles.
- Tractor Beam (UFO): actually pull enemies, or rename it to something that matches a straight beam.
- Feather Fan, Gatling, Seeker Eyes: fine mechanically. They need a bigger readable muzzle and hit effect.
- Explosion, impact and charge-indicator art for all ships: same re-style as above.

## Also noted
- `ShipLoadout.cs:41` comment says Ion Battery "+2 pauses". `score.cs:250` adds 2. These agree.
- Hit tests read `ClearTarget.Live` and tags `"Enimey"` (sic) and `"Astr"`.
- The dock currently wraps weapon level and START SPEED rows in the popup, so the card is already tall; any new preview needs a layout budget (see `DockPopup.cs:117-118`).
