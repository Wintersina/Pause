from PIL import Image
FILLREF=[(32,17,14)]
def frame_only(board):
    out=board.copy(); px=out.load()
    for y in range(out.height):
        for x in range(out.width):
            r,g,b,a=px[x,y]
            f=FILLREF[0]
            if 150<=a<=200 and abs(r-f[0])<16 and abs(g-f[1])<16 and abs(b-f[2])<16: px[x,y]=(0,0,0,0)
    return out
def build_panel(board, fill, W=600, H=900, top=110, bot=205, side=150, nmod=3, mod=(108,197), plain=(201,213)):
    bw,bh=board.size; fr=frame_only(board)
    frame=Image.new('RGBA',(W,H)); mid=W-2*side; cx=bw//2
    frame.alpha_composite(fr.crop((cx-mid//2,0,cx-mid//2+mid,top)),(side,0))
    frame.alpha_composite(fr.crop((cx-mid//2,bot,cx-mid//2+mid,bh)),(side,H-(bh-bot)))
    span=H-(bh-bot)-top
    for (x0,x1,dx) in [(0,side,0),(bw-side,bw,W-side)]:
        col=Image.new('RGBA',(side,span)); f=fr.crop((x0,plain[0],x1,plain[1]))
        for yy in range(0,span,f.height): col.alpha_composite(f,(0,yy))
        m=fr.crop((x0,mod[0],x1,mod[1])); mh=m.height
        for i in range(nmod):
            my=int((i+0.5)*span/nmod-mh/2); col.paste(Image.new('RGBA',(side,mh)),(0,my)); col.alpha_composite(m,(0,my))
        frame.paste(Image.new('RGBA',(side,span)),(dx,top)); frame.alpha_composite(col,(dx,top))
    for box,pos in [((0,0,side,top),(0,0)),((bw-side,0,bw,top),(W-side,0)),((0,bot,side,bh),(0,H-(bh-bot))),((bw-side,bot,bw,bh),(W-side,H-(bh-bot)))]:
        frame.paste(Image.new('RGBA',(box[2]-box[0],box[3]-box[1])),pos); frame.alpha_composite(fr.crop(box),pos)
    # interior fill behind: inner rect from the board's inner edge inset
    out=Image.new('RGBA',(W,H)); inset_x, inset_top, inset_bot = 40, 45, 45
    out.paste(Image.new('RGBA',(W-2*inset_x,H-inset_top-inset_bot),fill),(inset_x,inset_top))
    out.alpha_composite(frame); return out
if __name__=='__main__':
    b=Image.open('hud-boards-opt3/hud_board_Desert.png').convert('RGBA')
    p=build_panel(b,(39,24,13,255)); p.save('hud-final/panel_test.png')
