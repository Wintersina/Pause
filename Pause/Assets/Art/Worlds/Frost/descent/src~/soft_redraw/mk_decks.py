import sys; sys.path.insert(0,'entry'); from efin import *
R=O+'%s/raw/%s.png'
D={'deck':[('deck','r1_2',dict(nat=.4,sigma=5.0)),('deck','r1_0',dict(nat=.4,sigma=5.0)),('deck','r1_0',dict(nat=.5,sigma=3.5))],
   'deck_dark':[('deck_dark','r1_0',dict(nat=.4,sigma=5.0)),('deck_dark','r1_0',dict(nat=.5,sigma=3.5)),('deck','r1_0',dict(nat=.4,sigma=5.0))]}
TGT={'deck':.85,'deck_dark':.41}; ST={'deck':BRIGHT,'deck_dark':DARK}
for item,lst in D.items():
    for i,(src,t,kw) in enumerate(lst):
        out=f'{O}{item}/r1_{i}.png'; v=deck(R%(src,t),out,ST[item],TGT[item],**kw)
        im=Image.open(out).convert('RGB'); T=Image.new('RGB',(2048,1024))   # 2x2 tile check, half size
        s=im.resize((1024,512),Image.LANCZOS); [T.paste(s,(x,y)) for x in (0,1024) for y in (0,512)]
        T.save(f'entry/tile_{item}_{i}.png'); print(item,i,src,t,'meanval %.3f'%v)
