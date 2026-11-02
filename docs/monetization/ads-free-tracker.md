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
2. **Does buying Paragon or a dust pack also remove ads?** Proposed: Paragon yes, packs no.
3. **Do Remove Ads owners still see the optional double dust ad?** Proposed: yes.
4. **Forced ad frequency.** The ask was one after each replay; proposed cap of one per 3 minutes and none after a very short run.
5. **"Exp" booster.** The game has no XP system that we have found. Proposed: dust only.
6. **A cut-off sentence.** The message said "we also wanna always offer the options to ..." and then stopped. What was the rest?

## Work phases

- [ ] **1. Products in Play Console and billing code.** Code: PR https://github.com/Wintersina/Pause/pull/427 (Remove Ads entitlement and handler, cloud-save merge, 6,000 pack, dust-pack wording, tests, docs). Not complete until a real test purchase has been made.
  - [x] Play Console part, done and checked on the product list 2026-11-02: `me.hapticgate.pause.remove_ads` ("Support the devs by removing the ads", USD 3.99 US) and `stardust_6000` ("6,000 Star Dust", US set by hand to USD 20.00 because the 20 USD bulk price rounded to 19.99) are Active in 174 regions; the 2,500 and 850 packs are renamed "2,500 Star Dust" and "850 Star Dust" (prices unchanged).
  - [x] PR #427 merged.
  - [ ] Real test purchase of Remove Ads and a dust pack (purchase, acknowledge/consume, restore).
  - Still open: the two new products have no icon yet; the 850 and 2,500 icons and in-game tile art still show a pink heart, which reads as a donation; translations under "Manage translations" were not checked for the renamed packs.
- [ ] 2. Shop screen; move Paragon and the dust packs into it.
- [ ] 3. Play-time tracker (for the first ad-free hour). PR: https://github.com/Wintersina/Pause/pull/428. `PlayTime` counts seconds of unfrozen flying in real runs only (not menus, tutorial, practice, world entries or boss entrances, and nothing while backgrounded). **Frozen PAUSE time does not count** (the stricter reading: one honest hour of flying). Saved in PlayerPrefs (`playTimeSeconds`) and in the cloud save (merged by the larger value); older saves read as 0.
- [ ] 4. Ads SDK (AdMob proposed), consent prompt, forced and rewarded ads, frequency rules.
- [ ] 5. Boosters, after measuring typical dust income.
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
