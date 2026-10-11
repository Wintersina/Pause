# Achievements redesign

Replaces the 28 store achievements (see `achievements-audit.md`) with a 60-achievement set built around the game as it is
now: four live worlds plus dormant Tide, bosses, elites, the Codex, ships and skins, star dust, and the pause mechanic.
Every achievement pays a flat **25 star dust**, collected by the player in a new Codex ACHIEVEMENTS tab.

## 1. Rules

* 60 achievements: 58 live, 2 dormant (Tide: `world_tide_reached`, `boss_tide`). Dormant ones are fully defined and exist in the
  store config but are hidden from the Codex list, excluded from "x/N", not trackable and not claimable until `WorldManager.TideEnabled` flips
  (catalog field `requiresTide`). Their points still count in the 1000 (Play Games needs the whole set defined; they appear as "hidden" in the store).
* Google Play points: 60 achievements, total exactly **1000**, each a multiple of 5 in 5..180 (max here 40). Game Center: same numbers (max 100 each, 1000 total).
* Tier (display only, by difficulty): Bronze (easy), Silver, Gold, Platinum (hardest). Drives the badge frame colour.
* Group: World, Boss, Elite, Enemy, Codex, Collection, Dust, Skill, Pause, Meta (10 groups -> Codex filter chips).
* Hidden: 3 (Play Games "hidden" until revealed; in the Codex shown as "???" with a `?` badge until earned).
* Progress: achievements with a target are counters (`target > 0`); store incremental steps = target; others are one-shot.
* Tracking never runs in the tutorial, in developer mode (`DeveloperUnlocks.Enabled`), or while `score.paysRealDust` is false, matching `Codex.Discover`.
* Reward: 25 star dust each (1,500 total across 60), claimed per achievement or with COLLECT ALL.

## 2. The set

Columns: id (stable snake_case, also the file name of the art), title (max 24), description (max 100, store text), points, tier, group, target, hidden, rarity guess.

