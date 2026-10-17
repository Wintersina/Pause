#!/usr/bin/env python3
"""Resumable Codex queue for the three old 192 px elites (Cryo Siren, Rimebreaker, Resin Warden): one image per `codex exec` call (model
gpt-5.6-luna, every call under the shared lock), each frame a distinct painting of the same design.

  masters : cryo_siren, rimebreaker repaint their CURRENT art (inputs/*_current.png); resin_warden is designed from words (no image input)
  frames  : parked, liftoff, flightB, bankL, bankR, damaged -- each takes that ship's master as the image input and describes the pose change

Files land in frames/<ship>_<frame>.png (flat #00FF40 background). Finished files are skipped, so the queue is resumable.
  python3 run_queue.py [--wait-until EPOCH] [--only ship[,ship]] [--frames master,bankL,...]
"""
import os, re, subprocess, sys, time
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]                     # worktree root
REL = HERE.relative_to(ROOT)
LOCK = "/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh"
CODEX = "/Users/sina/.local/bin/codex"
LOGS = Path("/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/frv/logs")

COMMON = """YOU MUST USE THE BUILT-IN IMAGE GENERATION TOOL to make the art. Do not hand-draw it with scripts. Work only inside the folder {rel}/frames/ ; modify no other file; no git commits.

Game: "Pause", a vertical 2D shooter in HD pixel art. This is an elite enemy FLYING CRAFT seen top-down, nose pointing straight UP.
Style: hard-surface, grounded, military / industrial; painterly high-detail pixel-art material shading (painted steel, panel lines, rivets, vents, hydraulics, cables), crisp hard pixel edges, one strong readable silhouette. Not cartoony, no text, no symbols.
NO outline of any kind: no pale or white rim line, no border, no halo, no glow aura, no ground shadow; the edge of the ship is just the painted body against the background.
Engine bells at the rear (bottom) are drawn EMPTY AND DARK with NO FLAMES (flames are added later by code).
Forbidden colours anywhere on the ship: red, pink, magenta, lime green (#00FF40 is the key background).
Background: one perfectly flat pure #00FF40 green, nothing else in the picture; the ship centred with at least 12% margin on every side. Output ONE PNG about 1024x1024, ship alone. Copy the finished image to the exact file path given at the end.
"""

