# Google Play store listing draft

Package `me.hapticgate.pause`, paid app (USD 1.99). Default language: English (US). Every figure below is within the Play limit and checked for accuracy against the shipping build (four worlds: Space, Frost, Verdant, Ember; Tide, Storm and Desert are not mentioned).

## App title (max 30)

`Pause: Rail Survival` (20 chars)

Alternates: `Pause` (5), `Pause - Rail Runner Survival` (28).

## Short description (max 80)

`Ride rails, dodge telegraphed attacks, survive four endless worlds.` (67 chars)

## Full description (max 4000)

```
Pause is a survival game about reading danger and staying alive.

You fly a ship along glowing rails through space and beyond. Enemies do not chase you with endless gunfire: every attack is telegraphed. A pink warning shows where trouble is about to land, and your job is to read it, slide to safety and keep going. When it gets tight, you can lean on your shield and its shockwave to push danger away.

FOUR WORLDS, ONE ENDLESS JOURNEY
Cross Space, Frost, Verdant and Ember. Each world has its own look, music, enemies, rails and boss. Land on a planet, fight your way through, lift off and fly on. When you finish Ember the journey loops back to Space and keeps going, faster every loop. How many loops can you survive?

BOSSES AND ELITES
Every world ends in a boss with its own attack patterns, and elite ships hunt you along the way, each with a personality and a signature move. Learn them, beat them and add them to your Codex.

ATTACKS YOU CAN READ
Every enemy, mine and boss attacks in the material of its world: ice, fire, vines and neon. Every threat is announced before it strikes, so deaths feel earned and wins feel clean.

SHIPS, SKINS AND STAR DUST
Collect star dust during your runs and spend it in the shop on new ships and hull skins. Each ship flies and fights a little differently.

THE CODEX
Everything you meet is recorded in the Codex: enemies, elites, bosses and worlds, with their art and notes. Fill it as you discover them.

ACHIEVEMENTS AND LEADERBOARDS
Sign in with Google Play Games to unlock achievements, compete on the Top Score, Star Dust and Furthest Loop leaderboards, and keep your progress safe with cloud save across devices. Signing in is optional; the whole game works offline.

NEON PIXEL ART
Pixel art with glowing neon lighting, animated backdrops, planets and cities, and an original soundtrack.

NO ADS. NO IN-APP PURCHASES. ONE PRICE.
Pay once and play everything. Nothing is locked behind a timer or a second payment, and the game never collects analytics or shows ads.

Made by Sina Serati.
```

(2039 characters)

Do not add: Tide, Storm, Desert, "shooter", "multiplayer", "free", any ad/IAP wording that is not true.

Checks before pasting: the shop and ship counts are not stated (so a later ship count change cannot make the text wrong); "original soundtrack" is true (Audio engineer credited in README); the art is partly AI-assisted, so the text says "pixel art" and never "hand-made" or "hand-drawn"; keep it that way.

## Paste-ready

Title (20 of 30):

```
Pause: Rail Survival
```

Short description (67 of 80):

```
Ride rails, dodge telegraphed attacks, survive four endless worlds.
```

Full description (2039 of 4000):

```
Pause is a survival game about reading danger and staying alive.

You fly a ship along glowing rails through space and beyond. Enemies do not chase you with endless gunfire: every attack is telegraphed. A pink warning shows where trouble is about to land, and your job is to read it, slide to safety and keep going. When it gets tight, you can lean on your shield and its shockwave to push danger away.

FOUR WORLDS, ONE ENDLESS JOURNEY
Cross Space, Frost, Verdant and Ember. Each world has its own look, music, enemies, rails and boss. Land on a planet, fight your way through, lift off and fly on. When you finish Ember the journey loops back to Space and keeps going, faster every loop. How many loops can you survive?

BOSSES AND ELITES
Every world ends in a boss with its own attack patterns, and elite ships hunt you along the way, each with a personality and a signature move. Learn them, beat them and add them to your Codex.

ATTACKS YOU CAN READ
Every enemy, mine and boss attacks in the material of its world: ice, fire, vines and neon. Every threat is announced before it strikes, so deaths feel earned and wins feel clean.

SHIPS, SKINS AND STAR DUST
Collect star dust during your runs and spend it in the shop on new ships and hull skins. Each ship flies and fights a little differently.

THE CODEX
Everything you meet is recorded in the Codex: enemies, elites, bosses and worlds, with their art and notes. Fill it as you discover them.

ACHIEVEMENTS AND LEADERBOARDS
Sign in with Google Play Games to unlock achievements, compete on the Top Score, Star Dust and Furthest Loop leaderboards, and keep your progress safe with cloud save across devices. Signing in is optional; the whole game works offline.

NEON PIXEL ART
Pixel art with glowing neon lighting, animated backdrops, planets and cities, and an original soundtrack.

NO ADS. NO IN-APP PURCHASES. ONE PRICE.
Pay once and play everything. Nothing is locked behind a timer or a second payment, and the game never collects analytics or shows ads.

Made by Sina Serati.
```

