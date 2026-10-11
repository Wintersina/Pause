# Achievements store setup (Google Play Games Services and Game Center)

For the project owner. Package / bundle id: `me.hapticgate.pause` (new Play Console account, Google Cloud project number
`528367766818`). Status: **Android is done** (Play Games Services project `528367766818`: the 60 achievements are imported as drafts and their ids are in
`Assets/Resources/AchievementStoreIds.csv` and `docs/achievements-export/android-ids.csv`; they still need **Review and publish** in Play Console).
**Game Center (iOS) is still pending**: create the 60 from section 3. The 60 new achievements are defined in `docs/achievements-redesign.md`; the CSVs below are
generated from that table (the planned menu tool `Pause/Achievements/Export store CSV` regenerates them from code, see
`achievements-implementation-plan.md`).

## 1. Limits to respect

| | Google Play Games Services | App Store Connect Game Center |
|---|---|---|
| Max achievements | 200 | 100 per game (verify in App Store Connect) |
| Points per achievement | 5 to 180, multiples of 5 | 1 to 100 |
| Total points | 1000 | 1000 |
| Name / title | up to 100 chars (we keep <= 24) | 1 to 100 chars |
| Description | up to 500 chars (we keep <= 100) | pre-earned and earned descriptions, up to 255 chars |
| Icon | 512x512 PNG or JPG, 32-bit | 512x512 up to 1024x1024 PNG/JPG, flattened (no alpha) |
| Incremental | optional, steps 2..10000 | percentage reporting (0-100); no steps |
| Hidden | "Initial state: Hidden / Revealed" | "Hidden" checkbox |
| Repeatable | no | "Achievable More Than Once" (leave off) |

These limits are from memory of both consoles; they change, so re-check at entry time.

This set: 60 achievements, 1000 points, largest 40, smallest 5. Hidden at start in the store: `mega_domino`, `deaths_100`,
`pause_perfect_dodge` and the 2 dormant Tide ones (5 in total).

## 2. Google Play Console CSV

Play Console path: Play Games Services > Setup and management > Achievements. Bulk import support and exact column names differ by
console version; if the import rejects the file, enter the rows by hand in this order.
Incremental achievements: tick "Incremental" and enter the steps (= the counter target). The game reports percent and the plugin converts to
steps (`SocialBridge.PlatformPercent`). Dynamic targets (the `elite_*_all` family, `ship_all`, `codex_complete`, `boss_all`) are
listed as one-shots here and in code report 100% only on completion; if you prefer a progress bar in the store, set steps to the target in the redesign table.

Order of the rows is the display order in the console.

