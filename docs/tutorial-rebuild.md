# Tutorial rebuild

The tutorial is the first thing a new pilot plays, so it has to behave like the
real game. This is the audit of what it was, the new step table and the
assumptions made. (`docs/tutorial-flow.md` is the older design note; the step
table here replaces its timeline.)

## What was wrong (audit of the old flow)

| # | Problem | Where |
|---|---------|-------|
| 1 | Steps ended on a **timeout** even when the player did nothing (`TutorialStep.timeout`, `CanAdvance(... stepSeconds)`), so the tutorial "moved on without checking any user action". | `TutorialScript`, `Hints` |
| 2 | No fly-in: the ship just sat at its authored spot; the first line came after 0.6 s. | `Hints.Start` |
| 3 | The ship was whatever the pilot had equipped (`TutorialShipHull.Apply` used `ShipId.Equipped()`), with their skin, upgrades, colour hearts. | `spawnShips.cs` |
| 4 | The ship started undamaged and was dented only inside the green-atom step; hearts were topped back up at every beat, so the damage never lasted. | `Hints` |
| 5 | Dust and atoms spawned a metre above the ship ("rush" stream) or hovered at about 3.6 u/s: they did not arrive from the top of the level at speed. | `spawnGoodStuffTut`, drifts |
| 6 | No popups at all: the tutorial has no `ScoreHud`, and `RunScore` is off, so a star or atom showed no "+N", and the "+1 HULL / +1 PAUSE / SHIELD" words never appeared. | `ScoreHud`, `RunScore`, `collisionDetection.PickupWord` |
| 7 | The pause lesson was "touch to teleport" with nothing to teleport over. No wall, no reason to pause. | `TutorialScript` |
| 8 | **Replay restarted the main game**: the pause quick-action replay (and the scene's replay buttons) call `tutButtonClicks.replay`, which was LIFT OFF (`TutorialLiftOff.Begin`, then load `gameS1`). | `tutButtonClicks` |
| 9 | 50 pauses instead of a handful (a run has 5), so "pauses are limited" was never true. | `score.cs` |
| 10 | The first press blinked the ship to the finger (a pause-jump) instead of the run's launch glide; a press on a pause quick-action also steered the ship. | `movePlayerInTut` |
| 11 | The end needed a tap on a card; the pilot did not "lift off into the game". | `Hints.RunEnding` |

## New flow

1. **Fly-in.** The run-start portal arrival (`PortalArrival.SpawnDriven`, the
   same gateway, timeline and hand-off as a run's entry in Space) plays on the
   tutorial ship. It is held and shielded while it plays (the hooks every run
   already honours: `HoldsShip`, `ShieldsShip`, `FreePress`). The first line
   comes `Hints.introDelay` after it ends.
2. **Neon Comet, always.** `ShipId.TutorialFlight` is set when the tutorial is
   prepared (`startMenu.PrepareTutorialRun`) and when the scene loads, and
   cleared by `GameStateReset` on every other scene. While it is set:
   `ShipId.Equipped()` answers Neon Comet (`ShipId.TutorialShip`, id 1, the free
   starter), `ShipSkins.Equipped` answers Stock and `ShipWeaponUpgrades.Level`
   answers 0. Nothing is read from or written to the saved selection, skins,
   ownership or run stats, so there is nothing to restore.
3. **Two hearts, starting at one.** `ShipLives.TutorialHearts = 2`
   (`ShipLives.TutorialMax`) is the tutorial's maximum, a tutorial-only override;
   the real Neon Comet keeps its 1 heart (`ShipLives.Max`). `Hints.ApplyDamagedStart`
   sets `lifeCounter = 1` as soon as the ship's own Start has run, so the hull
   shows its critical damage art and the heart ring shows 1 of 2. Nothing can
   mend it except the green atom (`Hints.KeepHeartsRight`); the heal puts it
   back to 2 of 2. The tutorial can never end in a death
   (`collisionDetection.TutorialCannotDie`).
4. **Arrivals from the top.** Dust, atoms and the wall are created above the
   top edge of the view and rush down at 7.5 to 8 u/s toward the ship's lane
   (`TutorialStarDrift.Speed`, `TutorialAtomDrift.FloorSpeed`,
   `TutorialWall.Speed`) with the real prefabs and the real pickup path. They
   are only made by the step that needs them.
5. **Popups.** `HudStyler` attaches `ScoreHud` in popups-only mode in the
   tutorial; `RunScore.PracticePopups` raises the same "+N" popup (value shown
   at the pickup, rising above it) for dust and atoms, and `PickupWord` shows
   the "+1 HULL", "+1 PAUSE", "SHIELD", "WEAPON CHARGED" words above the ship.
   Same pool, look, motion and clock as a run. Nothing is scored or saved.
6. **The wall.** `TutorialWall`: one row of real roster aliens
   (`EnemyFactory`, brain off) across the whole lane with every gap narrower
   than half the ship. `TutorialWall.HasDodgeGap` is the solver and
   `PlanRow` the layout; tests run both on every screen-fit device. The only
   way over is the pause (see steps 4 and 5 below).
7. **Pause menu and replay.** The pause quick actions work in the tutorial
   (`PauseQuickActions` is bound to `tutButtonClicks`): REPLAY restarts the
   tutorial from the first step (`tutButtonClicks.PrepareRestart` + reload of
   `tutorialS5`), HOME goes to the start menu. LIFT OFF is a separate
   `liftOff()`; the Tutorial Complete card's LIFT OFF is bound to it.
8. **Lift off.** After the last step the robot says the closing line, then
   `TutorialLiftOff` flies the ship into a portal and loads `gameS1`, where the
   run begins as any run after the tutorial does (portal arrival, the player's
   own ship, a fresh run). `HasDoneTut` and `AchievementTracker.OnTutorialDone`
   are set exactly as before (at the start of the ending). `Hints.AutoLiftOff`
   = false brings back the Tutorial Complete card first.

## Step table

Every step ends only when the **line has been spoken and read** AND the
**player's real action** is done. There is no timeout and no timer advances a
step (`TutorialScript.CanAdvance` has no clock input). `par` is how long a
competent player needs (pacing test only).

| # | id | line | completes when the player... | what arrives | par s |
|---|----|------|------------------------------|--------------|------:|
| - | (fly-in) | ship flies in through the portal | (nothing: scripted, ~2.6 s) | | |
| 1 | `hold` | Hold anywhere to fly around. | steers the ship 2.5 u with a finger down (`FlyDistance`) | touch pulse while frozen | 3.0 |
| 2 | `dust` | Star dust! Grab it, that's your cash. | collects 3 pieces (`CollectStar`) | 6 pieces rush from the top, arrows; missed ones are rushed again | 4.0 |
| 3 | `heal` | The green atom repairs a heart. | the green atom repairs the missing heart (`HealHeart`: atom caught AND hearts up) | green atom from the top | 4.0 |
| 4 | `freeze` | A wall of aliens! Let go to pause time. | lifts the finger for 0.5 s with the wall in view (`WallFrozen`) | the wall rushes in and stops above the ship | 4.0 |
| 5 | `jump` | Touch above the wall to jump over it. | pause-jumps from below the wall to clear above it (`CrossWall`) | the wall waits | 4.0 |
| 6 | `strike` | An alien! Let go, then touch on it to blast it. | pause-jumps onto the alien and erases it (`TeleportKill`: `TeleportFx.Kills` goes up) | one `space_alien` slides in from the top (real time, so also while frozen) and hangs in the ship's reach, arrow on it; dodged or rammed, it comes back after 1 s | 5.0 |
| 7 | `refill` | The red atom refills your pauses. | catches the red atom | red atom | 4.0 |
| 8 | `shield` | The blue atom wraps you in a shield. | catches the blue atom | blue atom | 4.0 |
| 9 | `power` | The violet atom charges your weapon. | the armed weapon goes off (`FirePower`) | violet atom | 4.5 |
| - | (ending) | Nice flying! Lift off! | (nothing) | ship lifts off into the game | |

Rules every step shares:

* **Idle re-prompt.** No progress for `Hints.IdleHintSeconds` (7 s) while the
  world is running (or on the two pause steps, which want a lifted finger):
  the robot says the line again and the touch pulse shows.
* **Finger up pauses the tutorial.** The world freezes exactly as in a run
  (`moveBackGround`), every tutorial mover (dust, atoms, wall) only moves
  while the world moves, popups hold still, the idle clock stops (except on
  the pause steps), the line and the touch pulse stay up.
* **Pauses.** The scene starts with 3 (`score.TUTPAUSECOUNTER`, a run has 5)
  and every step starts with at least 2 (1 for the red-atom step), so letting go
  always freezes the tutorial.
* **Wall broken instead of jumped** (rammed, blinked onto, dragged through):
  the row is cleared, comes back after 1 s with a retry line, hearts are mended
  (once the heal is done). The step only completes on a real jump
  (`TutorialWall.Crossed`).
* **No broken states.** SKIP (the existing button) still finishes the
  tutorial and goes to the game; it is the only way past a step.

## The wall in numbers

Aliens are the roster's `space_alien`; the row is laid by
`TutorialWall.PlanRow(reach, pieceWidth, shipWidth)`: the first alien's outer
edge on `-ShipReach.HalfWidth`, the last on `+HalfWidth`, centres evenly spaced
so the widest gap is `GapShare` (45 %) of the ship's width. The solver
`HasDodgeGap` says a ship fits if there is room beside an end alien or between
two. A jump counts as "over" when it starts below the wall and lands more than
`CrossMargin` above it (clear of the aliens, the blink's blast radius and the
hull), so it never erases the wall. It stops at `HoldFraction` (62 %) of the
view's height, and does not come in until the ship is at least 1 u below that
line.

## Parity audit (tutorial vs the main game)

Fixed in this change: pause counter (50 -> 3), no popups, no pickup words,
replay target, launch glide on the first press, a press on a pause quick-action
steering the ship, ship identity (equipped ship / skin / upgrades / hearts),
damaged start, fly-in, end-of-tutorial hand-off, pickups arriving at hover
speed instead of from the top, the Tutorial Complete LIFT OFF being wired to the
same call as REPLAY.

Already at parity (covered by `TutorialParityTest`, `TutorialRobotTest`,
`ScreenFitTest`): HUD styling and safe areas, ship scale (`ShipScale`), finger
offset (`ShipReach.FingerOffset`), Space backdrop and rails, quick-action
placement, the ship's thruster / hearts / damage fx (`ShipThruster`,
`ShipLivesIndicator`, `lifeControler` run in `tutorialS5`).

Known remaining differences (deliberate, listed so nobody is surprised):
no READY / GO countdown (the fly-in replaces it), no teleport cooldown when out
of pauses (the tutorial keeps pauses up), no world banner / codex toasts /
boss and elite directors (the tutorial has no `WorldManager`), the star dust
earned is practice dust (`score.tutorialCurrency`, never saved).

## Rewards and persistence

Unchanged rules: `score.PaysRealDust` is false in `tutorialS5`
(`score.paysRealDust`), `RunScore` does not score, `StarDustLedger` banks
nothing, no leaderboard or best score is touched. The only persistent writes are
the ones the tutorial always made: `HasDoneTut = true` and
`AchievementTracker.OnTutorialDone()` at the end (and on SKIP). The green atom,
shield and the rest give no dust beyond practice dust.

## Assumptions

* "haul" = the "+N" popup and the "+1 HULL / +1 PAUSE / SHIELD" pickup words
  the main game shows when collecting.
* "green atom" = the heal atom (`HealAtom`); "the pause" = the finger-lift
  freeze plus the pause-jump (a touch somewhere else while frozen: the ship
  blinks there without crossing what is between, `TeleportFx`).
* "the replay in the tutorial" = the REPLAY quick action of the pause menu (and
  the scene's replay buttons); LIFT OFF is no longer called "replay".
* "Neon Comet" = roster id 1 (`shopingShips.Roster[1]`, key `NeonComet`), the
  free starter; it normally has 1 heart, the tutorial gives it 2.
* "scores above the items" = the "+N" appears over each star where it is
  caught (the main game's rule), rising from it; items do not wear permanent
  numbers while they fall.
* The shield and violet-atom steps from the old tutorial are kept after the
  pause lesson (the brief did not drop them); the old "freeze", "teleport",
  "hearts" steps are folded into the wall lesson; "enemies" is back as the
  `strike` step (pause-jump onto an alien to kill it), reusing `TutorialEnemy`.
* Lift off is automatic (the brief: "the player lifts off into the game");
  `Hints.AutoLiftOff` restores the card.

## Art needs

None new: the wall uses roster aliens, the hints reuse the touch pulse and
arrows, the pickups use the real prefabs. Nothing for Codex.
