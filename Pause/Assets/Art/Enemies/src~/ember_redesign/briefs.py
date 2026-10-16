#!/usr/bin/env python3
"""Ember enemy REDESIGN briefs (ground-up repaint, one Codex image per sprite, text-only prompts).

Why: the scripted hue remap (PR #28 first pass) only recoloured the old orange art. The user wants each Ember enemy redesigned the way
Tide was: new surface detail, re-thought parts, and a palette invented per enemy for its own concept, not one shared recipe.
Kept as they are (user approved, never touched): ember_fighter_1 "Cinder", ember_chaser "Cinder Fang", ember_elite_brass_vulture.

Rules baked into every prompt: top-down, FLYING machine, transparent background, no outline / rim / halo, no pink / magenta / player
red, orange reserved for flames, muzzles and engine glow. No reference image is attached (it would copy colours): the design is words only.

  python3 briefs.py            # prints every prompt (for review)
"""

STYLE = (
    "HD neon pixel art, hand-placed pixels with crisp hard edges, dense 5-6 step material ramps with a WIDE value range, rivets, panel "
    "seams, scratches and wear; real materials (metal, ceramic, glass, bone, stone) that each read differently; bright glow only on "
    "small functional parts (engines, weapon tips, vents, lenses, cores) with a tiny spill of light onto the plating beside them. "
    "Top-down orthographic view. If the subject is a craft it is an AIRBORNE flying machine, never a ground walker, never a swimmer; if it is a rock hazard it is natural stone with NO engines or thrusters. Genuinely transparent "
    "background, sprite centred with at least 12% empty margin on every side. ABSOLUTELY NO outline of any kind: no pale rim, no white or "
    "orange border, no glow halo, no drop shadow around the silhouette; the painted body simply ends against transparency. FORBIDDEN "
    "colours: pink, magenta, red (the player ship owns red), dominant orange or brown-orange hulls. Orange/yellow flame is allowed ONLY as "
    "engine exhaust, muzzle glow and small vents. No text, labels, grid, scenery or other sprites."
)

