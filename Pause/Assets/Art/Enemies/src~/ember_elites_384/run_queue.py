#!/usr/bin/env python3
"""Resumable Codex queue for the Ember elite 384 px re-render. One image per codex call, every call under the shared lock.
  run_queue.py <phase> [--wait-until EPOCH]    phase: masters | extras (damaged + flight B, from the vetted masters) | <job name>
Skips jobs whose output already exists and is a valid image; retries 429 (usage cap) after 15 min. Prints ALL_DONE when every job of the phase is valid."""
import subprocess, sys, time, os
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[5]
REL = "Pause/Assets/Art/Enemies/src~/ember_elites_384"
LOCK = "/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh"
LOGS = Path("/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/ember/logs")
NAMES = "brass_vulture sunstoke kilnback cauterizer ash_wraith coalrunner".split()      # priority order (the user's favourite first)

NAMEDISP = dict(sunstoke="Sunstoke (a spear-nosed interceptor)", coalrunner="Coalrunner (a broadside gunship)", brass_vulture="Brass Vulture (a salvage striker)",
                kilnback="Kilnback (an armoured hauler)", ash_wraith="Ash Wraith (a shadow skirmisher)", cauterizer="Cauterizer (a siege catamaran)")

# the flight-B pose: only these parts move, everything else (centre body, engine bells) stays pixel-exact where it is
POSE = dict(
    sunstoke="the two swept wing blades flare out and back a little further (about 5 percent wider span) and tilt, the side boiler vent flaps open a notch; the spear nose, the central fuselage and the two engine bells stay exactly where they are",
    coalrunner="the two side gun pods slide back about 6 px (recoil) and their barrels shorten, the stern plates lift slightly; the central hull and the three engine bells stay exactly where they are",
    brass_vulture="the hooked wing arms sweep wider and forward a few degrees and the recovery claw jaws open a notch; the lattice spine, the cockpit and the tail engine bell stay exactly where they are",
    kilnback="the big slab shields on both sides spread outward a few degrees and the hatches between them shift open a notch; the central cargo spine and the two engine bells stay exactly where they are",
    ash_wraith="the crescent fins sweep inward and back a few degrees and the split tail blades flex outward; the beak, the central body and the two engine bells stay exactly where they are",
    cauterizer="the two heat shields spread outward a few degrees and the six side vents glow slightly brighter; the central cannon spine and the two engine bells stay exactly where they are",
)

HEAD = ("YOU MUST USE IMAGE GENERATION (the built-in image generation tool) to make the art. Do not hand-draw it with scripts. Modify no file except the output "
        "file named at the end; no git commits.\n\n")
TAIL = ("Rules: top-down view, nose pointing straight UP, same left-right symmetry as the input. NO outline of any kind: no pale rim, no white border, no glow "
        "halo, no ground shadow; the ship edge is just the painted body against the background. NO engine flames: every engine nozzle is drawn EMPTY, a dark "
        "bell with its dark mouth facing the viewer. Do not add new hues: no red, no pink, no magenta, no lime green (#00FF40 is the key). Hard pixel edges, "
        "crisp painterly HD pixel art, no text. Background: one perfectly flat pure #00FF40 green, nothing else; the ship centred with at least 12 percent "
        "margin on every side. Output ONE PNG about 1024x1024. Copy the finished image to the exact file path given below.\n")


