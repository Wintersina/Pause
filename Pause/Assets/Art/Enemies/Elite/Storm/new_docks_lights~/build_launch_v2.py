#!/usr/bin/env python3
"""Launch-site door animation, constrained to the drawn door features."""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFilter
import math

HERE=Path(__file__).resolve().parent; W=H=768; FRAMES=6
# Exact feature geometry, recorded in manifest.md as well.
SITES={
 'floating_airship_dock': {'kind':'slide','pad':(384,430),'door':('poly',((214,376),(505,443),(584,508),(318,463))),'shift':(0,0,31,61,88,39)},
 'skyhook_cradle': {'kind':'slide','pad':(384,430),'door':('ellipse',(315,349,453,510)),'shift':(0,0,18,35,51,24)},
 'refinery_bay': {'kind':'roll','pad':(384,430),'door':('rect',(307,398,462,490))},
 'lightning_rod_silo': {'kind':'iris','pad':(384,430),'door':('circle',(384,430,145)),'shift':(0,22,55,95,130,65)},
}
INK=(7,18,27,255); PIT_HI=(31,72,86,255); PIT_LO=(3,12,20,255)
MIST=((202,231,238),(119,171,184),(65,111,127))

def no_bad(im,label):
    for r,g,b,a in im.getdata():
        if a<32: continue
        assert not (r>180 and b>150 and g<105 and g*2<min(r,b)), f'pink/magenta pixel in {label}'
        assert not (g>100 and g>r*1.60 and g>b*1.25), f'green pixel in {label}'

def mask(info,expand=0):
    kind,data=info['door']; m=Image.new('L',(W,H)); d=ImageDraw.Draw(m)
    if kind=='poly': d.polygon(data,fill=255)
    elif kind=='ellipse': d.ellipse(data,fill=255)
    elif kind=='rect': d.rectangle(data,fill=255)
    else:
        cx,cy,r=data; d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=255)
    return m.filter(ImageFilter.MaxFilter(expand*2+1)) if expand else m

def clipped(layer,m):
    layer.putalpha(ImageChops.multiply(layer.getchannel('A'),m)); return layer

