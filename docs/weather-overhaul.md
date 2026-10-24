# Weather overhaul: solar flares, always-on wind, findings

Branch `feat/weather-overhaul-solar-flares`. Builds on `docs/world-weather-hazards.md` (the hazard events and the ambient layer).
Code: `Scripts/Worlds/Hazards/` (`WeatherWind.cs`, `WindVisuals.cs` new; `HazardForce`, `HazardDef`, `HazardVisuals`, `WorldHazards`, `WorldWeatherTable`, `WeatherLayer` changed).
Tests: `WeatherOverhaulTest` (new), `WorldHazardsTest` (updated). Codex prompts: `docs/weather-codex-prompts/space_flare.md`, `wind_gust.md`, `run_wind.sh`.

## 1. Audit: how weather worked before this branch

Weather was three separate things, and only one of them touched gameplay.

| Layer | What it is | Gameplay effect |
| --- | --- | --- |
| Backdrop weather (`*Backdrop.cs`, `weather.png` atlases) | cloud ceilings, snow fields, blizzard / pollen / dust gusts, rain sheets, Storm lightning | none, a picture behind the board |
| Ambient layer (`WeatherLayer`, `wx_ambient`) | at most 40 small sprites (alpha <= .6) per world, only dragged by a live event | none |
| Hazard events (`WorldHazards`, `wx_hazard`) | one of two per world, one event at a time, rare | pushes only, never damage |

Per world (before): Space gravity well + ion wind (+ ion motes), Frost blizzard front + whiteout eddy, Verdant spore gust + vine updraft, Ember ash updraft + geyser plume,
Tide whirlpool + current surge, Storm tornado + gust front, Desert sandstorm front + dust devil.

What an event moved, and how hard (old numbers):

| Target | Rule | Old magnitude |
| --- | --- | --- |
| Player ship | finger offset | gain .55, cap .85 u |
| Enemy bodies | lane-clamped displacement | speed cap 2.4 u/s |
| Hostile shots (`IHostileShot`: boss and elite projectiles only) | `ShotShove` | cap 4 u/s, and x .6 during a boss fight |
| Player shots | `AttackProjectile.Shove` | x .5 |
| Elites | velocity kick | accel 3, max 6 |
| Beams, jets, waves, strikes, blasts, rail mines, pickups | not moved | none |

Event field strengths were 1.0 - 4.2 u/s at the core, a body takes the field times its mix (usually 1), so most events pushed bodies 1 - 3 u/s and bullets 1 - 4 u/s.

### Why it felt weak and plain

1. **It was rare.** One event per 44 - 110 s lasting 5 - 12 s: the weather acted on the board about 10% of the time. The other 90% nothing in the weather touched a bullet.
2. **Nothing bent a bullet continuously.** No steady wind, no shear: bullets flew straight except during an event.
3. **The caps cut the force.** Shot cap 4 u/s and body cap 2.4 u/s are below a normal bullet's own speed (3 - 5 u/s) and a lane crossing; a field with strength 3 could not shove harder than 2.4.
4. **Most events were small.** Verdant spore gust 1.0, Frost blizzard 1.6, Frost eddy 1.8: weaker than the caps, so the gains never mattered.
5. **Boss fights got a x .6 scale** on top of that, plus the player's shots x .5.
6. **Only boss and elite shots were movable.** Ordinary enemy attacks (zones, jets, waves, blasts, lasers) are not `IHostileShot`, so weather could never touch them (still true, see the suggestions).
7. **The art was plain.** The ambient layer was 40 tiny sprites at <= .6 alpha; hazard bodies were one semi-transparent loop (by design "no hot cores, airy in the middle"), drawn the same way in every world; gusts in the backdrop were bitmap clouds that did not interact with anything. Nothing connected what you saw to what pushed.
8. **Telegraphs only existed for events**, so the player was never taught to read wind.

## 2. What this branch does

### 2.1 Space: solar flares (the old gravity well and ion wind are gone)

New hazard shape `HazardShape.Sweep`: a VERTICAL band that crosses the screen sideways, its leading edge a shocked front (`shock` makes the leading half up to 50% stronger than the tail), shoving everything in its direction of travel. `bands` / `bandGap` make a train.

