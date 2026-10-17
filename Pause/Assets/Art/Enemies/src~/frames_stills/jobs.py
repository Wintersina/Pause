"""Frame-fix campaign, group STILLS: job table + prompt/input generator.

Each job redraws ONE idle cell (sprite, cell index) with Codex, using the
sprite's current design cell (cell 0) as the image input. build_frames.py
registers the results back into the sheets.

  python3 jobs.py        # writes in/<job>.png, prompts/<job>.md, queue.sh
"""
import os, sys
from PIL import Image
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "../../../../.."))          # Pause/ (unity project)
RES = ROOT + "/Assets/Art/Resources/Enemies/"
OUT_DIR = HERE + "/out"

# (sprite, cell, description of the object, what changes in this frame)
# Order is the Codex priority.
JOBS = [
 # ---- Space rocks (pink glow is the Space cue) ----
 ("space_rock_cluster", 1, "a cluster of three dark cratered boulders held by rusted orange straps, a pink-red glowing crack and a pink gem at the bottom",
  "the crack glow dims to a thin dull ember-red line and the gem darkens; fine grey dust motes drift off the upper-left edge; the light on the boulders turns a little so the left faces are brighter; a small chip is flaking off the edge of the right boulder"),
 ("space_rock_cluster", 2, "a cluster of three dark cratered boulders held by rusted orange straps, a pink-red glowing crack and a pink gem at the bottom",
  "the crack flares hot pink-white and sends short new vein branches across the neighbouring boulders; the gem blazes with a small star glint; the whole cluster is turned about 6 degrees clockwise so the lighting on the facets shifts; bolt heads on the straps catch a highlight"),
 ("space_rock_cluster", 3, "a cluster of three dark cratered boulders held by rusted orange straps, a pink-red glowing crack and a pink gem at the bottom",
  "the glow is at a medium level; hairline cracks open on the top-left boulder; three or four tiny rock chips float free near the lower right; the cluster is turned about 5 degrees counter-clockwise; a strap buckle glints"),
 ("space_rock_crater", 1, "a pale regolith asteroid slab with craters, rusted pipes and a copper valve on its left, and a small pink beacon bulb on a short mast at the top",
  "the beacon bulb pulses brighter with a soft pink halo; the little antenna beside it leans slightly; a puff of pale dust drifts off the left edge; one small pale rock chip hangs just below the bottom edge"),
 ("space_rock_crater", 2, "a pale regolith asteroid slab with craters, rusted pipes and a copper valve on its left, and a small pink beacon bulb on a short mast at the top",
  "the beacon bulb is nearly dark (a dull maroon ember); the light on the slab turns so the crater shadows are deeper on the right and the left rim is bright; a dust trail streams from the right edge; the hanging pipe has swung a little"),
 ("space_rock_crater", 3, "a pale regolith asteroid slab with craters, rusted pipes and a copper valve on its left, and a small pink beacon bulb on a short mast at the top",
  "the beacon glows at a medium level; two pale chips are breaking away from the top-right edge with a hairline crack behind them; a bright glint crosses one crater rim; the antenna leans the other way"),
 ("space_rock_dark", 1, "a dark grey riveted armoured asteroid with seam-divided plates and a thin pink glowing crack ending in a pink glow node",
  "the glowing crack grows longer and forks into two thin branches; the node glows hotter; a rivet on the upper-left plate glints; a few faint dust specks drift off the top edge"),
 ("space_rock_dark", 2, "a dark grey riveted armoured asteroid with seam-divided plates and a thin pink glowing crack ending in a pink glow node",
  "the glow shrinks to a small dim dot and the crack almost fades; the highlight plane shifts across the plates (light from the upper right instead); one plate seam lifts a hair and a small chip drifts away from it"),
 ("space_rock_dark", 3, "a dark grey riveted armoured asteroid with seam-divided plates and a thin pink glowing crack ending in a pink glow node",
  "the node pulses bright with a thin ring flare around it and extra hairline veins radiating; the plates are lit from the left; two small chips float beside the lower-right corner"),
 # ---- Frost ----
 ("frost_rock_rime", 1, "a six-pointed ice-crystal star with brass fittings and a glowing cyan hex core in the middle",
  "the cyan core dims to a deep teal; a bright white glint travels along the upper-left crystal arm; a few tiny frost crystals drift upward beside the arms"),
 ("frost_rock_rime", 2, "a six-pointed ice-crystal star with brass fittings and a glowing cyan hex core in the middle",
  "the cyan core is at its brightest with a small bloom; a glint sits on the right crystal arm; a small ice flake chips off the lowest tip and hangs just beside it; the facets show a slight turn of the light"),
 ("frost_rock_rime", 3, "a six-pointed ice-crystal star with brass fittings and a glowing cyan hex core in the middle",
  "a ring of pale frost shimmer around the core; a glint on the lower-left arm; a hairline frost crack appears on the top arm and a few frost specks drift off"),
 # ---- Space alien ----
 ("space_alien", 1, "a crab-like alien drone, a grey armoured carapace dome with a big magenta eye, bronze segmented legs with teal joints",
  "a walking step: the left legs lift and swing forward with the knees raised, the right legs push back and straighten; the carapace plates shift and the whole body lowers a few pixels; the eye is half lidded"),
 ("space_alien", 2, "a crab-like alien drone, a grey armoured carapace dome with a big magenta eye, bronze segmented legs with teal joints",
  "the mirrored step: right legs lift and swing forward, left legs push back; the body rises a few pixels and tilts a little; the eye pupil slides to the left and glows brighter; small side wing-plates flare out slightly"),
 ("space_alien", 3, "a crab-like alien drone, a grey armoured carapace dome with a big magenta eye, bronze segmented legs with teal joints",
  "legs together mid-stride, claws tucked in; the carapace tilts to the right; the eye is wide with a bright magenta glint; carapace plates ripple open a hair showing a magenta glow under the rim"),
 # ---- Tide / Verdant ----
 ("tide_rock_urchin", 1, "a spiny sea-mine urchin: a brass and dark-metal sphere with a glowing amber core and long ivory spikes",
  "the spikes on the left half flex and bend slightly clockwise while the right half flex the other way; the amber core pulses brighter; two tiny pale bubbles drift off the upper right"),
 ("tide_rock_urchin", 2, "a spiny sea-mine urchin: a brass and dark-metal sphere with a glowing amber core and long ivory spikes",
  "the spikes contract and stand a little shorter and straighter; the amber core dims; the body is turned a few degrees so the plate seams move; a cyan sparkle crosses the lower spikes"),
 ("tide_rock_urchin", 3, "a spiny sea-mine urchin: a brass and dark-metal sphere with a glowing amber core and long ivory spikes",
  "the spikes spread wider and fan outward, the top spikes slightly curved; the core glows at a medium level with a small ring; a thin line of silver bubbles trails off the bottom spike"),
 ("verdant_rock_knot", 1, "a rock bound in thorny brown vines with a glowing amber eye in the middle",
  "the vines tighten and pull in, thorn tips pointing sharper; the amber eye opens wider and brighter; one small spore mote floats off the upper left"),
 ("verdant_rock_knot", 2, "a rock bound in thorny brown vines with a glowing amber eye in the middle",
  "the vines loosen and bulge outward; a small leaf-bud tip sheds from the upper right; the amber eye is half lidded and dim; the light on the stone turns to come from the left"),
 ("verdant_rock_knot", 3, "a rock bound in thorny brown vines with a glowing amber eye in the middle",
  "the stone turned a few degrees clockwise so the vines and thorns sit in new positions; thorn tips glint; a drop of amber sap hangs below the eye; the eye is medium bright"),
 ("verdant_rock_spore", 1, "a mossy blue-grey boulder with pale yellow spore pods in the moss",
  "the spore pods swell and glow brighter pale yellow; a small puff of pale yellow spores drifts off the top right; the moss ripples and the light turns slightly"),
 ("verdant_rock_spore", 2, "a mossy blue-grey boulder with pale yellow spore pods in the moss",
  "the pods deflate and sink dim; the moss lies flatter; one pod on the left has burst, leaving a tiny crater with a few spores hanging above it"),
 ("verdant_rock_spore", 3, "a mossy blue-grey boulder with pale yellow spore pods in the moss",
  "a cloud of pale yellow spores drifts up and off the upper left; a different set of pods is swollen; a highlight crosses the right rock facets"),
 # ---- Minor Frost items (cell index = the cell to redraw) ----
 ("frost_chaser", 2, "a small frost-gunship with a brass and dark-steel cylindrical hull with a cyan glowing core, a cluster of ice-crystal wings and a long ice spike nose, pointing right",
  "a different flight pose than the input: the ice-crystal wing cluster on top folds a bit lower and the spike nose tilts slightly upward, the lower ice shards flare; the cyan core glows a little brighter; a few ice specks trail behind"),
 ("frost_chaser", 3, "a small frost-gunship with a brass and dark-steel cylindrical hull with a cyan glowing core, a cluster of ice-crystal wings and a long ice spike nose, pointing right",
  "a different flight pose than the input: the ice-crystal wing cluster on top lifts and spreads, the spike nose tilts slightly downward, the rear brass nozzle ring turns a few degrees, and the cyan core glows a little dimmer; a few ice specks trail behind"),
 ("frost_fighter_2", 3, "a frost fighter: a dark steel barrel body with a cyan core, big jagged ice blades along its left, hanging icicles below, a small antenna on top and a white steam puff at the right",
  "a different pose than the input: the left ice blade swings outward and up, the icicles swing the other way, the antenna sways, and the steam puff is larger and drifts further right"),
 ("frost_rock_shard", 1, "a crystalline frost rock: a big cyan ice crystal over a dark metal and brass body with a glowing cyan core, icicles hanging below",
  "a different pose than the input: the crystal tilts a few degrees left, a side icicle swings, the dark body plate shifts, a glint travels across the crystal and the cyan core glows brighter"),
]


