# Tide weather art

Painted with the built-in image generation tool, then palette-cleaned and
assembled by `build_tide.py` with nearest-neighbor scaling only.

## Prompt set

- Whirlpool candidates: isolated, top-down ocean whirlpool with broken spiral
  foam arms, calm central dimple, bubbles, spray and tiny brass flotsam;
  hand-placed-looking pixel texture, deep teal to sea-foam ramp, transparent.
- Current surge candidates: isolated, horizontal, top-down water-front band
  with a broken foam leading edge, lateral streaming water, bubbles and spray;
  transparent, tapered above and below.
- Ambient source: four separated motifs on transparency: bubble ring with one
  upper-left glint, slanted rain streak, spray cluster and tiny foam fleck.
- All prompts excluded red, pink, glow, UI, weapons, text and backgrounds.

Selected `whirlpool_candidate_2.png` for its clear spiral and
`surge_candidate_1.png` for its irregular foam line. The unused candidates and
the first denser-warning draft are retained here for comparison.

## Measurements

| Item | Result |
| --- | ---: |
| Whirlpool mean alpha, occupied pixels | 0.383 |
| Surge mean alpha, occupied pixels | 0.371 |
| Whirlpool / surge mean alpha, whole cell | 0.205 / 0.134 |
| Whirlpool / surge max alpha | 145 / 138 of 255 |
| Whirlpool / surge warning alpha mass | 28.4% / 27.4% of live frame |
| Front left/right alpha difference | 0.0 |
| Unused hazard cells 4–5 alpha sum | 0 |
| Pink / red share | 0.0000 / 0.0000 |

`python3 scripts/verify_weather_art.py Pause/Assets/Art/Weather~/Tide \
--world Tide --fronts B --sheet Pause/Assets/Art/Weather~/Tide/preview.png`
passes every line.

The current surge uses a repeating 64-native-pixel painted segment to close
its lateral loop. Its repeated crest rhythm is the weakest visual detail and
should be judged in motion in the game.
