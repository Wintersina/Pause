"""Verify the staged Storm planetfall art contract and print a compact audit."""
from __future__ import annotations

import json
import math
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
EXPECTED = {
    "storm_planet.png": (1024,1024),
    "storm_planet_limb.png": (2048,1024),
    "storm_cloud_deck.png": (2048,1024),
    "storm_cloud_deck_dark.png": (2048,1024),
    "storm_entry_fx.png": (3072,1024),
    "storm_breakthrough.png": (5120,1024),
    "storm_entry_streaks.png": (1024,2048),
}


def circle_fit(alpha: np.ndarray) -> dict:
    """360 radial ray exits, then robust algebraic least-squares circle fit."""
    seed = np.array([512.0, 495.0])
    points=[]; distances=[]
    for deg in range(360):
        t=math.radians(deg)
        dx,dy=math.cos(t),math.sin(t)
        r=np.arange(280,485,dtype=np.float32)
        x=np.rint(seed[0]+r*dx).astype(int)
        y=np.rint(seed[1]+r*dy).astype(int)
        valid=(x>=0)&(x<1024)&(y>=0)&(y<1024)
        good=(alpha[y[valid],x[valid]]>95)
        rv=r[valid]
        # The first run of five clear pixels is the planetary edge; this
        # rejects translucent one-pixel atmosphere flecks and skyhook pixels.
        runs=np.convolve((~good).astype(int),np.ones(5,dtype=int),mode="valid")
        idx=np.flatnonzero(runs==5)
        if len(idx):
            d=float(rv[int(idx[0])]);points.append((seed[0]+d*dx,seed[1]+d*dy));distances.append(d)
    p=np.asarray(points)
    if len(p)<330: raise AssertionError(f"only {len(p)} disc rays")
    keep=np.ones(len(p),dtype=bool)
    for _ in range(5):
        q=p[keep]
        A=np.column_stack((2*q[:,0],2*q[:,1],np.ones(len(q))))
        c=np.linalg.lstsq(A,q[:,0]**2+q[:,1]**2,rcond=None)[0]
        cx,cy=float(c[0]),float(c[1])
        rad=math.sqrt(float(c[2])+cx*cx+cy*cy)
        residual=np.abs(np.hypot(p[:,0]-cx,p[:,1]-cy)-rad)
        keep=residual<max(12,float(np.percentile(residual,78))*1.6)
    return {"center_px":[round(cx,1),round(cy,1)],"disc_radius_px":round(rad,1),
            "rays_measured":360,"rays_used_in_fit":int(keep.sum()),
            "ray_distance_p10_p50_p90_px":[round(float(x),1) for x in np.percentile(distances,[10,50,90])],
            "residual_p90_px":round(float(np.percentile(residual[keep],90)),1)}


def hue_shares(a: np.ndarray) -> tuple[dict,int,int,float,float]:
    rgb=Image.fromarray(a[:,:,:3],"RGB")
    hsv=np.asarray(rgb.convert("HSV"))
    h=hsv[:,:,0].astype(np.float32)*360/255
    s=hsv[:,:,1].astype(np.float32)/255
    v=hsv[:,:,2].astype(np.float32)/255
    visible=a[:,:,3]>32
    colored=visible&(s>.30)&(v>.13)
    n=max(1,int(colored.sum()))
    bands={
        "space_violet_259_330":(h>=259)&(h<=330),
        "shield_cyan_174_185":(h>=174)&(h<=185),
        "repair_lime_78_92":(h>=78)&(h<=92),
        "orange_fire_15_30":(h>=15)&(h<=30)&(s>.50),
        "tide_mint_150_170":(h>=150)&(h<=170),
        "pink_312_326":(h>=312)&(h<=326),
        "player_red_345_15":((h>=345)|(h<=15))&(s>.50),
        "pickup_amber_33_42":(h>=33)&(h<=42)&(s>.45),
    }
    shares={name:round(100*int((colored&mask).sum())/n,4) for name,mask in bands.items()}
    pixels=a[visible,:3].reshape(-1,3)
    unique=len(np.unique(pixels,axis=0)) if len(pixels) else 0
    return shares,int(visible.sum()),unique,round(float(np.percentile(v[visible],95)),3),round(float((visible&(v>.4)).sum()/max(1,visible.sum())*100),3)


def hole(frame: np.ndarray) -> dict:
    alpha=frame[:,:,3]
    cx,cy=256,338
    if alpha[cy,cx]>32: raise AssertionError("ship centre opaque")
    box=(100,220,412,460)
    x0,y0,x1,y1=box
    region=alpha[y0:y1,x0:x1]<=32
    visited=np.zeros_like(region,dtype=bool)
    q=deque([(cy-y0,cx-x0)])
    visited[cy-y0,cx-x0]=True
    pts=[]
    while q:
        y,x=q.popleft();pts.append((x+x0,y+y0))
        for ny,nx in ((y-1,x),(y+1,x),(y,x-1),(y,x+1)):
            if 0<=ny<region.shape[0] and 0<=nx<region.shape[1] and region[ny,nx] and not visited[ny,nx]:
                visited[ny,nx]=True;q.append((ny,nx))
    p=np.asarray(pts)
    xmin,ymin=p.min(axis=0);xmax,ymax=p.max(axis=0)
    if xmin<=x0 or xmax>=x1-1 or ymin<=y0 or ymax>=y1-1: raise AssertionError("opening leaks out of crown")
    return {"center_px":[round(float((xmin+xmax)/2),1),round(float((ymin+ymax)/2),1)],
            "size_px":[int(xmax-xmin+1),int(ymax-ymin+1)],"area_px":len(pts)}