SHIPS = {
    "cryo_siren": dict(
        name="CRYO SIREN (Frost world: an icy crystalline craft)",
        master_input="inputs/cryo_siren_current.png",
        master="""Attached image: the CURRENT low-resolution art of this ship. Paint the SAME ship again as a fresh, full-detail HD painting (not a filter, not a resize): keep the design exactly (same silhouette and proportions: the needle nose tip, the big ringed coolant dish with its glowing core crowning the slim spine, the narrow tail with the two small engine bells and the little side fins/struts), but with these CHANGES: remove the pale/white rim line around the whole ship; replace the washed-out flat white-blue with clean, believable painted materials and a rich multi-tone palette: pearl-white ice-ceramic armour (#EAF4FF light, #9CC4E8 mid, #5C84B8 shadow), deep indigo-navy structure plates and underframe (#222B58), polished silver-steel trim and panel lines, small warm brass-copper clamps and rivets (#C98A3E), and ONE glow colour: a luminous aqua-cyan coolant lens in the dish and thin conduit lines (#7FF3F0 core, #2FB8E0 edge). Chunky hard-surface plating with real panel breaks, vents, rivets and cable runs; ice crystal facets only as a few cut, clean crystal inserts, not frosting. The two rear engine bells are clearly visible, large and dark (empty).""",
        parts="side fins and the dish ring"),
    "rimebreaker": dict(
        name="RIMEBREAKER (Frost world: a compact icebreaker ram)",
        master_input="inputs/rimebreaker_current.png",
        master="""Attached image: the CURRENT low-resolution art of this ship. Paint the SAME ship again as a fresh, full-detail HD painting (not a filter, not a resize): keep the design exactly (same silhouette and proportions: the compact armoured hull with the heavy ribbed ram prow, the plated shoulders and the long claw-like trailing braces, the paired rear thrusters), but with these CHANGES: remove the pale/white rim line around the whole ship; replace the flat white-grey look with clean painted materials and a rich multi-tone palette: dark gunmetal blue-graphite rime-steel plates (#2E3A4E shadow, #4F6680 mid), chipped pale glacier-blue frost-crust plating on the ram prow and the leading edges (#CFE6F5 light, #86B5DD mid), safety-amber hazard marks and warning lamps (#F2A93B; NOT pink or magenta), copper clamp braces (#B8733A), and ONE glow colour: cyan coolant conduits (#6FE6FF). Chunky hard-surface plating, panel lines, rivets, hydraulics, vents. The two rear thruster bells are large, clearly visible and dark (empty).""",
        parts="ram claws and shoulder plates"),
    "resin_warden": dict(
        name="RESIN WARDEN (Verdant world: a heavy winged resin-mortar craft of a forest canopy fleet)",
        master_input=None,
        master="""Design this ship from scratch, ground-up. It is an AIRBORNE heavy mortar gunship of the forest-canopy fleet: a stout armoured fuselage of overlapping layered bark-like plates (segmented armour masses, believable hull plating, brass bands and rivets), with TWO BIG broad swept-back insect-style WINGS of amber resin-glass held in brass lattice frames (visible veins, translucent amber look painted opaque), and on the leading outer edge of each wing a thick MORTAR TUBE (a short stout brass-banded barrel pointing forward/up, with a dark open muzzle) as the weapon pods. A glowing amber resin tank sits in the middle of the fuselage; a short blunt armoured prow at the top; two big engine nacelles at the rear with large dark EMPTY bells. Winged like the other craft of its fleet (insect-winged hard-surface ships) but heavier, wider and stockier, perfectly left-right symmetric, strong readable silhouette, big shapes, no thin dangling parts. Drawn large and clean enough to read at about 1.6 world units on a phone.
PALETTE (invented for this ship, bright enough to pop on a dark forest backdrop): birch-bone ivory armour plates (#E9E0C8 light, #B9AE8E mid, #7B6E52 shadow), chestnut-umber structural plates and underframe (#7A4A2A, #4A2C1A), polished brass-bronze trim and wing frames (#D9A545), tiny moss-olive patches on a few plates (#6E7F34), wings amber resin-glass (#F6B93A with #FFE08A veins), ONE glow: molten amber resin in the tank and mortar muzzles (#FFC84A core, #FF9A2A halo). Not mostly green. No outline.""",
        parts="wings"),
}

POSES = {
    "parked": """Attached image: the master of this ship. Draw the SAME ship PARKED: landed on a pad, powered down. A real new drawing of the same design: the {parts} lowered / folded a little and tucked in closer to the hull, hatches and louvres closed, every lamp, lens and glow dark and dull (unlit), a slightly hunkered posture; all colours, plates and details otherwise identical. Same overall size, same centre position, same scale as the attached image. No flames.""",
    "liftoff": """Attached image: the master of this ship. Draw the SAME ship at LIFT-OFF IGNITION: powering up and starting to rise off the pad. A real new drawing of the same design: engine bell hatches and louvres open, the {parts} raised and spreading halfway between folded and fully open, lamps and glow lit and warming up, a faint heat-haze of lit vents around the bells (no flames). Same overall size, same centre position, same scale as the attached image.""",
    "flightB": """Attached image: the master of this ship. Draw the SAME ship in the second beat of its cruising idle: same design, size, centre and scale, but a visibly different drawing: the {parts} flexed or spread about 5% further, the plates shifted a few pixels, the glow lenses pulsing brighter with a different light distribution, small differences in panel lights and highlights. Still level, symmetric, nose up, no flames.""",
    "bankL": """Attached image: the master of this ship. Draw the SAME ship BANKING LEFT as it slides sideways: top-down view with a roll: the left side dips and is foreshortened (narrower, lower), the right side tips up and shows more of its upper surface and edge thickness; the {parts} follow the roll asymmetrically; a modest roll only (about 12 degrees): do NOT rotate or tilt the ship in the picture plane, the nose tip stays at the same screen position pointing straight up and the engine bells stay at the bottom, the hull just narrows slightly and shows asymmetric surfaces; same overall size, same centre position and same scale as the attached image. No flames.""",
    "bankR": """Attached image: the master of this ship. Draw the SAME ship BANKING RIGHT as it slides sideways: top-down view with a roll: the right side dips and is foreshortened (narrower, lower), the left side tips up and shows more of its upper surface and edge thickness; the {parts} follow the roll asymmetrically; a modest roll only (about 12 degrees): do NOT rotate or tilt the ship in the picture plane, the nose tip stays at the same screen position pointing straight up and the engine bells stay at the bottom, the hull just narrows slightly and shows asymmetric surfaces; same overall size, same centre position and same scale as the attached image. No flames.""",
    "damaged": """Attached image: the master of this ship. Draw the SAME ship BADLY DAMAGED after taking a heavy hit: a real new drawing: hull plating cracked and split with dark fractures, a chunk of armour blown off one side and bent open exposing dark inner structure and wiring, scorch marks and soot, dented panels, one of the {parts} broken or bent, a few tiny bright yellow-white spark pixels, a few thin dark smoke wisps hugging the hull, glow lenses flickering dim. Still clearly the same ship at the same overall size and centre, but visibly wrecked. Engine bells scorched and dark, no flames.""",
}

