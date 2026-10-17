#!/usr/bin/env python3
"""Per-ship pose briefs for the Desert / Ember frame-fix campaign (one Codex pose sheet per ship).

Each ship gets ONE image: a 3x2 grid of six poses of the SAME ship (the current art is the image input):
  row 1 = idle loop drawings 1-3 (cell 0 stays the existing master), row 2 = tell 1 (anticipation), tell 2 (peak), hit (recoil).
All ships face DOWN the screen (nose / weapon at the bottom, engines at the top). Hit recoil = knocked back UP the screen + slight tilt.
"""

SHIPS = {
    # ---------------- DESERT ----------------
    "desert_alien": dict(world="desert", cell=192,
        idle="the gilded filigree cage dome stays put while the blue flame streamers underneath sway through a slow loop: streamers "
             "swept left, swept right, drawn long; the little hanging bells swing; the glowing core pulses slightly",
        tell1="the cage ribs begin to part and flare outward like a blooming lantern, the glowing core swells brighter and whiter, "
              "streamers lift and spread wider",
        tell2="PEAK: the cage fully opened in a wide flare, core blazing white-hot at the centre, streamers whipped up and flared "
              "into a much wider silhouette than idle",
        hit="knocked back: the whole dome is shoved up the screen and tilted about 12 degrees, ribs flinching inward and slightly "
            "dented, streamers whipped upward and back, core dimmed"),
    "desert_big": dict(world="desert", cell=256,
        idle="the four side turbines (two each side) show different rotor blade positions in each drawing (blades turned a third of "
             "a step each time), the rear exhaust flames flicker to different lengths; hull identical",
        tell1="the four turbine pods begin to swivel outward and their rotors blur brighter, the front cannon / hatch glows, hull "
              "hunkers",
        tell2="PEAK: all four turbine pods swung fully outward and angled, a bright charge glow at the front weapon bay, exhaust "
              "flames long and hot, a visibly wider silhouette than idle",
        hit="knocked back: the hull is shoved up the screen and yawed about 8 degrees, turbines wobbling out of line, plating "
            "flinching, one turbine pod knocked askew"),
    "desert_chaser": dict(world="desert", cell=192,
        idle="the four glassy insect wings sit at different flap phases in each drawing (up, mid, down), the mandibles and legs "
             "shift slightly, the two rear jets flicker to different lengths; the carapace stays the same",
        tell1="the glass wings cock back and upward, the body crouches, the mandibles begin to open, jets flare",
        tell2="PEAK: wings raised high and spread wide, mandibles wide open, legs tucked forward ready to lunge, jets long and "
              "bright, a distinctly different silhouette from idle",
        hit="knocked back: the beetle is shoved up the screen and tilted about 15 degrees, one wing folded in, the carapace "
            "plating flinching, mandibles snapped shut"),
    "desert_fighter_1": dict(world="desert", cell=192,
        idle="the narrow sand-skimmer wedge with its twin yellow flames: the side fins ease out then in, the flames flicker to "
             "different lengths, a tiny canopy glint moves; same body",
        tell1="the side fins flare out and the intake opens, flames stretch longer, nose dips",
        tell2="PEAK: fins fully flared wide like a blade, intake gaping, twin flames at maximum length, a noticeably wider "
              "silhouette than idle",
        hit="knocked back: the wedge is shoved up the screen and yawed about 15 degrees, fins snapped inward, nose bent aside"),
    "desert_fighter_2": dict(world="desert", cell=192,
        idle="the big violet crystal mirror-panel wings sit at three different angles (folded slightly, neutral, spread slightly) "
             "while the green hull with its four blue-tipped barrels and hooked prow stays; blue barrel tips flicker",
        tell1="the crystal wings cock backward and upward, barrel tips begin to glow brighter blue",
        tell2="PEAK: wings swept far back and raised high, the four barrel tips blazing, prow lowered, a clearly different "
              "silhouette from idle",
        hit="knocked back: shoved up the screen and tilted about 14 degrees, wings snapped inward and one wing lagging, crystal "
            "facets flinching"),
    "desert_fighter_3": dict(world="desert", cell=192,
        idle="the round cyan lens in its brass housing with three jet legs: the three legs sway to different splay angles in each "
             "drawing, the lens glints in a different spot, jet flames flicker; same body",
        tell1="the three legs begin to spread outward, the lens iris widens and brightens",
        tell2="PEAK: legs splayed wide apart, the lens wide open and blazing bright cyan-white at its centre, jets long",
        hit="knocked back: shoved up the screen and tilted about 14 degrees, legs buckled and bunched, lens dimmed and flinching"),
    "desert_fighter_4": dict(world="desert", cell=192,
        idle="the squat sand-tank with swirling sand-coloured scroll bands: the sand scroll bands twist to a different phase in "
             "each drawing, the turret shifts a pixel or two, rear flames flicker; same body",
        tell1="the sand bands swell and the turret rises on its mount, side vents glow",
        tell2="PEAK: the sand bands flung outward in a bigger swirl, turret fully raised and charged with a bright glow, flames "
              "long",
        hit="knocked back: shoved up the screen and tilted about 10 degrees, the sand bands whipped to one side, plating flinching"),
    # ---------------- EMBER ----------------
    "ember_alien": dict(world="ember", cell=192,
        idle="the small caged imp with a yellow core: the pointed crown and curved fins breathe in and out, the tiny vents and "
             "horn flames flicker to different lengths; the core pulses",
        tell1="the cage fins begin to peel outward, the core swells brighter",
        tell2="PEAK: the cage fins flared wide open, core blazing white-hot, fang tips bared, wider silhouette",
        hit="knocked back: shoved up the screen and tilted about 15 degrees, fins crumpled inward, core dimmed"),
    "ember_big": dict(world="ember", cell=256,
        idle="the horned bone-skull fortress: the thruster flame stacks along the top flicker to different lengths, the jaw "
             "plates and horns shift a pixel or two in each drawing; the maw glow pulses",
        tell1="the jaw plates begin to open onto the glowing maw, horns tilt forward, engine flames lengthen",
        tell2="PEAK: jaws wide open, the maw blazing bright, the horns swung outward, side drums flared, much bigger silhouette",
        hit="knocked back: the skull is shoved up the screen and tilted about 8 degrees, jaw plates slammed shut and askew, bone "
            "plating flinching, horn knocked slightly out of line"),
    "ember_chaser": dict(world="ember", cell=192,
        idle="the blue-teal swept-wing chaser with a round brass-banded body and twin flames: the wing blades flex to different "
             "angles in each drawing (slightly forward, neutral, slightly back), flames flicker; body identical",
        tell1="the wings begin to sweep back and the body hunkers, flames lengthen",
        tell2="PEAK: wings swept sharply back into a dart, the body tucked, flames at maximum length, a clearly different "
              "silhouette from idle",
        hit="knocked back: shoved up the screen and tilted about 15 degrees, one wing folded back, plating flinching"),
    "ember_fighter_1": dict(world="ember", cell=192,
        idle="the dark blue-teal fighter (approved design: keep EXACTLY) with its cyan canopy and top flames: the swept wing tips "
             "flex slightly in each drawing, the engine flames flicker to different lengths",
        tell1="the wings begin to sweep forward and spread, the nose cannon glows, flames lengthen",
        tell2="PEAK: wings fully spread, the nose cannon glowing white-hot, flames long, wider silhouette",
        hit="knocked back: shoved up the screen and tilted about 14 degrees, one wing tip folded in, plating flinching"),
    "ember_fighter_2": dict(world="ember", cell=192,
        idle="the white and cobalt gunship with yellow chevrons: the two swept pincer wing arms flex to different angles in each "
             "drawing, the engine flames flicker; the central cannon barrel stays",
        tell1="the pincer wing arms begin to close inward toward the barrel, the cannon muzzle glows",
        tell2="PEAK: pincer arms closed tight around the cannon, the muzzle blazing white-hot, side vents glowing, flames long",
        hit="knocked back: shoved up the screen and tilted about 14 degrees, one pincer arm knocked wide, cannon dimmed"),
    "ember_fighter_3": dict(world="ember", cell=192,
        idle="the verdigris-green raider with two long ivory hooked horn-blade wings: the horn blades swing slightly through "
             "different angles in each drawing (a little open, neutral, a little closed), flames flicker",
        tell1="the horn blades begin to swing inward and forward, the vents glow",
        tell2="PEAK: horn blades swung in tight and forward like closing scissors around the nose, flames long, narrower "
              "silhouette than idle",
        hit="knocked back: shoved up the screen and tilted about 14 degrees, one horn blade knocked wide, plating flinching"),
    "ember_fighter_4": dict(world="ember", cell=192,
        idle="the round gunmetal and blue furnace gunship with a large amber furnace-eye grille: the eye glow pulses to "
             "different brightness in each drawing, the four side pylons shift slightly, the three rear flames flicker",
        tell1="the amber furnace eye brightens and the pylons begin to extend outward",
        tell2="PEAK: the furnace eye blazing bright orange-white through the grille, the four pylons fully extended, cannon tips "
              "white-hot, flames long, a bigger silhouette",
        hit="knocked back: shoved up the screen and tilted about 10 degrees, pylons knocked askew, the eye dimmed"),
}

