# Speed cap, limit break and the open portal

Branch `feature/speed-cap-and-open-portal` (from `integrate/oct05-full-master`). This design is committed
before the implementation; the checklist at the bottom is updated with each stage. Numbers marked
*(measured)* are filled in from the tests once they run. Nothing here has been played: it is simulation
and tests only.

The request: remove "top speed" from scoreboards and stats; cap speed at 35 unless the boost shield
breaks the limit; make the ramp to 35 slightly slower; let ships that start faster reach the boss sooner
and loop more; keep the portal open at the end of every level until the player flies through it, with
enemy density rising until they do or die, the final loop back included.

## 1. Speed

### The cap

`SpeedRamp.Cap` = **0.35 (HUD 35)** is the one natural speed limit: every world, every loop, the
tutorial. Gone: `WorldTheme.maxSpeed` (38 / 40 / 42 / 44), the per-loop cap bonus (+1 / +2), KEEP
FLYING's +4 and `LoopRules.AbsoluteMaxSpeed` (50).

`moveBackGround.speed` stays the effective scroll speed everything reads. It is now
`natural + SpeedRamp.Boost`:

* **natural** is what the ramp owns. `SpeedRamp.Tick` never raises it past the cap, and everything that
  sets a speed for a level (run start, portal arrival) goes through `SpeedRamp.SetNatural`, which clamps.
* **Boost** is the limit break (below). It is the only way the effective speed passes 35.

### The curve

Linear at the world's `speedRampPerSecond` up to `SpeedRamp.EaseKnee` (HUD **25**), then an ease into
the cap: the rate falls off as the square root of the distance left to the cap, so speed follows a
parabola that arrives at 35 with zero slope, in a finite time (`2 x (cap - knee) / rate` after the knee).
No wall, no asymptote. `Tick`, `SpeedAfter`, `DistanceOver` and `SecondsToCover` all follow it (closed
form; the inverse is a bisection).

Tunables, all in `SpeedRamp`: `Cap` .35, `EaseKnee` .25 (the ease spans `Cap - EaseKnee`).

Seconds from a start of 0 to each HUD speed, first pass (old = linear to 30, 40% rate above)
*(measured, `SpeedCapTest` "TABLE ramp")*:

| World | rate HUD/s | to 10 old / new | to 20 | to 30 | to 35 | old cap, reached at |
|---|---|---|---|---|---|---|
| Space | 0.315 | 31.7 / 31.7 | 63.5 / 63.5 | 95.2 / 98.0 | 134.9 / 142.2 | 38 at 159 s |
| Frost | 0.330 | 30.3 / 30.3 | 60.6 / 60.6 | 90.9 / 93.5 | 128.8 / 135.8 | 40 at 167 s |
| Verdant | 0.345 | 29.0 / 29.0 | 58.0 / 58.0 | 87.0 / 89.4 | 123.2 / 129.9 | 42 at 174 s |
| Ember | 0.365 | 27.4 / 27.4 | 54.8 / 54.8 | 82.2 / 84.5 | 116.4 / 122.7 | 44 at 178 s |

So the first 80 s of a world are unchanged, 30 comes about 2.5 s later, 35 about 6-7 s later, and 35 is
where it stops.

### What still differs between worlds

The cap is shared. Worlds differ by: ramp rate (above: Ember reaches 35 twenty seconds before Space),
`enemyRampScale` (phases arrive sooner: x1.00 / 1.10 / 1.20 / 1.35), the roster itself (each world's
enemies and behaviours), pilot load (3-2 in Space and Frost, 3.5-2.5 in Verdant and Ember), the boss.
Unchanged by this branch except that the cap no longer separates them.

### Limit break (the blue atom's boost)

While the blue atom's shield is up the ship boosts, as today: +5 HUD per blue atom caught while shielded.