RIMLESS = {"space_alien", "tide_rock_urchin", "verdant_rock_knot", "verdant_rock_spore"}
ORIG = ROOT + "/Assets/Art/Enemies/src~/ship_scale_border~/original/"


def base_sheet(sprite):
    """The rimless source strip: the outline pass's original/ when the sprite has one (Space rocks, Frost rime)."""
    p = ORIG + sprite + ".png"
    return Image.open(p if os.path.exists(p) else RES + sprite + ".png").convert("RGBA")


JOBS.sort(key=lambda j: j[1] != 3)   # the approved c3 frames first, then c1 / c2 / the minor cells (stable)


def cell0(sprite):
    a = base_sheet(sprite)
    cw = a.size[1]
    return a.crop((0, 0, cw, cw)), cw


PROMPT = (
    "Edit the attached sprite (image {inp}; {desc}) into a NEW ANIMATION FRAME of the same object for a neon pixel-art "
    "top-down shooter. Use your image generation tool. Keep the IDENTICAL design, palette, rendering style, lighting rules, "
    "canvas size, position and overall scale: the body must sit on the same pixels as in the input so the frames register. "
    "Change ONLY this: {change}. Keep every other detail painted exactly as in the input. {rim} It must be a genuinely different drawing from the input, not a copy. "
    "{world}Transparent background with a real alpha channel (no backdrop, no ground shadow, no text). "
    "Save the final PNG (RGBA) to {out} and reply with just the path."
)


