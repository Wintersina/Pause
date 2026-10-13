# Codex unlock rewards, achievement rewards, notification bubbles

## 1. Codex unlock reward (5 star dust, collected by the player)

* Discovering a Codex entry (`Codex.Discover`) earns `Codex.UnlockReward` = **5 star dust**. It is never auto-paid:
  the player collects it in the Codex. The Pilot's Log (free from the start), and the MUSIC / BACKDROPS ids that
  share the `codexSeen` list, pay nothing. Ships and worlds that are "discovered" only by ownership / progress (not
  in `codexSeen`) pay nothing either.
* Save: one new PlayerPrefs id list, `codexPaid` (`Codex.PaidKey`): the entries whose reward was collected.
  **Pending = in `codexSeen` and not in `codexPaid`**, so the pending state derives from the save (no transient flag)
  and survives restarts.
* Collect: `Codex.CollectReward(id)`, `CollectRewards(category)`, `CollectAllRewards()`. They add the ids to
  `codexPaid`, `StarDustLedger.Grant(5 x n)` and `PrefsSaver.SaveNow()` once (both writes are in PlayerPrefs
  before the single save, same pattern as achievements), so a second call finds nothing pending: idempotent and
  crash-safe. Developer mode shows no rewards and cannot collect (its discoveries are not real progress).
* UI: an entry's detail view shows a yellow **COLLECT +5** chip (right of the category pill) while its reward is
  pending; a category tab with pending rewards shows a yellow **COLLECT ALL +N** chip at the right end of the
  header. The header counter flashes "+N STAR DUST" for ~2 s (the existing menu dust feedback; there is no
  coin/beep sound, per the project sound rules, and the achievement claim has none either).
* **Existing saves**: no migration is needed or run. An old save has no `codexPaid`, so everything it already
  discovered is pending: each old entry is claimable once (nothing is paid automatically). Chosen deliberately so
  existing players get the rewards; balance impact below.
* Cloud save: `codexPaid` is a new additive list in `ProgressSnapshot` (no schema bump), merged by union
  (`ProgressMerge.MergePaid`). `ProgressSnapshot.Apply` unions the incoming list with the local one, so a restore of
  an older blob can never un-collect a reward (no double payment).

## 2. Achievement rewards: 5 or 25

Reward = `AchievementDef.Reward` = `AchievementCatalog.RewardFor(tier)`: **25 for Gold and Platinum badges,
5 for Bronze and Silver**. Criteria for 25: long grinds (5,000 kills / 5,000 pickups), high loops and scores
(loop 1, 2, 5; 80,000 points; Super Sonic), boss and elite feats (Ember / Tide boss, a no-hit boss, every boss,
50 elites, every Ember elite), collect-everything (every ship, every Codex entry, a full skin set), and the hidden
Perfect Pause. Everything else is a routine milestone (5). Rewards stay claimable, never auto-granted (unchanged).
Achievements already claimed at the old flat 25 stay claimed: no refund, no charge (the claimed flag is untouched).

25 star dust (17): `loop_1` `loop_2` `loop_5` `boss_ember` `boss_tide` `boss_no_hit` `boss_all` `elite_50`
`elite_ember_all` `kills_5000` `codex_complete` `ship_all` `skin_full_set` `stars_5000` `score_150k`
`speed_super_sonic` `pause_perfect_dodge`.

5 star dust (43): every other achievement (all Bronze and Silver badges, e.g. `meta_*`, `world_*_reached`,
`boss_space/frost/verdant`, `elite_first/10`, `elite_space/frost/verdant_all`, `elite_blink`, `kills_100/1000`,
`rocks_500`, `mines_25`, `chain_10`, `mega_domino`, `codex_10/50`, `codex_field_guide`, `ship_first/half`,
`skin_first/special`, `secret_power_first`, `stars_150/1000`, `dust_spent_10000`, `score_10k/50k`, `speed_flash/speedster`,
`deaths_10/100`, all `pause_*` except `pause_perfect_dodge`).

Balance: the full achievement payout drops from 60 x 25 = **1,500** to 43 x 5 + 17 x 25 = **640** star dust
(about 60% less) for a new player; players who already claimed keep what they got. The Codex unlock rewards add
5 per discovered entry (about 5 x the ~100 entries, ~500) on top, so lifetime free dust from collecting
(~1,100) is below the old achievements-only 1,500, but front-loaded: every discovery pays, only the hard
feats pay big. Old saves get their already-discovered entries as a one-time claimable lump.

## 3. Notification bubbles (yellow, persistent)

Yellow pixel disc (`CodexHomeButton.BubbleYellow`, ink outline, one shadow row, no line boxes). The state is
read from the save every time, so viewing a tab or an entry never clears it; only collecting does.

* Home CODEX button: count of every uncollected reward, `AchievementStore.ClaimableCount + Codex.PendingTotal`
  ("9+" above 9).
* Codex category tabs: dot while `Codex.PendingIn(category) > 0`. ACHIEVEMENTS tab: dot while any achievement is
  claimable. MUSIC never. Each tab clears independently when its own rewards are collected.
* Entry cards: dot while that entry's reward is pending.
* Dots sit inside their card / tab with a margin, off the label, 13 px art snapped (ScreenFit conventions); the
  chips are checked on the ScreenFit device set by `CodexNewDotsTest`.

Tests: `CodexNewDotsTest` (rewards, migration, bubbles, persistence, per-tab independence, cloud merge, screen fit),
`AchievementCatalogTest` (the 5 / 25 table), `AchievementClaimTest`, `CodexAchievementsTabTest`.