| Tunable (`SpeedRamp`) | Value | Meaning |
|---|---|---|
| `BoostPerAtom` | .05 | each blue atom adds this to the boost target |
| `MaxBoost` | .10 | the most the boost can add (HUD 45 at the cap) |
| `BoostRisePerSecond` | .25 | the boost comes on over 0.2 s (was a one-frame jump) |
| `BoostSettlePerSecond` | .05 | when the shield ends the boost eases off: 1 s per atom |

* The boost is an offset on top of natural speed; the ramp keeps running underneath. (Before: the +5 was
  added to the one speed value, the ramp then stopped at the cap, and the -5 at the end left the ship
  5 under where it would have been. That is fixed by the split.)
* Below the cap a boost is just a boost. At or near the cap it passes 35: the limit break. When the
  shield ends, speed returns smoothly to natural, so to at most 35.
* **Distance flown during a boost counts toward the level** (the level clock integrates the effective
  speed). Boosting is how you get ahead.
* A boss still holds speed at 20: the boost is cancelled when the intro starts and atoms give none during
  the fight (as before).
* Fairness above 35: `EnemyDensity` clamps at HUD 35 (rate, ceiling, pilot load: the same as at 35); shots
  are world-space or board-relative already; the estimate to the boss (`SecondsLeftInWorld`) is taken on
  natural speed, so a boost only makes the boss arrive up to about a second early and the countdown
  converges as it does for any speed change. The shield's release shockwave and "+5 ABSORB" do not read
  speed; tests run them at boosted speed.

### Ship start speed is the advantage

