#!/usr/bin/env python3
"""Plan the Unity CI run: pick suites, then pack them into duration-balanced shards.

Usage (env ROOT = repo root; see .github/workflows/unity-tests.yml):
  ci_plan.py --event pull_request --files changed.txt --max-shards 4 --out plan.json
  ci_plan.py --event push --max-shards 4
  ci_plan.py --event workflow_dispatch --suites A,B --graphics skip

Selection:
  - suites given explicitly (dispatch input)      -> exactly those
  - push / dispatch without suites                -> every suite
  - pull_request                                  -> affected_tests.py mapping + smoke set,
    widened to EVERY suite when the change touches the workflow, this planner, the
    runner/harness (AllTests.cs, TestHarness*.cs), ProjectSettings, Packages, or when the
    mapping selects more than FULL_THRESHOLD of the suites (or the file list is unavailable).
Packing: longest-first onto the least-loaded shard, using scripts/ci-suite-times.txt
(seconds per suite; unknown suites get DEFAULT_SECONDS). Shard count is capped so a shard
carries at least TARGET_SECONDS of work (each shard pays ~10 min of editor start/import).
Writes JSON {"mode","reason","suites","shards","matrix":{"include":[{"shard","suites","est"}]}} and
a markdown summary on stdout.
"""
import argparse, json, math, os, re, subprocess, sys

ROOT = os.environ.get("ROOT") or os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TESTS = "Pause/Assets/Editor/Tests"
DEFAULT_SECONDS = 8.0
TARGET_SECONDS = 480.0
FULL_THRESHOLD = 0.45
SMOKE = "AchievementCatalogTest,WorldLogicTest,RowHeartsTest,ColumnSpeedTest"
FULL_TRIGGERS = (
    ".github/workflows/unity-tests.yml",
    "scripts/tools/ci_plan.py",
    "scripts/tools/affected_tests.py",
    "scripts/ci-suite-times.txt",
    TESTS + "/AllTests.cs",
    TESTS + "/TestHarness",
    "Pause/ProjectSettings/",
    "Pause/Packages/",
)


# One AllTests entry: ("SuiteName", Class.Method). The method is usually Execute, but a
# split suite registers slices (ScreenFitTest1 -> ScreenFitTest.Slice1), so any method name
# counts; the class name maps a suite back to its source file.
SUITE_RE = re.compile(r'^\s*\("([A-Za-z0-9_]+)",\s*([A-Za-z0-9_]+)\.[A-Za-z0-9_]+\)', re.M)


def registered():
    """[(suite, class)] in AllTests order."""
    with open(os.path.join(ROOT, TESTS, "AllTests.cs"), encoding="utf-8") as f:
        return SUITE_RE.findall(f.read())


def all_suites():
    return [s for s, _ in registered()]


def graphics_suites(suites):
    cls = dict(registered())
    out = []
    for s in suites:
        p = os.path.join(ROOT, TESTS, cls.get(s, s) + ".cs")
        if os.path.isfile(p):
            with open(p, encoding="utf-8", errors="replace") as f:
                if re.search(r"Camera\.Render|Graphics\.Blit|ReadPixels|RenderTexture", f.read()):
                    out.append(s)
    return out


def durations():
    d = {}
    with open(os.path.join(ROOT, "scripts", "ci-suite-times.txt")) as f:
        for line in f:
            p = line.split()
            if len(p) == 2:
                d[p[0]] = float(p[1])
    return d


def affected(files):
    env = dict(os.environ, ROOT=ROOT, SMOKE=SMOKE)
    r = subprocess.run([sys.executable, "-I", os.path.join(ROOT, "scripts/tools/affected_tests.py")],
                       input="\n".join(files) + "\n", capture_output=True, text=True, env=env)
    if r.returncode != 0:
        raise RuntimeError(r.stderr)
    notes = [l[2:].strip() for l in r.stdout.splitlines() if l.startswith("#")]
    line = next((l for l in r.stdout.splitlines() if l.startswith("SUITES ")), "SUITES ")
    return [s for s in line[7:].split(",") if s], notes


def pack(suites, n, dur):
    bins = [[0.0, []] for _ in range(n)]
    for s in sorted(suites, key=lambda x: (-dur.get(x, DEFAULT_SECONDS), x)):
        b = min(bins, key=lambda b: b[0])
        b[0] += dur.get(s, DEFAULT_SECONDS)
        b[1].append(s)
    return bins


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--event", required=True)
    ap.add_argument("--files", help="file with changed paths, one per line (pull_request)")
    ap.add_argument("--suites", default="")
    ap.add_argument("--graphics", default="xvfb")
    ap.add_argument("--max-shards", type=int, default=4)
    ap.add_argument("--out", default="plan.json")
    a = ap.parse_args()

    everything = all_suites()
    dur = durations()
    notes = []
    if a.suites.strip():
        want = [s.strip() for s in a.suites.split(",") if s.strip()]
        known = {s.lower(): s for s in everything}
        chosen = []
        for w in want:
            k = known.get(w.lower()) or known.get((w + "Test").lower())
            chosen.append(k or w)   # unknown names still go to Unity, which reports them as failures
        mode, reason = "manual", "suites input"
    elif a.event == "pull_request":
        files = []
        if a.files and os.path.isfile(a.files):
            files = [l.strip() for l in open(a.files) if l.strip()]
        if not files:
            chosen, mode, reason = everything, "full", "changed-file list unavailable or empty"
        else:
            trig = [f for f in files if f.startswith(FULL_TRIGGERS)]
            if trig:
                chosen, mode, reason = everything, "full", "touches test infrastructure: " + ", ".join(trig[:3])
            else:
                chosen, notes = affected(files)
                if len(chosen) > FULL_THRESHOLD * len(everything):
                    chosen, mode, reason = everything, "full", f"{len(chosen)} affected suites exceeds {int(FULL_THRESHOLD*100)}% of {len(everything)}"
                else:
                    mode, reason = "selected", f"{len(files)} changed files mapped to {len(chosen)} suites (incl. smoke set)"
    else:
        chosen, mode, reason = everything, "full", a.event + " runs the full suite"

    if a.graphics == "skip":
        gfx = set(graphics_suites(everything))
        dropped = [s for s in chosen if s in gfx]
        chosen = [s for s in chosen if s not in gfx]
        notes.append("graphics=skip dropped: " + ",".join(dropped))
    if not chosen:
        chosen = [s for s in SMOKE.split(",") if s in everything]

    est = sum(dur.get(s, DEFAULT_SECONDS) for s in chosen)
    n = max(1, min(a.max_shards, len(chosen), math.ceil(est / TARGET_SECONDS)))
    bins = pack(chosen, n, dur)
    include = [{"shard": i, "suites": ",".join(b[1]), "est": round(b[0])} for i, b in enumerate(bins)]
    plan = {"mode": mode, "reason": reason, "suites": len(chosen), "shards": n,
            "matrix": {"include": include}}
    with open(a.out, "w") as f:
        json.dump(plan, f)

    print("### Test plan")
    print(f"Mode **{mode}**: {reason}. {len(chosen)} of {len(everything)} suites, est. {round(est)} s of suite time over {n} shard(s).")
    for it in include:
        print(f"- shard {it['shard']} (~{it['est']} s): {it['suites']}")
    if notes:
        print("\n<details><summary>Mapping notes</summary>\n\n```\n" + "\n".join(notes[:80]) + "\n```\n</details>")


if __name__ == "__main__":
    main()
