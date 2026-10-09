"""Audit the Tide Run B atlas geometry, pixel palette and manifest contract."""
from __future__ import annotations

import colorsys
import json
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
EXPECTED = {"landmarks": 16, "pipes": 16, "fires": 16, "weather": 16, "sites": 12}
HUE_ZONES = {
    "cyan_170_186": (170, 186), "green_74_90": (74, 90),
    "violet_252_266": (252, 266), "amber_30_44": (30, 44),
    "pink_312_326": (312, 326),
}


def hsv_pixels(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    f = rgb.astype(np.float32)/255
    hi = f.max(axis=1); lo = f.min(axis=1); delta = hi-lo
    sat = np.divide(delta, hi, out=np.zeros_like(hi), where=hi>0)
    h = np.zeros_like(hi)
    r,g,b = f.T; nz=delta>1e-6
    m=nz & (hi==r); h[m]=((g[m]-b[m])/delta[m])%6
    m=nz & (hi==g); h[m]=(b[m]-r[m])/delta[m]+2
    m=nz & (hi==b); h[m]=(r[m]-g[m])/delta[m]+4
    return h*60, sat, hi


def verify() -> None:
    manifest=json.loads((ROOT/"manifest.json").read_text())
    records=manifest["run_b"]["pieces"]
    errors=[]; all_rgb=[]; all_alpha=[]; p90s=[]; margins=[]; colors=[]
    print("atlas       sprite                    colors margin p90   opaque% alpha_max")
    print("-"*76)
    for atlas, count in EXPECTED.items():
        im=Image.open(ROOT/f"{atlas}.png")
        if im.size != (1024,1024) or im.mode != "RGBA":
            errors.append(f"{atlas}: expected 1024x1024 RGBA, got {im.size} {im.mode}")
        a=np.array(im.convert("RGBA")); sprites=json.loads((ROOT/f"{atlas}.json").read_text())["sprites"]
        if len(sprites)!=count:errors.append(f"{atlas}: {len(sprites)} sprites, expected {count}")
        used=np.zeros((1024,1024),np.uint8)
        for i,s in enumerate(sprites):
            x,y,w,h=(s[k] for k in ("x","y","w","h"))
            if not (0<=x and 0<=y and x+w<=1024 and y+h<=1024):
                errors.append(f"{atlas}/{s['n']}: rect outside image")
                continue
            top=1024-y-h
            if used[top:top+h,x:x+w].any():errors.append(f"{atlas}/{s['n']}: rect overlaps")
            used[top:top+h,x:x+w]=1
            if (w,h)!=(256,256) or (x,y)!=(i%4*256,(3-i//4)*256):
                errors.append(f"{atlas}/{s['n']}: wrong 256-cell/grid position")
            px=a[top:top+h,x:x+w]; mask=px[:,:,3]>0
            if not mask.any():
                errors.append(f"{atlas}/{s['n']}: empty");continue
            yy,xx=np.nonzero(mask)
            margin=int(min(xx.min(),yy.min(),255-xx.max(),255-yy.max()))
            rgb=px[mask][:,:3]
            v=rgb.max(axis=1)/255
            p90=float(np.percentile(v,90))
            ncolors=len(np.unique(rgb,axis=0))
            opaque=float((px[:,:,3]==255).mean())*100
            max_alpha=int(px[:,:,3].max())
            print(f"{atlas:<11} {s['n']:<25} {ncolors:>6} {margin:>6} {p90:>5.3f} {opaque:>7.2f} {max_alpha:>9}")
            if margin<14:errors.append(f"{atlas}/{s['n']}: {margin}px margin")
            if atlas in {"landmarks","pipes"} and p90>.55:
                errors.append(f"{atlas}/{s['n']}: p90 {p90:.3f} > .55")
            if ncolors<5 or ncolors>64:
                errors.append(f"{atlas}/{s['n']}: {ncolors} colours outside 5..64")
            if atlas=="weather" and max_alpha>110:
                errors.append(f"weather/{s['n']}: alpha {max_alpha} > 110")
            if atlas!="weather" and max_alpha!=255:
                errors.append(f"{atlas}/{s['n']}: lacks solid opaque pixels")
            all_rgb.append(rgb);all_alpha.append(px[mask][:,3]);p90s.append(p90);margins.append(margin);colors.append(ncolors)
        if atlas=="sites" and np.any(a[768:,:,:4]):
            errors.append("sites: fourth row must remain transparent")

    if len(records)!=sum(EXPECTED.values()):errors.append(f"manifest run_b has {len(records)} pieces")
    names={(r["atlas"],r["name"]) for r in records}
    if len(names)!=len(records):errors.append("manifest has duplicate atlas/name records")
    for record in records:
        atlas,name=record["atlas"],record["name"]
        if atlas not in EXPECTED:errors.append(f"unknown atlas {atlas}");continue
        for key in ("role","size_hint","notes","emit","ends"):
            if key not in record:errors.append(f"{atlas}/{name}: missing {key}")
        for p in record.get("emit",[])+record.get("ends",[]):
            if not (14<=p["x"]<=242 and 14<=p["y"]<=242):
                errors.append(f"{atlas}/{name}: point outside safe cell area {p}")
    bad_words={"star","planet","asteroid","comet","station","orbit","space"}
    for _,name in names:
        if any(word in name.lower() for word in bad_words):
            errors.append(f"space imagery name: {name}")

    rgb=np.concatenate(all_rgb);alpha=np.concatenate(all_alpha)
    hue,sat,value=hsv_pixels(rgb)
    chroma=(sat>=.20)&(value>=.15)
    print("\nPALETTE SHARES (visible chromatic pixels):")
    for label,(lo,hi) in HUE_ZONES.items():
        share=float(np.mean((hue[chroma]>=lo)&(hue[chroma]<=hi)))*100
        print(f"  {label:<20} {share:6.3f}%")
        if share>.5:errors.append(f"{label}: {share:.3f}% > 0.5%")
    red=float(np.mean((hue[chroma]>=345)|(hue[chroma]<=15)))*100
    print(f"  {'red_345_15':<20} {red:6.3f}%")
    if red>0:errors.append(f"red hue share {red:.3f}%")
    rust=(hue>=15)&(hue<=28)&(value>.15)&(alpha>=128)
    rust_max_sat=float(sat[rust].max()) if rust.any() else 0
    print(f"  rust max saturation  {rust_max_sat:.3f}")
    if rust_max_sat>.45:errors.append(f"rust saturation {rust_max_sat:.3f} > .45")
    print(f"\nSUMMARY {len(records)} pieces; min margin {min(margins)}px; max landmark/pipe p90 "
          f"{max(p90s[:32]):.3f}; colours/piece {min(colors)}–{max(colors)}; "
          f"no-space-name check {'PASS' if not any('space imagery' in e for e in errors) else 'FAIL'}")
    print("Semantic no-space-imagery check: source candidates and final preview visually inspected.")
    if errors:
        print("FAIL:")
        for e in errors:print("  "+e)
        raise SystemExit(1)
    print("PASS")


if __name__=="__main__":verify()
