# Go-free checklist (launch day)

Part of the [ads-free tracker](ads-free-tracker.md), phase 6. Prepared 2026-11-02. **Nothing here has been done yet**: Pause is still a paid app on Google Play at USD 1.99, and nobody has touched the price, the Data safety form or AdMob.

Legend: **[user]** only the user can do it (account, legal, money). **[agent]** a coding agent can do it. Tick boxes as you go. Where Google's or Apple's rules may have changed, the item says "re-check at the time": do that rather than trusting this page. Sources are linked inline; "unconfirmed" means I could not verify it from an official page.

## 0. What must already be true (do not start until all are ticked)

- [ ] **[agent]** PR #427 (Remove Ads entitlement and purchase handler) is merged (done), and the shop screen / play-time tracker / ads SDK phases (tracker 2 to 4) are merged.
- [ ] **[user]** A real test purchase of Remove Ads and of a dust pack has been made on a device (purchase, acknowledge, consume, restore). Still outstanding on 2026-11-02.
- [ ] **[user]** Remove Ads and `stardust_6000` are Active in Play Console (done 2026-11-02, 174 regions). The 2,500 and 850 packs were renamed (done). Open: icons for the two new products, the pink-heart art on the 850 and 2,500 packs, and translations of the renamed packs.
- [ ] **[user]** The Play store listing's website field is exactly `https://hapticgategames.com` (needed for `app-ads.txt` verification, section 2).
- [ ] **[user]** The hapticgate-site privacy-policy PR is reviewed by a human (legal text) and ready to merge at go-live (sections 5 and 6).

## No existing buyers (decided 2026-11-02)

Nobody has actually bought the game (the only paid-game order was refunded), so there is no buyer conversion and no ad-free grant. Going free needs no release ordering: the price flip can happen whenever the rest of section 0 is done and the owner gives the explicit go-ahead.

## 1. Google Play Console (all [user])

Ads-related declarations must be correct at the time of the release that contains ads. Menu names change: re-check each at the time.