```csv
Order,Name,Description,Points,Incremental steps (blank = standard),Initial state,Icon file (512x512)
1,First Flight,Finish the tutorial and take your first real run.,5,,Revealed,meta_first_flight.png
2,Logged On,Sign in to Google Play Games or Game Center.,5,,Revealed,meta_logged_on.png
3,Cold Front,Reach the Frost world.,10,,Revealed,world_frost_reached.png
4,Into the Green,Reach the Verdant world.,10,,Revealed,world_verdant_reached.png
5,Trial by Fire,Reach the Ember world.,15,,Revealed,world_ember_reached.png
6,Deep Water,Reach the Tide world.,20,,Hidden,world_tide_reached.png
7,Full Circle,Complete a full loop of every world.,25,,Revealed,loop_1.png
8,Second Lap,Complete two loops in a single run.,25,,Revealed,loop_2.png
9,Orbit Lifer,Complete five loops in a single run.,40,,Revealed,loop_5.png
10,Archon Down,Defeat the Void Archon.,10,,Revealed,boss_space.png
11,Leviathan Slain,Defeat the Hoarfrost Leviathan.,15,,Revealed,boss_frost.png
12,Bloom Queen Felled,Defeat the Bloom Queen.,20,,Revealed,boss_verdant.png
13,Drake Slayer,Defeat the Cinder Drake.,25,,Revealed,boss_ember.png
14,Tide Turner,Defeat the Tide boss.,25,,Hidden,boss_tide.png
15,Untouchable,Defeat a boss without losing a heart.,25,,Revealed,boss_no_hit.png
16,Boss Collector,Defeat every live world's boss.,35,4,Revealed,boss_all.png
17,Elite Hunter,Destroy your first elite ship.,10,,Revealed,elite_first.png
18,Veteran Hunter,Destroy 10 elite ships.,10,10,Revealed,elite_10.png
19,Elite Exterminator,Destroy 50 elite ships.,25,50,Revealed,elite_50.png
20,Space Elites,Down every Space elite.,15,4,Revealed,elite_space_all.png
21,Frost Elites,Down every Frost elite.,20,5,Revealed,elite_frost_all.png
22,Verdant Elites,Down every Verdant elite.,15,,Revealed,elite_verdant_all.png
23,Ember Elites,Down every Ember elite.,25,6,Revealed,elite_ember_all.png
24,Blink Strike,Destroy an elite with a pause blink.,10,,Revealed,elite_blink.png
25,Gunner,Destroy 100 enemies.,5,100,Revealed,kills_100.png
26,Hundredfold,"Destroy 1,000 enemies.",10,1000,Revealed,kills_1000.png
27,Warlord,"Destroy 5,000 enemies.",25,5000,Revealed,kills_5000.png
28,Rock Breaker,Destroy 500 asteroids and rocks.,10,500,Revealed,rocks_500.png
29,Minesweeper,Destroy 25 rail mines.,10,25,Revealed,mines_25.png
30,Chain Reaction,Reach a 10-kill chain.,10,,Revealed,chain_10.png
31,MEGA DOMINO,Trigger a MEGA DOMINO.,15,,Hidden,mega_domino.png
32,Curious Pilot,Discover 10 Codex entries.,5,10,Revealed,codex_10.png
33,Cartographer,Discover 50 Codex entries.,10,50,Revealed,codex_50.png
34,Field Guide,Fully catalogue one world's enemies and hazards.,15,,Revealed,codex_field_guide.png
35,Completionist,Discover every Codex entry.,35,,Revealed,codex_complete.png
36,Hangar Debut,Buy your first ship.,5,,Revealed,ship_first.png
37,Squadron,Own half the hangar.,15,8,Revealed,ship_half.png
38,Fleet Admiral,Own every ship.,40,,Revealed,ship_all.png
39,Fresh Paint,Buy your first ship skin.,5,,Revealed,skin_first.png
40,Special Edition,Buy a Special skin.,10,,Revealed,skin_special.png
41,Full Wardrobe,Own all skins for one ship.,25,,Revealed,skin_full_set.png
42,Secret Power,Unleash a ship's secret power.,10,,Revealed,secret_power_first.png
43,Dust Gatherer,Collect 150 star dust pickups.,10,150,Revealed,stars_150.png
44,Dust Hoarder,"Collect 1,000 star dust pickups.",20,1000,Revealed,stars_1000.png
45,Dust Baron,"Collect 5,000 star dust pickups.",25,5000,Revealed,stars_5000.png
46,Big Spender,"Spend 10,000 star dust in the shop.",15,10000,Revealed,dust_spent_10000.png
47,Rookie Score,"Score 10,000 points in a run.",10,,Revealed,score_10k.png
48,Ace Score,"Score 50,000 points in a run.",20,,Revealed,score_50k.png
49,Legend Score,"Score 150,000 points in a run.",35,,Revealed,score_150k.png
50,Flash,Reach the speed cap.,15,,Revealed,speed_flash.png
51,Speedster,Break the speed limit.,20,,Revealed,speed_speedster.png
52,Super Sonic,Max out the limit break.,35,,Revealed,speed_super_sonic.png
53,Crash Course,Crash 10 times.,5,10,Revealed,deaths_10.png
54,Frequent Flyer,Crash 100 times.,10,100,Hidden,deaths_100.png
55,Paused,Spend your first pause.,5,,Revealed,pause_first.png
56,Correct Pause,Destroy an enemy by blinking onto it.,10,,Revealed,pause_blink_kill.png
57,Blink Master,Blink 100 times.,10,100,Revealed,pause_blink_100.png
58,Hold Your Breath,Clear a world without spending a pause.,20,,Revealed,pause_no_pause_world.png
59,Time Banker,Hold 15 pauses at once.,10,,Revealed,pause_hoarder.png
60,Perfect Pause,Blink out of a hostile shot's path.,25,,Hidden,pause_perfect_dodge.png
```

