import sys,numpy as np
from PIL import Image
im=np.array(Image.open(sys.argv[1]).convert('RGBA')).astype(int);cs=im.shape[0];n=im.shape[1]//cs
c=[im[:,i*cs:(i+1)*cs] for i in range(n)]
def d(a,b): m=(a[...,3]>0)|(b[...,3]>0); return round(float(np.abs(a-b).sum(2)[m].mean()),0)
print(sys.argv[1].split('/')[-1],'idle loop mean abs RGB diff 0>1>2>3>0:',[d(c[i],c[(i+1)%4]) for i in range(4)])