- [ ] **Ads declaration.** App content > Ads: "Yes, my app contains ads". ([Play help](https://support.google.com/googleplay/android-developer/answer/6334373) covers pricing; the ads declaration sits in App content. Unconfirmed page link: re-check.)
- [ ] **Advertising ID declaration.** App content > Advertising ID: "Yes". Uses: Advertising or marketing, Analytics, Fraud prevention (these match what the SDK does, below). The manifest needs `com.google.android.gms.permission.AD_ID` when targeting Android 13+; the Google Mobile Ads SDK normally merges it in, but [agent] must check the merged manifest of the built AAB (Play blocks the release if the declaration says yes and the permission is missing). Source: Play Console help text relayed in community threads; the official page was not fetched: re-check.
- [ ] **Data safety form** (App content > Data safety). Keep existing answers (Play Games / Billing) and add what the ad SDK does. Source: [Google Mobile Ads SDK Play data disclosure](https://developers.google.com/admob/android/play-data-disclosure) (the page described version 25.5.0 when checked on 2026-11-02; check the version used in the build). Google says developers are solely responsible for the answers.

  | Play data type | Collected | Shared | Purposes | Basis / notes |
  |---|---|---|---|---|
  | Approximate location | Yes | Yes | Advertising or marketing; Analytics; Fraud prevention | Google: the SDK collects the IP address, which "may be used to estimate the device's general location". Mapping IP to this type is my inference. |
  | App interactions | Yes | Yes | Advertising or marketing; Analytics; Fraud prevention | Google: "user product interactions: app launch, taps, video views". |
  | Diagnostics | Yes | Yes | Analytics; Fraud prevention (the page lists advertising, analytics and fraud prevention overall) | Google: app launch time, hang rate, energy usage. |
  | Device or other IDs | Yes | Yes | Advertising or marketing; Analytics; Fraud prevention | Google: Android advertising ID, app set ID, other account identifiers. Ad ID can be reset or deleted by the user and blocked by manifest. |

  - Data encrypted in transit: **Yes** (Google: TLS).
  - "Collection is optional / users can choose": Google says the ad ID is optional for the user (Android settings; Limited Ads); the other items are automatic. Whether to tick "optional" for the ID is the user's judgement; unconfirmed what Play expects.
  - Account deletion / data deletion question: the game keeps data on the device and in Google Play Games' own storage; HapticGate holds no server data. Ad data is held by Google. Decide the wording with the privacy-policy reviewer.
  - **UMP (consent) SDK:** I could not find Google's official data-disclosure page for the UMP SDK (the AdMob disclosure page covers only the Mobile Ads SDK). **Unconfirmed**: re-check the UMP docs at the time; the rows above likely cover it.
  - Optional ad features or mediation add disclosures of their own; none are planned.
- [ ] **Target audience and content.** Pause is rated Everyone 10+ (IARC). The IARC rating is separate from the Target audience age-group declaration. If the declared target age groups include children (under 13), Google Play's Families policy applies: only Families-certified ad SDKs, child-directed treatment on every ad request, no personalised ads or ad ID, restricted ad content ([Families self-certified ads SDK policy](https://support.google.com/googleplay/android-developer/answer/12918983); [AdMob Families help](https://support.google.com/admob/answer/6223431): with child age groups only, AdMob serves Families-compliant ads automatically; for mixed audiences set child-directed treatment true and cap the ad content rating at G). That lowers ad revenue and changes the Data safety answers (no ad ID). **Recommended**: declare the target audience as 13+ only, keep the game's marketing non-child-directed, and do not set child-directed treatment. I could not confirm from official text how Play treats an Everyone 10+ game that declares 13+; this is a **user decision** to re-check against the current Families policy before submitting.
- [ ] **Price change** (Monetize > App pricing; menu naming unconfirmed). Switch the price from USD 1.99 to free. **One-way**: "Once your app has been offered for free, the app can't be changed to paid" and charging again needs a new package name ([Play help](https://support.google.com/googleplay/android-developer/answer/6334373)). Do this only after section 0 is complete and the owner has given the explicit go-ahead.
- [ ] **What buyers see on Play after the flip** (unconfirmed; check the listing and Library afterwards): the listing shows "Install" with the "Contains ads" and "In-app purchases" labels. Google's help does not say what happens to past buyers; as far as I know they keep the app in their library with no re-charge, and are not refunded automatically.
- [ ] Re-check the Data safety form and Ads declaration show as complete (no "action required" banner) before sending the release for review.

## 2. AdMob

- [x] **[user]** AdMob account created (done 2026-11-02, publisher `pub-4397064133944218`).
- [x] **[user]** Payment profile complete (the banner cleared 2026-11-02). The owner keeps the Individual profile for now; changing it is not a todo.
- [x] **[user]** Both AdMob apps (Android and iOS) are created (done 2026-11-02).
- [x] **[user]** All four ad units (interstitial and rewarded, Android and iOS) are created, and their ids are in `AdsConfig.cs` on PR #432. Nothing to create or enter again.
- [ ] **[user]** Consent message: AdMob > Privacy & messaging. Create the GDPR message (EEA, UK, Switzerland) and, if wanted, the US state-regulations message; for iOS the ATT message later ([UMP docs](https://developers.google.com/admob/ump/android/quick-start); not fetched, re-check). Publish the messages; the in-game UMP calls do nothing without them. **Not created yet (2026-11-02).**
- [ ] **[user]** Add the test devices (Settings > Test devices) so real ad units can be tested without invalid-traffic risk. Never tap live ads.
- [ ] **[agent]** `app-ads.txt` (the code of AdMob verification): the hapticgate-site PR #9 adds it and awaits merge (the Dockerfile `COPY` line must include it, or it 404s in production). The file is exactly this line (the second field is Google's fixed certification-authority id, re-check at [Google's page](https://support.google.com/admob/answer/9363762)); after the merge deploys, check `https://hapticgategames.com/app-ads.txt` in a browser.

  ```
  google.com, pub-4397064133944218, DIRECT, f08c47fec0942fa0
  ```

- [ ] **[user]** After #9 is merged and deployed: in AdMob > Apps > app-ads.txt, "Check for updates"; verification can take up to 24 hours. AdMob uses the website from the Play listing and looks at that hostname only ([AdMob help](https://support.google.com/admob/answer/9776740)). The Play listing website must be the apex domain exactly as the file is served; the crawler excludes `www.` as a place to look.
- [ ] **[user]** Link AdMob to Play if prompted (AdMob > App settings > Link with store).

## 3. Code (agent)

- [x] The real AdMob ids are in `AdsConfig.cs` on PR #432 (merge pending). Confirm no Google demo ids remain in the release build.
- [ ] Set `AdsConfig.Enabled = true`. Keep `AdsConfig.EnabledOnIos = false` until StoreKit ships (section 4).
- [ ] Confirm the built AAB's merged manifest has the AdMob app id and the `AD_ID` permission, and that no debug/test ad unit ids remain.
- [ ] Bump `AndroidBundleVersionCode` and cut the release with the `release-android` skill.
- [ ] After release: a fresh-install test (no ads for the first hour, then ads, Remove Ads works, restore works).

## 4. iOS (todos only; blocked until the Apple Developer account is accepted)

iOS has never been sold, so there is no price change; it launches free with ads and Remove Ads. **Rule: iOS ads stay off (`AdsConfig.EnabledOnIos = false`) until Remove Ads can be purchased on iOS**, so no iOS player sees ads with no way to remove them.

Owner decision 2026-11-02: **todos only, no iOS work for now.** Work starts when the Apple Developer account is accepted. The AdMob iOS app and both iOS ad units already exist and their ids are in `AdsConfig.cs` (PR #432); nothing to create there. The step-by-step list is phase 7 of the [tracker](ads-free-tracker.md).

- [ ] **[user]** Apple Developer organization enrollment for HapticGate LLC: pending acceptance. Then the bundle ID.
- [ ] **[agent]** StoreKit layer as in [store-products-and-rewards.md](../store-products-and-rewards.md) section 5 (5.1 code work, 5.2 per-product checklist); feed transactions into the existing `Handle` methods; a **Restore Purchases** button is required for non-consumables on iOS.
- [ ] **[user]** App Store Connect products: `me.hapticgate.pause.remove_ads` and `ship_paragon` Non-Consumable; `support_developer_850`, `support_developer_2500`, `stardust_6000` Consumable (ids per section 5 of that doc; Apple ids cannot be reused after deletion, unconfirmed). `prereg_stardust_100` is Android only.
- [ ] **[agent]** App Tracking Transparency: `NSUserTrackingUsageDescription`, the ATT prompt after the UMP flow, and the SKAdNetwork identifiers Google publishes (re-check Google's guidance). **[user]** creates the ATT/IDFA message in AdMob Privacy & messaging.
- [ ] **[user]** Test on the **iOS simulator** with Google's test ads first (the real ad units are never used for testing).
- [ ] **[user]** Get access to a real iPhone for a check before App Store release, and confirm what the simulator cannot cover (re-check at the time).
- [ ] **[user]** App Store Connect > App Privacy labels for the ad SDK (re-check Google's iOS data disclosure and Apple's rules at the time) and the age rating with ads.
- [ ] **[user]** After the App Store listing is live: AdMob > iOS app > Add store, then get it through AdMob review.
- [ ] **[agent]** Set `AdsConfig.EnabledOnIos = true` only in the release where Remove Ads can be bought on iOS.
- [ ] **[user]** The privacy policy already names Apple Game Center; the new ads wording covers iOS (ATT) too.

## 5. Privacy: every place that must be updated

All of these must say the same thing as the game does at the moment ads go live. Re-check each store's current rules at the time.

- [ ] **Website policy** (`https://hapticgategames.com/privacy/`, hapticgate-site PR, draft, **human review required**; merge at go-live). **[agent]** merge after **[user]** review.
- [ ] **Play Console Data safety form** (section 1) matches the policy.
- [ ] **Play Console privacy policy URL** (Store presence / App content > Privacy policy) is `https://hapticgategames.com/privacy/`. **[user]** check only.
- [ ] **AdMob > Privacy & messaging**: consent (GDPR, optionally US states) message published; on iOS the ATT/IDFA message (section 2 and 4). **[user]**
- [ ] **App Store privacy labels** (App Store Connect > App Privacy) when iOS ships (section 4). **[user]**
- [ ] **In-game access.** The game already links to the policy: the home screen footer has a "Privacy Policy" button (`startMenu.cs`, opens `ExternalLinks.PrivacyPolicy` = `https://hapticgategames.com/privacy/`; moved there from Options on 2026-10-12). No code change needed. **[agent]** The consent "privacy options" entry (branch `feat/ads-admob`) should sit in the **Options screen** (`leaderboardS3`, next to the sign-in / sign-out rows) and may also be offered beside the footer Privacy Policy button; Google expects players to be able to reopen their consent choices from the app's settings (re-check the UMP docs).

## 6. Website (hapticgate-site)

- [ ] **[user]** A human reviews the privacy-policy PR (legal text, drafted by an agent).
- [ ] **[agent]** Merge it **at go-live**, when the ads build is live (the policy would be inaccurate earlier: it describes ads). Its deploy is automatic on push to `main` (Cloud Run).
- [ ] **[agent]** `app-ads.txt` and the Dockerfile line: hapticgate-site PR #9, awaiting merge (section 2).
- [ ] **[user]** Make sure the Play listing's privacy-policy URL is `https://hapticgategames.com/privacy/` (not changed by this work; check only).

## 7. Rollback: what can and cannot be undone

| Thing | Can it be undone? |
|---|---|
| Ads in the game | Yes, by a new release with `AdsConfig.Enabled = false`. Players on the old version keep seeing ads until they update. Rolling back a Play release is not possible (a release can be halted, a new one must be shipped). |
| AdMob units / consent messages | Yes, they can be paused or edited in AdMob. |
| Data safety / ads declaration | Editable at any time, but must match the live build. |
| `app-ads.txt` | Yes, edit or remove the file; stale entries only reduce ad revenue. |
| **App price (paid to free)** | **No.** "Once your app has been offered for free, the app can't be changed to paid"; a paid version would need a new app with a new package name and loses reviews, installs and the listing. |
| Privacy policy | Yes, but changing it back to "no ads" while ads are live would be inaccurate. |

If ads misbehave after launch: ship a release with `Enabled = false` first (fast), investigate, re-enable.

## 8. Today's Play reality (2026-11-02)

- The app is still **paid at USD 1.99** with no buyers (11 device acquisitions in 28 days, the only paid order refunded).
- Already live in Play Console: the two new products (`me.hapticgate.pause.remove_ads`, USD 3.99 US; `stardust_6000`, USD 20.00 US) are Active in 174 regions, and the 2,500 and 850 packs are renamed "2,500 Star Dust" and "850 Star Dust" (prices unchanged). See the [tracker](ads-free-tracker.md) and [store-products-and-rewards.md](../store-products-and-rewards.md).
- Outstanding: **real test purchases** of Remove Ads and a dust pack (purchase, acknowledge/consume, restore); product icons; pink-heart art on old packs; translations.
- Not done and not in this PR: Data safety / ads / advertising ID changes, the AdMob consent message, `app-ads.txt` merge and verification, price change, any release.
