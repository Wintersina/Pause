# Measures the Space hazard rock strips (Art/Backgrounds/Resources/Worlds/Space/HazardRocks) for the numbers in
# SpaceHazardRockTypes.cs: fill = widest side / frame, radius = area-equivalent radius / frame (alpha >= 128).
import sys
from PIL import Image
import numpy as np
D=sys.argv[1]
for t in ["shard","ember","ore","crystal"]:
    row=[]
    for k in ["","_chunk"]:
        a=np.array(Image.open(f"{D}/space_hazard_rock_{t}{k}_blink.png").convert("RGBA"))[:,:,3]>=128
        fills=[];rads=[]
        for f in range(4):
            m=a[:,f*512:(f+1)*512]; ys,xs=np.where(m)
            fills.append(max(xs.max()-xs.min()+1,ys.max()-ys.min()+1)/512); rads.append((m.sum()/3.14159265)**.5/512)
        row+= [round(max(fills),3),round(float(np.mean(rads)),3)]
    print(f'new SpaceHazardRockType("{t}", fill: {row[0]}f, radius: {row[1]}f, chunkFill: {row[2]}f, chunkRadius: {row[3]}f, ...)')
