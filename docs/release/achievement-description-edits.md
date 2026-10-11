# Achievement description edits (hand edits in the consoles)

The 60 achievements were imported as drafts before the score thresholds were raised for the 2x-score streak
(see `docs/speed-and-loops.md`, "Max-speed streak"). Names, ids, points and icons are unchanged.
Only these descriptions differ from what was imported. Google Play's CSV import forbids commas, so the
text has no thousands separator in the stores (the game itself shows "Score 5,000 points in a run.").

## Google Play Console > Play Games Services > Achievements (3 rows)

| Achievement name | Internal id | Old description (as exported last) | New description |
|---|---|---|---|
| Rookie Score | score_10k | Score 2,500 points in a run. | Score 5000 points in a run. |
| Ace Score | score_50k | Score 8,000 points in a run. | Score 20000 points in a run. |
| Legend Score | score_150k | Score 30,000 points in a run. | Score 80000 points in a run. |

If the draft still carries the very first text from `achievements-store-setup.md`
("Score 10,000 / 50,000 / 150,000 points in a run."), replace it with the same new text.

## Game Center (App Store Connect > Achievements, 3 rows; pre-earned and earned description both)

| Title | Reference name | Old | New |
|---|---|---|---|
| Rookie Score | Pause score_10k | Score 2,500 points in a run. | Score 5000 points in a run. |
| Ace Score | Pause score_50k | Score 8,000 points in a run. | Score 20000 points in a run. |
| Legend Score | Pause score_150k | Score 30,000 points in a run. | Score 80000 points in a run. |

## Optional: comma-free text on five other rows

The exporter now strips commas from every store description. The previous CSV quoted them
("Destroy 1,000 enemies."). If the import accepted or mangled those, make them comma-free too; if they
imported fine, leave them.

| Achievement name | New description |
|---|---|
| Hundredfold | Destroy 1000 enemies. |
| Warlord | Destroy 5000 enemies. |
| Dust Hoarder | Collect 1000 star dust pickups. |
| Dust Baron | Collect 5000 star dust pickups. |
| Big Spender | Spend 10000 star dust in the shop. |

Source of truth: `docs/achievements-export/play-console.csv` and `game-center.csv` (regenerated).
