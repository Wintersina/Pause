#!/usr/bin/env python3
"""Resumable Codex queue for the Desert/Ember pose sheets (one image per `codex exec`, shared lock, model GPT-5.6-Luna).

    python3 run_queue.py [--wait-until EPOCH] [names...]

Makes inputs/<name>.png (current idle cell 0 on #00FF40, 3x nearest) then asks Codex for sheets/<name>.png. A sheet is done when
it is a valid image with a mostly green background and six populated grid squares. Retries on 429 / usage-limit with a 15 min backoff.
Writes ALL_DONE (in this folder) when every sheet is valid. Logs: logs/<name>.log
"""
import re, subprocess, sys, time
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import ships

ROOT = HERE.parents[4]  # .../Pause (project root)
RES = ROOT / "Assets/Art/Resources/Enemies"
LOCK = "/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh"
CODEX = str(Path.home() / ".local/bin/codex")
ORDER = ["desert_fighter_1", "desert_fighter_2", "desert_fighter_3", "desert_fighter_4", "desert_chaser",
         "ember_fighter_2", "ember_fighter_3", "ember_fighter_4", "ember_chaser", "ember_fighter_1",
         "desert_alien", "ember_alien", "desert_big", "ember_big"]
KEY = np.array([0, 255, 64])


def make_input(name):
    p = HERE / "inputs" / f"{name}.png"
    if p.exists():
        return p
    p.parent.mkdir(exist_ok=True)
    cell = ships.SHIPS[name]["cell"]
    im = Image.open(RES / f"{name}.png").convert("RGBA").crop((0, 0, cell, cell))
    bg = Image.new("RGBA", im.size, (0, 255, 64, 255)); bg.alpha_composite(im)
    bg.convert("RGB").resize((cell * 3, cell * 3), Image.NEAREST).save(p)
    return p


def good(p):
    if not p.exists():
        return False
    try:
        a = np.asarray(Image.open(p).convert("RGB")).astype(int)
    except Exception:
        return False
    H, W = a.shape[:2]
    if W < 900 or H < 600:
        return False
    bgc = np.abs(a - KEY).sum(-1) < 120
    if bgc.mean() < .45 or bgc.mean() > .98:
        return False
    for r in range(2):
        for c in range(3):
            sq = ~bgc[r * H // 2:(r + 1) * H // 2, c * W // 3:(c + 1) * W // 3]
            if sq.mean() < .03:
                return False
    return True


def run(name):
    (HERE / "sheets").mkdir(exist_ok=True); (HERE / "logs").mkdir(exist_ok=True)
    inp = make_input(name)
    out = HERE / "sheets" / f"{name}.png"
    pr = ships.prompt(name, f"sheets/{name}.png")
    r = subprocess.run([LOCK, CODEX, "exec", "-i", str(inp), "-m", "gpt-5.6-luna", "--skip-git-repo-check", "-s", "workspace-write",
                        "-C", str(HERE), "-"],
                       input=pr, text=True, capture_output=True)
    (HERE / "logs" / f"{name}.log").write_text(r.stdout + "\n--- stderr ---\n" + r.stderr)
    text = (r.stdout + r.stderr).lower()
    if good(out):
        return "ok"
    if re.search(r"429|usage[_ ]limit|rate limit|try again", text):
        return "limit"
    return "bad"


def main():
    args = sys.argv[1:]
    if "--wait-until" in args:
        i = args.index("--wait-until"); t = int(args[i + 1]); del args[i:i + 2]
        while time.time() < t:
            time.sleep(min(300, max(1, t - time.time())))
    names = args or ORDER
    tries = {n: 0 for n in names}
    while True:
        todo = [n for n in names if not good(HERE / "sheets" / f"{n}.png")]
        if not todo:
            (HERE / "ALL_DONE").write_text("done\n"); print("ALL_DONE"); return
        n = todo[0]
        if tries[n] >= 4:
            print("giving up on", n); names = [x for x in names if x != n]
            if not names:
                print("EXHAUSTED"); return
            continue
        print(time.strftime("%H:%M:%S"), "generating", n, "try", tries[n] + 1, flush=True)
        res = run(n)
        print(" ->", res, flush=True)
        if res == "limit":
            time.sleep(900)            # backoff; does not count as an attempt
        elif res == "bad":
            tries[n] += 1


if __name__ == "__main__":
    main()
