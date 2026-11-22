from PIL import Image
import colorsys, sys
o2=Image.open('desert-hud/hud_board_desert_opt2_thin_worm_peek.png').convert('RGBA')
o3=Image.open('desert-hud/hud_board_desert_opt3_buried_machinery.png').convert('RGBA')
def bar_top(im, x):
    for y in range(im.height):
        if im.getpixel((x,y))[3]>200: return y
print('opt2 top at x=300:', bar_top(o2,300), 'opt3:', bar_top(o3,300))
def is_frame(p):
    r,g,b,a=p
    h,s,v=colorsys.rgb_to_hsv(r/255,g/255,b/255)
    if b>r*0.8: return True                       # teal gem / dark blue
    if r>215 and g>170: return True               # light beige bar
    if 0.10<h<0.17 and s>0.6 and v>0.6: return True  # saturated gold bracket
    return False
dy=int(sys.argv[1]) if len(sys.argv)>1 else 0
dx=int(sys.argv[2]) if len(sys.argv)>2 else 0
worm=Image.new('RGBA',o2.size)
top2=bar_top(o2,300)
for y in range(0,top2+6):
    for x in range(0,110):
        p=o2.getpixel((x,y))
        if p[3]<40: continue
        if y>=top2-6 and is_frame(p): continue
        if x<60 and y>=top2-8 and is_frame(p): continue
        if x>=52 and y>=top2-3: continue
        worm.putpixel((x,y),p)
worm.save('hud-boards-opt3/worm_only.png')
out=o3.copy(); out.alpha_composite(worm,(dx,dy)); out.save('hud-boards-opt3/hud_board_Desert.png')
z=out.crop((0,0,160,90)).resize((640,360),Image.NEAREST); z.save('hud-boards-opt3/zoom.png')