def main() -> None:
    files={};report={};fail=[]
    print("file                         size       RGBA  colors     p95V bright% red% violet% cyan% lime% orange% mint% amber%")
    for name,size in EXPECTED.items():
        im=Image.open(ROOT/name)
        a=np.asarray(im.convert("RGBA"))
        files[name]=a
        if im.size!=size or im.mode!="RGBA": fail.append(f"{name}: size/mode {im.size} {im.mode}")
        shares,visible,unique,p95,bright=hue_shares(a)
        report[name]={"visible_pixels":visible,"distinct_rgb_colors":unique,"p95_value":p95,
                      "bright_pixel_percent":bright,"forbidden_hue_share_percent":shares}
        print(f"{name:28} {im.width:4}x{im.height:<4} {str(im.mode=='RGBA'):5} {unique:8} {p95:5.3f} {bright:6.2f} "
              f"{shares['player_red_345_15']:4.2f} {shares['space_violet_259_330']:7.2f} "
              f"{shares['shield_cyan_174_185']:5.2f} {shares['repair_lime_78_92']:5.2f} "
              f"{shares['orange_fire_15_30']:7.2f} {shares['tide_mint_150_170']:5.2f} "
              f"{shares['pickup_amber_33_42']:6.2f}")
        for band in ("player_red_345_15","space_violet_259_330","shield_cyan_174_185","repair_lime_78_92","orange_fire_15_30","tide_mint_150_170","pink_312_326"):
            if shares[band]>.15: fail.append(f"{name}: {band} {shares[band]}%")
        if name in ("storm_planet.png","storm_planet_limb.png","storm_cloud_deck.png","storm_cloud_deck_dark.png") and unique<3000:
            fail.append(f"{name}: insufficient painted tonal range")
    for name in ("storm_cloud_deck.png","storm_cloud_deck_dark.png"):
        a=files[name]
        x=np.array_equal(a[:,0],a[:,-1]);y=np.array_equal(a[0],a[-1])
        print(name,"wrap X/Y",x,y)
        if not x or not y: fail.append(f"{name}: seam mismatch")
    a=files["storm_entry_streaks.png"]
    print("storm_entry_streaks.png wrap Y",np.array_equal(a[0],a[-1]))
    if not np.array_equal(a[0],a[-1]):fail.append("streak seam mismatch")
    planet=files["storm_planet.png"]
    yy,xx=np.where(planet[:,:,3]>32)
    margins=[int(xx.min()),int(1023-xx.max()),int(yy.min()),int(1023-yy.max())]
    disc=circle_fit(planet[:,:,3]);print("planet disc 360-ray fit",disc,"margins LRTB",margins)
    if min(margins)<18: fail.append("planet margin under 18 px")
    limb=files["storm_planet_limb.png"]
    edges=[int(np.flatnonzero(limb[:,x,3]>100)[0]) for x in (0,1024,2047)]
    print("limb horizon L/C/R",edges)
    if edges!=[720,355,720]: fail.append("limb horizon registration")
    fx=files["storm_entry_fx.png"]
    holes=[]
    for i in range(6):
        f=fx[:,i*512:(i+1)*512]
        borders=not(f[:8,:,3].any() or f[-8:,:,3].any() or f[:,:8,3].any() or f[:,-8:,3].any())
        try: measured=hole(f)
        except AssertionError as e: measured={"error":str(e)};fail.append(f"entry frame {i}: {e}")
        holes.append(measured)
        print("entry frame",i,"border clear",borders,"opening",measured)
        if not borders:fail.append(f"entry frame {i}: border")
        if "size_px" in measured and not(143<=measured["size_px"][0]<=153 and 161<=measured["size_px"][1]<=172):
            fail.append(f"entry frame {i}: opening size")
    b=files["storm_breakthrough.png"]
    for i in range(5):
        f=b[:,i*1024:(i+1)*1024]
        clear=not(f[:12,:,3].any() or f[-12:,:,3].any() or f[:,:12,3].any() or f[:,-12:,3].any())
        print("breakthrough frame",i,"border clear",clear)
        if not clear:fail.append(f"breakthrough frame {i}: border")
    master=Image.open(ROOT/"masters/storm_planet_master_4096.png")
    if master.size!=(4096,4096) or master.mode!="RGBA":fail.append("master size/mode")
    report["planet_measurement"]={**disc,"alpha_margin_lrtb_px":margins}
    report["limb_horizon_lcr_px"]=edges
    report["entry_opening_per_frame"]=holes
    (ROOT/"src~/verification.json").write_text(json.dumps(report,indent=2)+"\n")
    print("RESULT", "PASS" if not fail else "FAIL: "+"; ".join(fail))
    if fail:raise SystemExit(1)


if __name__=="__main__":main()