## Graphics assets

| Asset | Size | Source |
|---|---|---|
| App icon | 512x512 PNG (32-bit) | `Pause/Assets/Art/AppIcon/OrbitalRail` master (1024 master, downscale) |
| Feature graphic | 1024x500 | needs a new render: ship on a rail in front of the ringed city planet (Space), title on the left |
| Phone screenshots | 1080x1920 portrait, 2 to 8 | plan below |
| Tablet screenshots (optional) | 1600x2560 (7") / 2000x3200 (10") | same shots re-captured on a tablet frame or ScreenFit tablet entries |

## Screenshot plan (8 shots, 1080x1920 portrait)

| # | Scene | Moment | Caption idea |
|---|---|---|---|
| 1 | Space, gameS1 | Mid-run on the rails, pink telegraph visible, HUD showing speed | "Read the warning. Dodge." |
| 2 | Space to Frost | Planetfall into the Frost planet, ship above the ice | "Four worlds, one endless journey" |
| 3 | Frost or Verdant | Elite ship attack with its signature pattern incoming | "Elites with their own moves" |
| 4 | Any world | Boss fight (Hoarfrost Leviathan or Bloom Queen) with boss hearts bar | "Boss at the end of every world" |
| 5 | Ember | Ember backdrop with lava rails, shield shockwave firing | "Shield shockwave pushes danger back" |
| 6 | Shop | Ship dock with several ships and skins | "Collect ships and skins" |
| 7 | Codex | Codex page with an elite or boss entry discovered | "Fill the Codex" |
| 8 | Leaderboard / achievements | Leaderboard panel with Top Score, or the achievements tab | "Climb the leaderboards, unlock 60 achievements" |

Ordering note: shot 1 is what appears first on the store page; make it the clearest gameplay frame.

## Owner shot list (dev APK, no capture done by Claude)

The test phone (R5CY70N4N9D, Galaxy Z Flip7, 1080x2520) was attached when this was written; nothing was installed on it.

Setup:
1. `make android-dev-deploy ANDROID_SERIAL=R5CY70N4N9D` installs the dev build (package `me.hapticgate.pause`; it replaces any installed build of that id, so uninstall first if the signing key differs). Developer mode is on by default: everything unlocked, start world selectable in Options.
2. Dev mode shows a DEV badge and never reports scores or achievements. Shots 1 to 5 (gameplay) are fine if the badge is cropped or out of frame. If the badge is visible in the frame, re-shoot those from a release build (`make android-release` with the real keystore) instead. Shots 6 to 8 need the real progress state: for the leaderboard / achievements shot use a signed-in release build, not dev.
3. Capture: `adb -s R5CY70N4N9D exec-out screencap -p > shotN.png`. The phone is 1080x2520 (2.33:1) and Play rejects ratios beyond 2:1, so crop each to 1080x2160 (or 1080x1920): `sips -c 1920 1080 shotN.png` crops about the centre; keep the HUD and ship inside the crop. Final files: PNG or JPEG, 1080 wide, 16:9 to 9:16, 2 to 8 images, 8 MB max each.

| # | How to stage it | Frame |
|---|---|---|
| 1 | Options > start world Space, start a run, wait for the first pink telegraph | Ship on the rail, pink warning visible, HUD readable |
| 2 | Space: let planetfall start (or run until the Frost planet fills the lower half) | Planet approach with ship above it |
| 3 | Options > start world Frost or Verdant, play to the first elite | Elite mid-attack with its signature pattern |
| 4 | Start world Frost or Verdant, reach the boss | Boss on screen with the boss hearts bar |
| 5 | Start world Ember, trigger the shield shockwave near enemies | Lava backdrop, shockwave ring visible |
| 6 | Main menu > Shop (ship dock) | Several ships and skins, owned and locked markers |
| 7 | Main menu > Codex | A page with an elite or boss entry discovered |
| 8 | Release build, signed in: Leaderboards panel (Top Score) or the Achievements list | Real name and scores; hide any personal info |

Shoot 2 to 3 candidates of each, pick the cleanest. Shot 1 appears first on the store page.

Optional tablet shots (7" 1600x2560, 10" 2000x3200) and the 1024x500 feature graphic and 512x512 icon (see Graphics assets) are separate jobs. Editor preview tools (`BossThemedPreview`, `ElitePreview`, `CodexPreview`, `DockPreview`, `EmberBackdropPreview`) and the ScreenFit tool (`docs/tools/screen_fit_sheets.py`) can render the Codex, shop and leaderboard screens headless if a device capture is awkward.

Store policy: screenshots must show real gameplay or UI, no device frames implying other products, no misleading text.

## Other listing fields

- Category: Games > Arcade (entered). Tags: arcade, survival, pixel art, space.
- Contact email: hapticgate@gmail.com. Privacy policy URL: https://sinaserati.com/hapticgate/privacy/ (the hosted text needs the replacement paragraph from `privacy-policy.md`).
- Price: USD 1.99, set per country in Pricing. Contains ads: No. In-app purchases: No.