After creating them, copy the generated resource ids (`CgkI...`) back as a two-column list `internal_id,CgkI...` for the implementation agent
(or paste them into the code table, section 4).

## 3. App Store Connect Game Center CSV

App Store Connect > your app > Services > Game Center > Achievements (inside an Achievement Group if you use groups). Required per achievement: reference
name, ID, points, hidden, "Achievable More Than Once" = No, and per localization (English U.S. at minimum): title, pre-earned description, earned
description, image. Id scheme: `me.sinaserati.Pause.ach_<internal id>`.

```csv
Reference name,Achievement ID,Point value,Hidden,Achievable more than once,Title,Pre-earned description,Earned description,Image
Pause meta_first_flight,me.sinaserati.Pause.ach_meta_first_flight,5,No,No,First Flight,Finish the tutorial and take your first real run.,Finish the tutorial and take your first real run.,meta_first_flight_1024.png
Pause meta_logged_on,me.sinaserati.Pause.ach_meta_logged_on,5,No,No,Logged On,Sign in to Google Play Games or Game Center.,Sign in to Google Play Games or Game Center.,meta_logged_on_1024.png
Pause world_frost_reached,me.sinaserati.Pause.ach_world_frost_reached,10,No,No,Cold Front,Reach the Frost world.,Reach the Frost world.,world_frost_reached_1024.png
Pause world_verdant_reached,me.sinaserati.Pause.ach_world_verdant_reached,10,No,No,Into the Green,Reach the Verdant world.,Reach the Verdant world.,world_verdant_reached_1024.png
Pause world_ember_reached,me.sinaserati.Pause.ach_world_ember_reached,15,No,No,Trial by Fire,Reach the Ember world.,Reach the Ember world.,world_ember_reached_1024.png
Pause world_tide_reached,me.sinaserati.Pause.ach_world_tide_reached,20,Yes,No,Deep Water,Reach the Tide world.,Reach the Tide world.,world_tide_reached_1024.png
Pause loop_1,me.sinaserati.Pause.ach_loop_1,25,No,No,Full Circle,Complete a full loop of every world.,Complete a full loop of every world.,loop_1_1024.png
Pause loop_2,me.sinaserati.Pause.ach_loop_2,25,No,No,Second Lap,Complete two loops in a single run.,Complete two loops in a single run.,loop_2_1024.png
Pause loop_5,me.sinaserati.Pause.ach_loop_5,40,No,No,Orbit Lifer,Complete five loops in a single run.,Complete five loops in a single run.,loop_5_1024.png
Pause boss_space,me.sinaserati.Pause.ach_boss_space,10,No,No,Archon Down,Defeat the Void Archon.,Defeat the Void Archon.,boss_space_1024.png
Pause boss_frost,me.sinaserati.Pause.ach_boss_frost,15,No,No,Leviathan Slain,Defeat the Hoarfrost Leviathan.,Defeat the Hoarfrost Leviathan.,boss_frost_1024.png
Pause boss_verdant,me.sinaserati.Pause.ach_boss_verdant,20,No,No,Bloom Queen Felled,Defeat the Bloom Queen.,Defeat the Bloom Queen.,boss_verdant_1024.png
Pause boss_ember,me.sinaserati.Pause.ach_boss_ember,25,No,No,Drake Slayer,Defeat the Cinder Drake.,Defeat the Cinder Drake.,boss_ember_1024.png
Pause boss_tide,me.sinaserati.Pause.ach_boss_tide,25,Yes,No,Tide Turner,Defeat the Tide boss.,Defeat the Tide boss.,boss_tide_1024.png
Pause boss_no_hit,me.sinaserati.Pause.ach_boss_no_hit,25,No,No,Untouchable,Defeat a boss without losing a heart.,Defeat a boss without losing a heart.,boss_no_hit_1024.png
Pause boss_all,me.sinaserati.Pause.ach_boss_all,35,No,No,Boss Collector,Defeat every live world's boss.,Defeat every live world's boss.,boss_all_1024.png
Pause elite_first,me.sinaserati.Pause.ach_elite_first,10,No,No,Elite Hunter,Destroy your first elite ship.,Destroy your first elite ship.,elite_first_1024.png
Pause elite_10,me.sinaserati.Pause.ach_elite_10,10,No,No,Veteran Hunter,Destroy 10 elite ships.,Destroy 10 elite ships.,elite_10_1024.png
Pause elite_50,me.sinaserati.Pause.ach_elite_50,25,No,No,Elite Exterminator,Destroy 50 elite ships.,Destroy 50 elite ships.,elite_50_1024.png
Pause elite_space_all,me.sinaserati.Pause.ach_elite_space_all,15,No,No,Space Elites,Down every Space elite.,Down every Space elite.,elite_space_all_1024.png
Pause elite_frost_all,me.sinaserati.Pause.ach_elite_frost_all,20,No,No,Frost Elites,Down every Frost elite.,Down every Frost elite.,elite_frost_all_1024.png
Pause elite_verdant_all,me.sinaserati.Pause.ach_elite_verdant_all,15,No,No,Verdant Elites,Down every Verdant elite.,Down every Verdant elite.,elite_verdant_all_1024.png
Pause elite_ember_all,me.sinaserati.Pause.ach_elite_ember_all,25,No,No,Ember Elites,Down every Ember elite.,Down every Ember elite.,elite_ember_all_1024.png
Pause elite_blink,me.sinaserati.Pause.ach_elite_blink,10,No,No,Blink Strike,Destroy an elite with a pause blink.,Destroy an elite with a pause blink.,elite_blink_1024.png
Pause kills_100,me.sinaserati.Pause.ach_kills_100,5,No,No,Gunner,Destroy 100 enemies.,Destroy 100 enemies.,kills_100_1024.png
Pause kills_1000,me.sinaserati.Pause.ach_kills_1000,10,No,No,Hundredfold,"Destroy 1,000 enemies.","Destroy 1,000 enemies.",kills_1000_1024.png
Pause kills_5000,me.sinaserati.Pause.ach_kills_5000,25,No,No,Warlord,"Destroy 5,000 enemies.","Destroy 5,000 enemies.",kills_5000_1024.png
Pause rocks_500,me.sinaserati.Pause.ach_rocks_500,10,No,No,Rock Breaker,Destroy 500 asteroids and rocks.,Destroy 500 asteroids and rocks.,rocks_500_1024.png
Pause mines_25,me.sinaserati.Pause.ach_mines_25,10,No,No,Minesweeper,Destroy 25 rail mines.,Destroy 25 rail mines.,mines_25_1024.png
Pause chain_10,me.sinaserati.Pause.ach_chain_10,10,No,No,Chain Reaction,Reach a 10-kill chain.,Reach a 10-kill chain.,chain_10_1024.png
Pause mega_domino,me.sinaserati.Pause.ach_mega_domino,15,Yes,No,MEGA DOMINO,Trigger a MEGA DOMINO.,Trigger a MEGA DOMINO.,mega_domino_1024.png
Pause codex_10,me.sinaserati.Pause.ach_codex_10,5,No,No,Curious Pilot,Discover 10 Codex entries.,Discover 10 Codex entries.,codex_10_1024.png
Pause codex_50,me.sinaserati.Pause.ach_codex_50,10,No,No,Cartographer,Discover 50 Codex entries.,Discover 50 Codex entries.,codex_50_1024.png
Pause codex_field_guide,me.sinaserati.Pause.ach_codex_field_guide,15,No,No,Field Guide,Fully catalogue one world's enemies and hazards.,Fully catalogue one world's enemies and hazards.,codex_field_guide_1024.png
Pause codex_complete,me.sinaserati.Pause.ach_codex_complete,35,No,No,Completionist,Discover every Codex entry.,Discover every Codex entry.,codex_complete_1024.png
Pause ship_first,me.sinaserati.Pause.ach_ship_first,5,No,No,Hangar Debut,Buy your first ship.,Buy your first ship.,ship_first_1024.png
Pause ship_half,me.sinaserati.Pause.ach_ship_half,15,No,No,Squadron,Own half the hangar.,Own half the hangar.,ship_half_1024.png
Pause ship_all,me.sinaserati.Pause.ach_ship_all,40,No,No,Fleet Admiral,Own every ship.,Own every ship.,ship_all_1024.png
Pause skin_first,me.sinaserati.Pause.ach_skin_first,5,No,No,Fresh Paint,Buy your first ship skin.,Buy your first ship skin.,skin_first_1024.png
Pause skin_special,me.sinaserati.Pause.ach_skin_special,10,No,No,Special Edition,Buy a Special skin.,Buy a Special skin.,skin_special_1024.png
Pause skin_full_set,me.sinaserati.Pause.ach_skin_full_set,25,No,No,Full Wardrobe,Own all skins for one ship.,Own all skins for one ship.,skin_full_set_1024.png
Pause secret_power_first,me.sinaserati.Pause.ach_secret_power_first,10,No,No,Secret Power,Unleash a ship's secret power.,Unleash a ship's secret power.,secret_power_first_1024.png
Pause stars_150,me.sinaserati.Pause.ach_stars_150,10,No,No,Dust Gatherer,Collect 150 star dust pickups.,Collect 150 star dust pickups.,stars_150_1024.png
Pause stars_1000,me.sinaserati.Pause.ach_stars_1000,20,No,No,Dust Hoarder,"Collect 1,000 star dust pickups.","Collect 1,000 star dust pickups.",stars_1000_1024.png
Pause stars_5000,me.sinaserati.Pause.ach_stars_5000,25,No,No,Dust Baron,"Collect 5,000 star dust pickups.","Collect 5,000 star dust pickups.",stars_5000_1024.png
Pause dust_spent_10000,me.sinaserati.Pause.ach_dust_spent_10000,15,No,No,Big Spender,"Spend 10,000 star dust in the shop.","Spend 10,000 star dust in the shop.",dust_spent_10000_1024.png
Pause score_10k,me.sinaserati.Pause.ach_score_10k,10,No,No,Rookie Score,"Score 10,000 points in a run.","Score 10,000 points in a run.",score_10k_1024.png
Pause score_50k,me.sinaserati.Pause.ach_score_50k,20,No,No,Ace Score,"Score 50,000 points in a run.","Score 50,000 points in a run.",score_50k_1024.png
Pause score_150k,me.sinaserati.Pause.ach_score_150k,35,No,No,Legend Score,"Score 150,000 points in a run.","Score 150,000 points in a run.",score_150k_1024.png
Pause speed_flash,me.sinaserati.Pause.ach_speed_flash,15,No,No,Flash,Reach the speed cap.,Reach the speed cap.,speed_flash_1024.png
Pause speed_speedster,me.sinaserati.Pause.ach_speed_speedster,20,No,No,Speedster,Break the speed limit.,Break the speed limit.,speed_speedster_1024.png
Pause speed_super_sonic,me.sinaserati.Pause.ach_speed_super_sonic,35,No,No,Super Sonic,Max out the limit break.,Max out the limit break.,speed_super_sonic_1024.png
Pause deaths_10,me.sinaserati.Pause.ach_deaths_10,5,No,No,Crash Course,Crash 10 times.,Crash 10 times.,deaths_10_1024.png
Pause deaths_100,me.sinaserati.Pause.ach_deaths_100,10,Yes,No,Frequent Flyer,Crash 100 times.,Crash 100 times.,deaths_100_1024.png
Pause pause_first,me.sinaserati.Pause.ach_pause_first,5,No,No,Paused,Spend your first pause.,Spend your first pause.,pause_first_1024.png
Pause pause_blink_kill,me.sinaserati.Pause.ach_pause_blink_kill,10,No,No,Correct Pause,Destroy an enemy by blinking onto it.,Destroy an enemy by blinking onto it.,pause_blink_kill_1024.png
Pause pause_blink_100,me.sinaserati.Pause.ach_pause_blink_100,10,No,No,Blink Master,Blink 100 times.,Blink 100 times.,pause_blink_100_1024.png
Pause pause_no_pause_world,me.sinaserati.Pause.ach_pause_no_pause_world,20,No,No,Hold Your Breath,Clear a world without spending a pause.,Clear a world without spending a pause.,pause_no_pause_world_1024.png
Pause pause_hoarder,me.sinaserati.Pause.ach_pause_hoarder,10,No,No,Time Banker,Hold 15 pauses at once.,Hold 15 pauses at once.,pause_hoarder_1024.png
Pause pause_perfect_dodge,me.sinaserati.Pause.ach_pause_perfect_dodge,25,Yes,No,Perfect Pause,Blink out of a hostile shot's path.,Blink out of a hostile shot's path.,pause_perfect_dodge_1024.png
```

