# Store products and rewards (Google Play, and the Apple plan)

Reference for every in-app product and reward set up on Google Play, so the same can be rebuilt on the App Store later.
Recorded from the Play Console on 2026-10-26 (app `me.hapticgate.pause`, developer account HapticGate) and checked against the code
on master (28ccb3775, PR #261). Related docs: `achievements-store-setup.md` (Play Games / Game Center ids and the Apple side of achievements),
`leaderboards.md` (App Store Connect Game Center steps), `paragon/` (Paragon dock screenshots), `economy.md` (star dust).

Status of the Apple side: StoreKit 2 code exists (section 5); the App Store Connect products still have to be created by hand from the table in 5.2.

## 0. Free-with-ads update (2026-11-02)

Pause is moving to a free game with ads (tracker: `monetization/ads-free-tracker.md`). Product changes, code in PR "feat/iap-remove-ads-and-dust-pack":

| Product ID | Type | Price | Name (Play Console, set by the user) | Grant |
|---|---|---|---|---|
| `me.hapticgate.pause.remove_ads` (new) | one-time, permanent | target $4 (shows USD 3.99) | "Support the devs by removing the ads" | permanent ad-free entitlement (`AdFree.IsActive`) |
| `stardust_6000` (new) | repeatable, consumed | USD 20.00 | "6,000 Star Dust" | +6,000 star dust |
| `support_developer_2500` (id can't change) | repeatable | USD 10.00 | renamed "2,500 Star Dust" | +2,500 star dust |
| `support_developer_850` (id can't change) | repeatable | target $4 (USD 3.99) | renamed "850 Star Dust" | +850 star dust |

The phrase "support the devs" now belongs only to Remove Ads; the dust packs are plain star dust packs (in-game toasts read "+850 star dust" and so on). The Console side was done by the user and checked on the product list on 2026-11-02: both new products are Active, purchase option `buy`, 174 countries/regions, Digital app sales. Remove Ads shows USD 3.99 in the US (4 USD bulk price), description "Support the developers and remove all forced ads from Pause, permanently." `stardust_6000` shows USD 20.00 in the US (the 20 USD bulk price rounded to 19.99, so the US was set by hand; other regions keep Play's converted price), description "6,000 star dust to spend in the game. Can be bought as many times as you like." The 2,500 and 850 packs are renamed "2,500 Star Dust" and "850 Star Dust" with descriptions "<amount> star dust to spend in the game. Can be bought as many times as you like."; prices unchanged. Still open: the new products have no icon; the 850 and 2,500 icons and tile art still show a pink heart (reads as a donation); translations of the renamed packs were not checked. The section 1 table and name list below keep the original (pre-2026-11-02) names for the record.

Remove Ads (`Billing/RemoveAdsPurchase.cs`, `Billing/AdFree.cs`): non-consumable, Paragon pattern. `Purchased` -> `AdFree.Grant` (PlayerPrefs key `adFreeOwned`) and acknowledge (retried on every owned-purchases query, which is also the restore); `Pending` waits; response code 7 triggers a restore query. Future ad code checks `AdFree.IsActive`. The flag is in the cloud save (`ProgressSnapshot.adFree`, additive, no schema bump): merged by OR in `ProgressMerge`, never cleared by `Apply`, counts as progress in `HasProgress`. An older save lacking the field reads as false and cannot take ownership away. Toasts: "Ads removed. Thank you for supporting the devs!", pending "Purchase pending. Ads are removed when the payment completes.". Sold on the Shop screen (section 6, UI after the Shop PR).

6,000 pack: one more row in `SupportPurchase.Packs` (`SupportPurchase.Huge`), same token-with-the-dust idempotence rule and consume; sold on the Shop screen like the other packs. Tests: `RemoveAdsPurchaseTest`, `SupportPurchaseTest`.

## 1. The four products side by side

All four are Play Console "One-time products", purchase option id `buy` (Buy type, must be Activated), Active, 174 countries/regions, tax category "Digital app sales".

| | Paragon Ship | Support the Developer | Support the Developer: Small Pack | Pre-registration reward |
|---|---|---|---|---|
| Product ID | `me.hapticgate.pause.ship_paragon` | `support_developer_2500` | `support_developer_850` | `prereg_stardust_100` |
| Type | non-consumable (permanent) | repeatable (game consumes it) | repeatable (game consumes it) | one-time, consumed by the game |
| Price | target $5, shows USD 4.99 (US) | USD 10.00 | target $4, shows USD 3.99 (US) | bulk 0.10 USD (Play rejects $0) |
| Grant | unlocks the Paragon hull (id 16) | +2,500 star dust | +850 star dust | +100 star dust once |
| Code on master | yes | yes | yes (on master; the "pending in PR #262" wording below is out of date) | yes |
| Art | [paragon_ship_icon_1024.png](store-products/art/paragon_ship_icon_1024.png) (1024x1024, ship on dark navy) | [support_developer_2500_icon.png](store-products/art/support_developer_2500_icon.png) (512x512, dust pile + small pink heart); in-game sprite [support_developer_2500_ui.png](store-products/art/support_developer_2500_ui.png) (256x256 transparent) | reuses/varies the support art (no separate file saved) | [prereg_stardust_100_icon.png](store-products/art/prereg_stardust_100_icon.png) (512 product icon); [prereg_stardust_100_badge.png](store-products/art/prereg_stardust_100_badge.png) (512 reward badge) |

Names and descriptions as first entered on Play (items 2 and 3 were renamed on 2026-11-02, see section 0):

1. Paragon Ship: "Unlock the Paragon, the premium ship with extra hearts, a fast start and its own weapons."
2. Support the Developer: "Support the developer of Pause and receive 2,500 star dust to spend in the game. Can be bought as many times as you like. Thank you!"
3. Support the Developer: Small Pack: "Support the developer of Pause and receive 850 star dust to spend in the game. Can be bought as many times as you like. Thank you!"
4. 100 Star Dust (Pre-registration Reward): "A thank-you for pre-registering for Pause: 100 star dust to spend in the game, added to your account the first time you open it."

Ship unlock gate (`ShipUnlocks`, `Ship/ShipUnlocks.cs`): the three dearest roster ships in dock order (Jade Phantom 9,100, Gold Warden 12,000 star dust, and Paragon last) can only be bought once the player owns other ships: Jade Phantom and Gold Warden need 4 owned, Paragon needed 5 until 2026-11-08, when the user dropped its gate (`ShipUnlocks.GatesParagon = false`: Paragon is buyable with only the starter owned; the two dust ships keep their 4). Owned counts the free starter and every roster ship, read from the saved `boughtship<id>` keys (the ship being bought is not counted); no save format changed. Locked, the dock button reads "OWN 4 SHIPS" / "OWN 5 SHIPS" and does nothing; `shopingShips.TryPurchase`, `SpaceDock.Buy`, the post-death ship offer and (while `GatesParagon` is on) `ParagonPurchase.RequestBuy` all refuse. The Play product, purchase restore and `PremiumShips.Grant` are untouched, and a ship already owned stays owned and usable whatever the count (a player who bought Paragon before reaching 5 ships keeps it). Tests: `ShipUnlocksTest`.

Paragon is a real-money cheat ship: it is not on leaderboards and star dust never buys it (`PremiumShips.DustPurchasable`, `shopingShips.TryPurchase` refuses it).

## 2. Pre-registration reward on Play

- Play Console > Pre-registration > Rewards: choose product `prereg_stardust_100`, upload the 512x512 reward badge (PNG/JPEG, up to 1 MB), and give a **required Terms and Conditions URL**: `https://hapticgategames.com/pause/100dust-terms/` (website PR https://github.com/Wintersina/hapticgate-site/pull/4). The terms page says that where a country requires a minimum payment the player pays it to receive the reward.
- Why the product costs 0.10 USD: Play cannot save a $0 one-time product ("Price must be between USD 0.05 and USD 999.99", with per-currency minimums). The bulk price 0.10 USD worked; some regions round up to their own minimum (CAD 0.10, AUD 0.10, BRL 0.30, PEN 0.30).
- Limits Play states: one reward per pre-registration campaign; it cannot be edited or deleted after saving; it must be a product made specifically for it and be active before the campaign starts. When recorded, the pre-registration track itself was still "Inactive".

## 3. Play Console gotchas learned

- A product ID can never be changed or reused once created.
- Name up to 55 characters, description 50 to 200, icon 1:1 PNG 512 to 1080 px.
- The purchase option stays Draft until you click Activate on it separately.
- Free ($0) one-time products are rejected. After a bulk price edit, check the per-region minimum-price errors (bulk 0.10 USD, then fix what it flags).
- The "Monetize with Play" page stays gated ("Finish setting up your app"), but One-time products is still reachable through the Products menu.
- Play Games OAuth needs the Play App Signing SHA-1, not only the upload-key SHA-1 (see `achievements-store-setup.md`).

## 4. Game-side contract (verified in code)

| Product | Hard-coded id | Handler |
|---|---|---|
| Paragon | `PremiumShips.ParagonProductId` (`Pause/Assets/Scripts/Ship/PremiumShips.cs:38`) | `Billing/ParagonPurchase.cs` |
| 2,500 pack | `SupportPurchase.ProductId` (`Billing/SupportPurchase.cs:25`), `DustAmount = 2500` | `Billing/SupportPurchase.cs` |
| 850 pack | `support_developer_850` (`SupportPurchase.Small`) | `Billing/SupportPurchase.cs` |
| 6,000 pack | `stardust_6000` (`SupportPurchase.Huge`) | `Billing/SupportPurchase.cs` |
| Remove Ads | `RemoveAdsPurchase.ProductId` = `me.hapticgate.pause.remove_ads` | `Billing/RemoveAdsPurchase.cs` |
| Pre-reg | `PreRegistrationReward.ProductId` (`Billing/PreRegistrationReward.cs:24`), `DustAmount = 100` | `Billing/PreRegistrationReward.cs` |

`Billing/PlayBilling.cs` is the only store code: Google Play Billing through JNI (no Unity IAP package), `#if UNITY_ANDROID && !UNITY_EDITOR`; elsewhere a stub (`PlayBilling.cs:463`). It queries product details for Paragon and the support product (inapp type, `PlayBilling.cs:131`), queries owned purchases on launch/return, and feeds every owned or updated purchase to each handler.

Rules:
- **Paragon (non-consumable):** `Purchased` -> `PremiumShips.Grant` (same `boughtship<id>` key the roster always used, so cloud save picks it up) and acknowledge the purchase (Play refunds unacknowledged purchases after 3 days; failures are retried on the next owned-purchases query, which is also the restore). `Pending` waits. Already-owned (response code 7) triggers a restore query.
- **Support packs (repeatable):** pay `StarDustLedger.Grant(DustAmount)`, then consume so it can be bought again. Idempotence: the purchase token is written to PlayerPrefs (`supportGrantedTokens`, newline separated) in the same save as the dust and removed only after a confirmed consume; a failed consume retries next launch without paying twice; a new purchase has a new token. `Pending` pays nothing.
- **Pre-reg reward:** identical token scheme with key `preregGrantedTokens`; it arrives without developerPayload and maybe without orderId, so neither is read. Sets `ToastDue`; the home screen shows "Pre-registration reward: +100 star dust" (`PlayBilling.cs:50-53`).
- **Store gating:** price comes from the store layer; `Available` is `Price != null && LaunchFlow != null`, so in the editor (and on iOS until the App Store answers, section 5) every Shop row reads UNAVAILABLE and nothing looks buyable. The Shop itself has no platform-specific UI: a later StoreKit layer only sets `Price` and `LaunchFlow`.
- **Toasts:** `ParagonPurchase.GrantedMessage` "Paragon unlocked"; each `SupportPurchase.Pack.grantedMessage` is now just "+2,500 star dust" / "+850 star dust" / "+6,000 star dust" (it used to read "Thank you for supporting the developer! ..."); pending and error messages exist for both.
- **UI on master:** a "SUPPORT THE DEVELOPER" header button (`Shop/SupportButton.cs`, sprite `Resources/Vfx/support_developer_2500_ui`) and support tiles in the dock rack (`Shop/DockSupportTile.cs`; the bottom news ticker was removed). `SpaceDock.cs` subscribes to `SupportPurchase.Changed/Rewarded`.
- **UI after PR #262:** the header button is removed; the two support packs become dock tiles in the two spare berths (slots 17 and 18 of 18) of the 16-ship rack (`Shop/DockSupportTile.cs`), shown only when Play returns a price; one ticker line per pack.
- **UI after the Shop PR (current):** a Shop screen (`Shop/ShopPanel.cs`, rows from `Shop/ShopCatalog.cs`) opens over the home menu from a SHOP row under Space dock (`Shop/ShopHomeEntry.cs`; on every platform). It scrolls and has three sections: ADS (Remove Ads: "Support the devs by removing the ads", Play's price, OWNED once bought, no second purchase), SHIP (Paragon, with its leaderboard warning and no ownership gate since 2026-11-08, formerly "OWN 5 SHIPS") and STAR DUST (850, 2,500, 6,000, each with Play's price, repeatable). Rows with no price read UNAVAILABLE and do nothing. Purchase results keep the existing toasts; the dust read-out follows `SupportPurchase.Rewarded`. Restore is automatic (the owned-purchases query on every launch and focus return), and the Shop says so. The dock no longer sells anything: the pack tiles and `Pack.inDock` are gone, Paragon's berth (tag SHOP) and card button point to the Shop, and the lone last berth is centred in the rack. A Boosters section later is one more entry in `ShopCatalog.Sections()`. Art: placeholders from existing assets (the dock's star dust glyph, Paragon's hull, a NO ADS tile); the pink-heart pack art is unused. Tests: `ShopScreenTest`, plus the updated `ShopTest`, `SupportPurchaseTest`, `ParagonPurchaseTest`, `PremiumShipTest`.
- **Editor/dev:** `SupportPurchase.Simulate()` and `PreRegistrationReward.Simulate()` (developer mode only) via `Editor/Tools/PreRegistrationRewardMenu.cs`.
- **Tests (run on CI only):** `PreRegistrationRewardTest`, `SupportPurchaseTest`, `ParagonPurchaseTest`, `PremiumShipTest` (all in `Pause/Assets/Editor/Tests/`).

## 5. Apple App Store (StoreKit 2): record and setup

Status: **code written, not yet exercised on a device** (PR `feat/ios-storekit`). The Apple Developer account is HapticGate LLC (approved 2026-11-20). Before any product can be sold, App Store Connect needs the **Paid Applications Agreement** (banking and tax) completed and the app record created for bundle id `me.hapticgate.pause`: TO VERIFY current requirements at entry time. In-App Purchase needs no entitlement in the app; just make sure the capability is on for the App ID (it is on by default).

### 5.1 How the code works
- `Plugins/iOS/PauseStoreKit.swift`: StoreKit 2 bridge (iOS 15+): `Product.products`, `purchase()`, `Transaction.updates` (listener runs for the app's whole life), `Transaction.unfinished`, `Transaction.currentEntitlements`, `AppStore.sync()`. It talks to C# through `@_cdecl` functions and one callback (kind + tab-separated payload).
- `Billing/AppStoreBilling.cs`: the iOS MonoBehaviour (`#if UNITY_IOS && !UNITY_EDITOR`; a no-op elsewhere). It starts the bridge, loads the products, and queues native messages onto Unity's main thread.
- `Billing/AppleStore.cs` (plain C#, every platform, covered by `AppleStoreTest` on CI): the **id table** (`AppleProducts`), message parsing, the persisted finished-transaction ledger (`AppleStoreLedger`, key `appleFinishedTx`, last 64) and `AppleStoreRouter`, which sets `Price`/`LaunchFlow` on the existing purchase classes (so the Shop rows turn buyable by themselves) and feeds every transaction into the existing `ParagonPurchase.Handle`, `RemoveAdsPurchase.Handle` and `SupportPurchase.Handle` (StoreKit transaction id -> `PlayPurchase.token`).
- Consumables (star dust packs): granted once per transaction id (`SupportPurchase`'s token record plus the ledger), and `finish()` is called only after the dust was saved. A crash between grant and finish retries the finish at the next launch without paying twice; a refunded (revoked) consumable pays nothing and is just finished.
- Non-consumables (Paragon, Remove Ads): re-derived from `Transaction.currentEntitlements` at every launch, on every return to the app and on Restore, and granted idempotently. Like on Play, a refund never takes ownership away from the game or the cloud save.
- Ask to Buy / SCA: `purchase()` returns `.pending`; nothing is granted, the pending toast shows, and the approved transaction arrives later through `Transaction.updates`. A cancelled sheet just frees the button.
- Pre-registration reward: **skipped on iOS** (option 1 below). `prereg_stardust_100` is not in the table.
- **Restore Purchases** is a button at the bottom of the Shop screen (scroll content, under the last row, above the note), shown on iOS only (`ShopCatalog.RestoreVisible`). It calls `AppStore.sync()` (may prompt for the Apple ID), re-reads the entitlements, grants what is missing and toasts "Purchases restored." or "Your purchases are up to date.". It reads RESTORING... while busy.
- Build: `IOSCapabilitiesPostProcess` links `StoreKit.framework`, sets `SWIFT_VERSION=5.0` on UnityFramework and `ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES=YES` on the app target (Swift in a Unity plugin).

### 5.2 Product ids: identical to Google Play
Apple product ids are free-form per app, so the Play ids are reused unchanged: one id per product on both stores, no per-platform branches. They live in one table (`AppleProducts.All()` in `Billing/AppleStore.cs`, built from the same constants Play uses). An Apple product id can never be reused after deletion (TO VERIFY), so type them exactly.

| Reference name | Product ID | Type | Suggested price point (USD) | Display name | Description |
|---|---|---|---|---|---|
| Paragon Ship | `me.hapticgate.pause.ship_paragon` | Non-Consumable | 4.99 | Paragon Ship | Unlock the premium Paragon ship. |
| Remove Ads | `me.hapticgate.pause.remove_ads` | Non-Consumable | 3.99 | Remove Ads | Remove all forced ads, forever. |
| Star Dust 850 | `support_developer_850` | Consumable | 3.99 | 850 Star Dust | 850 star dust to spend in the game. |
| Star Dust 2500 | `support_developer_2500` | Consumable | 9.99 | 2,500 Star Dust | 2,500 star dust to spend in the game. |
| Star Dust 6000 | `stardust_6000` | Consumable | 19.99 | 6,000 Star Dust | 6,000 star dust to spend in the game. |

Notes:
- Apple prices come from its own price points; the nearest US points are used above (Play's 10.00 and 20.00 become 9.99 and 19.99). Keep value per dust consistent across the three packs. Pick the same or comparable points for other territories, or accept Apple's auto-conversion.
- Texts are the Play ones shortened: Apple limits are about 30 characters for the display name and 45 for the description (TO VERIFY in App Store Connect). The long Play descriptions in section 1 do not fit.
- Per product (App Store Connect > the app > Monetization > In-App Purchases): reference name, product id, type, availability (all territories, as on Play), price, English (U.S.) localization above, tax category (the default for app software/games), Family Sharing off for the non-consumables (typical for a game; TO VERIFY), a **review screenshot** (required per product; capture the Shop screen from an iOS build) and optional review notes. A product shows as "Ready to Submit"; submit the first ones **together with the app version** for review (TO VERIFY).
- Optional promotional images: [paragon_ship_icon_1024.png](store-products/art/paragon_ship_icon_1024.png) for Paragon; the dust packs use art that is not 1024 yet.
- The first in-app purchase review needs the Restore Purchases button (it is in the Shop) and working products in a build.

### 5.3 The pre-registration reward on Apple
Decision: **skip it on iOS.** Apple has pre-orders, not pre-registration with an item reward, and there is no verified way to detect a pre-order in the app. If an early-adopter gift is wanted later, do it app-side (not tied to Apple) or as an offer-code consumable (TO VERIFY availability); both are out of this change.

### 5.4 Testing
1. **Xcode StoreKit configuration file (local, no App Store Connect needed):** [store-products/Pause.storekit](store-products/Pause.storekit) holds the five products above with the same ids, types and prices. Build iOS from Unity, open the generated Xcode project, Product > Scheme > Edit Scheme > Run > Options > StoreKit Configuration > pick that file (copy it next to the project if Xcode wants it inside). Run on a device or simulator: the Shop prices appear, purchases succeed without payment, and Xcode's Debug > StoreKit > Manage Transactions can approve/decline Ask to Buy, refund (revoke) a transaction, delete a non-consumable (to try Restore) and make a purchase fail. The file's `identifier` is arbitrary; regenerate in Xcode if it complains about the format version.
2. **Sandbox tester (real App Store Connect products):** App Store Connect > Users and Access > Sandbox > Testers: create a tester with a fresh email. On the device: Settings > Developer (appears after a run from Xcode) > Sandbox Apple Account, or sign in when the purchase sheet asks. Products must exist in App Store Connect first (Ready to Submit is enough) and the Paid Applications Agreement must be active, then run a TestFlight or Xcode build with a distribution/development profile. Sandbox purchases are free; test: buy each pack twice (dust adds up), buy Paragon and Remove Ads, delete and reinstall the app then tap RESTORE PURCHASES, and turn on "Ask to Buy" for the tester family member to see the pending path.
3. CI covers only the platform-independent half (`AppleStoreTest`, the Shop restore layout in `ShopScreenTest`). The Swift file and `AppStoreBilling` only compile in an iOS build, so the first local iOS build is the compile check.

## 6. Open questions / decisions for the user
1. Keep identical product ids on Apple, or Apple-style prefixed ids?
2. Pre-registration reward on iOS: skip, offer codes, or something else?
3. Price points: accept nearest Apple points (4.99 / 3.99 / 9.99 or 10.00)?
4. Is the Apple organization enrollment active, and are the Paid Apps Agreement, banking and tax done?
5. StoreKit path: Unity IAP package or a small native StoreKit 2 plugin (the Android side deliberately avoids Unity IAP)?
6. Need a 1024 art variant for the 850 pack and for Apple's promotional images?
7. Unverified here: the pack 850 art file (not provided), and the state of PR #262 (open, not merged).
