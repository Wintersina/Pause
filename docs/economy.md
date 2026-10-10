# Star dust economy

Star dust is a float (`StarDustLedger`, saved as `PlayerCurrecny`). Shop prices (ships 600-5,800, skins 300 / 750)
and saved balances are unchanged.

## The dial

`ScoreRules.DustRewardScale = 0.60` (one place, `Core/Scoring/ScoreRules.cs`). Every dust REWARD goes through
`ScoreRules.RewardDust(base)`; the star and blue-atom PICKUPS do not. There is no per-award rounding (dust is a
float, so a 0.12 kill crumb pays 0.072, never 0 and never a lost remainder); only the end-of-run score bonus is
tidied to 0.01 after scaling.

| Source | Base (master) | Now | Path |
|---|---|---|---|
| Small star pickup | 0.5 | 0.5 (unchanged) | `collisionDetection` -> `score.AwardStarDust` |
| Large star pickup | 1 | 1 (unchanged) | same |
| Blue (shield) atom pickup | 2 | 2 (unchanged) | same; a collectible the pilot flies into |
| Flight trickle | 1/12 per unit distance | x0.60 (0.05/unit) | `ScoreRules.FlightDust` |
| Any kill (weapons, ultimate, powers, ram, teleport kill, death combo chain) | 0.12 | 0.072 | `AwardDestroyedTarget` -> `score.AwardRewardDust` |
| Elite down | 15 | 9 | `EliteRewards.Pay` -> `score.AwardRewardDust` |
| End-of-run score bonus | min(1.5, 0.02 sqrt(score)) | x0.60, cap 0.90 | `ScoreRules.ScoreDustBonus` |
| Bosses, worlds, loops, achievements, daily, leaderboards | no dust | - | - |

Tutorial practice dust uses the same paths (so the Tutorial Complete card shows the reduced reward numbers);
it never touches the saved balance. Developer mode earns no bonus, as before.

## Effect

`DustEconomyTest` simulates seeded runs: pickups identical to master, rewards 0.60x. Rewards are only a part of a
typical run (the stars are most of it), so the combined income drops by less than 40% (see the test log lines
`[DUSTECON]`). Prices are untouched, so ships and skins take proportionally more runs to afford.

## Max-speed streak (2x score)

Holding the speed cap for 15 s without losing a heart doubles the score gained from then on
(`ScoreMultiplier`, see `docs/speed-and-loops.md`). Star dust is not multiplied: the dust rewards and the
flight trickle are unchanged. The end-of-run score bonus reads `RunScore.Total`, so a doubled score raises
it, but it is `min(1.5, 0.02 sqrt(score))` x `DustRewardScale` and caps at 5,625 points (0.90 dust), so
doubling a 3,000-point run moves it from 0.66 to 0.78 dust at most. No retune needed.
