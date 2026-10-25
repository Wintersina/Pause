#!/bin/bash
C=/private/tmp/claude-501/-Users-sina-Developer-Pause/78449f62-3aee-4de3-b492-ffaa027892ab/scratchpad/codex
for n in rift_lancer eventide_bastion orbit_reaver singularity_hauler; do
  [ -f "$C/out/${n}_left.png" ] && continue
  { cat "$C/brief.md"; echo "Attached reference: in/$n.png. Save the result as exactly: out/${n}_left.png"; } > "$C/brief_$n.md"
  codex exec -m gpt-5.6-luna --skip-git-repo-check -s workspace-write -C "$C" -i "$C/in/$n.png" -o "$C/${n}_last.txt" - < "$C/brief_$n.md" > "$C/$n.log" 2>&1
done
echo done > "$C/done"