The pre-earned and earned texts are identical in this export. For hidden achievements the pre-earned text is never shown until revealed.

## 4. Id mapping in code

Proposal (details in the implementation plan): replace `AchievementIds` with one table `AchievementStoreIds` where every row is
`internalId, androidId, iosId, points`. `androidId` starts as a placeholder `TODO_android_<internalId>` (never starts with `CgkI`) and `iosId`
is derived (`IosPrefix + "ach_" + internalId`). `SocialBridge` skips the report (logs once) if the id is a placeholder, so nothing is sent with a fake id.
The owner pastes real Android ids into `Assets/Resources/AchievementStoreIds.csv` (a TextAsset, easier than editing C#) and the
menu tool `Pause/Achievements/Export Store CSVs` regenerates the two CSVs above from the code catalogue.
Tests: id uniqueness, 1000-point total, <= 100 points each, title and description length limits, art file present per id.

## 5. Order of operations for the owner

1. DONE (Android): the 60 achievements from section 2 are imported as drafts. Remaining: Review and publish in Play Console, and add testers.
2. DONE (Android): icons uploaded with the rows. iOS: upload the 60 icons (section 6) while creating each Game Center row.
3. DONE (Android): ids pasted into `AchievementStoreIds.csv`. iOS: create the 60 ids exactly as in section 3, then set `AchievementIds.IosIdsConfirmed`.
4. Publish the Play Games configuration and add testers so achievements work for non-testers.
5. Test on a device: sign in, earn one (for example `meta_first_flight`), check Play Games / Game Center.

## 6. Art specification (for Codex)

| Need | Spec |
|---|---|
| Master | 1024x1024 PNG, RGB 8-bit, **no alpha**, sRGB, opaque near-black background, subject centred, 10% safe margin |
| Google Play upload | 512x512 PNG downscaled from the master (use Lanczos or nearest, whichever keeps the pixel art crisp). Play Games shows achievement icons as circles in its UI (believed; flag), so keep all content inside a centred circle of 90% diameter. This is also safe if shown square. |
| Apple upload | 1024x1024 PNG, no alpha, no pre-rounded corners (Apple applies its own mask; 512 is the minimum) |
| In game | 128x128 PNG, alpha allowed (round medal on transparent background), pixel-art scale from a 128 or 256 source, point filter, no mipmaps |
| Naming | `<internal id>.png` for the 128 in-game icon; `<internal id>_1024.png` for the master; the Google 512 is exported from the master by script |
| Style | one family: a round riveted brass/iron medal, neon rim light, subject centred, consistent line weight; rim metal by tier (copper, steel, brass, white-gold); world accent in the glow. No locked variant is drawn (greyscale is applied in code). |
| Count | 60 icons; per-id concepts in `achievements-redesign.md` section 4 |

Install locations: `Pause/Assets/Art/Resources/Achievements/<id>.png` (128) and staged masters in `Pause/Assets/Art/Achievements/src~/<id>_1024.png` (tilde folder, Unity ignores it). INSTALLED: the 60 pixel-art badges are in both places; `AchievementArtImporter` sets the sprite import (point filter, no mipmaps, uncompressed, 100 PPU) and `AchievementCatalogTest` guards presence, size, alpha and orphans.
Store uploads: `Pause > Achievements > Export store icons` (or `-executeMethod AchievementStoreExport.ExportIcons`) writes `docs/achievements-export/play-icons-512/<id>.png` (nearest-neighbour from the masters; committed, upload these to Play) and `docs/achievements-export/gamecenter-1024/<id>.png` (RGB, no alpha; committed too, about 1.8 MB).