Unchanged table (`ShipStartSpeed`): regular ships 0 / 5 / 10 / 15 / 20 by colour, high-end 10 - 30. A
world is a distance (what a start of 0 covers in 120 s on the world's curve), so a faster start meets the
boss sooner, and arrives in every later world at its start speed again. **Every new world resets the speed**
(`WorldManager.Advance`: portal, planetfall, the loop lift-off and the loop portal) to exactly what a
fresh run starts at (`WorldManager.RunStartSpeed`: the selected ship and colour's START SPD), a limit-break
boost in progress included; the ramp then climbs again at the new world's rate. Loops no longer arrive
faster (`LoopRules.ArrivalSpeed` is not used any more): their difficulty is the ramp rate, density and
enemies. Score, distance, hearts and charge carry over; nothing resets mid-world or mid-boss
(`WorldSpeedResetTest`). With the cap at 35 nobody can out-ramp a fast start: time to the boss by start
speed *(measured, `SpeedCapTest` "TABLE time to the boss")*, seconds old / new:

| World (distance old / new) | 0 | 5 | 10 | 15 | 20 | 25 | 30 |
|---|---|---|---|---|---|---|---|
| Space (22.1 / 22.1) | 120/120 | 105/105 | 93/93 | 83/83 | 75/75 | 69/69 | 65/65 |
| Frost (22.9 / 23.0) | 120/120 | 106/106 | 94/94 | 84/84 | 77/77 | 71/71 | 67/68 |
| Verdant (23.7 / 23.8) | 120/120 | 107/107 | 95/95 | 86/86 | 78/79 | 73/73 | 68/70 |
| Ember (24.7 / 24.8) | 120/120 | 107/107 | 96/97 | 87/88 | 80/81 | 75/76 | 70/73 |
| First pass, 4 levels of flight | 480/480 | 425/425 | 379/379 | 341/341 | 310/311 | 287/290 | 270/276 |

A start of 30 meets every boss 39-46% sooner than a stock start and flies a first pass in 276 s
instead of 480 (boss fights not counted). The cap costs the fastest starts up to 3 s a level against
the old curve (they used to keep climbing past 35 late in a level); nobody can make the time up by
out-ramping them any more.

Starting at or near 35 (tested with a start override of 35): the calm arrival is skipped (HUD >= 10, as
before), the first enemy is primed, the boss warning's lead (30 s) is shorter than the shortest possible
level (about 63 s at a flat 35), density starts at its HUD-35 values.

### Things that were keyed to speeds above 35

| What | Was | Now |
|---|---|---|
| `ScoreRules.SpeedTierHud` | 20 / 30 / 40 / 46 -> x1.25 / 1.5 / 2 / 2.5 | 20 / 30 / 35 -> x1.25 / 1.5 / 2; **limit break** (HUD above the cap) -> `LimitBreakMultiplier` x2.5 |
| `EnemyDensity` rate / ceiling / pilot load | sampled to 46, flat above `HighHud` 35 | unchanged: 35 is now the top of the natural range |
| Loop cap bonus, KEEP FLYING speed | +1 / +2, +4, never past 50 | removed |
| `achievement_speedster` (now `speed_speedster`) | never unlocked by code | **repurposed**: unlocked on the first limit break (speed above 35 with the boost shield); tracked by `SpeedMilestones` -> `AchievementTracker` |
| Top Speed leaderboard, best speed | submitted, shown | removed (section 2) |

Scoring potential: x2 used to need HUD 40 (a later world's cap or a fast ship) and x2.5 needed 46 (loops
only). Now x2 is the sustained final speed, and x2.5 is the few seconds of a limit break. A stock ship
reaches 35 only late in Verdant / Ember or on a loop; a fast ship holds x2 for most of every level. That
is the intended reward for start speed. The chain x speed cap (`MaxTotalMultiplier` 8) is unchanged.

### Loops: another axis than speed

Loops can no longer add speed. Kept from before (`LoopRules`, capped at `MaxScaledLoops` 3): arrival
speed 4 / 8 / 12, ramp x1.1 / 1.2 / 1.3 (35 arrives sooner), enemy phases x1.15 / 1.30 / 1.45, spawn
density x1.1 / 1.2 / 1.3, boss cooldowns x0.9 / 0.8 / 0.7 and one more pattern from the start, elites
more often, boss and world bonuses x1.5 / 2 / 2.5 / 3.

New per loop (all tunables in `LoopRules`):

| Axis | Tunable | Loop 2 / 3 / 4+ | Where it lands |
|---|---|---|---|
| Pilot load | `PilotLoadPerLoop` .5 | +0.5 / +1 / +1.5 | `EnemyDensity.MaxPilotLoad` |
| Threat ceiling | `ThreatsPerLoop` 1 | +1 / +2 / +3 bodies (x view scale) | `EnemyDensity.MaxThreats` |
| Fighter tiers | `TierShiftPerLoop` 1, cap 2 | tiers shift up 1 / 2 / 2 | `enmiesOnBoard.ChooseExtraDef` |
| Shot budget | `ShotsPerLoop` 2 | 14 / 16 / 18 roster shots alive | `EnemyThreat.ShotBudget` |
| Shot cadence | `VolleyGapPerLoop` .1 | gap x0.9 / 0.8 / 0.7 | `EnemyThreat.Gap` |
| Score | `ScorePerLoop` .15, cap at loop 5 | flight and kill points x1.15 / 1.30 / 1.45 (x1.6 at most) | `RunScore` (applied after the chain x speed cap) |

## 2. "Top speed" is gone

Removed: the death panel's "SPEED n / BEST n" line, the saved best speed (`HighestSpeed` is no longer
read or written by the game), the Top Speed leaderboard (table row, submission, the legacy
`leaderboard_highest_speed_reached` call, `LeaderboardRunStats.topSpeed`), `score.topSpeed` and
`dustPerSecondAtTopSpeed`. The live SPEED read-out in the gameplay HUD stays: it is the current speed.

* **Dust.** The trickle was `0.05 x min(1, speed / 0.6)` dust per second. Speed never reached 0.6, so it
  was always proportional to speed: 1/12 dust per unit of speed-seconds. It is now exactly that, named
  for what it is: `ScoreRules.DustPerDistance` = 1/12 per unit of distance flown. Same income at every
  speed (`ScoringTest` checks the two formulas agree from HUD 0 to 45); a limit break pays for the
  extra distance as it did before. Per level it is unchanged because a level is a distance: 1.84 / 1.91 / 1.98 / 2.06 dust for Space / Frost / Verdant / Ember, old and new alike (`SpeedCapTest`
  "flight dust"), about
  7.8 for a first pass, whatever the ship. What is lower is the dust per *minute* late in a long level
  for a stock ship (it now holds 35 where it used to creep to 38-44: up to 20% less trickle for those
  seconds), and nothing is paid while a portal is kept waiting past its grace (anti-farm). A faster
  ship earns the same per level but more levels per run.
* **Reward scale.** Since the dust-economy change the trickle is paid at `ScoreRules.DustRewardScale` (0.60) of the per-level figures above (see docs/economy.md).
* **Saves.** `ProgressSnapshot.highestSpeed` stays in the cloud-save format as a legacy field: it still
  round-trips and merges (max), so an older build on another device keeps its value and old saves load.
  The game no longer uses it.
* **Stores** (cannot be done from code; listed in `docs/leaderboards.md`): archive / stop showing the
  "Highest Speed Reached" leaderboard in Play Console and App Store Connect; re-word the Speedster
  achievement.

## Where a run starts

`WorldManager.RunStartWorld` picks the world a run begins on, first match wins (the log line
`[WorldManager] run start: ... source ...` names which):

1. **replay pin** (`replay pin`): REPLAY restarts the world the run began in (`RunLoop.StartWorld`).
2. **developer pick** (`developer pick`): developer mode on and a start world picked in the developer panel.
3. **player start world** (`player start world`): the player's START WORLD choice in Options
   (`PlayerStartWorld`, PlayerPrefs `playerStartWorld`). Ignored while developer mode is on.
4. **furthest planet** (`furthest planet`): a later run starts on the furthest planet reached
   (`highestWorld`), the default. With `startAtHighestUnlocked` off every run starts in Space (`journey (Space)`).

START WORLD (Options, `StartWorldOptions`): the row is a dimmed, untappable "START WORLD LOCKED" (hint "BEAT A
WORLD'S BOSS TO UNLOCK") until the player has finished a level, i.e. `highestWorld >= 1` (a world's portal or
planetfall only opens after its boss). After that it is `<  START <WORLD>  >`, cycling (wrapping) through every
world with index <= `highestWorld`; the name also steps forward. Picking the furthest world clears the key, so
the setting then follows new unlocks; picking an earlier world saves it. A saved value above `highestWorld`
(progress restore, cloud sync) is clamped on read. Starting early never lowers `highestWorld`: `CurrentIndex`
only raises it. The choice is part of the cloud snapshot (`ProgressSnapshot.startWorld` = world + 1, 0 = never
chosen; merged from the newer side like `spawnShip`). The row sits above LeaderBoard (one slot higher, y -33, in builds
with developer mode, whose switch uses that slot) and is hidden while developer mode is on.

## 3. The portal stays open

### State machine

`WorldManager.Stage` (replaces `FinalRoute`: `None / Choosing / KeepFlying / LoopBack / Encore`):

```
Level --distance flown--> Boss --encounter over--> Portal --ship enters--> Level (next world,
                                                     |                      or the run's start world
                                                     +-- stays until entered, or the pilot dies     with loop + 1)
```

* **Level**: the level clock eats distance. The boss warning runs in its last 30 s.
* **Boss**: `BossEncounter` (unchanged).
* **Portal**: the portal is open and waiting. The level clock is stopped. `PortalPressure` runs on
  flight time. This is the same in every world. After the final world the portal leads back to the
  world the run started in, one loop on ("SPACE  LOOP 2"), wearing that world's colour.

Deleted: `FinalChoicePanel` (the KEEP FLYING / LOOP BACK prompt and its countdown), KEEP FLYING and
its endless escalation, the encore pass, `RunLoop.EncorePass` / `DifficultyIndex`, the missed portal's
retry lap (`OnPortalMissed`, `portalLifetime`, `LoopPortalRetrySeconds`), `LoopRules.Endless*`,
`AutoPickSeconds`, `MaxSpeed*`, `AbsoluteMaxSpeed`.

### How the portal stays reachable

It appears above the view, comes down at 1.6 u/s to a station half way up the view (`Portal.StationHeight`
.5) and **holds there**, drifting slowly sideways about a home 0.7-1.2 u off the centre line (a sine,
+/-0.4 u, 9 s period) and never leaving. `OpenPortalTest` flies ten minutes beside it: on station it
never leaves the view and stays inside the ship's reach (x within +/-2.4, y within the ship's clamp). It moves only on
flying frames. There is no lifetime and no "missed".

