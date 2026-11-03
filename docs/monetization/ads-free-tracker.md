# Free with ads: work tracker

Pause is moving from a paid game to a free game with ads. This page tracks the decisions, the open questions and the work. Last updated 2026-11-02.

## Decisions made

- First 1 hour of play is ad-free.
- $4 one-time Remove Ads, named "Support the devs by removing the ads".
- $20 / 6,000 dust pack added.
- Dust packs no longer say "support the developer".
- New scrollable Shop screen; the Space Dock stays as the hangar; Paragon is sold in the Shop and appears in the dock once bought.
- No forced ads while a booster is active.
- 2026-11-02: there are no existing buyers (the owner says nobody bought the game; Play Console shows the only paid-game order was refunded). No conversion and no ad-free grant: the existing-buyer code from #429 is removed. The question on existing buyers is closed.

## Open questions (need the user)

1. ~~Existing buyers~~ **Closed 2026-11-02**: there are none, so no conversion. The game is still paid on Play; going paid to free stays one-way and needs the owner's explicit go-ahead.
2. **Does buying Paragon or a dust pack also remove ads?** Proposed: Paragon yes, packs no. **Default used (phase 4): Paragon owners get no forced ads (`AdPolicy.ParagonRemovesAds = true`), dust packs do not remove ads.**
3. **Do Remove Ads owners still see the optional double dust ad?** Proposed: yes. **Default used (phase 4): yes (`AdPolicy.RemoveAdsOwnersSeeDoubleDust = true`).**
4. **Forced ad frequency.** The ask was one after each replay; proposed cap of one per 3 minutes and none after a very short run. **Default used (phase 4): `MinSecondsBetweenAds = 180`, `MinRunSecondsForAd = 45` (unfrozen flying seconds), and no forced ad right after a rewarded ad at the same death.**
5. **"Exp" booster.** Confirmed by search: the game has no XP, experience or level system (no such identifier in `Scripts/`). Built as a star dust multiplier only; no XP was invented. Needs a yes if the user meant something else.
6. **Hazard clear and leaderboards.** A run that used a hazard clear still counts for score and every leaderboard (the clear itself pays no score). It does help a player survive, so it is a paid advantage on the score board. Alternative: flag such runs like Paragon runs (`Boosters`/`HazardClear.UsedThisRun` already tells you). Which do you want?
7. **Booster prices.** 600 dust for 10 minutes of x2, 200 per hazard clear charge (derived in `docs/economy.md`). OK, or should the multiplier be cheaper/pricier?
8. **A cut-off sentence.** The message said "we also wanna always offer the options to ..." and then stopped. What was the rest?
9.  **Paragon's "own 5 ships" gate.** Today the Shop still refuses to sell Paragon until 5 ships are owned (shown as "OWN 5 SHIPS"). Should it go, now that Paragon is sold from a shop? It is one switch: `ShipUnlocks.GatesParagon = false`. Until decided, the gate stays.

## Work phases

- [ ] **1. Products in Play Console and billing code.** Code: PR https://github.com/Wintersina/Pause/pull/427 (Remove Ads entitlement and handler, cloud-save merge, 6,000 pack, dust-pack wording, tests, docs). Not complete until a real test purchase has been made.
  - [x] Play Console part, done and checked on the product list 2026-11-02: `me.hapticgate.pause.remove_ads` ("Support the devs by removing the ads", USD 3.99 US) and `stardust_6000` ("6,000 Star Dust", US set by hand to USD 20.00 because the 20 USD bulk price rounded to 19.99) are Active in 174 regions; the 2,500 and 850 packs are renamed "2,500 Star Dust" and "850 Star Dust" (prices unchanged).
  - [x] PR #427 merged.
  - [ ] Real test purchase of Remove Ads and a dust pack (purchase, acknowledge/consume, restore).
  - Still open: the two new products have no icon yet; the 850 and 2,500 icons and in-game tile art still show a pink heart, which reads as a donation; translations under "Manage translations" were not checked for the renamed packs.