FUNC = r"""run_job() { n=$1
  [ -f "$H/out/$n.ok" ] && return 0
  for try in 1 2 3 4 5 6; do
    rm -f "$H/out/$n.png"
    $S/codex_lock.sh /Users/sina/.local/bin/codex exec -m gpt-5.6-luna --skip-git-repo-check -s workspace-write -C "$H" -i "$H/in/$n.png" - < "$H/prompts/$n.md" > "$H/out/$n.log" 2>&1
    if [ -s "$H/out/$n.png" ] && $S/venv/bin/python3 -c "from PIL import Image;Image.open('$H/out/$n.png').load()" 2>/dev/null; then touch "$H/out/$n.ok"; return 0; fi
    if grep -q -E "usage_limit_reached|429" "$H/out/$n.log"; then sleep 900; else sleep 20; fi
  done; }"""

def main():
    for d in ("in", "prompts", "out"):
        os.makedirs(HERE + "/" + d, exist_ok=True)
    lines = ["#!/bin/bash", "# resumable queue: runs each missing job, one image per codex call, under the global lock",
             "S=/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad",
             'cd "$(dirname "$0")"', "H=$(pwd)", FUNC]
    for sprite, cell, desc, change in JOBS:
        name = f"{sprite}_c{cell}"
        c, cw = cell0(sprite)
        c.resize((cw * 4, cw * 4), Image.NEAREST).save(f"{HERE}/in/{name}.png")
        world = ("No pink anywhere (cyan/white/ice tones only). " if sprite.startswith(("frost", "tide", "verdant"))
                 else "Pink/magenta glow is allowed (it is the Space world's cue). ")
        rim = ("The sprite has NO outline: do not add any outline, pale rim, white border or halo around it." if os.path.exists(ORIG + sprite + ".png") or sprite in RIMLESS else "Keep its existing thin pale outline exactly as in the input, nothing more.")
        p = PROMPT.format(rim=rim, inp=name + ".png", desc=desc, change=change, world=world, out=f"{HERE}/out/{name}.png")
        open(f"{HERE}/prompts/{name}.md", "w").write(p)
        lines.append(f'run_job {name}')
    lines.append('[ "$(ls $H/out/*.ok 2>/dev/null | wc -l)" -ge ' + str(len(JOBS)) + ' ] && touch $H/ALL_DONE')
    open(HERE + "/queue.sh", "w").write("\n".join(lines) + "\n")
    os.chmod(HERE + "/queue.sh", 0o755)
    print(len(JOBS), "jobs")


if __name__ == "__main__":
    main()
