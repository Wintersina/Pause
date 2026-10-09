"""Verify all Tide backdrop_v3 tiles; exits nonzero on a failed contract."""
from pathlib import Path
from PIL import Image
import numpy as np
import json

ROOT=Path(__file__).resolve().parent.parent
BANDS={'C':(170,186),'G':(74,90),'V':(252,266),'A':(30,44),'P':(312,326)}


def metrics(path):
    im=Image.open(path)
    rgba=np.asarray(im.convert('RGBA'))
    hsv=np.asarray(im.convert('HSV'),dtype=np.float32)
    h=hsv[:,:,0]*360/255;s=hsv[:,:,1]/255;v=hsv[:,:,2]/255
    present=rgba[:,:,3]>0
    chroma=present&(s>.12)&(v>.06)
    bands={key:float(np.mean(chroma&(h>=lo)&(h<=hi))) for key,(lo,hi) in BANDS.items()}
    bands['R']=float(np.mean(chroma&((h>=345)|(h<=15))))
    edge=max(float(np.abs(rgba[0].astype('int16')-rgba[-1].astype('int16')).mean()),
             float(np.abs(rgba[:,0].astype('int16')-rgba[:,-1].astype('int16')).mean()))
    return {'mode':im.mode,'size':im.size,'p90':float(np.quantile(v[present],.9)),
            'mean':float(v[present].mean()),'wrap':edge,'occupied':float(present.mean()),
            'max_alpha':int(rgba[:,:,3].max()),'colors':len(np.unique(rgba.reshape(-1,4),axis=0)),
            'bands':bands}


def main():
    failures=[];data={}
    print('file       p90   mean  wrap  flow%  colors  C%    G%    V%    A%    P%    R%   status')
    for variant in range(1,5):
        for role in ['sky','far','mid','flow']:
            path=ROOT/f'v{variant}'/f'{role}.png';m=metrics(path);data[(variant,role)]=m
            bad=[]
            if m['mode']!='RGBA' or m['size']!=(512,1024):bad.append('format')
            if m['wrap']!=0:bad.append('wrap')
            if role=='flow':
                if not (.005<=m['occupied']<=.25) or m['max_alpha']>=255:bad.append('flow alpha')
                if m['colors']<200:bad.append('colors')
            else:
                lo,hi=(.30,.40) if variant==4 else (.38,.48)
                if not (lo<=m['p90']<=hi):bad.append('p90')
                if m['occupied']!=1 or m['max_alpha']!=255:bad.append('opaque')
                if m['colors']<3000:bad.append('colors')
                if role=='mid':
                    ml=.17 if variant==4 else .20
                    if not (ml<=m['mean']<=.28):bad.append('mean')
            if any(share>.0001 for share in m['bands'].values()):bad.append('hue')
            if bad:failures.append(f'v{variant}/{role}: {", ".join(bad)}')
            bs=' '.join(f'{m["bands"][key]*100:5.3f}' for key in ['C','G','V','A','P','R'])
            print(f'v{variant}/{role:<4} {m["p90"]:.3f} {m["mean"]:.3f} {m["wrap"]:5.2f} {m["occupied"]*100:5.2f} {m["colors"]:7d} {bs}  {"FAIL" if bad else "PASS"}')
        sky,far,mid=[data[(variant,r)]['p90'] for r in ['sky','far','mid']]
        if not (.03<=far-sky<=.07 and .03<=mid-far<=.07):failures.append(f'v{variant}: depth p90 steps')
    manifest=json.loads((ROOT/'manifest.json').read_text())
    if len(manifest['files'])!=16:failures.append('manifest file count')
    print('RESULT:', 'PASS (16 files)' if not failures else 'FAIL: '+'; '.join(failures))
    return 1 if failures else 0

if __name__=='__main__':raise SystemExit(main())
