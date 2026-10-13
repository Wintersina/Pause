import json
m=json.load(open('measure.json'))
HEAVY={'hauler','bastion','siege','tug','herder','tender','ironclad','logger','abyss','netter','pearler'}
FLOOR,HFLOOR=1.5,1.65
out={'elites':{},'enemies':{}}
for k,e in m['elites'].items():
    fl=HFLOOR if e['brain'] in HEAVY else FLOOR
    kk=max(1.0,fl/e['long_u'])
    kk=round(kk,2) if kk>1.0 else 1.0
    out['elites'][k]=dict(k=kk,floor=fl,heavy=e['brain'] in HEAVY)
for k,e in m['enemies'].items():
    out['enemies'][k]=dict(k=1.2)
json.dump(out,open('tiers.json','w'),indent=1)
for k,v in out['elites'].items(): print(k,v['k'],round(m['elites'][k]['long_u']*v['k'],2))
