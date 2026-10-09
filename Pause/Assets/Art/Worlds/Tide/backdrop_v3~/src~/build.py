"""Build Tide atmosphere-level ground tiles from selected painted imagegen sources.
All resampling is nearest-neighbour. The final edge repair selects original painted
pixels with a deterministic stippled seam pass, then moves the join to tile centre.
"""
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np
import json

ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent
SIZE = (512, 1024)
SELECT = {1: (1, 2), 2: (3, 4), 3: (6, 5), 4: (7, 8)}
TARGET = {1: {'sky': .383, 'far': .430, 'mid': .470},
          2: {'sky': .383, 'far': .430, 'mid': .470},
          3: {'sky': .383, 'far': .430, 'mid': .470},
          4: {'sky': .307, 'far': .351, 'mid': .388}}
NOTES = {
  1: 'Open swell, thin whitecap veins, kelp and sparse rusted buoys',
  2: 'Top-down oil rig rings, dense industrial decks and pipe bridges',
  3: 'Submerged factory and street grid with tower crowns and caustic marks',
  4: 'Night-black current vortices, sparse beacons and mint plankton'
}
RNG = np.random.default_rng(20261009)


def load_source(number, shift=(0,0)):
    im = Image.open(SRC / f'candidate_{number:02}.png').convert('RGB')
    # The painted portrait is 887x1774, exactly 1:2. Native-pixel selection only.
    im = im.resize(SIZE, Image.Resampling.NEAREST)
    return np.roll(np.asarray(im, dtype=np.uint8), shift, axis=(1,0)).copy()


def hue_clean(rgb):
    # Keep the source's spatially painted light and texture; restrict world hues.
    hsv = np.asarray(Image.fromarray(rgb).convert('HSV'), dtype=np.float32)
    h, s, v = hsv[:,:,0] * 360 / 255, hsv[:,:,1] / 255, hsv[:,:,2] / 255
    ocean = (h >= 75) & (h < 214)
    mint = ocean & (v > .27) & (s > .35) & (h < 178)
    h[ocean] = 164 + np.clip((h[ocean]-165)*.10, -6, 5)
    h[mint] = 153 + np.clip((h[mint]-155)*.12, -4, 7)
    rust = (h < 75) | (h >= 330)
    h[rust] = 21 + np.clip((h[rust]-26)*.03, -1.5, 1.5)
    s[rust] = np.minimum(s[rust], .43)
    violet = (h >= 214) & (h < 330)
    h[violet] = 225 + np.clip((h[violet]-230)*.05, -5, 5)
    # Greys receive ocean hue but retain their low saturation.
    h[s < .09] = 165
    # Keep rust structural and prevent white/mint fields from dominating play.
    v[rust] = np.minimum(v[rust], .52)
    s = np.minimum(s, .72)
    packed = np.stack((np.round(h*255/360), np.round(s*255), np.round(v*255)), axis=2).clip(0,255).astype('uint8')
    return np.asarray(Image.fromarray(packed, 'HSV').convert('RGB'))


def calibrate(rgb, target, gamma=1.10):
    hsv=np.asarray(Image.fromarray(rgb).convert('HSV'),dtype=np.float32)
    val=hsv[:,:,2]/255
    p90=float(np.quantile(val,.90))
    # Slightly hard material ramps preserve a low mean while reaching the p90 target.
    mapped=np.clip(target*np.power(val/max(p90,1e-5),gamma),0,.73)
    hsv[:,:,2]=np.round(mapped*255)
    return np.asarray(Image.fromarray(hsv.astype('uint8'),'HSV').convert('RGB'))


def repair_axis(a, axis, width=56):
    """Stipple painted strips over the wrap join; never interpolate RGB values."""
    b=a.copy(); n=a.shape[axis]
    # Replicate the last strip at the first edge, then progressively choose
    # the original first strip. A common edge pixel makes exact toroidal wrap.
    for k in range(width):
        t=k/(width-1)
        choose=RNG.random(a.shape[1-axis]) < t
        if axis==0:
            b[k,:,:]=np.where(choose[:,None],a[k,:,:],a[n-width+k,:,:])
        else:
            b[:,k,:]=np.where(choose[:,None],a[:,k,:],a[:,n-width+k,:])
    if axis==0: b[0,:,:]=b[-1,:,:]
    else: b[:,0,:]=b[:,-1,:]
    return b