COMMON = (
    "Use your built-in image generation tool (do not hand-draw with scripts). Work only inside this folder; modify no other file; "
    "no git. The attached image is the current game sprite of one enemy ship of a vertical 2D shooter in HD pixel art, seen top-down, "
    "nose / weapon pointing DOWN the screen (input shown on a flat green background). Paint ONE image: a sheet of SIX poses of the "
    "SAME ship laid out as a 3-column by 2-row grid of equal squares, one pose centred in each square with generous empty margin "
    "(at least 12% on all sides), nothing crossing between squares, no grid lines, no labels, no text. Keep the SAME design, "
    "silhouette, palette, materials, proportions, pixel scale and drawing style in every pose; do not redesign, recolour, add or "
    "remove parts, change the ship size, or change the overall pixel size. Every pose must be a genuinely DIFFERENT drawing "
    "(limbs / wings / fins / rotors / lighting actually change), not a copy shifted around. "
    "ROW 1, left to right = idle loop drawings A, B, C: {idle}. "
    "ROW 2, left to right = (4) TELL warm-up: {tell1}; (5) TELL peak: {tell2}; (6) HIT recoil: {hit}. "
    "Style rules: crisp hard pixel edges, no blur or soft anti-aliasing, NO outline, no pale rim, no glow halo, no drop shadow around "
    "the silhouette (the painted body simply ends against the background). Forbidden: pink, magenta, red. Background: one perfectly "
    "flat pure #00FF40 green everywhere outside the ships (chroma key; never use that green on a ship). "
    "Output ONE PNG, 1536x1024 or the nearest size. Save it exactly to {out} (copy it from the generated images folder if needed) "
    "and reply with just the path."
)


def prompt(name, out):
    s = SHIPS[name]
    return COMMON.format(idle=s["idle"], tell1=s["tell1"], tell2=s["tell2"], hit=s["hit"], out=out)


if __name__ == "__main__":
    for n in SHIPS:
        print("==", n, "\n", prompt(n, "sheets/" + n + ".png"), "\n")