def bay(info,circular=False):
    layer=Image.new('RGBA',(W,H)); kind,data=info['door']
    if kind=='circle': cx,cy,r=data; box=(cx-r,cy-r,cx+r,cy+r)
    elif kind=='ellipse': x0,y0,x1,y1=data; box=(x0+18,y0+18,x1-18,y1-18)
    elif kind=='poly': box=(250,407,548,488)
    else: x0,y0,x1,y1=data; box=(x0+8,y0+8,x1-8,y1-8)
    x0,y0,x1,y1=box; d=ImageDraw.Draw(layer)
    for y in range(y0,y1+1):
        t=(y-y0)/max(1,y1-y0); c=tuple(round(PIT_LO[i]*(1-t)+PIT_HI[i]*t) for i in range(3))+(255,)
        d.line((x0,y,x1,y),fill=c)
    if circular:
        cx,cy=info['pad']
        for dx in range(-62,63,18): d.line((cx+dx,cy-60,cx+dx//2,cy+62),fill=PIT_HI,width=2)
        d.ellipse((cx-45,cy-45,cx+45,cy+45),outline=(120,206,215,255),width=3)
        d.ellipse((cx-28,cy-28,cx+28,cy+28),outline=(213,157,65,255),width=2)
    else:
        for x in range(x0+16,x1-8,26):
            d.line((x,y0+10,x,y1-8),fill=PIT_HI,width=3); d.line((x+5,y0+10,x+5,y1-8),fill=PIT_LO,width=2)
        d.line((x0+12,y1-13,x1-12,y1-13),fill=(213,157,65,255),width=3)
    return clipped(layer,mask(info))

def registered_open(name,base):
    """Register the generated open painting and hard-lock everything outside the hatch."""
    info=SITES[name]; p=HERE/f'{name}_open.png'
    generated=Image.open(p).convert('RGBA').resize((W,H),Image.Resampling.LANCZOS)
    opened=base.copy()
    opened.paste(generated,(0,0),mask(info))
    opened.save(p)
    return opened

def mist(info,frame):
    if frame not in (2,3,4,5): return Image.new('RGBA',(W,H))
    kind,data=info['door']
    if kind=='circle': cx,cy,r=data; x0,y0,x1,y1=cx-r,cy-r,cx+r,cy+r
    elif kind=='poly': x0,y0,x1,y1=214,376,584,508
    else: x0,y0,x1,y1=data
    amount={2:.55,3:.82,4:.92,5:.60}[frame]
    layouts={2:[(.50,.65,.32,.22)],3:[(.35,.65,.38,.25),(.67,.53,.28,.24)],4:[(.32,.69,.40,.29),(.58,.49,.35,.32),(.46,.30,.24,.22)],5:[(.48,.64,.34,.23),(.70,.52,.22,.18)]}
    layer=Image.new('RGBA',(W,H))
    for ox,oy,ow,oh in layouts[frame]:
        cx=round(x0+(x1-x0)*ox); cy=round(y0+(y1-y0)*oy); ww=round((x1-x0)*ow); hh=round((y1-y0)*oh)
        cloud=Image.new('RGBA',(W,H)); cd=ImageDraw.Draw(cloud)
        for j,col in enumerate(MIST):
            scale=1-j*.12; alpha=round((92+34*j)*amount)
            for lx,ly,lw,lh in ((-.34,.18,.34,.38),(-.08,-.16,.40,.44),(.22,.05,.38,.36),(.42,.20,.28,.28)):
                b=(round(cx+ww*(lx-lw/2)*scale),round(cy+hh*(ly-lh/2)*scale),round(cx+ww*(lx+lw/2)*scale),round(cy+hh*(ly+lh/2)*scale))
                cd.ellipse(b,fill=(*col,alpha))
        layer=Image.alpha_composite(layer,cloud.filter(ImageFilter.GaussianBlur(2.2)))
    return clipped(layer,mask(info,8))

def copy_translated(out,base,src,dx,dy,clip):
    moved=Image.new('L',(W,H)); moved.paste(src,(dx,dy)); moved=ImageChops.multiply(moved,clip)
    shifted=Image.new('RGBA',(W,H)); shifted.paste(base,(dx,dy)); out.paste(shifted,(0,0),moved)

def iris_masks(info):
    cx,cy=info['pad']; result=[]
    for start in range(-90,270,60):
        m=Image.new('L',(W,H)); pts=[(cx,cy)]
        for a in range(start,start+61,2):
            q=math.radians(a); pts.append((round(cx+151*math.cos(q)),round(cy+151*math.sin(q))))
        ImageDraw.Draw(m).polygon(pts,fill=255); result.append(ImageChops.multiply(m,mask(info)))
    return result

def render(name,base,f,include_mist=True):
    info=SITES[name]; door=mask(info)
    if f==0: return base.copy()
    if f==1:
        out=base.copy(); cx,cy=info['pad']; ImageDraw.Draw(out).ellipse((cx-4,cy-4,cx+4,cy+4),fill=(255,207,72,255)); return out
    opened=registered_open(name,base)
    # The hand-painted open state is revealed through a mechanism-shaped wipe.
    # Closing uses the same masks in reverse, so the reveal is monotonic.
    coverage={2:.40,3:.75,4:1.0,5:.50}[f]
    reveal=Image.new('L',(W,H)); rd=ImageDraw.Draw(reveal)
    kind,data=info['door']
    if kind=='circle':
        cx,cy,r=data; rr=round(145*math.sqrt(coverage)); rd.ellipse((cx-rr,cy-rr,cx+rr,cy+rr),fill=255)
    elif kind=='poly':
        # Longitudinal wipe along the dock's own diagonal platform axis.
        t=coverage; a=(214,376); b=(584,508); px=-132; py=370
        q0=(a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t)
        rd.polygon([a,b,(b[0]+px,b[1]+py),(a[0]+px,a[1]+py)],fill=255)
        if t < 1: rd.polygon([(a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t),(b[0],b[1]),(b[0]+px,b[1]+py),(a[0]+(b[0]-a[0])*t+px,a[1]+(b[1]-a[1])*t+py)],fill=0)
        # Use a centered seam-outward reveal inside the actual hatch footprint.
        axis=Image.new('L',(W,H)); ImageDraw.Draw(axis).polygon([(214,376),(584,508),(584,508),(318,463)],fill=255)
        reveal=ImageChops.multiply(axis,ImageChops.lighter(reveal,Image.new('L',(W,H))))
    elif kind=='ellipse':
        x0,y0,x1,y1=data; h=round((y1-y0)*coverage); rd.rectangle((x0,y1-h,x1,y1),fill=255)
    else:
        _,(x0,y0,x1,y1)=info['door']; h=round((y1-y0)*coverage); rd.rectangle((x0,y0,x1,y0+h),fill=255)
    reveal=ImageChops.multiply(reveal,door)
    out=base.copy(); out.paste(opened,(0,0),reveal)
    # A narrow shaded lip makes the moving front legible at game scale.
    edge=Image.new('RGBA',(W,H)); ed=ImageDraw.Draw(edge)
    if kind=='circle' and coverage < 1:
        cx,cy,_=data; rr=round(145*math.sqrt(coverage)); ed.ellipse((cx-rr,cy-rr,cx+rr,cy+rr),outline=(8,18,26,255),width=4)
    elif kind=='ellipse' and coverage < 1:
        x0,y0,x1,y1=data; yy=y1-round((y1-y0)*coverage); ed.line((x0,yy,x1,yy),fill=(8,18,26,255),width=4)
    elif kind=='rect' and coverage < 1:
        x0,y0,x1,y1=data; yy=y0+round((y1-y0)*coverage); ed.line((x0,yy,x1,yy),fill=(8,18,26,255),width=4)
    elif kind=='poly' and coverage < 1:
        t=coverage; x=round(214+(584-214)*t); y=round(376+(508-376)*t); ed.line((x,y,x-132,y+39),fill=(8,18,26,255),width=4)
    edge.putalpha(ImageChops.multiply(edge.getchannel('A'),door)); out=Image.alpha_composite(out,edge)
    return Image.alpha_composite(out,mist(info,f)) if include_mist else out

def assert_contained(base,frame,info,f):
    allowed=mask(info,8 if f>=2 else 0)
    for p,q,a in zip(base.getdata(),frame.getdata(),allowed.getdata()):
        if p!=q: assert a,('changed pixel outside declared door region',f)

def main():
    rows=[]
    for name,info in SITES.items():
        base=Image.open(HERE/f'{name}.png').convert('RGBA').resize((W,H),Image.Resampling.LANCZOS); base.save(HERE/f'{name}.png')
        if not (HERE/f'{name}_open.png').exists(): raise FileNotFoundError(HERE/f'{name}_open.png')
        frames=[render(name,base,f) for f in range(FRAMES)]
        assert frames[0].tobytes()==base.tobytes(),(name,'base changed')
        for f,frame in enumerate(frames): assert_contained(base,frame,info,f); no_bad(frame,name)
        # Opening is monotonic by declared geometry; pixel diffs may dip when
        # a moved panel happens to land on identical source pixels.
        opening = info.get('shift', (0,0,0,0,0,0))
        if info['kind'] == 'roll': opening = (0,0,52,84,122,72)
        assert opening[2] <= opening[3] <= opening[4] and opening[4] == max(opening), (name, opening)
        strip=Image.new('RGBA',(W*6,H))
        for i,x in enumerate(frames): strip.paste(x,(i*W,0))
        strip.save(HERE/f'{name}_launch_v2.png'); rows.append((name,frames))
    sheet=Image.new('RGBA',(960,920),(7,18,27,255)); d=ImageDraw.Draw(sheet)
    for row,(name,frames) in enumerate(rows):
        d.text((8,row*230+8),name.replace('_',' '),fill=(255,213,91,255))
        for i,x in enumerate(frames):
            sheet.alpha_composite(x.resize((150,150),Image.Resampling.NEAREST),(i*160+4,row*230+34)); d.text((i*160+58,row*230+190),str(i),fill=(213,157,65,255))
    no_bad(sheet,'launch_v2_contact_sheet.png'); sheet.convert('RGB').save(HERE/'launch_v2_contact_sheet.png')
    zoom=Image.new('RGBA',(1120,1160),(7,18,27,255)); zd=ImageDraw.Draw(zoom)
    for row,(name,frames) in enumerate(rows):
        kind,data=SITES[name]['door']
        if kind=='circle': cx,cy,_=data
        elif kind=='poly': cx,cy=400,442
        else: x0,y0,x1,y1=data; cx,cy=(x0+x1)//2,(y0+y1)//2
        zd.text((8,row*290+6),name.replace('_',' '),fill=(255,213,91,255))
        for col,f in enumerate(frames[2:6]):
            zoom.alpha_composite(f.crop((cx-128,cy-128,cx+128,cy+128)),(col*280+12,row*290+26)); zd.text((col*280+16,row*290+266),str(col+2),fill=(213,157,65,255))
    no_bad(zoom,'launch_v2_mist_zoom.png'); zoom.save(HERE/'launch_v2_mist_zoom.png')
if __name__=='__main__': main()
