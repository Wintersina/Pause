# Tide weather native-resolution rebuild

Built with the built-in image generation tool. Each delivered cell samples a
larger painted source with nearest-neighbour reduction; no output cell is
enlarged. The compositor is `build_tide_native_v2.py`.

## Source choices and prompt set

- Whirlpool: `whirlpool_native_source_v2.png` selected from two generated
  whirlpool candidates for its clear inward spiral, foam arms, dimple, bubbles,
  and tiny flotsam. Prompt: overhead Tide whirlpool with broken concentric foam
  arms, calm centre, bubbles and spray at the rim, hand-painted crisp high
  resolution pixel art in the specified teal and mint palette, transparent,
  airy, no glow or weapon features.
- Current surge: `surge_native_source_v2.png` selected from two generated surge
  candidates for its continuous leading foam and layered horizontal flow.
  Prompt: overhead horizontal Tide current with a broken foam leading rim,
  long left-to-right water streaks, bubbles and spray, tapering transparent
  edges, finely detailed high resolution pixel art in the specified palette,
  no glow, shore, UI, or weapon features.
- Ambient: `ambient_native_source_v2.png`. Prompt: four separated painted
  motifs on transparency: a bubble ring, slanted rain streaks, spray droplet
  cluster, and foam flecks; crisp pixel details, pale upper-left edge light,
  specified teal and mint palette, no glow.

The alternate generated candidates are retained beside the selected sources.

## Final verification

`python3 scripts/verify_weather_art.py Pause/Assets/Art/Weather~/Tide --world Tide --fronts B --sheet Pause/Assets/Art/Weather~/Tide/preview.png`

- Result: PASS (`verify_native_v2.txt` has every line).
- Worst block share: hazard 0.68, ambient 0.38 (limit: below 0.97).
- Body mean visible alpha: whirlpool 0.449, surge 0.496.
- Warning alpha mass versus body: whirlpool 0.257, surge 0.331.
- Pink and red share: 0.0000 in both sheets.
- Surge left and right edge alpha difference: 0.0 in every live frame;
  actual RGBA edge columns also match.

Visual caveat: the surge seam has a repeated crest motif where its source
painting was made periodic. It tiles cleanly, but the repetition may be easier
to notice at a large Unity playback scale.
