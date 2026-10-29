# Store products and rewards (Google Play, and the Apple plan)

Reference for every in-app product and reward set up on Google Play, so the same can be rebuilt on the App Store later.
Recorded from the Play Console on 2026-10-26 (app `me.hapticgate.pause`, developer account HapticGate) and checked against the code
on master (28ccb3775, PR #261). Related docs: `achievements-store-setup.md` (Play Games / Game Center ids and the Apple side of achievements),
`leaderboards.md` (App Store Connect Game Center steps), `paragon/` (Paragon dock screenshots), `economy.md` (star dust).

Status of the Apple side: **nothing exists yet**. The game's billing is Google Play only (see section 5), so the Apple section is a plan, not a record.

## 1. The four products side by side

All four are Play Console "One-time products", purchase option id `buy` (Buy type, must be Activated), Active, 174 countries/regions, tax category "Digital app sales".

| | Paragon Ship | Support the Developer | Support the Developer: Small Pack | Pre-registration reward |
|---|---|---|---|---|
| Product ID | `me.hapticgate.pause.ship_paragon` | `support_developer_2500` | `support_developer_850` | `prereg_stardust_100` |
| Type | non-consumable (permanent) | repeatable (game consumes it) | repeatable (game consumes it) | one-time, consumed by the game |
| Price | target $5, shows USD 4.99 (US) | USD 10.00 | target $4, shows USD 3.99 (US) | bulk 0.10 USD (Play rejects $0) |
| Grant | unlocks the Paragon hull (id 16) | +2,500 star dust | +850 star dust | +100 star dust once |
| Code on master | yes | yes | **no: in PR #262 / pending** | yes |
| Art | [paragon_ship_icon_1024.png](store-products/art/paragon_ship_icon_1024.png) (1024x1024, ship on dark navy) | [support_developer_2500_icon.png](store-products/art/support_developer_2500_icon.png) (512x512, dust pile + small pink heart); in-game sprite [support_developer_2500_ui.png](store-products/art/support_developer_2500_ui.png) (256x256 transparent) | reuses/varies the support art (no separate file saved) | [prereg_stardust_100_icon.png](store-products/art/prereg_stardust_100_icon.png) (512 product icon); [prereg_stardust_100_badge.png](store-products/art/prereg_stardust_100_badge.png) (512 reward badge) |

Names and descriptions as entered on Play:

1. Paragon Ship: "Unlock the Paragon, the premium ship with extra hearts, a fast start and its own weapons."
2. Support the Developer: "Support the developer of Pause and receive 2,500 star dust to spend in the game. Can be bought as many times as you like. Thank you!"
3. Support the Developer: Small Pack: "Support the developer of Pause and receive 850 star dust to spend in the game. Can be bought as many times as you like. Thank you!"
4. 100 Star Dust (Pre-registration Reward): "A thank-you for pre-registering for Pause: 100 star dust to spend in the game, added to your account the first time you open it."

Ship unlock gate (`ShipUnlocks`, `Ship/ShipUnlocks.cs`): the three dearest roster ships in dock order (Jade Phantom 5,300, Gold Warden 7,000 star dust, and Paragon last) can only be bought once the player owns other ships: Jade Phantom and Gold Warden need 4 owned, Paragon needs 5. Owned counts the free starter and every roster ship, read from the saved `boughtship<id>` keys (the ship being bought is not counted); no save format changed. Locked, the dock button reads "OWN 4 SHIPS" / "OWN 5 SHIPS" and does nothing; `shopingShips.TryPurchase`, `SpaceDock.Buy`, the post-death ship offer and `ParagonPurchase.RequestBuy` all refuse. The Play product, purchase restore and `PremiumShips.Grant` are untouched, and a ship already owned stays owned and usable whatever the count (a player who bought Paragon before reaching 5 ships keeps it). Tests: `ShipUnlocksTest`.

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
| 850 pack | `support_developer_850`: not on master; added by PR #262 (pack table in `SupportPurchase`) | pending |
| Pre-reg | `PreRegistrationReward.ProductId` (`Billing/PreRegistrationReward.cs:24`), `DustAmount = 100` | `Billing/PreRegistrationReward.cs` |

`Billing/PlayBilling.cs` is the only store code: Google Play Billing through JNI (no Unity IAP package), `#if UNITY_ANDROID && !UNITY_EDITOR`; elsewhere a stub (`PlayBilling.cs:463`). It queries product details for Paragon and the support product (inapp type, `PlayBilling.cs:131`), queries owned purchases on launch/return, and feeds every owned or updated purchase to each handler.

Rules:
- **Paragon (non-consumable):** `Purchased` -> `PremiumShips.Grant` (same `boughtship<id>` key the roster always used, so cloud save picks it up) and acknowledge the purchase (Play refunds unacknowledged purchases after 3 days; failures are retried on the next owned-purchases query, which is also the restore). `Pending` waits. Already-owned (response code 7) triggers a restore query.
- **Support packs (repeatable):** pay `StarDustLedger.Grant(DustAmount)`, then consume so it can be bought again. Idempotence: the purchase token is written to PlayerPrefs (`supportGrantedTokens`, newline separated) in the same save as the dust and removed only after a confirmed consume; a failed consume retries next launch without paying twice; a new purchase has a new token. `Pending` pays nothing.
- **Pre-reg reward:** identical token scheme with key `preregGrantedTokens`; it arrives without developerPayload and maybe without orderId, so neither is read. Sets `ToastDue`; the home screen shows "Pre-registration reward: +100 star dust" (`PlayBilling.cs:50-53`).
- **Android-only gating:** price comes from Play; `Available` is `Price != null && LaunchFlow != null`, so on iOS and in the editor no purchase UI appears (Paragon keeps its locked COMING SOON berth).
- **Toasts:** `ParagonPurchase.GrantedMessage` "Paragon unlocked"; `SupportPurchase.GrantedMessage` "Thank you for supporting the developer! +2,500 star dust" (the 850 pack shows its own amount in PR #262); pending and error messages exist for both.
- **UI on master:** a "SUPPORT THE DEVELOPER" header button (`Shop/SupportButton.cs`, sprite `Resources/Vfx/support_developer_2500_ui`) and support tiles in the dock rack (`Shop/DockSupportTile.cs`; the bottom news ticker was removed). `SpaceDock.cs` subscribes to `SupportPurchase.Changed/Rewarded`.
- **UI after PR #262:** the header button is removed; the two support packs become dock tiles in the two spare berths (slots 17 and 18 of 18) of the 16-ship rack (`Shop/DockSupportTile.cs`), shown only when Play returns a price; one ticker line per pack.
- **Editor/dev:** `SupportPurchase.Simulate()` and `PreRegistrationReward.Simulate()` (developer mode only) via `Editor/Tools/PreRegistrationRewardMenu.cs`.
- **Tests (run on CI only):** `PreRegistrationRewardTest`, `SupportPurchaseTest`, `ParagonPurchaseTest`, `PremiumShipTest` (all in `Pause/Assets/Editor/Tests/`).

## 5. Apple App Store plan

Enrollment: per the project notes the Apple Developer enrollment is an organization account (HapticGate LLC) and the D-U-N-S number was the critical path; whether the account is active is **not recorded in the repo: TO VERIFY**. In-app purchases and Paid Applications Agreement (banking, tax) must be complete in App Store Connect before products can be sold: TO VERIFY current requirements at entry time.

### 5.1 Code work still needed
The game has **no iOS purchase path**: `PlayBilling` compiles to a stub off Android, and `ParagonPurchase`/`SupportPurchase`/`PreRegistrationReward` all say iOS never gets a price (confirmed in code comments and the `#if` at `PlayBilling.cs:463`). iOS has Game Center (achievements/leaderboards) only. To sell on iOS:
1. Add a StoreKit layer (StoreKit 2 via a small native plugin, or Unity IAP) that fills `ParagonPurchase.SetPrice/LaunchFlow` and `SupportPurchase.SetPrice/LaunchFlow` and feeds transactions into the existing `Handle` methods. `PlayPurchase` is the shared shape (product id, token, state, acknowledged): map the StoreKit transaction id to `token`.
2. Keep the idempotence rule: record the transaction id with the dust grant, then call `finish()` on the transaction (the equivalent of consume). Handle `Pending` ("Ask to Buy") as Pending.
3. Paragon restore: expose a "Restore purchases" action (Apple review expects one for non-consumables; TO VERIFY) that re-reads current entitlements and calls `ParagonPurchase.Handle`.
4. Product ids: `ParagonProductId` already carries the iOS-friendly `me.hapticgate.pause.` prefix; the support ids and prereg id are Play-style short names. Apple product ids are per app and can also be reused as-is; decide whether to keep identical ids across stores (simplest, fewer code branches) or prefix them. Apple ids cannot be reused after deletion: TO VERIFY.
5. Dock tiles/ticker already key off `Available`, so they appear once a price is set.

### 5.2 Mapping and per-product checklist (App Store Connect > app > Monetization > In-App Purchases)

| Play product | Apple type | Notes |
|---|---|---|
| Paragon Ship | Non-Consumable | permanent; restorable |
| Support the Developer (2,500) | Consumable | repeatable |
| Support the Developer: Small Pack (850) | Consumable | repeatable |
| Pre-registration reward (100) | no direct equivalent | see 5.3 |

Fields to prepare per product (verify exact field names/limits in App Store Connect when you get there):
- Reference name (internal, e.g. "Paragon Ship") and Product ID (see 5.1.4).
- Price: Apple uses its own price points, not Play's 3.99/4.99/10.00. Closest to expect: Paragon 4.99, small pack 3.99, big pack 9.99 or 10.00 if that point exists: TO VERIFY in the price picker (Apple's US points are fixed values; pick the nearest and keep value-per-dust consistent: 850 for 3.99 vs 2,500 for 10.00).
- Display name and description per localization (start with English U.S.); reuse the Play texts above, checking Apple's length limits (TO VERIFY).
- Review screenshot (required per IAP; TO VERIFY size) and review notes. Capture the dock tile / Paragon buy sheet from an iOS build.
- Promotional image (1024x1024, optional): reuse [paragon_ship_icon_1024.png](store-products/art/paragon_ship_icon_1024.png) and the support art (upscale the 512 icon, or regenerate at 1024). TO VERIFY whether it is optional and its alpha rules.
- Tax category and availability: choose the equivalent of "Digital app sales" (app-store software/games IAP default) and all territories as on Play.
- Family Sharing for the non-consumable: decide (TO VERIFY; off is typical for a game cosmetic).

### 5.3 The pre-registration reward on Apple (all TO VERIFY)
Apple has app **pre-orders** (free or paid), not pre-registration with an item reward. Realistic options:
1. **Skip it** on iOS; simplest and no store risk.
2. **Promo / offer codes for the 100 dust**: Apple offers offer codes for in-app purchases (TO VERIFY availability for consumables and redemption limits); you would create a consumable (e.g. `prereg_stardust_100`, price tier 0.99 or free via code) and hand out codes. Needs the StoreKit work in 5.1 and a code distribution plan.
3. **One-time grant keyed to a pre-order**: no verified mechanism to detect a pre-order in the app; do not rely on this. Alternative: an app-side one-time launch grant for early adopters, which is not tied to Apple.
Reuse of the reward terms page `https://hapticgategames.com/pause/100dust-terms/` is fine for any option that mentions the 100 dust (it explains minimum-payment countries, which would not apply to Apple).

## 6. Open questions / decisions for the user
1. Keep identical product ids on Apple, or Apple-style prefixed ids?
2. Pre-registration reward on iOS: skip, offer codes, or something else?
3. Price points: accept nearest Apple points (4.99 / 3.99 / 9.99 or 10.00)?
4. Is the Apple organization enrollment active, and are the Paid Apps Agreement, banking and tax done?
5. StoreKit path: Unity IAP package or a small native StoreKit 2 plugin (the Android side deliberately avoids Unity IAP)?
6. Need a 1024 art variant for the 850 pack and for Apple's promotional images?
7. Unverified here: the pack 850 art file (not provided), and the state of PR #262 (open, not merged).
