#!/bin/bash
# Wire a staged world: Art/Boulders~/<W>/{boulder.png,break.png,manifest.json} -> Art/Resources/Boulders/<W>/ and enable it in CoverBoulderTable.
set -e
W=$1
S="Pause/Assets/Art/Boulders~/$W"; D="Pause/Assets/Art/Resources/Boulders/$W"
mkdir -p "$D"
for f in boulder.png break.png manifest.json; do cp "$S/$f" "$D/$f"; done
python3 - "$W" <<'P'
import sys,re
w=sys.argv[1]; p="Pause/Assets/Scripts/Gameplay/Cover/CoverBoulderTable.cs"
s=open(p).read()
s=s.replace('world = "%s", folder = "Boulders/%s", enabled = false'%(w,w),'world = "%s", folder = "Boulders/%s", enabled = true'%(w,w))
open(p,"w").write(s)
P
