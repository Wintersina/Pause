import json,glob
D={}
for f in sorted(glob.glob('/Users/sina/Developer/Pause-shipscale/Pause/Assets/Art/Resources/Elites/Defs/*.json')):
    j=json.load(open(f)); D[j['key']]=j
# attack aim behaviour: (class, description)
# class T = tracks live then frozen at share; L = live to the instant of fire; F = frozen at tell start (telegraphed); N = no pilot aim
AIM={
 'lance_dash':('T',.5,'re-aims through the first half of the tell, then locks the ram line'),
 'surf_dash':('T',.5,'as lance_dash, lane drawn at the lock'),
 'rift_rail':('T',.6,'sight line tracks 60% of the tell then locks'),
 'armour_shatter':('T',.5,'stripped: sight tracks 50% then locks; plated: shards aimed at the live pilot at fire'),
 'blink_shards':('L',1,'blinks, then fans shards at the pilot position read at the instant of firing'),
 'resin_mortar':('L',1,'row of ringed spots re-centred on the live pilot at the end of the tell'),
 'ward_curtain':('L',1,'row of bolts centred on the live pilot at the end of the tell, gap beside it'),
 'pearl_curtain':('L',1,'as ward_curtain'),
 'gravity_sling':('L',1,'well placed lobAhead of the live pilot at the end of the tell'),
 'floe_cast':('L',1,'row height and lance height re-read from the live pilot at fire (gap column fixed at tell start)'),
 'frost_bloom':('L',1,'orb thrown at the live pilot at fire; every 3rd cast a beam sweep that locks at 70% of its tell'),
 'claw_dive':('F',0,'dive point locked at the first frame of the tell'),
 'crescent_volley':('F',0,'volley spot locked at the first frame of the tell'),
 'ice_ram':('F',0,'lane locked at the first frame of the tell'),
 'leaf_dive':('F',0,'dive lane + slash row locked and drawn at tell start'),
 'depth_charge':('F',0,'burst rings locked and drawn at tell start'),
 'net_cast':('F',0,'net spots locked and drawn at tell start'),
 'thunder_strike':('F',0,'strike lanes locked and drawn at tell start'),
 'vine_lash':('F',0,'lash aimed at tell start, drawn'),
 'spore_burst':('F',0,'burst rings locked and drawn at tell start'),
 'log_roll':('F',0,'trunk spots locked and drawn at tell start'),
 'broadside':('N',0,'side cannons fire along the lane; no pilot aim'),
 'slag_drop':('N',0,'drops straight down its own lane'),
 'siege_cannon':('N',0,'planted, sight line down its own lane'),
 'drone_deploy':('N',0,'releases tethered drones'),
}
FOLLOW={ # brain -> (what it follows, brain-side tightness note, proposed followLag s)
 'interceptor':('x and y of the pilot, goal = pilot + (weave .45, -follow)','exact (zero lag), nose snaps at turnRate x1.4',.35),
 'gunship':('mirrors pilot height and holds a lane beside it','exact x and y (+.1), speed x1.1',.45),
 'striker':('circles the live pilot, re-centred every frame','exact centre',.30),
 'hauler':('lane slides to pilot x at .55 x speed','already slow',.25),
 'skirmisher':('orbits the live pilot at keepDistance','exact centre, nose snaps',.25),
 'siege':('top lane tracks pilot x at .6 x speed','already slow',.25),
 'breaker':('prowls followDistance ahead, sweeping across pilot x','exact centre of the sweep',.30),
 'warden':('fixed side station','no follow',.20),
 'bastion':('lane tracks pilot x at .45 x speed, clamped','already slow',.25),
 'reaver':('orbit centre = live pilot + lift','exact centre',.35),
 'lancer':('flank = live pilot +- laneOffset, nose on pilot','exact flank, nose snaps',.30),
 'tug':('lane tracks pilot x at .5 x speed','already slow',.25),
 'herder':('herding lane beside pilot','exact',.30),
 'kiter':('fixed range above the pilot','exact',.30),
 'tender':('keeps range above the pilot','exact',.30),
 'ironclad':('straight legs at the pilot, never dodges','exact aim of each leg',.35),
 'logger':('hauler-like lane','already slow',.25),
 'thorn':('gunship lane beside pilot','exact',.40),
 'drifter':('bastion-like drift','already slow',.25),
 'diver':('striker circle','exact',.35),
 'surfer':('interceptor-like stalk','exact',.35),
 'abyss':('siege-like top lane','already slow',.25),
 'netter':('hauler lane','already slow',.25),
 'stormray':('skirmisher orbit','exact',.30),
 'pearler':('bastion lane','already slow',.25),
}
rows=[]
for k,d in D.items():
    a=AIM[d['attack']]; f=FOLLOW[d['brain']]
    tell=d['tellSeconds']
    tell2=max(tell,.6) if a[0] in 'TL' else tell
    if d['key'] in ('ember_elite_ash_wraith','ember_elite_brass_vulture'): tell2=max(tell2,.65)
    if a[0]=='T': trk0=a[1]*tell; trk2=min(a[1],.5)*tell2
    elif a[0]=='L': trk0=tell; trk2=.5*tell2
    else: trk0=trk2=0
    # severity
    sev=0
    if a[0]=='L': sev+=2
    if a[0]=='T': sev+=1
    if f[1].startswith('exact'): sev+=1
    if d['turnsToFace'] and d['turnRate']*1.4>300: sev+=1
    if tell<.6 and a[0] in 'TL': sev+=1
    rows.append(dict(key=k,name=d['displayName'],world=d['world'],brain=d['brain'],attack=d['attack'],tell=tell,tell2=tell2,trk0=trk0,trk2=trk2,warn0=(tell-trk0) if a[0] in 'TL' else (tell if a[0]=='F' else tell),warn2=(tell2-trk2) if a[0] in 'TL' else tell2,
        cls=a[0],aimdesc=a[2],follow=f[0],tight=f[1],lag=f[2],turn=d['turnRate'],face=d['turnsToFace'],sev=sev,speed=d['speed'],dash=d.get('dashSpeed')))
json.dump(rows,open('lockon.json','w'),indent=1)
for r in sorted(rows,key=lambda r:-r['sev']): print(r['sev'],r['world'][:4],r['name'].ljust(20),r['brain'].ljust(11),r['attack'].ljust(15),r['cls'],'tell %.2f->%.2f trk %.2f->%.2f warn %.2f->%.2f'%(r['tell'],r['tell2'],r['trk0'],r['trk2'],r['warn0'],r['warn2']))
