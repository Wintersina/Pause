# Go-free checklist (launch day)

Part of the [ads-free tracker](ads-free-tracker.md), phase 6. Prepared 2026-11-02. **Nothing here has been done yet**: Pause is still a paid app on Google Play at USD 1.99, and nobody has touched the price, the Data safety form or AdMob.

Legend: **[user]** only the user can do it (account, legal, money). **[agent]** a coding agent can do it. Tick boxes as you go. Where Google's or Apple's rules may have changed, the item says "re-check at the time": do that rather than trusting this page. Sources are linked inline; "unconfirmed" means I could not verify it from an official page.

## 0. What must already be true (do not start until all are ticked)

- [ ] **[agent]** The existing-buyer PR (`feat/existing-buyer-ad-free`) is merged and has **shipped to Play production and reached all paying users** (see "Release order" below). This is the hard gate.
- [ ] **[agent]** PR #427 (Remove Ads entitlement and purchase handler) is merged and shipped, and the shop screen / play-time tracker / ads SDK phases (tracker 2 to 4) are merged.
- [ ] **[user]** A real test purchase of Remove Ads and of a dust pack has been made on a device (purchase, acknowledge, consume, restore). Still outstanding on 2026-11-02.
- [ ] **[user]** Remove Ads and `stardust_6000` are Active in Play Console (done 2026-11-02, 174 regions). The 2,500 and 850 packs were renamed (done). Open: icons for the two new products, the pink-heart art on the 850 and 2,500 packs, and translations of the renamed packs.
- [ ] **[user]** The Play store listing's website field is exactly `https://hapticgategames.com` (needed for `app-ads.txt` verification, section 2).
- [ ] **[user]** The hapticgate-site privacy-policy PR is reviewed by a human (legal text) and ready to merge at go-live (sections 5 and 6).

## Release order (read this first)

The existing-buyer rule is evaluated **on the player's device, the first time a build containing it launches**. So:

1. **Ship the build with `ExistingBuyer` while the app is still PAID.** Do not flip the price first. A paying player who updates gets the grant on their first launch of that build.
2. The same build, or a later one, may carry the ads. If ads are in the same update, `AdsConfig.Enabled` is on only after the grant check has run (the check runs before the first scene, so it always is). An ads build that reaches a paid player *without* `ExistingBuyer` would show a buyer ads: never ship that.
3. Wait until the build is at 100% rollout and most buyers have updated (Play Console > Statistics / Release dashboard; the number of buyers is small, so this is quick). Buyers who update **later**, even after the price flip, are still recognised, because their existing progress is what is tested. The flip does not have to wait for every last buyer, but each day of delay shrinks the "reinstalled before updating" group (below).
4. Only then flip the price to free (section 1). From that moment a brand-new install has no progress at its first launch, so it is never granted. The marker is stored per install and never re-evaluated, so progress made later never grants.
5. **Optional paid-era clause.** `ExistingBuyer.PaidEraEndsUtc` (unix seconds, 0 = off) also grants an install whose first launch is earlier than that time even if it has no progress yet: it covers someone who bought *after* updating but *before* the flip. Set it to the planned flip time in the release that carries the code. If the flip happens earlier than that time, free installs between the flip and that time also get ads off (harmless but a small revenue leak); if later, buyers in the gap are not covered. With 0 the gap is simply not covered (a handful of people at current volume).
6. If the price flip is done and an old (pre-`ExistingBuyer`) install is still out there, updating it later is fine. Only a fresh install of the new build after the flip is treated as a free player.

### The rule, and where it can fail

