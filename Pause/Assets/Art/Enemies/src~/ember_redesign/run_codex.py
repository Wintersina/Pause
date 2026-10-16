#!/usr/bin/env python3
"""Resumable Codex runner for the Ember redesign: one `codex exec` per missing sprite, sequential (never two at once).

    python3 run_codex.py                 # every sprite that has no src/<name>.png yet
    python3 run_codex.py ember_alien     # only these
    python3 run_codex.py --force NAME    # regenerate

A sprite is "done" when src/<name>.png exists and has transparent corners and >= 5% opaque pixels. Logs go to logs/<name>.log. If Codex
reports a usage / quota limit the runner stops at once (re-run after the quota resets; finished sprites are skipped).
Then vet each src/*.png at 1x and 3x, and run build_redesign.py.
"""
import re, subprocess, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import briefs

SRC = HERE / "src"; LOGS = HERE / "logs"
LOCK = "/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh"   # global one-at-a-time Codex lock
CODEX = Path.home() / ".local/bin/codex"


def good(p):
    if not p.exists():
        return False
    try:
        a = np.asarray(Image.open(p).convert("RGBA"))[:, :, 3]
    except Exception:
        return False
    corners = (a[0, 0], a[0, -1], a[-1, 0], a[-1, -1])
    return max(corners) < 16 and (a > 128).mean() > .05


def run(name):
    SRC.mkdir(exist_ok=True); LOGS.mkdir(exist_ok=True)
    out = SRC / f"{name}.png"
    p = briefs.prompt(name, str(out))
    r = subprocess.run([LOCK, str(CODEX), "exec", "--skip-git-repo-check", "--sandbox", "workspace-write", "-C", str(SRC), "-"],
                       input=p, text=True, capture_output=True)
    (LOGS / f"{name}.log").write_text(r.stdout + "\n--- stderr ---\n" + r.stderr)
    text = (r.stdout + r.stderr).lower()
    if re.search(r"usage limit|quota|rate limit|try again (at|in)|limit reached", text) and not good(out):
        print("QUOTA: Codex says the usage limit is hit; stopping. Re-run later."); sys.exit(2)
    return good(out)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    force = "--force" in sys.argv
    names = args or list(briefs.BRIEFS)
    for n in names:
        if n in briefs.KEEP:
            print("keep (approved):", n); continue
        if good(SRC / f"{n}.png") and not force:
            print("done:", n); continue
        for attempt in (1, 2):
            print(f"generating {n} (attempt {attempt}) ...", flush=True)
            if run(n):
                print("ok:", n); break
        else:
            print("FAILED:", n, "(see logs)")


if __name__ == "__main__":
    main()
