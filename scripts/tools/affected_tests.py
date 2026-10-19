#!/usr/bin/env python3
"""Map changed file paths (stdin, one per line, repo-relative) to AllTests suites.

Used by scripts/affected-tests.sh. Env: ROOT (repo root), SMOKE (comma list).
Prints "# ..." info lines and a final "SUITES a,b,c" line (AllTests order).
Rules are documented in the header of scripts/affected-tests.sh.
"""
import os, re, sys

ROOT = os.environ["ROOT"]
SMOKE = [s for s in os.environ.get("SMOKE", "").split(",") if s]
TESTS = os.path.join(ROOT, "Pause/Assets/Editor/Tests")
ALLTESTS = os.path.join(TESTS, "AllTests.cs")

def read(p):
    try:
        with open(p, encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return ""

def strip_comments(t):
    t = re.sub(r"/\*.*?\*/", " ", t, flags=re.S)
    return re.sub(r"//[^\n]*", " ", t)

TYPE_RE = re.compile(r"\b(?:class|struct|enum|interface)\s+([A-Za-z_]\w*)")

# suites in AllTests order: name -> class
suites = re.findall(r'^\s+\("(\w+)",\s*(\w+)\.\w+', read(ALLTESTS), flags=re.M)
order = [n for n, _ in suites]
suite_class = dict(suites)

tests = {}   # relpath -> (text, declared types)
for fn in sorted(os.listdir(TESTS)):
    if fn.endswith(".cs") and fn != "AllTests.cs":
        t = strip_comments(read(os.path.join(TESTS, fn)))
        tests[os.path.join("Pause/Assets/Editor/Tests", fn)] = (t, set(TYPE_RE.findall(t)))

src = {}     # all other .cs under Assets (scripts + editor tools), for types and asset refs
for base, _, fns in os.walk(os.path.join(ROOT, "Pause/Assets")):
    if "/Plugins" in base or "/GooglePlayGames" in base or "/ExternalDependencyManager" in base:
        continue
    for fn in fns:
        if fn.endswith(".cs"):
            rel = os.path.relpath(os.path.join(base, fn), ROOT)
            if rel not in tests and not rel.endswith("AllTests.cs"):
                src[rel] = read(os.path.join(base, fn))

def suites_declared_in(rel):
    types = tests[rel][1]
    return {n for n, c in suite_class.items() if c in types}

def word_re(types):
    return re.compile(r"\b(?:" + "|".join(sorted(map(re.escape, types))) + r")\b")

chosen = set()
notes = []
unmapped = []

HUB_LIMIT = 25   # a helper type used by more test files than this (TestHarness...) is not followed
_hub = {}
def is_hub(tname):
    if tname not in _hub:
        rx = re.compile(r"\b" + re.escape(tname) + r"\b")
        _hub[tname] = sum(1 for t, _ in tests.values() if rx.search(t)) > HUB_LIMIT
    return _hub[tname]

def tests_for_types(types, label, seen_types=None):
    """suites whose test files reference any of `types`; helper files are followed."""
    found = set()
    seen = set(types)
    queue = set(types)
    for _ in range(4):
        if not queue:
            break
        rx = word_re(queue)
        nxt = set()
        for rel, (t, declared) in tests.items():
            if rx.search(t):
                s = suites_declared_in(rel)
                found |= s
                if not s:                      # helper file: follow its types, unless a hub
                    nxt |= {d for d in declared - seen if not is_hub(d)}
        seen |= nxt
        queue = nxt
    return found

def types_of_text(t):
    return {x for x in TYPE_RE.findall(strip_comments(t)) if len(x) > 2}

ASSET_EXT = {".png", ".jpg", ".jpeg", ".tga", ".psd", ".webp", ".gif", ".json", ".csv", ".txt", ".bytes",
             ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".prefab", ".unity", ".asset", ".mat", ".shader",
             ".cginc", ".hlsl", ".anim", ".controller", ".ttf", ".otf", ".mp4", ".mov", ".xml", ".flare"}
IGNORE_PREFIX = ("docs/", "scripts/", ".claude/", "HapticGateFramesV2/", "Pause/Assets/Marketing/")
IGNORE_EXT = {".md", ".sh", ".py", ".yml", ".yaml", ".gitignore"}
FULL = False

# Fallback table when no test names the asset: path prefix -> regex over suite names.
# Add a row when a folder keeps slipping through (documented in affected-tests.sh).
TABLE = [
    ("Pause/Assets/Art/Ships/", r"Ship|Skin|Shop|Nozzle|Exhaust|Hull|Dock"),
    ("Pause/Assets/Art/Weapons/", r"Ultimate|Weapon|ShotSkin|Power|Shot"),
    ("Pause/Assets/Art/Pickups/", r"Pickup|Atom"),
    ("Pause/Assets/Art/Enemies/", r"Enem|Elite|Mine|Death"),
    ("Pause/Assets/Art/Bosses/", r"Boss"),
    ("Pause/Assets/Art/UI/", r"Splash|Title|Home|Codex|Tutorial|ScreenFit|Shop|Menu"),
    ("Pause/Assets/Audio/", r"Music|Sound|Audio|Sfx|Beat"),
    ("Pause/Assets/Resources/Audio/", r"Music|Sound|Audio|Sfx|Beat"),
    ("Pause/Assets/Art/Achievements/", r"Achievement"),
]

for line in sys.stdin:
    f = line.strip()
    if not f:
        continue
    ext = os.path.splitext(f)[1].lower()
    if f.endswith(".meta"):
        f = f[:-5]
        ext = os.path.splitext(f)[1].lower()
        if not ext:
            continue                           # folder meta
    if f.startswith(IGNORE_PREFIX) or ext in IGNORE_EXT or f in ("Makefile", "README.md", ".gitignore"):
        continue
    if f.startswith(("Pause/ProjectSettings/", "Pause/Packages/")) or ext in (".asmdef", ".asmref"):
        FULL = True
        notes.append(f"{f}: project-wide setting -> full suite")
        continue
    if ext == ".cs":
        if f == "Pause/Assets/Editor/Tests/AllTests.cs":
            notes.append(f"{f}: runner only -> smoke set")
            continue
        text = tests[f][0] if f in tests else strip_comments(read(os.path.join(ROOT, f)))
        types = {x for x in TYPE_RE.findall(text) if len(x) > 2}
        hit = set()
        if f in tests:
            hit |= suites_declared_in(f)
        hit |= tests_for_types(types, f)
        if not types and not hit:
            unmapped.append(f)
        notes.append(f"{f}: {len(hit)} suites" + (f" (types: {', '.join(sorted(types)[:4])}{'...' if len(types) > 4 else ''})" if types else ""))
        chosen |= hit
        continue
    if ext in ASSET_EXT or f.startswith("Pause/Assets/"):
        rel = f[len("Pause/Assets/"):] if f.startswith("Pause/Assets/") else f
        stem = os.path.splitext(os.path.basename(f))[0]
        stem = re.sub(r"\.\w+$", "", stem)
        tokens = []          # (token, search scripts too?) most specific first
        if len(stem) >= 4:
            tokens.append((stem, True))
        if "Resources/" in rel:
            rrel = rel.rsplit("Resources/", 1)[1]
            tokens.append((os.path.splitext(rrel)[0], True))        # the Resources.Load key
        parts = os.path.dirname(rel).split("/")
        for n in range(len(parts), 2, -1):                         # folders, down to 3 components
            tokens.append(("/".join(parts[:n]), False))
        if "Resources/" in rel:
            rp = os.path.dirname(rel.rsplit("Resources/", 1)[1]).split("/")
            for n in range(len(rp), 0, -1):
                if rp[:n] != [""]:
                    tokens.append(("/".join(rp[:n]), False))
        hit = set()
        used = None
        for tok, in_src in tokens:
            tok_hit = set()
            ttypes = set()
            for trel, (t, declared) in tests.items():
                if tok in t:
                    s_ = suites_declared_in(trel)
                    tok_hit |= s_
                    if not s_:
                        ttypes |= declared
            if in_src:
                for srel, st in src.items():
                    if tok in st:
                        ttypes |= types_of_text(st)
            if ttypes:
                tok_hit |= tests_for_types(ttypes, tok)
            if tok_hit:
                hit, used = tok_hit, tok
                break
        if not hit:
            for prefix, rx in TABLE:
                if f.startswith(prefix):
                    hit = {n for n in order if re.search(rx, n)}
                    used = "table " + prefix
                    break
        if not hit:
            unmapped.append(f)
        notes.append(f"{f}: {len(hit)} suites" + (f" via '{used}'" if used else ""))
        chosen |= hit
        continue
    unmapped.append(f)

if FULL:
    chosen = set(order)
chosen |= {s for s in SMOKE if s in order}
bad = [s for s in SMOKE if s not in order]
for n in notes:
    print("# " + n)
if unmapped:
    print("# no mapping (smoke set only for these): " + ", ".join(unmapped[:12]) + (" ..." if len(unmapped) > 12 else ""))
if bad:
    print("# unknown smoke suites ignored: " + ",".join(bad))
print("SUITES " + ",".join(s for s in order if s in chosen))