| Hazard | Shape | Strength | Band | Speed | Warning | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `solar_flare` | one wide front | 6.5 u/s, caps x1.7 | 3.0 u wide | 5.2 u/s (2 s to cross) | 2.4 s | a hard single shove, bullets flung ~6.5 u |
| `flare_storm` | three fronts 5.4 u apart | 5.4 u/s, caps x1.6 | 2.1 u wide | 4.2 u/s (about 7 s for the train) | 2.6 s | three punches with a quiet gap you can step through |

Sun side and what a flare does (Space only):

* **Born at the sun.** A flare travels away from the sun: sun on the left of the backdrop, left to right; sun on the right, right to left (`HazardDef.fromSun`, `SpaceSunPick.FlareTravel`). The sun is one per-run pick shared by all four Space backdrop variants (`SpaceSunPick.Side`), so there is no per-variant side. The origin edge glow, the band start, the push and the mirrored art all follow `HazardField.side`. Fallback: if the sun art is not installed (no sun to see) the side is random as before.
* **Wipes bullets.** Every boss and elite shot (`IHostileShot`) the live band touches is destroyed with the shot-vs-shot pop (`HazardDef.wipesShots`, `HostileShots.Wipe`). Landed pools (Fixed mass) stay; the player's shots are untouched.
* **Stuns for 2 s.** Enemies and elites the band touches are stunned for `WorldHazards.StunSeconds` (2 s). The stun is refreshed every frame a target is inside the band, so it lapses 2 s after the band has left it. Two small dazed sparkles circle above each stunned head (`DazedSparks`, pooled, no outline). Bosses are exempt.
* **Hazards are flung.** Rocks and mines get the push times `HazardDef.objectPush` (2.4 / 2.2) with the speed cap raised to match; no stun, no damage. The player's push is unchanged and a flare never damages the player.

Space frequency: first flare at 24 s, then every 48 - 78 s (was 34 s / 70 - 110 s).

Telegraph (readable, then the sweep): the origin edge glows magenta-orange and flickers (random each 1/16 s), hot slivers and sparks drift in from it and grow over 2.4 - 2.6 s; nothing moves until the warning ends. Live: layered glow (violet, magenta, orange, gold), a white-hot core line, 12 plasma arcs per band streaming behind it (hot at the front, magenta to violet at the tail), sparks thrown ahead of the shock. The painted sheet (`wx_hazard.png`, vertical-band cells, leading edge at 78% of the cell) overlays it.

Fairness: the player takes at most `PlayerCap` (1.0 u), at a body mix .45 - .5 (about .4 u seen in the test), springs back in ~1 s after the flare passes; elites take .4 and are not damaged; nothing is ever damaged. Test: `FlareCrossing`.

Space ambient is now drifting solar sparks and plasma motes (gold, white-hot, magenta) in place of the ion motes.

### 2.2 The wind: shared, per-world, always on (`WeatherWind`)

One data-driven system, not per-world code. Every world except Space has a `WindProfile` in `WorldWeatherTable`:

* a steady **breeze** (so every bullet bends a little all the time) that swells and ebbs (9 s),
* a **shear**: wind differs by height (layers flowing opposite ways) or, for Ember, by column (thermals that lift in some columns and sink in others); depth > 1 reverses direction in the troughs,
* **turbulence** (fast flutter),
* scheduled **gusts**: calm, then a 1.7 s **warning** (the upwind edge glows, streaks start to lean in), then a 3 - 3.4 s gust that ramps in (.7 s) and out (1 s). First gust after 10 s in a world; never while an event is running or warned; the wind fades in/out (2.5 s) with the calm start, transitions, boss intros.
* per-target gains (`TargetMix`): hostile shots 1, bodies .55, player's shots .5, elites .25, ship .22 (capped by the ship offset cap).

