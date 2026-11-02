# Free with ads: work tracker

Pause is moving from a paid game to a free game with ads. This page tracks the decisions, the open questions and the work. Last updated 2026-11-02.

## Decisions made

- First 1 hour of play is ad-free.
- $4 one-time Remove Ads, named "Support the devs by removing the ads".
- $20 / 6,000 dust pack added.
- Dust packs no longer say "support the developer".
- New scrollable Shop screen; the Space Dock stays as the hangar; Paragon is sold in the Shop and appears in the dock once bought.
- No forced ads while a booster is active.

## Open questions (need the user)

1. **Is the game paid on Play right now, and how do we treat existing buyers?** Proposed: free Remove Ads for anyone with an existing save. Going paid to free is one-way.
2. **Does buying Paragon or a dust pack also remove ads?** Proposed: Paragon yes, packs no.
3. **Do Remove Ads owners still see the optional double dust ad?** Proposed: yes.
4. **Forced ad frequency.** The ask was one after each replay; proposed cap of one per 3 minutes and none after a very short run.
5. **"Exp" booster.** The game has no XP system that we have found. Proposed: dust only.
6. **A cut-off sentence.** The message said "we also wanna always offer the options to ..." and then stopped. What was the rest?

## Work phases

- [ ] **1. Products in Play Console and billing code.** Code: PR https://github.com/Wintersina/Pause/pull/427 (Remove Ads entitlement and handler, cloud-save merge, 6,000 pack, dust-pack wording, tests, docs). Not complete until PR #427 is merged and a real test purchase has been made.
  - [x] Play Console part, done and checked on the product list 2026-11-02: `me.hapticgate.pause.remove_ads` ("Support the devs by removing the ads", USD 3.99 US) and `stardust_6000` ("6,000 Star Dust", US set by hand to USD 20.00 because the 20 USD bulk price rounded to 19.99) are Active in 174 regions; the 2,500 and 850 packs are renamed "2,500 Star Dust" and "850 Star Dust" (prices unchanged).
  - [ ] PR #427 merged.
  - [ ] Real test purchase of Remove Ads and a dust pack (purchase, acknowledge/consume, restore).
  - Still open: the two new products have no icon yet; the 850 and 2,500 icons and in-game tile art still show a pink heart, which reads as a donation; translations under "Manage translations" were not checked for the renamed packs.
- [ ] 2. Shop screen; move Paragon and the dust packs into it.
- [ ] 3. Play-time tracker (for the first ad-free hour).
- [ ] 4. Ads SDK (AdMob proposed), consent prompt, forced and rewarded ads, frequency rules.
- [ ] 5. Boosters, after measuring typical dust income.
- [ ] 6. Going free (step by step: [go-free-checklist.md](go-free-checklist.md); existing-buyer code: `Billing/ExistingBuyer.cs`, PR on `feat/existing-buyer-ad-free`): Console declarations (contains ads, Data safety, advertising ID), privacy policy, `app-ads.txt` on hapticgategames.com, price change, existing-buyer handling.
- [ ] 7. iOS, later.
