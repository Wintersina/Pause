#!/usr/bin/env python3
"""Resumable sequential Codex runner for the Verdant redesign (masters, then wing-pose variants made from each master).

    python3 run_codex.py [name ...]          # masters for names without src/<name>.png, then variants _a/_b for crafts
    python3 run_codex.py --masters           # masters only
    python3 run_codex.py --force NAME        # redo master + variants of NAME
Every codex exec is wrapped in the shared lock script. Stops cleanly on a quota message (re-run after the reset).
"""
import re, subprocess, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import briefs

SRC = HERE / "src"; LOGS = HERE / "logs"
LOCK = "/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh"
CODEX = str(Path.home() / ".local/bin/codex")


def good(p):
    if not p.exists():
        return False
    try:
        a = np.asarray(Image.open(p).convert("RGBA"))[:, :, 3]
    except Exception:
        return False
    return max(a[0, 0], a[0, -1], a[-1, 0], a[-1, -1]) < 16 and (a > 128).mean() > .04


def run(tag, prompt, out, image=None):
    SRC.mkdir(exist_ok=True); LOGS.mkdir(exist_ok=True)
    cmd = [LOCK, CODEX, "exec", "--skip-git-repo-check", "--sandbox", "workspace-write", "-C", str(SRC)]
    if image:
        cmd += ["-i", str(image)]
    cmd += ["-"]
    r = subprocess.run(cmd, input=prompt, text=True, capture_output=True)
    (LOGS / f"{tag}.log").write_text(r.stdout + "\n--- stderr ---\n" + r.stderr)
    text = (r.stdout + r.stderr).lower()
    if re.search(r"usage limit|quota|rate limit|try again (at|in)|limit reached", text) and not good(out):
        print("QUOTA: stopping; re-run later.", flush=True); sys.exit(2)
    return good(out)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    force = "--force" in sys.argv
    masters_only = "--masters" in sys.argv
    names = args or list(briefs.BRIEFS)
    for n in names:
        m = SRC / f"{n}.png"
        if force or not good(m):
            for attempt in (1, 2):
                print(f"master {n} (attempt {attempt})", flush=True)
                if run(n, briefs.prompt(n, str(m)), m):
                    print("ok", n, flush=True); break
            else:
                print("FAILED", n, flush=True); continue
        if masters_only or briefs.BRIEFS[n]["kind"] != "craft":
            continue
        for w in "ab":
            o = SRC / f"{n}_{w}.png"
            if good(o) and not force:
                continue
            for attempt in (1, 2):
                print(f"variant {n}_{w} (attempt {attempt})", flush=True)
                if run(f"{n}_{w}", briefs.variant_prompt(n, w, str(m), str(o)), o, image=m):
                    print("ok", n, w, flush=True); break
            else:
                print("FAILED", n, w, flush=True)


if __name__ == "__main__":
    main()