- **Local rule:** at the very first launch of a build containing the code (Android only), before the first scene and before any cloud restore or the pre-registration reward can add dust, a device with real progress (tutorial done, anything beyond the starter ship, any star dust, a reached world, a best score, a skin) is an existing buyer: `AdFree` is granted, a marker is stored, a one-time toast "THANKS FOR BUYING PAUSE: ADS ARE OFF" shows on the home screen.
- **Cloud rule:** the cloud save has a new field `adsEra`, written by every build with this code. When a downloaded cloud save **lacks** it (written by a paid-era build) and has progress, applying it grants ad-free. Existing `adFree` keeps merging as "owned on either side wins", so the grant follows the account to other devices.
- **New free player:** on a new install nothing is on the device before the first scene, so the local rule grants nothing. If they then restore from their own cloud save, that save was written by a build with the code, so it carries `adsEra` and grants only if it already has `adFree`. Free players cannot create a save without `adsEra`.
- **Hole 1, reinstall before updating, no cloud save:** a buyer who uninstalled, never opened the new build and has no Play Games save arrives as a fresh install after the flip and is **not** recognised. Cause: for a custom billing layer there is nothing to ask. The app's own purchase is not in Play Billing's purchase list (that lists in-app products only). Play Integrity's licensing verdict only says the account installed or updated the app from Play ([verdicts](https://developer.android.com/google/play/integrity/verdicts)); a free download gives the same verdict, so it cannot separate buyers from free players after the flip. I found no official API that lists paid-app buyers to the app. Size: at most the number of buyers who reinstall in that window and have no cloud save; at the current volume (11 acquisitions in 28 days) this is a few people. Remedy: Play Console order history shows paid-app orders, so the user can recognise a buyer who writes to dev@hapticgategames.com and send a Play promo code for the Remove Ads product (promo-code limits unconfirmed: re-check in Play Console > Monetize). Count the buyers in the Console before the flip.
- **Hole 2, Android Auto Backup:** Unity apps usually allow Auto Backup, which can restore PlayerPrefs (marker and `adFreeOwned` included) after a reinstall. That is fine for buyers; for a new free player it only matters if they restore a backup, which then comes from their own device. Not verified for this build's manifest.
- **Hole 3, clearing app data:** a buyer who clears data before updating loses local progress; they are recovered only via the cloud rule.
- **iOS:** has never been sold, so it cannot have paying buyers. The rule is off on iOS (platform check, tested), including the cloud rule, so no iOS player is ever granted for free by it.
- **Developer mode / sideload / editor:** the rule runs on Android builds only.

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
- [ ] **Price change** (Monetize > App pricing; menu naming unconfirmed). Switch the price from USD 1.99 to free. **One-way**: "Once your app has been offered for free, the app can't be changed to paid" and charging again needs a new package name ([Play help](https://support.google.com/googleplay/android-developer/answer/6334373)). Do this only after section 0 and the release order are complete.
- [ ] **What buyers see on Play after the flip** (unconfirmed; check the listing and Library afterwards): the listing shows "Install" with the "Contains ads" and "In-app purchases" labels. Google's help does not say what happens to past buyers; as far as I know they keep the app in their library with no re-charge, and are not refunded automatically. Because the game is no longer paid, past buyers are recognised only by the in-game rule above.
- [ ] Re-check the Data safety form and Ads declaration show as complete (no "action required" banner) before sending the release for review.

## 2. AdMob

- [ ] **[user]** Create the AdMob account, with the Google account that owns the Play Console (codingsina, **not** the WinterSina account). Accept terms.
- [ ] **[user]** Payments: set up the payment profile (HapticGate LLC address and bank details), complete the tax information (a US LLC normally gives W-9 style details), and the address PIN verification if AdMob asks. Thresholds and steps change: re-check at the time.
- [ ] **[user]** Add the app for Android (published, so pick "Yes, listed on Google Play" and search `me.hapticgate.pause`, or enter the package). Add the iOS app as not yet published (leave it until iOS ships; section 4).
- [ ] **[user]** Create ad units (Apps > the app > Ad units): per platform one **Interstitial** and one **Rewarded** (the "double dust" ad). That is 2 now (Android), 2 later (iOS). Copy each ad unit id (`ca-app-pub-XXXX/YYYY`) and the app id (`ca-app-pub-XXXX~ZZZZ`) to the agent.
- [ ] **[user]** Consent message: AdMob > Privacy & messaging. Create the GDPR message (EEA, UK, Switzerland) and, if wanted, the US state-regulations message; for iOS the ATT message later ([UMP docs](https://developers.google.com/admob/ump/android/quick-start); not fetched, re-check). Publish the messages; the in-game UMP calls do nothing without them.
- [ ] **[user]** Add the test devices (Settings > Test devices) so real ad units can be tested without invalid-traffic risk. Never tap live ads.
- [ ] **[agent]** `app-ads.txt` (the code of AdMob verification). After the AdMob account exists, add `/app-ads.txt` to the hapticgate-site repo with exactly this line (use the real publisher id from AdMob > Settings; the second field is Google's fixed certification-authority id, re-check at [Google's page](https://support.google.com/admob/answer/9363762)):

  ```
  google.com, pub-XXXXXXXXXXXXXXXX, DIRECT, f08c47fec0942fa0
  ```

  The site's nginx serves any file at the domain root (`location / { try_files $uri $uri/ =404; }`), as text/plain, and `robots.txt` allows all crawlers. **But** the site's `Dockerfile` copies only a fixed list of root files: `app-ads.txt` must be added to its `COPY` line or it will 404 in production. Then deploy and check `https://hapticgategames.com/app-ads.txt` in a browser.
- [ ] **[user]** In AdMob > Apps > app-ads.txt, "Check for updates"; verification can take up to 24 hours. AdMob uses the website from the Play listing and looks at that hostname only ([AdMob help](https://support.google.com/admob/answer/9776740)). The Play listing website must be the apex domain exactly as the file is served; the crawler excludes `www.` as a place to look.
- [ ] **[user]** Link AdMob to Play if prompted (AdMob > App settings > Link with store).

## 3. Code (agent)

- [ ] Branch `feat/ads-admob` (another agent is building the ads config constants): once merged, fill in the **real** ids from section 2 in the ads config, replacing Google's demo ids. Constant names are whatever that branch defines: check it, don't assume.
- [ ] Set `AdsConfig.Enabled = true`. Keep `AdsConfig.EnabledOnIos = false` until StoreKit ships (section 4).
- [ ] Set `ExistingBuyer.PaidEraEndsUtc` as decided in the release order (optional; 0 = off).
- [ ] Confirm the built AAB's merged manifest has the AdMob app id and the `AD_ID` permission, and that no debug/test ad unit ids remain.
- [ ] Bump `AndroidBundleVersionCode` and cut the release with the `release-android` skill. **Do not send for review until the release order is satisfied.**
- [ ] After release: a fresh-install test (no ads for the first hour, then ads, Remove Ads works, restore works) and an upgrade test from the paid build (existing-progress device gets the toast and no ads).

## 4. iOS (later; nothing here blocks the Android launch)

iOS has never been sold, so there is no existing-buyer grant and no price change; it launches free with ads and Remove Ads. **Rule: iOS ads stay off (`AdsConfig.EnabledOnIos = false`) until Remove Ads can be purchased on iOS**, so no iOS player sees ads with no way to remove them.

- [ ] **[agent]** StoreKit layer as in [store-products-and-rewards.md](../store-products-and-rewards.md) section 5 (5.1 code work, 5.2 per-product checklist): fill `RemoveAdsPurchase`, `SupportPurchase` and `ParagonPurchase` price/launch from StoreKit and feed transactions into the existing `Handle` methods. A **Restore Purchases** button is required for non-consumables on iOS.
- [ ] **[user]** App Store Connect > Monetization > In-App Purchases (product ids per section 5 of that doc; Apple ids cannot be reused after deletion, unconfirmed):

  | Play product | Apple type |
  |---|---|
  | `me.hapticgate.pause.remove_ads` | Non-Consumable |
  | `me.hapticgate.pause.ship_paragon` | Non-Consumable |
  | `support_developer_850`, `support_developer_2500`, `stardust_6000` | Consumable |
  | `prereg_stardust_100` | Android pre-registration only; the Apple equivalent is separate and unverified (section 5.3 of that doc) |

- [ ] **[user]** App Store Connect > App Privacy: answer the "data used to track you" and "data linked to you" questions for the ad SDK (typically identifiers, usage data, diagnostics; **unconfirmed**: the AdMob iOS data-disclosure page could not be fetched, re-check Google's iOS App Store data disclosure page for the SDK version in use). Apple's current privacy-label rules: re-check at the time.
- [ ] **[agent]** App Tracking Transparency: add `NSUserTrackingUsageDescription` to Info.plist, show the ATT prompt (after the UMP flow, per Google's guidance; re-check), and add the SKAdNetwork identifiers Google publishes. **[user]** Create the ATT/IDFA message in AdMob Privacy & messaging.
- [ ] **[user]** Add the iOS ad units to AdMob (section 2), put the real iOS ids in the ads config, then **[agent]** set `AdsConfig.EnabledOnIos = true` only in the release where Remove Ads is purchasable on iOS.
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
- [ ] **[agent]** After AdMob exists, add `app-ads.txt` and the Dockerfile line (section 2).
- [ ] **[user]** Make sure the Play listing's privacy-policy URL is `https://hapticgategames.com/privacy/` (not changed by this work; check only).

## 7. Rollback: what can and cannot be undone

| Thing | Can it be undone? |
|---|---|
| Ads in the game | Yes, by a new release with `AdsConfig.Enabled = false`. Players on the old version keep seeing ads until they update. Rolling back a Play release is not possible (a release can be halted, a new one must be shipped). |
| AdMob units / consent messages | Yes, they can be paused or edited in AdMob. |
| Data safety / ads declaration | Editable at any time, but must match the live build. |
| `app-ads.txt` | Yes, edit or remove the file; stale entries only reduce ad revenue. |
| **App price (paid to free)** | **No.** "Once your app has been offered for free, the app can't be changed to paid"; a paid version would need a new app with a new package name and loses reviews, installs and the listing. |
| Existing-buyer grants | Granted entitlements are permanent and cloud-merged ("owned on either side wins"); they are not revoked. |
| Privacy policy | Yes, but changing it back to "no ads" while ads are live would be inaccurate. |

If ads misbehave after launch: ship a release with `Enabled = false` first (fast), investigate, re-enable.

## 8. Today's Play reality (2026-11-02)

- The app is still **paid at USD 1.99** with very few buyers (11 device acquisitions in 28 days).
- Already live in Play Console: the two new products (`me.hapticgate.pause.remove_ads`, USD 3.99 US; `stardust_6000`, USD 20.00 US) are Active in 174 regions, and the 2,500 and 850 packs are renamed "2,500 Star Dust" and "850 Star Dust" (prices unchanged). See the [tracker](ads-free-tracker.md) and [store-products-and-rewards.md](../store-products-and-rewards.md).
- Outstanding: **real test purchases** of Remove Ads and a dust pack (purchase, acknowledge/consume, restore); product icons; pink-heart art on old packs; translations.
- Not done and not in this PR: AdMob account, Data safety / ads / advertising ID changes, price change, any release.
