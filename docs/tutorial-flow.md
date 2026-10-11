# Tutorial flow

The script is one table, `Pause/Assets/Scripts/Tutorial/TutorialScript.cs`
(`Steps`), played by `Hints`. Each step: a line the robot speaks, a player
action that completes it, a cue that sets the scene up, a `par` (seconds a
competent player needs) and a `timeout` (the most it may take). A step ends
once its line is spoken and read AND the player did the thing or the timeout
ran out, so the tutorial can never stall.

## Beat timeline

Goal: 30 to 45 seconds for a normal player from the first frame to the
Tutorial Complete card; every step timing out is still under 75 s
(`TutorialPolishTest` sums both from the table with a fake clock).

| # | step id | line | action | par s | timeout s |
|---|---------|------|--------|------:|----------:|
| - | (intro) | robot pops in | | 0.6 | |
| 1 | `hold` | Hold anywhere to fly. | fly 0.8 s | 2.0 | 6 |
| 2 | `freeze` | Let go. Time freezes! | stay frozen 0.4 s | 2.0 | 4 |
| 3 | `teleport` | Touch to teleport. Each costs a pause. | spend 1 pause | 3.0 | 6 |
| 4 | `dust` | Star dust! Grab it, that's your cash. | catch 1 piece (a stream is rushed in front of the ship, on it in ~1 s, an arrow over every piece) | 3.0 | 6 |
| 5 | `heal` | The green atom repairs a heart. | catch the green atom (the ship is dented first) | 3.5 | 7 |
| 6 | `shield` | The blue atom wraps you in a shield. | catch the blue atom | 3.5 | 6 |
| 7 | `refill` | The red atom refills your pauses. | catch the red atom | 3.5 | 6 |
| 8 | `power` | The violet atom charges your weapon. | catch the violet atom: the armed weapon goes off | 4.0 | 8 |
| 9 | `hearts` | Your hearts shield you from a crash. | read | 0 | 3 |
| 10 | `enemies` | An alien! Dodge it, or teleport onto it! | the alien crashes, is dodged or teleported onto | 4.5 | 7 |
| - | (ending) | card comes up | | 0.6 | |

Competent path is about 33 s; the all-timeouts worst case about 60 s.
Each line's speaking time is `TutorialScript.SpeakSeconds` (the robot's own
pacing) plus `Hints.readSeconds` (0.6 s).

## Atom order

Every atom type is introduced exactly once, by one scripted atom that drops
in as its line starts, with one sentence and the arrow indicator above it.
There are no other atoms in the tutorial: nothing respawns when one is
missed (it hovers on screen until caught; an uncaught one leaves with its
step), and the run's own pickup spawners (`spawnGoodStuff`,
`HealAtomSpawner`) are not in the scene.

1. **Green, repair** - hearts are what keep a run alive; the ship takes a dent
   first so the repair is seen working.
2. **Blue, shield** - the next thing that saves a run (a free hit).
3. **Red, pause** - what the freezing learned in step 2 and 3 costs.
4. **Violet, weapon charge** - last, as the reward: one atom fills the armed
   weapon and it fires.

## Hearts

The tutorial ship flies with `ShipLives.TutorialExtraHearts` (2) extra
hearts on its hull's own, orbiting it like in a run. The alien's crash costs
one heart; the tutorial can never end in a death (`collisionDetection.
TutorialCannotDie`), and every beat starts with all hearts back (`Hints`).
Real runs fly `ShipLives.Max` as always.

## Tutorial Complete and LIFT OFF

The card is `TutorialCompletePanel`: the Flight Complete panel's frame, title
slab, divider and button plates (`DeathPanelView`), the title TUTORIAL
COMPLETE and two buttons, LIFT OFF and HOME. LIFT OFF (`TutorialLiftOff`)
plays the world-entry portal backwards - the ship flies into a gateway and
shrinks away (`PortalArrival.SpawnDeparture`) - then loads `gameS1` directly,
where the run begins with the usual portal arrival (`WorldEntry.Plan`). It plays
once; HOME goes to the start menu.

## Ship size and finger offset

The tutorial's ship is drawn at `ShipScale.Main` like gameplay (it was left
out of `ShipScale`, so it was 26% smaller). Every touch controller puts the
ship `ShipReach.FingerOffset` (1.25 u) above the finger.
