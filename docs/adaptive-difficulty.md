# Adaptive difficulty (spawn density follows the pilot)

Ordinary hazards and pickups spawn a little more often while the pilot is doing well (so the kill chain has targets
and the star dust keeps coming) and a little less often while they struggle (so they can recover inside the level).
Only HOW OFTEN things spawn moves, inside fixed bounds. Never touched: enemy hit points, damage, attack patterns,
hitboxes, world speed, elites, bosses, the 8 s calm start, the zoom-out, per-kill score and dust.

Code: `Gameplay/Spawning/PerformanceMeter.cs` (pure C#), `AdaptiveDifficulty.cs` (bounds, gates, log),
`AdaptiveFlowOverlay.cs` (developer readout). Test: `AdaptiveDifficultyTest` (`[ADAPT]`).

## The meter

`flow` in [-1, +1], 0 neutral, from the last 25 s of running game time:

| Input | Effect |
|---|---|
| kill chain (`RunScore.Chain`, 8 = full) | +0.45 |
| kills per second (0.5/s = full) | +0.25 |
| damage-free time (30 s = full), only once something was killed in the window | +0.30 |
| each heart lost in the window | -0.50 |
| down to the last heart | -0.30 |

The raw score is smoothed: low-pass (tau 6 s) and slew limit (+0.04/s up, -0.08/s down), so a rate never lurches.
Not wired: star dust catch rate (no "missed" signal exists), pauses used.

The meter stands still (time, window, events) during authored beats: boss, elite fight, planetfall, lift-off,
portal wait, world entry, death. The multipliers read exactly x1 then, so those beats are the same for everyone.
A world change halves the flow and forgets the window; a new run (and `OnRevive`, no revive mechanic exists yet)
resets it to 0. The calm start needs no special case: with no kills, flow stays 0.

## The multipliers (named constants in `AdaptiveDifficulty`)

| Target | Range | Hook |
|---|---|---|
| ordinary enemies, rocks, aliens, mines, chasers: spawn rate | x0.75 .. x1.25 | `enmiesOnBoard.Roll` (interval divided by it). Rails are excluded: they are rides |
| star dust cluster rate | x0.85 .. x1.35 | `spawnGoodStuff` star timers (together with the boss x3) |
| red (pause) and green (heal) atom timers, only when flow < 0 | up to x1.4 faster | `spawnGoodStuff`, `HealAtomSpawner`. Per-world budgets and caps unchanged: no new free hearts |
| star x position, only when flow > 0 | pulled up to 50% toward the ship | `spawnGoodStuff.StarX` |

Linear in flow on each side of 0. At flow 0 everything is exactly the old behaviour.

## Exploit and economy

Easy mode cannot beat playing well: ease is capped at x0.75 enemies, dust never below x0.85, per-kill score and dust
are unchanged, recovery pickups only speed up timers that already have hard per-world budgets. A farming player
loses the +35% dust and the chain feed. Dust per run at the extremes: the star pickups (most of a run, see
`docs/economy.md`) move to 0.85x / 1.35x; kill crumbs follow the kill count (about 0.75x .. 1.25x of an unchanged
0.072 each). Roughly 0.85x for a struggling run and 1.3x for a flawless one (estimate, not measured in play).

## Gates

`AdaptiveDifficulty.On` = `RunActive` (set at run start: a real-dust run, so not the tutorial or practice, and not
the premium Paragon ship). There is no switch: a real run always adapts.

## Debug

`[FLOW] flow=0.42 enemies x1.11 dust x1.15 recovery x1.00` whenever a multiplier moves more than 0.05.
Editor and `PAUSE_DEV` builds in developer mode show `FLOW +0.42 en x1.11 dust x1.15` above the DEV badge.
