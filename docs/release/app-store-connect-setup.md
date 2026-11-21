# App Store Connect setup (scripted + manual)

`scripts/appstoreconnect/` creates the Game Center achievements, the Top Score leaderboard and the in-app
products through the official App Store Connect API. Everything is **dry-run by default**, idempotent
(re-run = update, never duplicate) and **never deletes** (Apple never frees an achievement, leaderboard or
product id once used).

> Written without live credentials: resource and attribute names follow the App Store Connect API
> (gameCenterDetails, gameCenterAchievements, gameCenterAchievementLocalizations, gameCenterAchievementImages,
> gameCenterLeaderboards, gameCenterLeaderboardLocalizations, inAppPurchasesV2, inAppPurchaseLocalizations),
> but were not run against Apple. First `check` and a single-item `--only` run will confirm them; fix any
> 4xx message in `scripts/appstoreconnect/asc.py` / `mapping.py`.

## 1. API key

1. App Store Connect > Users and Access > Integrations > App Store Connect API > Team Keys > **+**.
2. Name "pause-setup", access **Admin** or **App Manager**. Download the `.p8` once (Apple lets you download it a single time).
3. Store it outside the repo: `mkdir -p ~/.appstoreconnect/private_keys && mv AuthKey_XXXX.p8 ~/.appstoreconnect/private_keys/ && chmod 600 ...`
4. Env vars (Key ID is on the key row, Issuer ID at the top of the page):

```
export ASC_KEY_ID=XXXXXXXXXX
export ASC_ISSUER_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
export ASC_KEY_PATH=~/.appstoreconnect/private_keys/AuthKey_XXXXXXXXXX.p8
pip install -r scripts/appstoreconnect/requirements.txt
```

The key is never printed or committed.

## 2. Commands

```
python3 -I scripts/appstoreconnect/asc.py check
python3 -I scripts/appstoreconnect/asc.py achievements [--only meta_first_flight,boss_all] [--apply]
python3 -I scripts/appstoreconnect/asc.py leaderboards [--apply]
python3 -I scripts/appstoreconnect/asc.py iap [--apply]
python3 -m unittest discover -s scripts/appstoreconnect/tests
```

Without env vars a dry-run still works and lists the full plan from local data. With credentials a dry-run
reads App Store Connect and shows create / update / unchanged per item. Suggested first live run:
`check`, then `achievements --only meta_first_flight --apply` and look at it in the console, then the rest.

Data sources: achievements from `docs/achievements-export/game-center.csv` plus `gamecenter-1024/<id>.png`
(1024x1024 RGB, already generated; the CSV's Image column says `<id>_1024.png`, the files are `<id>.png`, the script
resolves that); leaderboard from `docs/leaderboards.md`; products from `scripts/appstoreconnect/data/iap_products.json`
(TODO: sync with the final id table of the `feat/ios-storekit` PR, section 5 of `docs/store-products-and-rewards.md`).

## 3. Points split

The catalogue has **68** achievements (not 60) and the export CSV already totals **exactly 1000** points
(Game Center cap: 1000 per app, 100 per achievement; max used is 40). Tiers: 5 for the 24 easy/early ones,
10 / 15 / 20 for mid ones, 25 for hard (all bosses from Ember, Perfect Pause...), 35 for Boss Collector, Completionist,
Legend Score, Super Sonic; 40 for Fleet Admiral and Orbit Lifer. Score ladder: Rookie 5,000 = 5, Ace 20,000 = 20,
Legend 80,000 = 35. Three are hidden (MEGA DOMINO, Frequent Flyer, Perfect Pause): the API's
`showBeforeEarned=false`. None repeatable. Edit points in the CSV (regenerate via the Unity export) and re-run.

## 4. What the scripts cannot do (manual checklist)

1. **Agreements, Tax, Banking**: Business > Agreements: accept the Paid Applications Agreement, add banking and tax forms (needed for any IAP; the app is free but sells IAP).
2. **App record**: Apps > **+** New App: iOS, name, primary language, bundle id `me.hapticgate.pause`, SKU. (The API cannot create app records.) `check` finds it by bundle id.
3. **Game Center**: the scripts create the Game Center detail if missing; confirm it is enabled for the app. `asc.py leaderboards --apply` creates both boards (Top Score `me.hapticgate.pause.top_score`, Boosted `me.hapticgate.pause.boosted_runs`, both BEST_SCORE, high to low, integer). There is no per-version attach step: the Game Center items in "Prepare for Submission" ship with the version submission.
4. **IAP**: set price (price points are not scripted: Paragon 4.99, Remove Ads 3.99, 850 pack 3.99, 2,500 pack 9.99, 6,000 pack 19.99 or nearest), upload each review screenshot, check availability and tax category, attach products to the version for review. Confirm the Family Sharing choice (off).
5. **App Privacy**: answer the nutrition-label questions from `docs/release/app-privacy-ios.md`. Privacy policy URL.
6. **Age rating** questionnaire, category (Games > Arcade/Action), copyright, support URL, contact (business phone / dev@ forwarders).
7. **Screenshots** (iPhone 6.9" and 6.5", iPad if universal), description, keywords, promo text, app icon in the build.
8. **Firebase console** (project for the shared leaderboard): iOS app > add the **App Store ID** and **Team ID** so Game Center auth works.
9. **AdMob**: once the app is live, Apps > the iOS app > **Add store** link (the iOS app ids are in the notes).
10. Nothing in code: `AchievementIds.IosIdsConfirmed` is already `true` and both leaderboard ids (Top Score, Boosted) are in `LeaderboardBoards`.
11. TestFlight / sandbox: test one unlock and one score submission before review.
