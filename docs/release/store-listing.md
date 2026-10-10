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

HAND-MADE NEON PIXEL ART
Hand-drawn pixel art with glowing neon lighting, animated backdrops, planets and cities, and an original soundtrack.

NO ADS. NO IN-APP PURCHASES. ONE PRICE.
Pay once and play everything. Nothing is locked behind a timer or a second payment, and the game never collects analytics or shows ads.

Made by Sina Serati.
```

(2061 characters)

Do not add: Tide, Storm, Desert, "shooter", "multiplayer", "free", any ad/IAP wording that is not true.

Checks before pasting: the shop and ship counts are not stated (so a later ship count change cannot make the text wrong); "original soundtrack" is true (Audio engineer credited in README); "hand-drawn" is the owner's call, change to "pixel art" if parts were AI-assisted.

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

## How to produce them

- Best: real device captures (`adb exec-out screencap -p > shot.png` on the Pixel-class 1080x1920+ phone, then crop or resize to exactly 1080x1920). Developer-mode builds (`make android-dev-deploy`) can jump to a world or boss for staging but show a DEV badge, so do not use them for store shots.
- Editor-rendered: the `-executeMethod X.Run` preview tools in `Pause/Assets/Editor/Tools/Previews` (for example `BossThemedPreview`, `ElitePreview`, `CodexPreview`, `DockPreview`, `EmberBackdropPreview`, `ExplosionPreview`) render gameplay elements and screens headless, and the ScreenFit tool (`Pause/Assets/Editor/Tools/ScreenFit`, `docs/tools/screen_fit_sheets.py`) lays every menu screen out on a device matrix with real safe-area insets. These make good Codex, shop and leaderboard shots; gameplay shots 1 to 5 look best from a real device.
- Store policy: screenshots must show real gameplay or UI, with no device frames implying other products and no misleading text.

## Other listing fields

- Category: Games > Action (or Arcade). Tags: arcade, survival, pixel art, space.
- Contact email: [CONTACT EMAIL]. Privacy policy URL: publish `docs/release/privacy-policy.md` at a public URL first.
- Price: USD 1.99, set per country in Pricing. Contains ads: No. In-app purchases: No.
