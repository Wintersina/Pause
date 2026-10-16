#!/usr/bin/env python3
"""Verdant enemy REDESIGN briefs (art/verdant-enemies): ground-up flying ships, one Codex image per master, text-only prompts.

Each craft gets a MASTER (src/<name>.png, text-only prompt so Codex cannot copy another ship's colours) and two WING-POSE variants made
by Codex from that master (src/<name>_a.png, src/<name>_b.png): A = wings lifted halfway, B = wings fully raised / rotor blurred.
The seven-cell strip is assembled from them (build_redesign.py). Rocks are natural stone, one image each, no engines.
Kept (approved PR #25, never regenerated): verdant_alien (Snap Sprout).

  python3 briefs.py            # prints every prompt (review)
"""

STYLE = (
    "HD neon pixel art, hand-placed pixels with crisp hard edges, dense 5-6 step material ramps with a WIDE value range, rivets, "
    "seams, scratches and wear; real materials (bark, wood, resin, brass, copper, bone, chitin, glass, stone) that each read differently; "
    "bright glow only on small functional parts (engines, weapon tips, lenses, cores) with a tiny spill of light onto the plating beside "
    "them. Top-down orthographic view. A craft is an AIRBORNE flying machine with real wings AND real engines, organic-meets-machine "
    "(a jungle gunship), never a plant, never a ground creature. Genuinely transparent background, sprite centred with at least 12% empty "
    "margin on every side. ABSOLUTELY NO outline of any kind: no pale rim, no white border, no glow halo, no drop shadow; the painted body "
    "simply ends against transparency. FORBIDDEN colours: pink, magenta, pure red (the player ship owns red), and an all-green or "
    "gold-and-lime hull (the jungle backdrop is green, so the hull must contrast with it). No text, labels, grid, scenery or other sprites."
)

# name -> dict(kind, cell, size (longest side px at final cell), facing, brief, wings (pose-variant instructions))
BRIEFS = {
    "verdant_chaser": dict(
        kind="craft", cell=192, size=150, facing="down",
        brief="Dragonsting: a needle-slim dragonfly interceptor. A long narrow hull with a rounded cockpit-eye canopy at the nose, four long "
              "slender membrane wings (two pairs, swept slightly back) braced by brass vein struts, and a long barbed tail boom ending in a "
              "twin thruster with a short flame. PALETTE (this ship only): deep indigo-blue hull plates with navy shadow, iridescent teal "
              "glass-membrane wings with copper veins, amber resin lamps, a pale cyan cockpit lens, small bone-white rivets.",
        wings="the four dragonfly wings"),
    "verdant_fighter_1": dict(
        kind="craft", cell=192, size=161, facing="down",
        brief="Gnat: a small, quick gunship in the shape of a bug. A compact rounded pod hull with a stubby nose cannon pointing down, two big "
              "round-tipped resin-membrane wings on short brass hinge arms, a small twin-nozzle thruster at the top with flames, two tiny "
              "side lamps. PALETTE (this ship only): bone-ivory and cream hull with walnut-brown bark plating and dark seams, pale amber resin "
              "wings with copper veins, a cyan cockpit lens, hot orange-white flame.",
        wings="the two round resin wings"),
    "verdant_fighter_2": dict(
        kind="craft", cell=192, size=170, facing="down",
        brief="Wasp: a banded flying gunship. A narrow cockpit thorax, a long tapered striped abdomen pointing DOWN the screen ending in a "
              "heavy stinger cannon with a glowing muzzle, two pairs of smoked-glass wings, two rear engine nozzles with flames at the top. "
              "PALETTE (this ship only): charcoal-black and sulphur-yellow hazard bands on the abdomen, gunmetal thorax with brass trim, "
              "smoke-grey glass wings with fine black veins, a violet-white glowing stinger tip, cyan canopy.",
        wings="the two pairs of smoked-glass wings"),
    "verdant_fighter_3": dict(
        kind="craft", cell=192, size=168, facing="down",
        brief="Mantis: a praying-mantis gunship. A narrow armoured thorax with a triangular head and two amber lens eyes at the bottom, two long "
              "folded bone-blade scythe arms curving forward, two broad leaf-shaped membrane wing-sails held back, twin rear thrusters with "
              "flames. PALETTE (this ship only): moss-teal and deep sea-green armour plates, bone-ivory scythe blades with brass edges, copper "
              "joints and pistons, amber eye lenses, translucent sea-glass wing-sails.",
        wings="the two leaf-shaped wing-sails"),
    "verdant_fighter_4": dict(
        kind="craft", cell=192, size=171, facing="down",
        brief="Hornet Queen: a regal armoured four-winged heavy gunship. A crowned head with a brass crown and two scythe-blade arms at the "
              "bottom, a thick armoured thorax, a heavy abdomen with a stinger cannon pointing down, two pairs of large veined wings, three "
              "rear engine nozzles with flames. PALETTE (this ship only): dark walnut and bronze armour with jet-black seams, polished brass "
              "crown and trim, electric-violet translucent wings with gold veins, a bright amber core and stinger tip.",
        wings="the four large wings"),
    "verdant_big": dict(
        kind="craft", cell=256, size=218, facing="down",
        brief="Bloom Maw: a heavy flower-shaped flying gunship fortress. Five big hinged petal-shaped armour plates around a central toothed "
              "mouth cannon that points down the screen, each petal doubling as a wing with a small turbine/engine pod and flame between "
              "the petals at the rear, brass hinges and rivets, bark-wood understructure. PALETTE (this ship only): deep cobalt-blue and "
              "midnight-navy petals with bone-ivory edges and plating seams, bark-brown understructure, a glowing saffron-gold maw with ivory "
              "teeth, brass turbine pods with orange-white flames. NO purple or violet anywhere.",
        wings="the five petal-wings"),
    "verdant_rock_pod": dict(
        kind="rock", cell=192, size=170, facing="any",
        brief="Thorn Pod: a hazard (NOT a craft: no engines, no thrusters, no jets, no machinery except a couple of rope or brass bands): a "
              "wind-blown boulder of dark bark-wrapped stone, a round seed-pod shape ringed with long hard thorns, with a split seam down one "
              "side glowing soft amber-orange like pollen light. PALETTE: charcoal-brown bark with grey-violet shadows and pale tan highlights, "
              "bone-white thorns, amber glow in the seam, a little moss.",
        wings=""),
    "verdant_rock_spore": dict(
        kind="rock", cell=192, size=166, facing="any",
        brief="Spore Rock: a hazard (NOT a craft: no engines, no thrusters, no jets, no machinery): a chunky faceted boulder of slate-blue "
              "mossy stone, with teal and olive moss patches, a few pale cream spore-vent holes that glow faintly, small grey mushroom "
              "shelves clinging to it, cracks. PALETTE: slate blue-grey stone, teal moss, cream and pale lemon vents, no bright lime.",
        wings=""),
    "verdant_rock_knot": dict(
        kind="rock", cell=192, size=164, facing="any",
        brief="Bramble Knot: a hazard (NOT a craft: no engines, no thrusters, no jets, no machinery): a rough chunk of grey granite strangled "
              "by thick walnut-brown thorny vines knotted around it, with long thorns, a few dry leaves, and one small glowing bile-yellow bud "
              "in the knot. PALETTE: warm grey granite, walnut and umber vines, bone thorns, a small gold-yellow glow.",
        wings=""),
    "verdant_rock_vine": dict(
        kind="rock", cell=192, size=170, facing="any",
        brief="Vine Rock: a hazard (NOT a craft: no engines, no thrusters, no jets, no machinery): a ragged lump of rust-clay earth and stone "
              "torn up by the wind, with torn pale roots and a few dark vines trailing from it, small tufts of moss, one or two tiny "
              "glowing pale-green buds. PALETTE: rust-clay and ochre earth, grey stone, dark vines, moss, a pale mint glow.",
        wings=""),
}

