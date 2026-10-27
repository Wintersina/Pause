"""Frost middle grade: bases from the Codex repaints (v1..v4 _a/_b), graded per role to the BRIGHTEST art whose AS-DRAWN stats
(BackdropGrade lift 1 + (1.95-1)*share, sat 1.05) stay inside WorldBackdropTest's Frost bounds, bounds unchanged:
value p90 <= .55, sky lum <= .18, far/mid lum <= .24, chroma <= .26, lane (20-80% width) median rel-lum <= .0778 (7:1 vs the brightest
enemy tone; body 2.5:1 allows .1216), lane value p90 <= .55.   usage: tune_frost.py write|search [margin]"""
import sys, json
from pathlib import Path
import numpy as np
from PIL import Image
sys.argv = sys.argv[:1] + sys.argv[1:]
import build_frost as B
W, H = B.W, B.H
HERE = Path(__file__).parent
R = Path("/Users/sina/Developer/Pause-worktrees/frost-896/Pause/Assets/Art/Backgrounds/Resources/Worlds/Frost/Backdrop3")
SHARE = {"sky": .55, "far": .8, "mid": 1.0}
CAP = {"sky": (.18, .55), "far": (.24, .55), "mid": (.24, .55)}   # (lum, p90)
CHROMA = .26
def lin(c): return np.where(c <= .03928, c / 12.92, ((c + .055) / 1.055) ** 2.4)
def rellum(c): return lin(c) @ np.array([.2126, .7152, .0722])
def drawn(a, share):
    k = max(1.0, 1 + .95 * share); sat = 1 + .05 * min(1, (k - 1) / .95) if k > 1 else 1
    v = a.max(-1, keepdims=True); kk = np.where(v > 1e-4, (1 - (1 - v) ** k) / np.maximum(v, 1e-4), 0); r = a * kk
    l = r.mean(-1, keepdims=True); return np.clip(l + (r - l) * sat, 0, 1)

def art_grade(base_u8, m, s, white, floor):
    """AFFINE grade (keeps the painted contrast): rgb' = blue floor + rgb * m (m searched), saturation s, highlight shoulder at `white`."""
    rgb = base_u8.astype(np.float32) / 255
    gray = rgb.mean(-1, keepdims=True); rgb = np.clip(gray + (rgb - gray) * s, 0, 1)
    rgb = 1 - (1 - rgb * m) * (1 - np.array(floor, np.float32))   # screen: the blue floor lifts the darks, the ice keeps its own tone
    val = rgb.max(-1, keepdims=True)
    nv = np.where(val > white * .8, white * .8 + (val - white * .8) * .35, val); nv = np.minimum(nv, white)
    return np.clip(rgb * (nv / np.maximum(val, 1e-3)), 0, 1)

def stats(art, share, lane=False, pre=None):
    d = drawn(art, share) if pre is None else pre
    v = d.max(-1); lum = (d @ np.array([.2126, .7152, .0722])); ch = v - d.min(-1)
    out = dict(p90=float(np.percentile(v, 90)), lum=float(lum.mean()), chroma=float(ch.mean()))
    if lane:
        x0, x1 = int(W * .2), int(W * .8); dl = d[::2, x0:x1:2]
        out["lane_rl"] = float(np.median(rellum(dl))); out["lane_p90"] = float(np.percentile(dl.max(-1), 90))
        out["lane_ch"] = float((dl.max(-1) - dl.min(-1)).mean())
    return out

def ok(role, st, margin):
    lc, pc = CAP[role]
    good = st["lum"] <= lc * margin and st["p90"] <= pc * margin and st["chroma"] <= CHROMA * margin
    if "lane_rl" in st: good = good and st["lane_rl"] <= .0778 * margin and st["lane_p90"] <= .55 * margin and st["lane_ch"] <= CHROMA * margin
    return good

PARAMS = json.loads((HERE / "frost_params.json").read_text()) if (HERE / "frost_params.json").exists() else {}
def bases():
    out = {}
    for v in ("v1", "v2", "v3", "v4"):
        base = B.build_canvas(B.load(HERE / f"{v}_a.png"), B.load(HERE / f"{v}_b.png"))
        out[v] = base
    return out
def layer_of(base, role):
    if role == "far": return np.ascontiguousarray(np.roll(base, (H // 3, W // 3), axis=(0, 1))[:, ::-1])
    if role == "sky": return np.ascontiguousarray(np.roll(base, (H * 2 // 3, -W // 4), axis=(0, 1)))
    return base

def flow_img(v):
    f = Image.open(HERE / "old_512~" / v / "flow.png").convert("RGBA").resize((W, H), Image.LANCZOS)
    return np.asarray(f).astype(np.float32) / 255

def search(base, role, v, s, white, floor, margin, fl):
    lo, hi = .05, 1.2
    L = layer_of(base, role)
    for _ in range(14):
        m = (lo + hi) / 2
        art = art_grade(L, m, s, white, floor)
        if role == "mid":   # lane: flow overlay on the mid as drawn
            d = drawn(art, 1.0); fd = drawn(fl[..., :3], 1.0); a = fl[..., 3:]
            comp = d * (1 - a) + fd * a
            st = stats(art, 1.0, lane=False); lane = stats(None, 1.0, lane=True, pre=comp)
            st.update({k: lane[k] for k in ("lane_rl", "lane_p90", "lane_ch")})
        else: st = stats(art, SHARE[role])
        if ok(role, st, margin): lo = m
        else: hi = m
    return lo, st

if __name__ == "__main__":
    mode = sys.argv[1]; margin = float(sys.argv[2]) if len(sys.argv) > 2 else .97
    bs = bases(); res = {}
    CFG = {"v1": dict(s=.9, white=.62), "v2": dict(s=.9, white=.55), "v3": dict(s=.9, white=.62), "v4": dict(s=.9, white=.62)}
    FLOOR = (.015, .045, .10)
    for v in ("v1", "v2", "v3", "v4"):
        fl = flow_img(v); c = CFG[v]
        for role in ("mid", "far", "sky"):
            m, st = search(bs[v], role, v, c["s"], c["white"], FLOOR, margin, fl)
            res[f"{v}/{role}"] = dict(median_target=round(m, 3), **{k: round(x, 3) for k, x in st.items()})
            print(v, role, res[f"{v}/{role}"])
            if mode == "write":
                art = art_grade(layer_of(bs[v], role), m, c["s"], c["white"], FLOOR)
                u8 = np.rint(art * 255).astype(np.uint8); u8 = np.dstack([u8, np.full(u8.shape[:2], 255, np.uint8)]); u8[-1] = u8[0]
                Image.fromarray(u8, "RGBA").save(R / v / f"{role}.png")
        if mode == "write": Image.open(HERE / "old_512~" / v / "flow.png").convert("RGBA").resize((W, H), Image.LANCZOS).save(R / v / "flow.png")
    (HERE / "frost_grade_result.json").write_text(json.dumps(res, indent=1))