# name -> dict(kind, facing, size (target longest side in px at final cell), role, brief)
BRIEFS = {
    "ember_alien": dict(
        kind="enemy", cell=192, size=137, facing="down",
        brief="Ember Imp: a small caged flame-sprite drone. A tall hovering cage of curved fins around a glowing core, with a pointed crown "
              "and tail spike, little fang-like tips, a ring of tiny hover vents underneath. PALETTE (invent nothing else): bottle-green and "
              "dark moss-black cage fins with verdigris wear, bone-white fang tips and rivets, a molten citrine-yellow core with a white-hot "
              "centre, small acid-lime status lamps. The cage reads as living enamel, not rusty metal."),
    "ember_big": dict(
        kind="enemy", cell=256, size=218, facing="down",
        brief="Magma Skull: a broad, heavy flying furnace-fortress seen from above shaped like a horned skull. Crown of armour plates, two big "
              "curved horns, wide jaw plates that open onto a glowing maw at the bottom, side thruster drums and rear engine stacks with "
              "flames. PALETTE: bleached bone-ivory skull plates with cracks and soot stains, graphite and blued-steel armour underneath, "
              "sulphur-yellow glowing eye slits and maw, a few cobalt-blue lamps and verdigris copper pipes. Bone and steel, not orange."),
    "ember_fighter_2": dict(
        kind="enemy", cell=192, size=162, facing="down",
        brief="Scorch: a broad flying gunship with one heavy cannon barrel pointing down the screen, two swept pincer-shaped wing arms with "
              "wingtip lamps, two rear engine nozzles with flames, a cockpit canopy, side vents that glow before it fires. PALETTE: "
              "enamel off-white and cobalt-blue hull panels in hazard livery with signal-yellow chevrons and numbers-free stripes, matte "
              "black cannon with a white-hot muzzle, cyan canopy glass, chipped paint showing grey steel."),
    "ember_fighter_3": dict(
        kind="enemy", cell=192, size=168, facing="down",
        brief="Brand: a sleek swept raider with two long hooked horn-blade wings curving forward, a narrow spine hull, small wing vents and a "
              "twin-nozzle engine with flames at the rear, a tiny cockpit lens. PALETTE: oxidised verdigris-green hull with deep teal-black "
              "shadows, ivory horn-blades with a worn edge, gold-yellow vent grills and engine rings, a small cyan lens."),
    "ember_fighter_4": dict(
        kind="enemy", cell=192, size=171, facing="down",
        brief="Pyre: a round heavy furnace dreadnought gunship: a large grated furnace eye in the middle, a smokestack-shaped dorsal turret, "
              "four stubby side pylons with cannon tips and eight vent ports around the rim, three rear engine nozzles with flames. PALETTE: "
              "brushed gunmetal and silver plating with dark steel seams, electric-blue pylons and ring lights, a bright amber furnace eye "
              "behind a grille, white-hot cannon tips."),
    "ember_rock_magma": dict(
        kind="hazard", cell=192, size=157, facing="any",
        brief="Magma Rock: a cluster of tall hexagonal basalt columns of uneven heights fused together, with a jagged stepped top and one long diagonal fracture (an angular, wide-and-tilted silhouette, clearly NOT a round boulder), a rock hazard (NOT a craft: no engines, no thrusters, no jets, no exhaust flames, "
              "no machinery except a few brass staples across the cracks). Deep fractures with luminous veins. PALETTE: charred blue-black basalt with violet "
              "shadows and pale ash-grey frost on the lit edges, veins glowing sulphur-yellow with a tiny orange core, tarnished brass staples."),
    "ember_rock_cinder": dict(
        kind="hazard", cell=192, size=157, facing="any",
        brief="Cinder Chunk: a tall block of slag-iron, a rock hazard (NOT a craft: no engines, no thrusters, no jets, no exhaust flames), "
              "with pipe wraps, flanges and a small forge vent door with a glow. PALETTE: slate-grey slag iron with soot-black recesses, "
              "verdigris-patina copper pipes and bolts, pale chalk-white mineral crust, the vent glowing lemon-yellow."),
    "ember_rock_obsidian": dict(
        kind="hazard", cell=192, size=164, facing="any",
        brief="Obsidian Shard: a long narrow blade of volcanic glass, a rock hazard (NOT a craft: no engines, no thrusters, no jets, no "
              "flames), faceted body, hairline veins. PALETTE: glossy black glass with deep violet-blue sheen and indigo reflections, "
              "ice-white edge highlights, a few thin amber veins, a cyan glint on the tip."),
    "ember_rock_islet": dict(
        kind="hazard", cell=192, size=168, facing="any",
        brief="Lava Islet: a floating chunk of rock slab, a natural hazard (NOT a craft: no engines, no thrusters, no jets, no exhaust), "
              "a small factory stack with pipes on top emitting smoke, a few molten drips glowing at the underside edge. PALETTE: "
              "slate-blue and grey-violet basalt, pale cream-grey ash patches, rust-iron stack with teal-green lamp, neutral grey-lavender "
              "smoke, lava only as small gold-yellow glowing drips and a few cracks."),
    "ember_elite_sunstoke": dict(
        kind="elite", cell=192, size=150, long=160, facing="up",
        brief="Sunstoke: an elite spear interceptor, nose UP: tall heat-lance nose spike, narrow split swept wings, two offset boiler thrusters "
              "at the rear (flames at the bottom, nozzle centres about x=86 and x=106 of 192, y=160). PALETTE: pearl-white and warm cream "
              "ceramic hull, sapphire-blue canopy and wing panels, a citrus-yellow glowing lance tip, brushed titanium boilers."),
    "ember_elite_coalrunner": dict(
        kind="elite", cell=192, size=132, long=142, facing="up",
        brief="Coalrunner: a compact armoured broadside gunship, nose UP: broad armoured prow, side furnace nacelles with gun ports, "
              "three rear engine nozzles with flames (x about 69, 96, 123 of 192, y=150). PALETTE: olive-drab and sand-khaki military "
              "plating with chipped dark-green paint, steel gun barrels, bright signal-yellow identification stripes, a white-hot gun "
              "port glow and a cyan cockpit lamp."),
    "ember_elite_kilnback": dict(
        kind="elite", cell=192, size=150, long=158, facing="up",
        brief="Kilnback: an elite armoured hauler, nose UP: tall rectangular forge-pack on its back, two slab shield wings, hanging chain "
              "ballast, two slag hatches and two big rear nozzles with flames (x about 79 and 113 of 192, y=165). PALETTE: slate-blue slab "
              "shields over cream ceramic-tile panels with brown kiln stains, black iron chains, glowing white-mint hatch slits, "
              "turquoise lamps."),
    "ember_elite_ash_wraith": dict(
        kind="elite", cell=192, size=150, long=156, facing="up",
        brief="Ash Wraith: an elite shadow skirmisher, nose UP: slim crescent hull, swept shroud fins, a beak shard gun at the nose, split "
              "twin exhaust tails with flames at the rear (x about 83 and 109 of 192, y=164). PALETTE: ash-grey and smoke-violet cloak "
              "panels with pale lilac frayed edges, cold white-blue core light, teal exhaust glow, tarnished silver."),
    "ember_elite_cauterizer": dict(
        kind="elite", cell=192, size=150, long=154, facing="up",
        brief="Cauterizer: an elite siege catamaran, nose UP: two wide hulls with six heat vents, a central pressure cannon at the nose, "
              "layered heat shields, two rear nozzles with flames (x about 66 and 126 of 192, y=163). PALETTE: matte black armour with "
              "emerald-green heat shields, polished gold pressure cannon bands, cyan vent glows, white-hot cannon mouth."),
}

KEEP = ("ember_fighter_1", "ember_chaser", "ember_elite_brass_vulture")   # user-approved, never regenerated


def prompt(name, out_path):
    b = BRIEFS[name]
    facing = {"down": "nose / weapon pointing DOWN the screen, engines at the top", "up": "nose pointing UP the screen, engines at the bottom",
              "any": "a tumbling hazard, any natural orientation, upright"}[b["facing"]]
    return (f"Use your built-in image generation tool to paint ONE game sprite from scratch, a ground-up redesign (new surface detail, "
            f"re-thought parts, its own colour scheme), for a vertical phone shooter set in the volcanic Ember world. Subject: {b['brief']} "
            f"Orientation: {facing}. Style: {STYLE} Output exactly one sprite on a square transparent canvas. Save the final PNG (RGBA, "
            f"transparent background) to {out_path} (copy it from the generated images folder if needed) and reply with just the path.")


if __name__ == "__main__":
    for n in BRIEFS:
        print("==", n, "\n", prompt(n, "<out>/" + n + ".png"), "\n")