def tile(rgb):
    a=repair_axis(rgb,0)
    a=repair_axis(a,1)
    # Move both repaired joins inside the texture; wrapping now shows the repair.
    return np.roll(a,(SIZE[1]//2,SIZE[0]//2),axis=(0,1))


def flow_from(mid, variant):
    hsv=np.asarray(Image.fromarray(mid).convert('HSV'),dtype=np.float32)
    v=hsv[:,:,2]/255; h=hsv[:,:,0]*360/255;s=hsv[:,:,1]/255
    threshold={1:.91,2:.74,3:.91,4:.90}[variant]
    luminous=(h>145)&(h<171)&(s>.24)
    level=np.quantile(v[luminous],threshold) if luminous.any() else np.quantile(v,threshold)
    mask=luminous&(v>=level)
    # The source's painted foam/caustic/plankton pixels become transparent drift.
    alpha=np.where(mask,np.clip(70+150*(v-level),65,185),0).astype('uint8')
    out=np.dstack((mid,alpha))
    # Alpha inherits the exact wrap from mid only if selected edges agree.
    out[0,:,:]=out[-1,:,:];out[:,0,:]=out[:,-1,:]
    return out


def painted_repair(variant, role):
    """Use imagegen's seam repaint, retaining the original toroidal border.

    A stippled 60px source-selection transition keeps painted source pixels crisp;
    no RGB averaging, smoothing, or blur is used.
    """
    old=np.asarray(Image.open(SRC/f'baseline_v{variant}_{role}.png').convert('RGB'))
    name='city_seam_repaint.png' if (variant,role)==(3,'mid') else f'v{variant}_{role}_seam_repaint.png'
    repaint=Image.open(SRC/name).convert('RGB').resize(SIZE,Image.Resampling.NEAREST)
    gamma=.83 if variant==4 and role=='mid' else 1.10
    new=calibrate(hue_clean(np.asarray(repaint)),TARGET[variant][role],gamma)
    yy,xx=np.indices((1024,512))
    edge=np.minimum.reduce([xx,511-xx,yy,1023-yy])
    chance=np.clip((edge-5)/60,0,1)
    # Blue-noise-like stipple without changing the underlying source colours.
    pick=RNG.random((1024,512))<chance
    out=np.where(pick[:,:,None],new,old).astype('uint8')
    return finish_wrap(sanitize_hues(out))


def sanitize_hues(rgb):
    hsv=np.asarray(Image.fromarray(rgb).convert('HSV'),dtype=np.float32)
    h=hsv[:,:,0]*360/255;s=hsv[:,:,1]/255;v=hsv[:,:,2]/255
    h[((h>=345)|(h<=15))&(s>.10)]=27
    h[(h>=170)&(h<=186)&(s>.10)]=165
    h[(h>=74)&(h<=90)&(s>.10)]=155
    h[(h>=252)&(h<=266)&(s>.10)]=225
    h[(h>=30)&(h<=44)&(s>.10)]=27
    h[(h>=312)&(h<=326)&(s>.10)]=225
    s[v<.17]=np.minimum(s[v<.17],.09)
    hsv[:,:,0]=np.round(h*255/360);hsv[:,:,1]=np.round(s*255)
    out=np.asarray(Image.fromarray(hsv.astype('uint8'),'HSV').convert('RGB')).copy()
    check=np.asarray(Image.fromarray(out).convert('HSV'),dtype=np.float32)
    red=((check[:,:,0]*360/255<=15)|(check[:,:,0]*360/255>=345)) & (check[:,:,1]/255>.12)
    out[red]=np.max(out[red],axis=1)[:,None]
    return out


def finish_wrap(rgb):
    # Preserve painted texture and make the 1px toroidal boundary exact.
    out=rgb.copy();out[0,:,:]=out[-1,:,:];out[:,0,:]=out[:,-1,:]
    return out


def build():
    manifest={'world':'Tide','version':'backdrop_v3','source':'Built-in image generation, 8 inspected painted candidates; selected pairs in src~/candidate_review.png', 'files':[]}
    for variant,(primary,alternate) in SELECT.items():
        folder=ROOT/f'v{variant}';folder.mkdir(parents=True,exist_ok=True)
        inputs={'sky':(alternate,(53,137)),'far':(alternate,(0,0)),'mid':(primary,(0,0))}
        outputs={}
        far=painted_repair(variant,'far')
        mid=painted_repair(variant,'mid')
        sky=sanitize_hues(calibrate(np.roll(far,(137,53),axis=(0,1)),TARGET[variant]['sky']))
        sky[0,:,:]=sky[-1,:,:]
        sky[:,0,:]=sky[:,-1,:]
        for role,finished in [('sky',sky),('far',far),('mid',mid)]:
            rgba=np.dstack((finished,np.full((1024,512),255,dtype=np.uint8)))
            outputs[role]=Image.fromarray(rgba,'RGBA')
            outputs[role].save(folder/f'{role}.png')
        flow=flow_from(np.asarray(outputs['mid'])[:,:,:3],variant)
        outputs['flow']=Image.fromarray(flow,'RGBA');outputs['flow'].save(folder/'flow.png')
        for role,im in outputs.items():
            a=np.asarray(im); opaque=a[:,:,3]>0
            hsv=np.asarray(im.convert('HSV'))
            vals=hsv[:,:,2][opaque]/255 if opaque.any() else np.array([0.])
            seam=max(float(np.abs(a[0].astype('int16')-a[-1].astype('int16')).mean()),
                     float(np.abs(a[:,0].astype('int16')-a[:,-1].astype('int16')).mean()))
            manifest['files'].append({'path':f'v{variant}/{role}.png','size':[512,1024],
              'role':role,'variant':variant,'notes':NOTES[variant] + ('; translucent painted drift' if role=='flow' else f'; painted candidate {inputs[role][0]}, imagegen seam repaint'),
              'p90_hsv_value':round(float(np.quantile(vals,.90)),4),
              'mean_hsv_value':round(float(vals.mean()),4),
              'wrap_edge_difference':round(seam,5),
              'opaque_fraction':round(float(opaque.mean()),4)})
    (ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    make_preview()


def make_preview():
    # 4 native-size role tiles per column, then a 1080x2400 phone composite.
    canvas=Image.new('RGB',(4320,6590),'#050c10')
    draw=ImageDraw.Draw(canvas)
    for v in range(1,5):
        x=(v-1)*1080;draw.text((x+12,8),f'v{v}: {NOTES[v]}',fill='#b7dacd')
        ims={role:Image.open(ROOT/f'v{v}'/f'{role}.png').convert('RGBA') for role in ['sky','far','mid','flow']}
        for i,role in enumerate(['sky','far','mid','flow']):
            y=36+i*1024
            base=Image.new('RGBA',SIZE,'#081315')
            base.alpha_composite(ims[role])
            canvas.paste(base.convert('RGB'),(x+284,y))
            draw.text((x+220,y+8),role,fill='#d4f0e7')
        # Opaque mid visually dominates after the ordered layer stack, as in-game.
        comp=Image.new('RGBA',SIZE,(0,0,0,0))
        for role in ['sky','far','mid','flow']:comp.alpha_composite(ims[role])
        phone=comp.resize((1080,2160),Image.Resampling.NEAREST)
        # Repeat to fill the 2400px portrait viewport, preserving wrap continuity.
        phone_canvas=Image.new('RGBA',(1080,2400))
        phone_canvas.paste(phone,(0,0));phone_canvas.paste(phone.crop((0,0,1080,240)),(0,2160))
        canvas.paste(phone_canvas.convert('RGB'),(x,4170))
    canvas.save(ROOT/'preview_variants.png',optimize=True)

if __name__=='__main__':build()