- [ ] 2. Shop screen; move Paragon and the dust packs into it. Code: PR (see the Shop PR on `feat/shop-screen`). Done in code: SHOP row on the home menu, scrolling Shop with Remove Ads, Paragon and the three dust packs; dock sells nothing and points Paragon to the Shop; the two freed dock berths are gone and the lone last berth is centred. Boosters later add one section to `ShopCatalog.Sections()`. Not complete until a real purchase from the Shop has been made on a device. Art is placeholder (no new art was made).
- [ ] 3. Play-time tracker (for the first ad-free hour). PR: https://github.com/Wintersina/Pause/pull/428. `PlayTime` counts seconds of unfrozen flying in real runs only (not menus, tutorial, practice, world entries or boss entrances, and nothing while backgrounded). **Frozen PAUSE time does not count** (the stricter reading: one honest hour of flying). Saved in PlayerPrefs (`playTimeSeconds`) and in the cloud save (merged by the larger value); older saves read as 0.
- [ ] 4. Ads SDK (AdMob), consent prompt, forced and rewarded ads, frequency rules. PR: https://github.com/Wintersina/Pause/pull/432 (stacked on #428). Code is in and **switched off**: `AdsConfig.MasterSwitch = false`.
  - What ships: Google Mobile Ads Unity plugin 11.6.0 (Android SDK 25.5.0, iOS pod ~> 13.11, UMP 4.0.0 / 3.1.0), `AdPolicy` (pure rules), `AdController` (consent, preload, interstitial before Replay, rewarded double star dust, timeouts), `DoubleDustOffer` (death panel button), `PrivacyOptionsRow` (Options), iOS App Tracking Transparency prompt after UMP (`PauseATT.mm`).
  - **Boosters:** nothing in this PR depends on boosters. The follow-up (`feat/booster-shop-wiring`) wired `AdPolicy.BoosterActiveCheck` to `Boosters.AnyActive` (its field default, so it holds in every build), made double dust pay on the unboosted base (`DoubleDustRules.Payout`, total stays at the 3x cap) and added the two booster rows to the Shop.
  - **AdMob state (created 2026-11-02, publisher pub-4397064133944218):** Android and iOS apps and their units ("Pause Forced Ad" interstitial, "Pause Double Dust" rewarded) exist and the real IDs are already in `AdsConfig.cs` (both platforms, so phase 7 only has to flip `EnabledOnIos`). Both apps show "Requires review" with limited serving. Android waits on `app-ads.txt` being published and the payment profile; iOS waits on an App Store listing being added to its AdMob app. Developer mode (editor, dev, PAUSE_DEV builds) uses Google's test units and dev builds carry the test App ID, so our own devices never request real ads.
  - **Switching ads on (the user, later):** (1) get the apps through AdMob review (above); (2) update the Play Console declarations (contains ads, Data safety, advertising ID) and publish `app-ads.txt` on hapticgategames.com; (3) set `AdsConfig.MasterSwitch = true`. Until then the Android build strips the SDK's advertising-ID permissions, init provider and WorkManager foreground service from the manifest (`AdsManifestGuard`). iOS needs `EnabledOnIos = true` as well, and **that must stay false until StoreKit ships (phase 7)**: iOS has no purchase code, so an iOS player could not buy Remove Ads.
  - **Heads-up even while the switch is off:** the SDK is in the build. `AdsManifestGuard` strips `AD_ID`, `ACCESS_ADSERVICES_*`, `FOREGROUND_SERVICE`, `WAKE_LOCK`, the SDK init provider and WorkManager's foreground service. Still merged in: `ACCESS_NETWORK_STATE`, the SDK's ad activities, WorkManager receivers/jobs, Custom Tabs queries and the app ID metadata (inert). Compare the release manifest in Play Console's bundle explorer before the first release with the SDK.
  - **Testing on a phone with Google's test ads (dev builds):** build a dev APK (`make android-dev-deploy`). A PAUSE_DEV / development build starts in developer mode, which forces ads on and skips the free hour and Paragon ownership, so test ads show with the shipped test IDs. Fly a run of 45+ seconds, die, tap REPLAY: an interstitial (labelled "Test Ad") should play first, then the next run starts; a second one will not show for 3 minutes. The "WATCH AN AD  DOUBLE STAR DUST" tab hangs under the death panel once a rewarded test ad has loaded; watching it adds the run's dust a second time (once only). In the EEA/UK (or with a VPN/test device ID set in AdMob) the consent form appears at launch and Options shows PRIVACY OPTIONS. A release build cannot be in developer mode, so it shows no ads until `MasterSwitch` is flipped.
  - **iOS state:** the project exports for iOS with the plugin (Unity 6000.3.23f1 `BuildScript.BuildIOS` succeeded; Info.plist gets `GADApplicationIdentifier`, `NSUserTrackingUsageDescription` and 50 SKAdNetwork IDs; AppTrackingTransparency linked; a Podfile names the Google Mobile Ads and UMP pods). NOT verified: `pod install` and an Xcode compile (CocoaPods is not installed on this Mac), and nothing was run on an iOS device. `EnabledOnIos` **must stay false until StoreKit (phase 7) ships**, so an iOS build never shows ads with no way to remove them.
  - **What the user must supply:** nothing for the IDs (done). Remaining: AdMob review, `app-ads.txt`, payment profile, App Store listing for iOS, Play declarations, then the switch.
