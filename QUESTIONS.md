# Pause: questions waiting on you

Updated from the agents' latest reports. Reply in chat with the numbers, e.g. "20 yes, 21 delete".

## Open decisions

None right now.



## Try these on your phone and tell me how they feel

8. **Resume slow-down.** From speed 15 (about 40–46s into a run), resuming slows time to 60% for
   half a second, then eases back to full speed over 0.4s.

9. **Pace.** The ramp feels the same as before, and each world now stops at its intended top speed.

10. **1-second countdown.** "READY" shows for 1s, then GO, and you can steer straight away.

16. **Back button.** In the dock, Back deselects the ship, then goes home. On the home screen, Back
    shows "press back again to quit". Nobody has seen that quit message on screen yet.

29. **Sign-in:** Options → Account row. Before the Play Console steps you'll see the "isn't set up"
    hint; after, your name and SIGN OUT. SIGN OUT disconnects Pause (stores have no in-app sign-out).

28. **Ship skins:** buy them in the dock (select an owned ship, tap a color chip). Hearts now sit beside
    the hull instead of on the weapon. HapticGate splash should fill your screen now.

25. **Ship exhausts** (now on your phone). Every ship has its own themed, colored flame, and
    Ninja and UFO have spin drifts. Preview: `scratchpad/exhaust/exhaust_sheet.png`.

## Store setup (only you can do these)

13b. **New Top Score leaderboard** (Play Console: Numeric, 0 decimals, larger is better, 0–1,000,000,
     tamper protection; paste its id into `LeaderboardBoards.cs` TopScore row. App Store Connect: classic
     leaderboard `me.sinaserati.Pause.top_score`, Integer, best score, high to low). Steps in `docs/leaderboards.md`.


Sign-in, cloud saves, achievements and leaderboards won't work until these are done.
Full steps: `docs/leaderboards.md` and `Pause/Assets/Scripts/Core/AchievementIds.cs`.

11. **Play Console** (sign-in now has a visible button in Options → Account; it will say
    "Play Games isn't set up for this build yet" until these are done):
    1. Play Console → Pause (`me.sinaserati.Pause`) → Grow users → Play Games Services →
       Setup and management → Configuration.
    2. Credentials → add an **Android** credential for `me.sinaserati.Pause` with the debug key SHA-1
       `A5:D3:CA:F1:3D:A2:33:BD:98:35:D7:DB:E6:DB:7A:FA:CF:43:EF:28` (that's the build on your phone).
       Keep/add the release key too: `E2:57:07:AC:A8:40:B0:41:E1:7C:38:D6:49:BF:3F:A1:50:01:AA:D5`,
       plus the App signing key SHA-1 from Setup → App integrity if you use Play App Signing.
    3. Check the Games project ID is **976061952733**.
    4. While Games services are unpublished: Testers → add the Google account on your phone.
    5. Turn on **Saved Games**.
    6. Leaderboard Top Speed: Numeric, 0 decimals, larger is better, tamper protection on.
    7. Wait a few minutes, force-stop Pause, then Options → SIGN IN WITH GOOGLE PLAY GAMES.

12. **Apple:**
    - Developer portal: enable Game Center and iCloud on App ID `me.sinaserati.Pause`, create
      the container `iCloud.me.sinaserati.Pause`, and regenerate the provisioning profile.
    - App Store Connect → Game Center: create the leaderboard `me.sinaserati.Pause.highest_speed`
      (Integer, best score, high to low) and the 28 achievements listed in `AchievementIds.cs`.

## In progress (you'll get previews; no action needed yet)

- **ON HOLD by you: neon pixel-art redesign** of enemies, bosses and backgrounds. Code-drawn samples weren't good enough. The style guide + toolkit are merged but nothing in the game was converted. Waiting on your call.

- **IN PROGRESS: endless loop after the final world.** After
  beating the Ember boss the player chooses: keep flying in Ember (until game over), or take a portal
  back to their starting world with their score intact and still accumulating. Higher speed = higher
  score multiplier.

- **Enemies, all worlds:** a much more detailed Akira pass on every enemy, plus the restored
  grass-capped rock and a floating rock for each world.
- **Forest background and walls:** bringing back the Akira accents and contrast, plus flat-cel walls
  in every world.
- **Ship color skins** you can buy with star dust (4 per ship).
- **Per-ship attacks and auto-charging secret powers.** Only the top ~3 ships get the screen clear.
- **End-of-level bosses** for each world.
- **Boss music:** once the bosses land, this becomes a TODO in `docs/TODO.md`. When you're ready,
  drop one track per world at `Resources/WorldMusic/Boss_<World>`.

## Answered

1. Speed double-ramp: fixed, and the same feel is kept.
2. Cloak invulnerability: fixed, and the blue-atom HUD timer is removed.
3. Back button: goes back one level at a time (deselect → home → quit).
4. Rail mines: custom per world, with no shared assets. The green atom stays the reference.
5. Mac save: leave it.
6. Engine flames: each ship has its own colored, themed flame, plus spinner drifts (done).
7. Neon Comet twin engines: fine. Purchasable color skins are in progress.
14. Cloak and attacks: every ship gets its own attack plus an auto-charging secret power (in progress).
15. Forest background: redo in progress.
20-22. Delete unused old art (Kenney fighters, all old asteroids/meteors, old mine images, plus a broad unused-asset sweep): in progress.
26. Flames follow skins "somewhat" (outer bands shift toward the skin, core stays): in progress.
27. Full screen on every aspect ratio (cap removed): in progress.
24. Spinner drift on home screen: yes (in progress).
23. Heavies: made properly big (~1.0-1.2u), with a fair-gap test (in progress).
19. Forest enemies: you liked the first-pass grass rock. It's coming back, and all enemies are getting
    a more detailed Akira pass.
