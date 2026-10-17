#!/usr/bin/env python3
"""Resumable Codex queue: one image per call, wrapped in the shared codex lock. Retries 429s with 15 min backoff."""
import subprocess, sys, time
from pathlib import Path
from PIL import Image
sys.path.insert(0, str(Path(__file__).parent))
from spec import SHIPS, prompt
HERE = Path(__file__).parent
LOCK = "/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh"
CODEX = str(Path.home() / ".local/bin/codex")


def valid(p):
    try:
        im = Image.open(p)
        im.load()
        return im.width >= 3 * 128 and abs(im.width - 3 * im.height) <= im.height * 0.25 + 8
    except Exception:
        return False


def main():
    only = set(sys.argv[1:])
    for name, cell, a, b, h in SHIPS:
        if only and name not in only:
            continue
        out = HERE / "out" / f"{name}.png"
        inp = HERE / "in" / f"{name}_in.png"
        if valid(out):
            continue
        for attempt in range(8):
            log = HERE / "out" / f"{name}.log"
            cmd = [LOCK, CODEX, "exec", "-m", "gpt-5.6-luna", "--skip-git-repo-check", "-s", "workspace-write",
                   "-C", str(HERE / "out"), "-i", str(inp), "-"]
            r = subprocess.run(cmd, input=prompt(name, cell, a, b, h, inp, out), text=True, capture_output=True)
            txt = (r.stdout or "") + (r.stderr or "")
            log.write_text(txt)
            if valid(out):
                print("done", name, flush=True)
                break
            if "429" in txt or "usage_limit" in txt or "usage limit" in txt.lower():
                print("rate limited, sleeping 15 min", name, flush=True)
                time.sleep(900)
            else:
                print("no valid output, retry", name, flush=True)
                time.sleep(20)
    missing = [n for n, *_ in SHIPS if not valid(HERE / "out" / f"{n}.png")]
    print("MISSING", missing) if missing else print("ALL_DONE")


main()
