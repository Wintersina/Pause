import os
p = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'j1d.md')
s = open(p).read()
a = s.index(' - the whole hull, nose to tail')
b = s.index('It must clearly be a wrecked version')
new = ''' - A FEW BOLD, READABLE FEATURES, not an all-over texture: AT LEAST HALF of the hull stays clean intact plating. Exactly these: (1) the CANOPY crystal clearly cracked with bone-white crack lines and one dark shattered facet; (2) one large BLOWN PANEL on the LEFT half of the hull (dark cavity, exposed ribs and cables, orange glowing rim, a few sparks); (3) one large BLOWN PANEL on the RIGHT half (a different shape), also with exposed machinery; (4) the nose cap torn open and the nose gem chipped; (5) one big burn scar (hard-stepped char with rust edge) spreading from each blown panel, never covering the whole hull; (6) the nozzle smashed and smoking, a few embers and cyan/amber sparks near the holes, both tail fins with a ragged bite and a smashed fin light.
 - Keep the damage compact and in these places so it can be cut out as zones: left blown panel around the left-centre of the hull (about x 360-470, y 330-560 of 1024), right blown panel on the right-centre (about x 560-690, y 480-700), canopy and nose on the centre line, nozzle at the bottom centre.
'''
s = s[:a] + new + s[b:]
s = s.replace('40-55% of the hull surface visibly destroyed', 'wreck')
s = s.replace('(> 40%)', '(> 18%)').replace('(> 25%)', '(> 8%)').replace('(each > 35%)', '(each > 8%)')
s = s.replace('the CURRENT wreck draft: painted style is right but it is FAR TOO MILD, it does not read as a wreck.',
              'the current wreck draft: painted style is right but too mild, it does not read as a wreck (no cracked canopy, damage only left).')
open(p, 'w').write(s)