- [ ] 5. Boosters (code done, PR pending merge). Dust x2 for 10 minutes of play (600 dust) and hazard clear charges (200 dust each), both bought with star dust and repeatable; `Boosters.AnyActive` is true while the multiplier has time left (hazard charges held do NOT count, so unheld ads never switch off for ever). Shop row API: `Boosters.Name/Description/Price/Status/CanBuy/Buy`; now wired into the Shop (BOOSTERS section; bought with dust, works without Play billing, not blocked by the single-purchase-flow guard). Defaults: 3x cap with the rewarded ad, no boosters in the tutorial.
- [ ] 6. Going free (step by step: [go-free-checklist.md](go-free-checklist.md)): Console declarations (contains ads, Data safety, advertising ID), privacy policy, `app-ads.txt` on hapticgategames.com, price change (owner's explicit go-ahead first; one-way).
- [ ] 7. iOS. **Blocked: starts when the Apple Developer account is accepted** (owner decision 2026-11-02: todos only, no iOS work for now). Detail: [go-free-checklist.md](go-free-checklist.md) section 4.
  - [ ] **[user]** Apple Developer organization enrollment for HapticGate LLC: pending acceptance. Then the bundle ID.
  - [ ] **[agent/user]** Install CocoaPods, run `pod install` on the exported project and compile in Xcode. Never done with the ads plugin.
  - [ ] **[agent]** StoreKit layer: Remove Ads, dust packs, Paragon, restore, cloud-save merge. The Shop must show real App Store prices; rows stay UNAVAILABLE until then.
  - [ ] **[user]** Create the same products in App Store Connect.
  - [ ] **[agent]** Boosters and the Shop checked on iOS.
  - [ ] **[user]** ATT prompt, SKAdNetwork IDs and consent form checked on the iOS simulator with Google's test ads first (the real ad units are not used for testing).
  - [ ] **[user]** Get access to a real iPhone for a check before App Store release, and confirm what the simulator cannot cover (re-check at the time).
  - [ ] **[user]** App Privacy labels, and age rating with ads.
  - [ ] **[user]** After the App Store listing is live: AdMob > iOS app > Add store, then get it through AdMob review.
  - [ ] **[agent]** Set `AdsConfig.EnabledOnIos = true` only in the release where Remove Ads can be bought on iOS.
  - [ ] **[user]** Game Center and sign-in checked; the Game Center ids and steps are in [achievements-store-setup.md](../achievements-store-setup.md) and [leaderboards.md](../leaderboards.md).