def prompt(job):
    kind, n = job.split(":")
    out = f"{REL}/masters/{n}.png" if kind == "master" else f"{REL}/masters/{n}_{kind}.png"
    d = NAMEDISP[n]
    if kind == "master":
        t = (f"The attached image ({REL}/inputs/{n}_in.png) is the current low-resolution game sprite of the elite enemy {d} of 'Pause', a vertical 2D "
             "shooter, enlarged 4x with a flat green key background. Re-render the SAME ship as a high-resolution HD pixel-art painting: identical design, "
             "silhouette, composition, proportions, every part in the same place, and the SAME colour palette (keep the exact hull, plate, trim and light "
             "colours of the input). Add real HD detail the small sprite could not show: crisp panel seams, rivets, vents, hydraulics, scuffs, material "
             "shading in 4-6 tone ramps, small clean emissive lights. Do not redesign, do not add or remove parts, do not crop. The input shows small "
             "engine flames: remove them and paint each engine bell empty and dark instead. ")
    elif kind == "damaged":
        t = (f"The attached image ({REL}/masters/{n}.png) is the finished sprite of the elite enemy {d}. Paint the SAME ship in the SAME position, scale and "
             "overall silhouette, but now badly DAMAGED and about to fall: scorched soot-black burn patches, large cracked and dented armour plates with small "
             "orange-hot cracks, two or three panels bent open or hanging loose by hinges exposing the dark inner frame and wiring, torn and bent edges and "
             "struts, a few dark grey smoke wisps curling off the hull (drawn as hard-edged pixel puffs, close to the ship). Same palette and materials as "
             "the input, only dirtier, darker and broken; keep the design recognisable and every engine bell present (empty, dark, no flames). The outer "
             "silhouette may change by at most 5 percent. ")
    else:
        t = (f"The attached image ({REL}/masters/{n}.png) is the finished sprite of the elite enemy {d}. Paint the SAME ship, same design and palette, in a "
             f"SECOND flight pose for a two-drawing idle loop: {POSE[n]}. Everything else is identical to the input, same position, same size, same detail. "
             "Move the named parts only a little so the two drawings read as a living machine, not a different ship. ")
    return HEAD + t + TAIL + f"Save as: {out}\n", out


def valid(path):
    try:
        im = Image.open(path).convert("RGB")
        return im.size[0] >= 512 and im.getpixel((4, 4))[1] > 200 and im.getpixel((4, 4))[0] < 60
    except Exception:
        return False


def jobs_for(phase):
    if phase == "masters":
        return [f"master:{n}" for n in NAMES]
    if phase == "extras":
        return [f"damaged:{n}" for n in NAMES] + [f"flightb:{n}" for n in NAMES]
    return [phase]


def main():
    a = sys.argv[1:]
    if "--wait-until" in a:
        i = a.index("--wait-until"); until = int(a[i + 1]); del a[i:i + 2]
        while time.time() < until:
            time.sleep(min(60, max(1, until - time.time())))
    LOGS.mkdir(parents=True, exist_ok=True)
    jobs = jobs_for(a[0])
    pending = list(jobs)
    tries = {j: 0 for j in jobs}
    while pending:
        job = pending[0]
        p, out = prompt(job)
        outp = REPO / out
        if valid(outp):
            pending.pop(0); continue
        kind, n = job.split(":")
        img = f"{REL}/inputs/{n}_in.png" if kind == "master" else f"{REL}/masters/{n}.png"
        outp.parent.mkdir(parents=True, exist_ok=True)
        log = LOGS / f"{kind}_{n}.log"
        print(time.strftime("%H:%M:%S"), "run", job, flush=True)
        r = subprocess.run([LOCK, "codex", "exec", "-m", "gpt-5.6-luna", "--skip-git-repo-check", "-s", "workspace-write", "-C", str(REPO), "-", "-i", str(REPO / img)],
                           input=p, text=True, capture_output=True)
        log.write_text((r.stdout or "") + "\n" + (r.stderr or ""))
        if valid(outp):
            print("ok", job, flush=True); pending.pop(0); continue
        txt = (r.stdout or "") + (r.stderr or "")
        tries[job] += 1
        print("fail", job, "429" if ("usage_limit" in txt or "429" in txt) else "other", tries[job], flush=True)
        if tries[job] >= 10:
            print("giving up on", job, flush=True); pending.pop(0); continue
        time.sleep(900 if ("usage_limit" in txt or "429" in txt) else 120)
    bad = [j for j in jobs if not valid(REPO / prompt(j)[1])]
    print("ALL_DONE" if not bad else f"INCOMPLETE {bad}", flush=True)


main()