| World | Wind | Breeze (x, y u/s) | Gust u/s (direction) | Gap s | Shear |
| --- | --- | --- | --- | --- | --- |
| Frost | gale | .45, 0 | 3.4 (side, up: a headwind that stalls falling bullets) | 7 - 12 | .7 over 6 u |
| Verdant | pollen drift | .3, .25 | 2.6 (side, lifting) | 8 - 13 | .9 over 5 u |
| Ember | thermals | 0, .75 (always lifting) | 3.2 (mostly up, a heat blast) | 7 - 11 | 1.3 by column, 3.2 u |
| Tide | currents | .65, 0 | 3.8 (side) | 8 - 12 | 1.4 over 7 u: opposing current layers |
| Storm | charged wind | -.5, -.3 | 4.4 (side, pulling bullets down: speeds them up) | 6 - 10 | 1.9 over 3.8 u, fast: zigzagging bullets |
| Desert | crosswind | 1.0, 0 | 4.6 (side) | 7 - 11 | .5 over 8 u |
| Space | none: the flares are its weather | | | | |

Measured in `WeatherOverhaulTest` (a bullet falling at 3.3 u/s for 2.8 s): the breeze alone bends it 1.2 - 2.9 u (every world), a gust 4.7 - 15.5 u.
That is deliberately strong (a gust can carry a bullet across the whole lane); the in-game caps (shot 7.5 u/s) bound it, and it is the first number to tune (section 5).

### 2.3 Stronger events and caps

Caps: shot 4 -> 7.5 u/s, body 2.4 -> 4.5, elite accel 3 -> 5 and max 6 -> 7.5, player cap .85 -> 1.0 (gain .55 -> .6), pilots .5 -> .6.
Event strengths: Frost front 1.6 -> 2.8, eddy 1.8 -> 3.0, Verdant gust 1.0 -> 2.2, updraft 2.2 -> 3.4, Ember updraft 2.8 -> 4.2, geyser 4.2 -> 6.0, Tide whirlpool 3.0 -> 4.6, surge 2.6 -> 4.4, Storm tornado 3.2 -> 4.8, gust front 2.2 -> 4.4, Desert sandstorm 3.0 -> 5.0, devil 2.2 -> 3.8.
A hazard may carry `capMul` (flares x1.6 - 1.7).

### 2.4 Visuals

* `WindVisuals`: 56 pooled streaks advected by the very function that pushes the bullets, so the player reads the force (layers visibly flow opposite ways where the shear flips; streaks lengthen and brighten in a gust and are invisible in a calm). The upwind edge glows during a warned gust. In the world's own cue colour (no pink outside Space).
* `WeatherLayer`: ambient particle count x1.6 (pool 40 -> 72), carried by the wind (strongest on light particles), streaks turn along the real velocity.
* `WindArt` (`wx_gust.png`, optional per world, 2048 x 1024, 4 x 2 cells of 512): painted gust ribbons, debris and the curling gust head that grows in from the upwind edge during the warning. Until a world has its sheet it keeps the procedural streaks.

### 2.5 Fairness against boss bullet waves (PR #30, `BossWaves.cs`, `WaveGaps`)

`WaveGaps.Choose` plans each wave's gap so the ship can reach it (plans at 4 u/s, the ship does about 5). Weather that bends the bullets differently could close a gap, so during a boss encounter:

* **Hostile shots are not pushed by the fields or the shearing wind at all.** Every live hostile shot gets only one **rigid, sideways** drift per frame: the same number for every bullet on screen (so a wave's gap moves WITH its bullets and keeps its shape), made of the wind's rigid part and any front or sweep's rigid part, capped at `BossShotWindCap` .12 u/s and slew-limited to .12 u/s per second (no abrupt reversal).
* Max drift over a 3 s flight: about .36 u, and two consecutive waves' drift differ by at most .13 u (under `WaveGaps.Slack` .14 u).
* Elite shots in a boss fight follow the same rule (one shared rule is simpler and safer).
* The player-affecting force is already a finger offset capped at 1.0 u, so the ship is always one finger move from any point.

Test (`WeatherOverhaulTest.BossShotsDriftTogether`, `WaveGapsStayReachable`): every bullet drifts identically each frame, never above the cap, never faster than the slew; the pure bound above; and 400 random wave chains through the REAL `WaveGaps` (found by reflection, so the file compiles with or without PR #30, it runs the chain check when #30 is in the tree). The chain model: ship at 5 u/s, human reaction, held in the previous gap while it passes, leaning toward the new one when it sees it, worst-case drift (triangle wave at the cap and slew limit). Result with #30 merged locally: 0 / 400 chains broken with no weather, 0 / 400 at the cap, and 20 / 400 at 8x the cap (so the test can fail). This branch does NOT touch `BossWaves.cs`, and merges with #30 without conflicts (a trial merge was clean; only `AllTests.cs` auto-merged).
Follow-up once #30 is on master: make the planner drift-aware (`WeatherWind` exposes the rigid wind; shift the spawn x by the predicted drift) and raise the cap to about .8 u/s.

### 2.6 Performance (60 fps)

All pooled: ambient 72 sprites, wind 56 streaks + 2 edge bars + 20 art sprites, hazard visuals 150 + 14 tiles; no allocation after prewarm (checked in the test at editor-noise level). The wind forces loop over `HostileShots.All`, `AttackProjectile.Flying` and `ClearTarget.Live` once per frame like an event does (no bounds cull, it is everywhere): `WorldHazardsTest.Cost` still holds the 2000-shot loop under 1 ms. A flare draws at most ~80 renderers. NOT measured on a phone.

## 3. Not verified without playing

Feel of the gust strength (the gust numbers are strong on purpose), how the flare reads at speed, whether 1.7 s is enough warning for a wind gust, how the wind reads with the busy Storm backdrop, frame time on the Flip7, that the rigid boss drift is visible enough to feel like weather without hurting, and the painted art in motion.

## 4. Art status

* Space `wx_hazard.png`: PLACEHOLDER (procedural, `Art/Weather~/SpaceFlare/src~/make_placeholder.py`) until the Codex sheet is vetted (`space_flare.md`, verifier `scripts/verify_flare_art.py`).
* `wx_gust.png` per world: Codex jobs prepared (`run_wind.sh`); staged under `Art/Weather~/<World>/gust` and wired as `Backdrop3/wx_gust.png` after vetting. Storm's is wired only after `art/storm-backdrops` lands (that branch owns the Storm backdrop art).

## 5. Suggestions, prioritized (what we did, what next, rough cost)

Done in this branch: flares, always-on wind with telegraphed gusts, the shear, stronger caps and events, wind streaks, denser wind-driven ambient, boss-fight drift bound with a proof.

Next, in order of value:

1. **Tune on a device (0.5 day).** Gust strength (2.6 - 4.6 u/s) and frequency; the body gain (.55) pushes most enemies to the rail in a long gust; the player's share (.22). Everything is in `WorldWeatherTable.W(...)` and the constants at the top.
2. **Drift-aware boss waves (0.5 day, after #30 merges).** Spawn waves offset by the predicted rigid drift and lift the boss-fight cap from .12 to about .8 u/s so weather is felt in boss fights too.
3. **Weather sound (1 day, user owns ambience).** A rising howl on the gust warning is the best telegraph there is; flare sizzle/boom. Missing today.
4. **Make ordinary enemy attacks windable (1 - 2 days).** Jets, waves, blasts, strikes and the rail-mine lasers are not `IHostileShot` and ignore weather. Let waves/blasts drift with the wind and lean jets; keep lasers fixed (they are rails).
5. **Per-world gust signatures (1 day each, art + code).** Frost: icy sheets scouring the ground; Ember: heat-haze distortion rising in columns; Storm: gusts carry lightning that arcs between bullets; Tide: foam lines on the layers; Desert: sand curtains that briefly dim the lower screen (only the backdrop, never the board).
6. **Layer the backdrop with the wind (1 day).** Drive the backdrop's cloud banks and gust sprites from `WeatherWind.Sample` so the pictures behind the board flow the same way as the force.
7. **Screen-space heat shimmer / refraction (1 - 2 days, a small shader).** The flare and Ember thermals warp the backdrop; costs fill rate, make it quality-tiered.
8. **A subtle player-visible wind cue (0.5 day).** A thin wind arrow or streak bias at the ship so the finger offset never feels like a bug.
9. **Weather-linked pickups and kills (1 day).** Bonus for killing an enemy with a flare-flung collision; atoms drifting in a gust (needs a design decision).
10. **Accessibility: a reduce-motion toggle** that flips `WeatherLayer.Reduced` and halves wind streaks (0.25 day).