Its approach stays clear:

* For the grace period nothing changes from today: no pilot is admitted and live pilots climb out
  (`PilotAirspace.MustClear`), elites do not lift off.
* After the grace, pilots are admitted again as part of the pressure, but never into the portal's
  column (`PilotAirspace` treats the portal's drift band plus a margin as reserved, outside the
  reserved-share accounting), and chasers orbit, they do not park. Board-riding hazards scroll through
  and are gone in about a second. So nothing can sit on the portal.
* Elites stay out for the whole wait (they pay dust and take seconds to lift off).

### Pressure (`PortalPressure`, every number a tunable)

`Seconds` = flight seconds since the portal opened (paused time does not count).
`Level` = `max(0, Seconds - GraceSeconds) / LevelSeconds`, continuous and unbounded.
Defaults: `GraceSeconds` 8, `LevelSeconds` 10.

| Dial | Formula | At 8 s | 30 s | 60 s | 120 s | 240 s | Bound |
|---|---|---|---|---|---|---|---|
| Level | | 0 | 2.2 | 5.2 | 11.2 | 23.2 | none |
| Spawn rate | x `GraceDensity` .6 in the grace, then x(1 + .35 Level) | x1 | x1.77 | x2.82 | x4.92 | x9.12 | none |
| Threat ceiling (bodies + half-shots, 10 u view) | +1.5 per Level | 10 | 13.3 | 17.8 | 24 | 24 | `BodyCap` 24 (x view scale, at most `BodyCapAbsolute` 36) |
| Pilot load | +.5 per Level | base | +1.1 | +2.6 | +4 | +4 | `PilotLoadBonusCap` 4 |
| Chasers | +1 per 2 Levels | base | +1 | +2 | +3 | +3 | `ChaserBonusCap` 3 |
| Roster shots alive | +2 per Level | 12 | 16 | 22 | 30 | 30 | `ShotCap` 30 |
| Volley gap | / (1 + .3 Level) | 0.40 s | 0.24 | 0.16 | 0.09 | 0.05 | none |
| **Overdrive** = Level past `OverdriveLevel` 6 | | 0 | 0 | 0 | 5.2 | 17.2 | none |
| Roster shot speed | x(1 + .08 Overdrive) | x1 | x1 | x1 | x1.42 | x2.38 | none |
| Chaser pursuit speed | x(1 + .10 Overdrive) | x1 | x1 | x1 | x1.52 | x2.72 | none |
| Guaranteed ship gap in a row | x max(0, 1 - .08 Overdrive) | x1 | x1 | x1 | x0.58 | 0 (from 193 s) | 0 |