ORDER_FRAMES = ["master", "bankL", "damaged", "flightB", "parked", "liftoff", "bankR"]


def jobs(only, frames):
    out = []
    for fr in ORDER_FRAMES:
        if frames and fr not in frames:
            continue
        for ship in ("resin_warden", "cryo_siren", "rimebreaker"):
            if only and ship not in only:
                continue
            out.append((ship, fr))
    return out


def valid(p):
    try:
        im = Image.open(p)
        im.verify()
        return Image.open(p).size[0] >= 512
    except Exception:
        return False


def prompt(ship, fr):
    cfg = SHIPS[ship]
    body = cfg["master"] if fr == "master" else POSES[fr].format(parts=cfg["parts"])
    if fr == "master":
        title = f"Ship: {cfg['name']}.\n"
    else:
        title = f"Ship: {cfg['name']}.\n"
    save = f"{REL}/frames/{ship}_{fr}.png"
    return COMMON.format(rel=REL) + "\n" + title + body + f"\n\nSave as: {save}\n", save


def run(ship, fr):
    text, save = prompt(ship, fr)
    LOGS.mkdir(parents=True, exist_ok=True)
    pf = LOGS / f"{ship}_{fr}.prompt.md"
    pf.write_text(text)
    if fr == "master":
        img = SHIPS[ship]["master_input"]
        img = str(HERE / img) if img else None
    else:
        img = str(HERE / "frames" / f"{ship}_master.png")
    cmd = [LOCK, CODEX, "exec", "-m", "gpt-5.6-luna", "--skip-git-repo-check", "-s", "workspace-write", "-C", str(ROOT)]
    if img:
        cmd += ["-i", img]
    cmd += ["-o", str(LOGS / f"{ship}_{fr}.last.txt"), "-"]
    with open(pf) as fin, open(LOGS / f"{ship}_{fr}.log", "w") as fout:
        subprocess.run(cmd, stdin=fin, stdout=fout, stderr=subprocess.STDOUT)
    return (ROOT / save), (LOGS / f"{ship}_{fr}.log").read_text()


def main():
    a = sys.argv[1:]
    if "--wait-until" in a:
        t = int(a[a.index("--wait-until") + 1])
        while time.time() < t:
            time.sleep(min(300, max(1, t - time.time())))
    only = a[a.index("--only") + 1].split(",") if "--only" in a else None
    frames = a[a.index("--frames") + 1].split(",") if "--frames" in a else None
    todo = jobs(only, frames)
    for ship, fr in todo:
        out = ROOT / REL / "frames" / f"{ship}_{fr}.png"
        fails = 0
        while not valid(out):
            if fr != "master" and not valid(ROOT / REL / "frames" / f"{ship}_master.png"):
                print(f"{ship} {fr}: master missing, skipped", flush=True)
                break
            p, log = run(ship, fr)
            if valid(p):
                print(f"{time.strftime('%H:%M:%S')} done {ship} {fr}", flush=True)
                break
            fails += 1
            limited = re.search(r"usage_limit|429|rate.?limit", log, re.I)
            print(f"{time.strftime('%H:%M:%S')} FAIL {ship} {fr} (#{fails}, limited={bool(limited)})", flush=True)
            if fails >= (12 if limited else 4):
                print(f"giving up on {ship} {fr}", flush=True)
                break
            time.sleep(900 if limited else 120)
    missing = [f"{s}_{f}" for s, f in todo if not valid(ROOT / REL / "frames" / f"{s}_{f}.png")]
    print("ALL_DONE" if not missing else "INCOMPLETE " + ",".join(missing), flush=True)


if __name__ == "__main__":
    main()
