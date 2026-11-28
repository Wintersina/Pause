# Frost planetfall entry clouds: raw paintings via generate-image-v2 (no style image). raw -> out2/bd_frost_entry_<item>/raw/<tag>.png
import io, os, sys, base64, time, threading
sys.path.insert(0,'/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/pl')
import plapi
from PIL import Image
from concurrent.futures import ThreadPoolExecutor
LOG=threading.Lock(); COUNT='entry/count.txt'
TAIL=' Soft painterly style, smooth gradients, soft feathered edges, no outlines, no hard pixel steps, fine detail, cold ice palette: pale blue-white highlights, steel blue shadows, no pink, no purple, no warm colours, no ground, no text, no UI.'
TOP='Seen from very high altitude looking straight down: '
BG=' Isolated in the centre on a plain flat solid very dark navy background (#0c1424) with an empty dark margin on every side; nothing touches the image edges.'
def gen(item, tag, prompt, size):
    d=f'out2/bd_frost_entry_{item}/raw'; os.makedirs(d,exist_ok=True)
    if os.path.exists(f'{d}/{tag}.png'): return item, tag, 'skip', 0, ''
    body={'description':prompt[:2000],'image_size':{'width':size[0],'height':size[1]},'no_background':False}
    res={}
    for a in range(12):
        try:
            r=plapi.call('POST','/generate-image-v2',body); res=plapi.wait(r.get('background_job_id') or r.get('id'))
            if res['status']=='completed': break
        except Exception as e:
            res={'status':'failed','e':str(e)[:300]}
            if '429' in str(e): time.sleep(25); continue
            if a>=1: break
        time.sleep(8)
    n=0
    for i,im in enumerate((res.get('last_response') or {}).get('images') or []):
        b=(im.get('base64') if isinstance(im,dict) else im).split(',',1)[-1]
        Image.open(io.BytesIO(base64.b64decode(b))).save(f'{d}/{tag}'+(f'_{i}' if i else '')+'.png'); n+=1
    with LOG: open(COUNT,'a').write(f'{item} {tag} {res.get("status")} {n}\n')
    return item, tag, res.get('status'), n, (str(res)[:300] if not n else '')
P={
 ('deck','r1_0'):(TOP+'a vast endless sea of soft billowing cumulus clouds filling the entire image from edge to edge, no sky, no horizon, no background. Bright pale blue-white rounded cloud tops lit from the top-left, soft steel-blue shading between the puffs, a few narrow winding gaps of deep cold blue between the cloud masses. Even coverage everywhere, no single focal point.',(688,344)),
 ('deck','r1_1'):(TOP+'a thick bright layer of stratocumulus clouds covering the entire image from edge to edge, no sky, no horizon, no background: soft lumpy rounded cloud cells packed together, glowing pale white-blue tops, gentle steel-blue shadows in the folds, thin wind-swept wisps, uniform density across the whole picture.',(688,344)),
 ('deck','r1_2'):(TOP+'a soft hazy sea of glowing clouds covering the entire image from edge to edge, no sky, no horizon, no background: large soft rounded cloud masses with misty blurred edges blending into each other, airbrushed smooth gradients, bright pale white-blue tops, soft cool blue shadows, wisps of thin mist drifting across, dreamy atmospheric depth, uniform coverage across the whole picture.',(688,344)),
 ('deck_dark','r1_0'):(TOP+'a dark heavy blizzard storm cloud layer filling the entire image from edge to edge, no sky, no horizon, no background: dim slate-blue and deep navy rolling cloud masses, soft billows with faint pale ice-blue rims, wind-torn streaks of snow mist between them, dark shadowy gaps, low contrast, moody night, uniform coverage across the whole picture.',(688,344)),
 ('breakthrough','r1_0'):(TOP+'the moment a ship bursts down through a cloud layer: a round blow-out of soft billowing cloud, a thick ring of puffy white-blue cloud and icy mist exploding outward in every direction around an empty clear dark hole in the centre, radial wisps and streamers of mist flung outward, the outer edge thinning into feathery translucent spray. Nothing inside the hole, no ship, no object.'+BG,(512,512)),
}
if __name__=='__main__':
    want=sys.argv[1:]
    jobs=[(it,t,p+TAIL,s) for (it,t),(p,s) in P.items() if not want or f'{it}/{t}' in want]
    with ThreadPoolExecutor(3) as ex:
        for r in ex.map(lambda j: gen(*j), jobs): print(*r, flush=True)
    print('DONE')
