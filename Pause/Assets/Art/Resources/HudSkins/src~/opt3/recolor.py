from PIL import Image
import colorsys
BASE='desert-hud/hud_board_desert_opt3_buried_machinery.png'
# world: (light hue, light sat scale, sand -> (h,s,v scale))
W={
 'Frost':  dict(light=(0.53,0.55), sand=(0.58,0.12,1.08)),
 'Verdant':dict(light=(0.22,1.0),  sand=(0.25,0.55,0.85)),
 'Ember':  dict(light=(0.07,1.0),  sand=(0.04,0.25,0.55)),
 'Storm':  dict(light=(0.76,0.85), sand=(0.70,0.18,0.80)),
}
def recolor(im, cfg):
    out=im.copy(); px=out.load()
    for y in range(im.height):
        for x in range(im.width):
            r,g,b,a=px[x,y]
            if a==0: continue
            h,s,v=colorsys.rgb_to_hsv(r/255,g/255,b/255)
            if 0.38<=h<=0.58 and s>0.3:            # teal lights/gems
                lh,ls=cfg['light']; nh,ns,nv=lh,min(1,s*ls),v
            elif 0.06<=h<=0.15 and s<0.68 and v>=0.68:   # pale sand drifts
                sh,ss,sv=cfg['sand']; nh,ns,nv=sh,ss*(0.6+s),min(1,v*sv)
            else: continue
            rr,gg,bb=colorsys.hsv_to_rgb(nh,ns,nv); px[x,y]=(int(rr*255),int(gg*255),int(bb*255),a)
    return out
if __name__=='__main__':
    base=Image.open(BASE).convert('RGBA')
    for k,c in W.items(): recolor(base,c).save(f'hud-final/frame_{k}.png')
    strip=Image.new('RGBA',(702,310*4),(30,30,40,255))
    for i,k in enumerate(W): strip.alpha_composite(Image.open(f'hud-final/frame_{k}.png'),(0,i*310))
    strip.resize((351,620),Image.NEAREST).save('hud-final/frames_check.png')

FILL={'Desert':(40,25,17),'Frost':(16,31,51),'Verdant':(19,34,22),'Ember':(34,20,15),'Storm':(19,26,42)}
def refill(im, world, alpha=None):
    out=im.copy(); px=out.load(); f=FILL[world]
    for y in range(im.height):
        for x in range(im.width):
            r,g,b,a=px[x,y]
            if 160<=a<=190 and abs(r-32)<14 and abs(g-17)<14 and abs(b-14)<14:
                px[x,y]=(f[0],f[1],f[2],a if alpha is None else alpha)
    return out