The first ten seconds after the grace are gentle on purpose (Level 1 at 18 s is +35% spawns and one and
a half more bodies). Bodies are capped for 60 fps; when the caps are reached (about 70-100 s) the
**overdrive** dials take over and have no ceiling: shots and chasers get faster without limit and the
row guard that guarantees a ship-wide gap shrinks to nothing, so rows can close. Staying is eventually
fatal. The speed cap applies throughout (natural speed climbs from the boss's 20 toward 35 as usual).

### Anti-farm

After the grace the run **earns nothing until the portal is flown**: no points from distance, kills,
elites, absorbs, pickups or teleports, no kill chain (it neither starts nor extends), no death-combo
points, no star dust (trickle, kills, pickups), and star clusters stop being released. Boss and world
bonuses are not affected (the world bonus is paid on entering). During the grace everything pays as
usual. `PortalPressure.EarningsClosed` is the one switch; `SpeedCapTest` / `OpenPortalTest` hold it.

### What the player sees and hears

* "PORTAL OPEN" banner when it appears (as today).
* At the end of the grace: "ENTER THE PORTAL" banner; from then a HUD chip under the banner area,
  "PORTAL  DANGER n" (n = whole Level + 1), and an edge glow that grows with Level, in the destination
  world's portal colour shifting toward white-hot amber at high levels. Never the player's red
  (`HostileGlow.IsPlayerRed` is checked on every colour in the test).
* The portal itself pulses faster with Level (its core's existing pulse).
* Sound: `PortalPressure.Beat` (Open, GraceOver, LevelUp, Entered) drives the boss warning's procedural
  ticks (`BossWarningAudio`), no audio files.
* The chip is its own component (`PortalPressureHud`), on its own overlay canvas, placed through
  `TopBand` (the band the score read-out, quick actions and boss chip share): centred on the band,
  `ChipTopOffset` under its top, never wider than it; the glows run down the band's ends, the rails'
  inner edges, never over rail art. `OpenPortalTest` checks six screens with and without a notch.
* Speed and the board roll: `BoardRoll.Advance` (the one board-scroll clock that rails, rail mines and
  walls share) is fed `moveBackGround.speed`, which is natural (capped) + the boost, so the cap and a
  limit break reach rails, mines and board alike. `RailsRollTest` now rolls at up to HUD 45.

Art / audio gaps: no dedicated portal-waiting art, no pressure meter art, no dedicated sound.

### Boss warning

`BossWarning.Read`: `None` while the portal is open (Stage Portal) and whenever this visit's boss is
done; `Ahead` only in Stage Level. The KEEP FLYING / LOOP BACK / encore / missed-portal cases are gone
with their states. A warning can never run while a portal waits.

### Runtime fix found by the tests

`enmiesOnBoard.RetryDeferred` did not look at the threat ceiling: a spawn deferred for lack of room
landed later whatever the board held. Under portal pressure (x5 spawn rate) the backlog landed all at
once, up to 38 bodies in view against a ceiling of 24 (past the absolute cap of 36). While a portal
waits, deferred spawns now wait for room like new ones; outside the wait the board is untouched (made
general it shifted other tuned suites, e.g. EliteEvasionTest's control). Measured with the real
spawner in the authored view, HUD 35 *(OpenPortalTest "BOARD")*: no portal peak 10-11 (ceiling 10);
30 s peak 16 (13.3); 120 s peak 24 (24); 600 s peak 25 (24). The ceiling is soft by a body or three
(it is checked as a spawn is placed, above the view); the absolute body cap is never reached.

## Max-speed streak: 2x score

*"If you maintain max speed for 15 seconds (35 speed) without losing a heart you start getting 2x score."*
`Core/Scoring/ScoreMultiplier.cs` holds every number; `ScoreMultiplierTest` pins the rule.

| Tunable (`ScoreMultiplier`) | Value | Meaning |
|---|---|---|
| `StreakSeconds` | 15 | seconds at the cap, no heart lost, before the multiplier starts |
| `Factor` | 2 | what score gained while it is on is multiplied by |
| `CapEpsilon` | .0005 | speed within this of `SpeedRamp.Cap` counts as at the cap |
| `CueShowAfterSeconds` | 2 | the HUD bar only appears once the streak is this old |
| `CueStepSeconds` | .5 | the bar fills in steps of this (pixel look) |
| `LossCueSeconds` | .6 | how long the "just lost" plate lingers |

* The streak counts running time only (`RunScore.Tick` is fed by `score.StepRunning`: not while the pause
  menu, the shield-expire shockwave, a boss intro / planetfall / lift-off freeze or the run entry hold the
  world; paused time neither counts nor breaks it).
* It ends, and the timer returns to 0, when a heart is lost (`collisionDetection`, the killing hit too;
  shielded / cloaked / invulnerable hits cost nothing and a heal does not restore it) or when speed drops
  below the cap (a limit-break boost above the cap still counts). A new run resets it. A world change does
  not touch it; only an actual speed change does. A boss holds speed at 20, so every boss fight ends it.
* The multiplier applies to score gained while it is on, through the single place `RunScore` adds score
  (`ScoreMultiplier.Gain`: flight, kills, elites, shield absorbs, dust, atoms, blinks, bosses, world clears,
  death combo). Earlier points are never revalued. It multiplies after `MaxTotalMultiplier` and the loop
  scale, so it can stack with the speed tier (x2 at the cap, x2.5 limit break): at the cap flight is worth
  4x, a x4-chain kill up to 16x.
* `RunScore.Total` is what leaderboards, the best score and score achievements read, so they see the
  doubled score automatically. `RunScore.Parts.secondsIn2x` records the time spent on x2 for a future
  achievement (none added).
* HUD (`ScoreX2Cue`, on the SPEED row): after 2 s at the cap a slim cyan bar fills along the bottom of the
  row over the 15 s; at 15 s a small teal "x2" plate pops in between the speed figure and the SPD badge;
  when it ends the plate dims to muted grey and sinks for 0.6 s. No sound, no red, no allocation; the bar
  yields to the resume slow-mo spool bar, which uses the same strip.

### Retuning note

A stock ship reaches 35 only 120-140 s into a world (see the ramp table), so on a 3-minute level x2 is on
for the last ~25-45 s before the boss, roughly +15-25% on a stock run; a fast-start ship holds the cap most of
the level and can approach +80-90% when it avoids hits. Rookie 2,500 stays right (a first world is mostly
before the streak). Ace 8,000 and Legend 30,000 are the ones to watch: if play-tests show fast-start
ships clearing Legend in one pass, raise Ace to ~10,000 and Legend to ~40,000. The star-dust score bonus
(`min(1.5, 0.02 sqrt(score))`, then x0.60) caps at 5,625 points, so it is already flat for the runs that
double; no change needed.

## Open questions

1. Grace 8 s / Level every 10 s: is the wait punished too early or too late? (Tunables.)
2. Should the earnings switch also stop the blue atom's own 2 dust and atoms' points? (It does today.)
3. `MaxBoost` .10 allows HUD 45 with two blue atoms in one shield. Lower it to .05 for a hard 40?
4. Loop score x1.15 per loop: enough to make loops "worth more" without speed?
5. The portal holds in view. If it should instead swing past repeatedly, `Portal` has the station
   numbers in one place.

## Progress

* [x] Study, this design
* [x] Stage 1: speed cap, curve, limit break (53496ef0)
* [x] Stage 2: top speed removed (panel, leaderboard, dust, achievement), docs/leaderboards.md
* [x] Stage 3: loop state machine, open portal, FinalChoicePanel deleted (53496ef0)
* [x] Stage 4: PortalPressure (escalation, anti-farm), HUD chip, audio hook (53496ef0)
* [x] Stage 5: per-loop axes, score tiers (53496ef0)
* [x] Stage 6: tests: SpeedCapTest, OpenPortalTest, existing suites updated (782af8ee and after)
* [x] Measured tables filled in
* [x] Merge integrate/oct05-full-master (BoardRoll, TopBand), AllTests.RunAll after it: 95 suites, only
  the two known base failures ([HOSTILE] Frost wrapper, [WB] Verdant/mid)
* [ ] Play it (not done: simulation and tests only)