| # | id | Title | Description | Pts | Tier | Group | Target | Hidden | Rarity |
|--:|---|---|---|--:|---|---|--:|:-:|---|
| 1 | `meta_first_flight` | First Flight | Finish the tutorial and take your first real run. | 5 | Bronze | Meta | - |  | Common |
| 2 | `meta_logged_on` | Logged On | Sign in to Google Play Games or Game Center. | 5 | Bronze | Meta | - |  | Common |
| 3 | `world_frost_reached` | Cold Front | Reach the Frost world. | 10 | Bronze | World | - |  | Common |
| 4 | `world_verdant_reached` | Into the Green | Reach the Verdant world. | 10 | Silver | World | - |  | Common |
| 5 | `world_ember_reached` | Trial by Fire | Reach the Ember world. | 15 | Silver | World | - |  | Uncommon |
| 6 | `world_tide_reached` | Deep Water | Reach the Tide world. | 20 | Silver | World | - |  | Uncommon (dormant) |
| 7 | `loop_1` | Full Circle | Complete a full loop of every world. | 25 | Gold | World | - |  | Rare |
| 8 | `loop_2` | Second Lap | Complete two loops in a single run. | 25 | Gold | World | - |  | Rare |
| 9 | `loop_5` | Orbit Lifer | Complete five loops in a single run. | 40 | Platinum | World | - |  | Epic |
| 10 | `boss_space` | Archon Down | Defeat the Void Archon. | 10 | Silver | Boss | - |  | Uncommon |
| 11 | `boss_frost` | Leviathan Slain | Defeat the Hoarfrost Leviathan. | 15 | Silver | Boss | - |  | Uncommon |
| 12 | `boss_verdant` | Bloom Queen Felled | Defeat the Bloom Queen. | 20 | Silver | Boss | - |  | Rare |
| 13 | `boss_ember` | Drake Slayer | Defeat the Cinder Drake. | 25 | Gold | Boss | - |  | Rare |
| 14 | `boss_tide` | Tide Turner | Defeat the Tide boss. | 25 | Gold | Boss | - |  | Rare (dormant) |
| 15 | `boss_no_hit` | Untouchable | Defeat a boss without losing a heart. | 25 | Gold | Boss | - |  | Epic |
| 16 | `boss_all` | Boss Collector | Defeat every live world's boss. | 35 | Platinum | Boss | 4 |  | Epic |
| 17 | `elite_first` | Elite Hunter | Destroy your first elite ship. | 10 | Bronze | Elite | - |  | Common |
| 18 | `elite_10` | Veteran Hunter | Destroy 10 elite ships. | 10 | Silver | Elite | 10 |  | Uncommon |
| 19 | `elite_50` | Elite Exterminator | Destroy 50 elite ships. | 25 | Gold | Elite | 50 |  | Rare |
| 20 | `elite_space_all` | Space Elites | Down every Space elite. | 15 | Silver | Elite | 4 |  | Uncommon |
| 21 | `elite_frost_all` | Frost Elites | Down every Frost elite. | 20 | Silver | Elite | 5 |  | Rare |
| 22 | `elite_verdant_all` | Verdant Elites | Down every Verdant elite. | 15 | Silver | Elite | 5 |  | Uncommon |
| 23 | `elite_ember_all` | Ember Elites | Down every Ember elite. | 25 | Gold | Elite | 6 |  | Rare |
| 24 | `elite_blink` | Blink Strike | Destroy an elite with a pause blink. | 10 | Silver | Elite | - |  | Uncommon |
| 25 | `kills_100` | Gunner | Destroy 100 enemies. | 5 | Bronze | Enemy | 100 |  | Common |
| 26 | `kills_1000` | Hundredfold | Destroy 1,000 enemies. | 10 | Silver | Enemy | 1000 |  | Uncommon |
| 27 | `kills_5000` | Warlord | Destroy 5,000 enemies. | 25 | Gold | Enemy | 5000 |  | Epic |
| 28 | `rocks_500` | Rock Breaker | Destroy 500 asteroids and rocks. | 10 | Bronze | Enemy | 500 |  | Uncommon |
| 29 | `mines_25` | Minesweeper | Destroy 25 rail mines. | 10 | Bronze | Enemy | 25 |  | Uncommon |
| 30 | `chain_10` | Chain Reaction | Reach a 10-kill chain. | 10 | Silver | Enemy | - |  | Uncommon |
| 31 | `mega_domino` | MEGA DOMINO | Trigger a MEGA DOMINO. | 15 | Silver | Enemy | - | yes | Rare |
| 32 | `codex_10` | Curious Pilot | Discover 10 Codex entries. | 5 | Bronze | Codex | 10 |  | Common |
| 33 | `codex_50` | Cartographer | Discover 50 Codex entries. | 10 | Silver | Codex | 50 |  | Rare |
| 34 | `codex_field_guide` | Field Guide | Fully catalogue one world's enemies and hazards. | 15 | Silver | Codex | - |  | Rare |
| 35 | `codex_complete` | Completionist | Discover every Codex entry. | 35 | Platinum | Codex | - |  | Epic |
| 36 | `ship_first` | Hangar Debut | Buy your first ship. | 5 | Bronze | Collection | - |  | Common |
| 37 | `ship_half` | Squadron | Own half the hangar. | 15 | Silver | Collection | 8 |  | Uncommon |
| 38 | `ship_all` | Fleet Admiral | Own every ship. | 40 | Platinum | Collection | - |  | Epic |
| 39 | `skin_first` | Fresh Paint | Buy your first ship skin. | 5 | Bronze | Collection | - |  | Common |
| 40 | `skin_special` | Special Edition | Buy a Special skin. | 10 | Silver | Collection | - |  | Uncommon |
| 41 | `skin_full_set` | Full Wardrobe | Own all skins for one ship. | 25 | Gold | Collection | - |  | Rare |
| 42 | `secret_power_first` | Secret Power | Unleash a ship's secret power. | 10 | Bronze | Collection | - |  | Uncommon |
| 43 | `stars_150` | Dust Gatherer | Collect 150 star dust pickups. | 10 | Bronze | Dust | 150 |  | Common |
| 44 | `stars_1000` | Dust Hoarder | Collect 1,000 star dust pickups. | 20 | Silver | Dust | 1000 |  | Uncommon |
| 45 | `stars_5000` | Dust Baron | Collect 5,000 star dust pickups. | 25 | Gold | Dust | 5000 |  | Epic |
| 46 | `dust_spent_10000` | Big Spender | Spend 10,000 star dust in the shop. | 15 | Silver | Dust | 10000 |  | Rare |
| 47 | `score_10k` | Rookie Score | Score 10,000 points in a run. | 10 | Bronze | Skill | - |  | Common |
| 48 | `score_50k` | Ace Score | Score 50,000 points in a run. | 20 | Silver | Skill | - |  | Rare |
| 49 | `score_150k` | Legend Score | Score 150,000 points in a run. | 35 | Platinum | Skill | - |  | Epic |
| 50 | `speed_flash` | Flash | Reach the speed cap. | 15 | Silver | Skill | - |  | Uncommon |
| 51 | `speed_speedster` | Speedster | Break the speed limit. | 20 | Silver | Skill | - |  | Rare |
| 52 | `speed_super_sonic` | Super Sonic | Max out the limit break. | 35 | Platinum | Skill | - |  | Epic |
| 53 | `deaths_10` | Crash Course | Crash 10 times. | 5 | Bronze | Skill | 10 |  | Common |
| 54 | `deaths_100` | Frequent Flyer | Crash 100 times. | 10 | Silver | Skill | 100 | yes | Uncommon |
| 55 | `pause_first` | Paused | Spend your first pause. | 5 | Bronze | Pause | - |  | Common |
| 56 | `pause_blink_kill` | Correct Pause | Destroy an enemy by blinking onto it. | 10 | Bronze | Pause | - |  | Common |
| 57 | `pause_blink_100` | Blink Master | Blink 100 times. | 10 | Silver | Pause | 100 |  | Uncommon |
| 58 | `pause_no_pause_world` | Hold Your Breath | Clear a world without spending a pause. | 20 | Silver | Pause | - |  | Rare |
| 59 | `pause_hoarder` | Time Banker | Hold 15 pauses at once. | 10 | Silver | Pause | - |  | Uncommon |
| 60 | `pause_perfect_dodge` | Perfect Pause | Blink out of a hostile shot's path. | 25 | Gold | Pause | - | yes | Epic |

