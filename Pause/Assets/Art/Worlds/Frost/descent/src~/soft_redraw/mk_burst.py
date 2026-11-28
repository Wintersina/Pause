import sys; sys.path.insert(0,'entry'); from efin import *
import efin
os.makedirs('out2/bd_frost_entry_breakthrough',exist_ok=True)
VAR={0:dict(raw='r1_0',nat=.5,sigma=1.2,gamma=1.25),1:dict(raw='r1_0',nat=.5,sigma=1.2,gamma=1.6,spin=2.2),2:dict(raw='r1_0',nat=.5,sigma=1.2,gamma=1.35,spin=1.5,round_=.9,zoom=.12)}
for i,v in VAR.items():
    v=dict(v); raw=O+'breakthrough/raw/'+v.pop('raw')+'.png'
    fr=burst(raw,f'{O}breakthrough/r1_{i}.png',**v); gif(fr,f'entry/breakthrough_r1_{i}.gif')
    S=Image.new('RGBA',(5120,1024),(40,58,92,255)); S.alpha_composite(Image.open(f'{O}breakthrough/r1_{i}.png')); S.resize((1600,320),Image.LANCZOS).save(f'entry/prev_burst_{i}.png')