KEEP = ("verdant_alien",)


def prompt(name, out_path):
    b = BRIEFS[name]
    facing = {"down": "nose / weapon pointing DOWN the screen, engines at the top", "any": "a tumbling hazard, any natural orientation, upright"}[b["facing"]]
    return (f"Use your built-in image generation tool to paint ONE game sprite from scratch, a ground-up redesign (new surface detail, "
            f"re-thought parts, its own colour scheme), for a vertical phone shooter set in the jungle Verdant world. Subject: {b['brief']} "
            f"Orientation: {facing}. Style: {STYLE} Output exactly one sprite on a square transparent canvas. Save the final PNG (RGBA, "
            f"transparent background) to {out_path} (copy it from the generated images folder if needed) and reply with just the path.")


VARIANTS = {
    "a": "mid-stroke: {w} lifted and tilted about halfway up, so they look slightly foreshortened and shifted, and any rotor or engine "
         "glow slightly different (flame a little longer)",
    "b": "top of the stroke: {w} swept fully up and back, strongly foreshortened and tilted (about 70% of their drawn span), a little "
         "motion-blurred at the tips, and engine flames flickering shorter",
}


def variant_prompt(name, which, in_path, out_path):
    b = BRIEFS[name]
    return (f"The attached image is the MASTER sprite of a game enemy ({b['brief'].split(':')[0]}). Use your built-in image generation tool to "
            f"paint the SAME craft in a different wing-beat pose for an animation frame: {VARIANTS[which].format(w=b['wings'])}. Everything "
            f"else must stay IDENTICAL to the master: same design, same palette and materials, same hull, cockpit, weapons, trim and "
            f"details, same size, same centre position and same top-down facing; ONLY the wings (and tiny engine flicker) change. Same "
            f"hard-edged HD pixel-art rendering. Genuinely transparent background, square canvas, sprite centred with a generous margin. "
            f"ABSOLUTELY NO outline, pale rim, glow halo or drop shadow. No text. Output exactly one sprite. Save the final PNG (RGBA, "
            f"transparent background) to {out_path} (copy it from the generated images folder if needed) and reply with just the path.")


if __name__ == "__main__":
    for n in BRIEFS:
        print("==", n, "\n", prompt(n, "<out>/" + n + ".png"), "\n")