Notes on targets: `-` means one-shot. Some "all" achievements derive their target at runtime from the catalog
(`elite_*_all`: the number of elites defined for that world; `ship_all`: shipTotal-1; `codex_complete`: `Codex.Total`; `boss_all`: `WorldManager.LiveWorldCount`).
Score thresholds (`score_*`) and `codex_50` must be calibrated against `ScoreRules` and the real `Codex.Total` before the implementation agent freezes them (open question).

## 3. Unlock conditions and tracking hooks

Hook names were verified by grep in this checkout. "NEW" marks a hook that does not exist yet. All hooks call one new
entry point (see implementation plan): `Achievements.Report(string id)` for one-shots, `Achievements.Add(string counterId, int n)` for counters, `Achievements.SetAtLeast(string counterId, int value)` for max-style counters.

| id | Condition (measurable) | Tracking hook |
|---|---|---|
| `meta_first_flight` | HasDoneTut becomes true (real, not skipped-in-dev) | achievementAPICalls.achievement_tutorial_completed() already called from Hints.cs:279 and TutorialSkip.cs:241 |
| `meta_logged_on` | Silent or interactive sign-in succeeds | CloudSync.cs:159 (achievementAPICalls.achievement_logged_on_successfully) already wired |
| `world_frost_reached` | Run enters world index 1 | WorldManager.Advance() (Codex.Discover(Codex.WorldId(CurrentIndex)) site); migrate from PlayerPrefs highestWorld >= 1 |
| `world_verdant_reached` | Run enters world index 2 | WorldManager.Advance(); migrate from highestWorld >= 2 |
| `world_ember_reached` | Run enters world index 3 | WorldManager.Advance(); migrate from highestWorld >= 3 |
| `world_tide_reached` | Run enters world index 4 (only when WorldManager.TideEnabled) | WorldManager.Advance(); migrate from highestWorld >= 4 |
| `loop_1` | RunLoop.Index reaches 1 in a real run (final world's portal/lift-off flown) | WorldManager.Advance() loop branch next to RunScore.OnLoop(RunLoop.Advance()) |
| `loop_2` | RunLoop.Index reaches 2 | Same hook, same branch |
| `loop_5` | RunLoop.Index reaches 5 | Same hook |
| `boss_space` | BossEncounter ends destroyed (explode true) in world 0 | BossEncounter.BeginOutro (explode var, BossEncounter.cs ~377); world field = world index |
| `boss_frost` | Boss destroyed in world 1 | BossEncounter.BeginOutro |
| `boss_verdant` | Boss destroyed in world 2 | BossEncounter.BeginOutro |
| `boss_ember` | Boss destroyed in world 3 | BossEncounter.BeginOutro |
| `boss_tide` | Boss destroyed in world 4 (TideEnabled only; boss not built yet) | BossEncounter.BeginOutro |
| `boss_no_hit` | Boss destroyed and no hull damage between fight start and outro | NEW: collisionDetection raises AchievementEvents.PlayerHurt; BossEncounter latches it (reset in its fight start) |
| `boss_all` | Distinct bosses destroyed >= LastLiveWorld+1 (4 now, 5 when Tide is on) | Same hook; counter = set of boss world ids (persist as bitmask) |
| `elite_first` | 1 elite killed by the pilot | EliteShip.Died static event (cause, EliteShip.Def.codexId, Def.world) |
| `elite_10` | 10 elite kills (lifetime) | EliteShip.Died |
| `elite_50` | 50 elite kills | EliteShip.Died |
| `elite_space_all` | All 4 Space elite codex ids killed at least once | EliteShip.Died + EliteCatalog.ForWorld(0) (Gameplay/Elites/EliteDef.cs:279) |
| `elite_frost_all` | All 5 Frost elites killed | EliteShip.Died + EliteCatalog world list |
| `elite_verdant_all` | All Verdant elites killed (5 defined: resin_warden, timber_hauler, thornlash, sporebloom, leafblade; the counter follows the catalog) | EliteShip.Died + EliteCatalog world list |
| `elite_ember_all` | All 6 Ember elites killed | EliteShip.Died + EliteCatalog world list |
| `elite_blink` | Elite killed with cause EliteDamage.Teleport | EliteShip.Died (cause == EliteDamage.Teleport; EliteShip.TeleportStrike) |
| `kills_100` | 100 enemy kills (tag Enimey, any world, not rocks) | collisionDetection.AwardDestroyedTarget -> RecordKillAchievement (extend: all Enimey, not only alien1); seed from old Aliens counter |
| `kills_1000` | 1,000 enemy kills | Same hook |
| `kills_5000` | 5,000 enemy kills | Same hook |
| `rocks_500` | 500 hazards tagged Astr | AwardDestroyedTarget (existing asteroid_destroyed counter, retarget thresholds) |
| `mines_25` | 25 kills where EnemyIdentity role is Mine | AwardDestroyedTarget + EnemyIdentity (Gameplay/Enemies) role == EnemyRole.Mine |
| `chain_10` | RunScore.Parts.bestChain >= 10 | RunScore.OnKill (chain var) -> check after chain update |
| `mega_domino` | DeathCrashDomino raises MegaDominoStarted | DeathCrashDomino.MegaDominoStarted (static event, line 95) |
| `codex_10` | Codex.DiscoveredCount >= 10 | Codex.Discovered static event |
| `codex_50` | Codex.DiscoveredCount >= 50 (verify against Codex.Total at build time) | Codex.Discovered |
| `codex_field_guide` | All listed Enemies+Hazards entries of any one world discovered | Codex.Discovered + CodexPanel.SectionsFor (counts per world) |
| `codex_complete` | Codex.DiscoveredCount == Codex.Total (bosses included, Tide excluded until enabled) | Codex.Discovered |
| `ship_first` | Own any ship besides the starter | shopingShips.TryPurchase (shopingShips.cs:85) success; achievementAPICalls.achievement_buy_your_first_ship exists but is never called |
| `ship_half` | 8 of 15 purchasable ships owned (shopingShips.shipTotal 16, index 0 unused) | shopingShips.TryPurchase + ShipId.IsOwned |
| `ship_all` | All ships owned | shopingShips.TryPurchase; achievement_buy_all_ships exists but never called |
| `skin_first` | ShipSkins.TryPurchase returns Bought | ShipSkins.TryPurchase (ShipSkins.cs:281) |
| `skin_special` | Bought a skin of ShipSkinKind.Special (index ShipSkins.Special) | ShipSkins.TryPurchase |
| `skin_full_set` | All ShipSkins.PerShip skins owned for any one ship | ShipSkins.TryPurchase (check Has/IsOwned loop) |
| `secret_power_first` | SecretPowerController.Fire called in a real run | SecretPowerController.Fire (Gameplay/Weapons/SecretPowers.cs:243) |
| `stars_150` | 150 star pickups (existing Stars counter) | collisionDetection star_collected (cs:350) -> achievementAPICalls.star_collected |
| `stars_1000` | 1,000 star pickups | Same |
| `stars_5000` | 5,000 star pickups | Same |
| `dust_spent_10000` | Lifetime spend >= 10,000 (new counter) | NEW counter in shopingShips.TryPurchase and ShipSkins.TryPurchase (both subtract from StarDustLedger.CurrencyKey) |
| `score_10k` | Run total >= 10,000 (calibrate vs ScoreRules) | score.SettleCurrentRun / RunScore.Total at bank |
| `score_50k` | Run total >= 50,000 (calibrate) | Same |
| `score_150k` | Run total >= 150,000 (calibrate) | Same |
| `speed_flash` | Natural speed at the cap (HUD 35) | achievementAPICalls.SpeedMilestones.Step (reported==1) |
| `speed_speedster` | First limit break (boost above cap) | SpeedMilestones.Step (reported==2) |
| `speed_super_sonic` | Boost at max at the cap | SpeedMilestones.Step (reported==3) |
| `deaths_10` | 10 deaths (existing Deaths counter) | achievementAPICalls.player_died |
| `deaths_100` | 100 deaths | Same |
| `pause_first` | First pause spent in a real run | score.pauseCounterFunction (score.cs:196); achievement_paused exists but never called |
| `pause_blink_kill` | Kill via pause teleport (bonusPoints = ScoreRules.TeleportKillBonus) | collisionDetection.AwardDestroyedTarget with bonusPoints > 0 (from TeleportFx.Strike); replaces achievement_correct_pause (never called) |
| `pause_blink_100` | 100 pause teleports | movePlayer.cs:140 next to RunScore.OnTeleport |
| `pause_no_pause_world` | Fly through a world's portal with zero pauses spent in it | score.pauseCounterFunction sets a flag; WorldManager.Advance checks and resets |
| `pause_hoarder` | score.pauseCounter >= 15 (start 5, red atom +2 via score.incromentPause) | score.incromentPause |
| `pause_perfect_dodge` | Teleport while a hostile shot is within 0.6 units, and survive 1 s | NEW check in movePlayer teleport branch using RunScore.IsHostileShot; Physics2D overlap |

Shared guards (one helper, `AchievementGuard.Real`): not in `tutorialS5`, not `DeveloperUnlocks.Enabled`, and `score.paysRealDust`.

Hooks that need a small NEW event: `AchievementEvents.PlayerHurt` (heart lost; for `boss_no_hit`), the blink-dodge probe in `movePlayer` (for `pause_perfect_dodge`),
the lifetime dust-spent counter in the two purchase methods, and a world-entry/loop notification inside `WorldManager.Advance`. Everything else subscribes to events or static hooks that already exist:
`Codex.Discovered`, `EliteShip.Died`, `DeathCrashDomino.MegaDominoStarted`, `RunScore.Scored`, `ShipSkins.Changed`.

## 4. Art brief per achievement

Common style for all badges: rustic goth cyberpunk steampunk, neon HD pixel art, a round medal/emblem with a heavy riveted brass-and-iron rim, centred subject, soft neon rim light, dark transparent-free background (solid near-black, for the store masters), tier shown by the rim metal (Bronze copper, Silver steel, Gold brass-gold, Platinum white-gold with a glow). World accent colour tints the subject glow.

| id | Icon concept | Accent hint | Tier |
|---|---|---|---|
| `meta_first_flight` | Brass wings badge over a tiny wormhole ring. | brass gold | Bronze |
| `meta_logged_on` | Brass plug-and-socket emblem with a green status lamp. | brass gold | Bronze |
| `world_frost_reached` | Snowflake medal on a cyan ringed planet. | Frost white-cyan | Bronze |
| `world_verdant_reached` | Lime leaf-and-gear medal over a jungle planet. | Verdant lime | Silver |
| `world_ember_reached` | Flame-in-cog medal over a lava planet. | Ember orange | Silver |
| `world_tide_reached` | Wave-and-anchor medal over an ocean planet. DORMANT. | Tide teal-mint | Silver |
| `loop_1` | Circular arrow chasing its own tail around a ringed city planet. | Space indigo/magenta | Gold |
| `loop_2` | Double circular arrow, laurel of rusted pipes. | Space indigo/magenta | Gold |
| `loop_5` | Five-spoked star wheel in magenta neon, platinum frame. | Space indigo/magenta | Platinum |
| `boss_space` | Capital-carrier skull medal, indigo/magenta glow. | Space indigo/magenta | Silver |
| `boss_frost` | Cracked-ice dragon head medal, cyan glow. | Frost white-cyan | Silver |
| `boss_verdant` | Thorned crown over a lime flower, gear petals. | Verdant lime | Silver |
| `boss_ember` | Wyrm skull wreathed in orange embers. | Ember orange | Gold |
| `boss_tide` | Kraken-eye medal in mint. DORMANT; title/art can change when the boss is named. | Tide teal-mint | Gold |
| `boss_no_hit` | Shield with an unbroken heart, laurel, brass. | brass gold | Gold |
| `boss_all` | Four boss silhouettes in a ring, platinum. | brass gold | Platinum |
| `elite_first` | Elite chevron medal with a crosshair. | brass gold | Bronze |
| `elite_10` | Chevron x2 with a brass rivet strip. | brass gold | Silver |
| `elite_50` | Chevron x3 over crossed lances, gold. | brass gold | Gold |
| `elite_space_all` | Four small ship icons around an indigo ring. | Space indigo/magenta | Silver |
| `elite_frost_all` | Five ice-shard ship icons in a frost ring. | Frost white-cyan | Silver |
| `elite_verdant_all` | Resin warden helm on lime leaves. | Verdant lime | Silver |
| `elite_ember_all` | Six cinder ship icons around a forge ring. | Ember orange | Gold |
| `elite_blink` | Elite hull split by a magenta lightning blink. | Space indigo/magenta | Silver |
| `kills_100` | Three brass bullet casings in a wreath. | brass gold | Bronze |
| `kills_1000` | Skull-and-gears medal. | brass gold | Silver |
| `kills_5000` | Crowned skull, platinum rim. | brass gold | Gold |
| `rocks_500` | Cracked boulder medal with a pickaxe. | brass gold | Bronze |
| `mines_25` | Spiked mine with a defused tag. | brass gold | Bronze |
| `chain_10` | Linked chain rings glowing magenta. | Space indigo/magenta | Silver |
| `mega_domino` | Row of falling domino tiles exploding in neon. | Space indigo/magenta | Silver |
| `codex_10` | Open book with a magnifier. | brass gold | Bronze |
| `codex_50` | Book with a star-chart ribbon. | brass gold | Silver |
| `codex_field_guide` | Book with a planet bookmark. | brass gold | Silver |
| `codex_complete` | Gold book sealed with a rivet crown. | brass gold | Platinum |
| `ship_first` | Small ship on a brass pedestal. | brass gold | Bronze |
| `ship_half` | Three ships in V formation. | brass gold | Silver |
| `ship_all` | Fleet emblem with anchor and stars, platinum. | brass gold | Platinum |
| `skin_first` | Paint bucket with a neon drip. | brass gold | Bronze |
| `skin_special` | Holographic ship with a star burst. | brass gold | Silver |
| `skin_full_set` | Five colour swatches fanned like cards. | brass gold | Gold |
| `secret_power_first` | Glowing meter ring bursting open. | Space indigo/magenta | Bronze |
| `stars_150` | Sack of glittering dust. | brass gold | Bronze |
| `stars_1000` | Overflowing sack of dust, brass bands. | brass gold | Silver |
| `stars_5000` | Dust-crowned vault door. | brass gold | Gold |
| `dust_spent_10000` | Coin purse with a cog clasp. | brass gold | Silver |
| `score_10k` | Bronze rank chevron. | brass gold | Bronze |
| `score_50k` | Silver rank chevron with stars. | brass gold | Silver |
| `score_150k` | Gold crown over a score ticker. | brass gold | Platinum |
| `speed_flash` | Lightning bolt on a speedometer. | Space indigo/magenta | Silver |
| `speed_speedster` | Speedometer needle past the red. | Space indigo/magenta | Silver |
| `speed_super_sonic` | Sonic-boom shockwave around a ship. | Space indigo/magenta | Platinum |
| `deaths_10` | Dented ship hull with a bandage. | brass gold | Bronze |
| `deaths_100` | Phoenix-feather medal with soot. | brass gold | Silver |
| `pause_first` | Pause bars glyph in a ring. | Space indigo/magenta | Bronze |
| `pause_blink_kill` | Pause glyph with a blink streak into a target. | Space indigo/magenta | Bronze |
| `pause_blink_100` | Three trailing ghost ships. | Space indigo/magenta | Silver |
| `pause_no_pause_world` | Pause glyph crossed out, breath bubble. | Space indigo/magenta | Silver |
| `pause_hoarder` | Stack of pause tokens in a vault. | Space indigo/magenta | Silver |
| `pause_perfect_dodge` | Bullet frozen mid-air beside a ghost ship. | Space indigo/magenta | Gold |

## 5. Reward and claim

Each achievement has two states beyond locked: UNLOCKED (earned, 25 dust waiting) and CLAIMED. The player collects in the Codex. Unlock never pays automatically; this keeps `StarDustLedger` (which overwrites prefs during runs) out of the hot path.

## 6. Migration plan

There is no local unlocked flag in the current build (only counters and the store), so migration derives state from progress that exists locally and, on sign-in, from the store.

| Old | New | How |
|---|---|---|
| `tutorial_complete` | `meta_first_flight` | `HasDoneTut == "true"` |
| `logged_on_successfully` | `meta_logged_on` | unlock on first successful sign-in after update (store flag is imported if present) |
| `flash`, `speedster`, `super_sonic` | same-named `speed_*` | local flag did not exist: import from the store (`Social.LoadAchievements` on sign-in, `Social.localUser`), else `HighestSpeed` pref (legacy float) as a fallback |
| `aliens` 5..3500 | `kills_100/1000/5000` | seed the new all-enemies counter from the old Aliens counter (a lower bound); the old Alien tiers are retired |
| `destroyer*` | `rocks_500` | seed from the Asteroids counter |
| `first_death`, `death_2..5` | `deaths_10`, `deaths_100` | seed from the Deaths counter |
| `stars`, `stars_2` | `stars_150`, `stars_1000`, `stars_5000` | seed from the Stars counter (same unit: pickups) |
| `buy_your_first_ship`, `buy_all_ships` | `ship_first`, `ship_all` | derive from `boughtshipN` keys (never unlocked before) |
| `paused`, `correct_pause`, `secret_ship` | `pause_first`, `pause_blink_kill`, retired | `secret_ship` had no meaning: dropped; the other two start from zero |
| world progress | `world_*_reached` | derive from `highestWorld` pref |
| Codex | `codex_10/50/complete`, `field_guide` | derive from `codexSeen` |
| Ships/skins | `ship_half`, `skin_*` | derive from owned keys |

Rules:

1. One-time migration at first launch of the new build, stamped by `ach_schema = 1`. It evaluates every derivable achievement and marks it UNLOCKED but **not claimed**, so every already-earned one is claimable for 25 dust (the owner's requirement).
2. Migration is idempotent and runs again after a cloud restore / account switch (cloud-merged counters can unlock more).
3. Retired local keys (`achv_count_*`, `achv_progress_*`, `achv_synced_*`) are kept for one release as seed data, then deleted. `AchievementTiers`, `AchievementSync` and the `AchievementCategory` enum are removed; `ProgressSnapshot.CounterKeys()` switches to the new keys.
4. Store side: unlocked-derived achievements are re-reported once after sign-in (idempotent, both stores ignore a repeat 100%).

What to delete: `AchievementTiers.cs`, `AchievementSync.cs`, the one-shot methods in `achievementAPICalls.cs` (class kept only if `SpeedMilestones` stays; better moved), `StringHolder.cs` achievement consts and the stale leaderboard const, `AchievementIds.All` old entries, `AchievementTiersTest.cs` (replaced), docs mentions in `leaderboards.md` / `speed-and-loops.md` / `enemy-behaviours.md` (update the tables).

Old store entries: if the Play Console game has already been **published**, published achievements generally cannot be deleted (flag: verify in Play Console); in that case leave the 28 legacy ones hidden/unused and add the 60 new ones. If it was never published, delete the 28 and create the 60 from the CSV. iOS has nothing to migrate.

