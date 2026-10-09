#!/bin/bash
# world_audit.sh -- list every place in the project that is keyed to a world.
#
#   .claude/skills/add-world/scripts/world_audit.sh [REF_WORLD] [NEW_WORLD]
#
# REF_WORLD (default Ember) is a finished world whose footprint you are
# copying; NEW_WORLD (optional) is the world being added -- with it the report
# also lists which of the reference's files do NOT mention the new world yet
# (that is your remaining-work list). Run it from any worktree of the repo
# (it finds the Pause/ project from its own location, or from $PWD).
#
# Sections:
#   1  code + test files that mention the reference world
#   2  the hard-coded "this many worlds" spots (arrays of four, == 3, Length == 4 ...)
#   3  art folders that carry the reference world's name
#   4  Unity-ignored staging folders (~) for the world
#   5  with NEW_WORLD: files that mention REF but not NEW (to do)
set -u
REF="${1:-Ember}"
NEW="${2:-}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../../../.." && pwd)"
[ -d "$root/Pause/Assets" ] || root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
A="$root/Pause/Assets"
ref_lc="$(echo "$REF" | tr '[:upper:]' '[:lower:]')"

echo "== 1. code/test files mentioning '$REF' (case-insensitive) =="
grep -rliE "$REF" --include='*.cs' "$A/Scripts" "$A/Editor" 2>/dev/null | sed "s#$A/##" | sort

echo
echo "== 2. hard-coded world-count / world-list spots (each must grow with the new world) =="
grep -rnE '"Space", *"Frost", *"Verdant", *"Ember"|"space", *"frost", *"verdant", *"ember"|Frost, Verdant, Ember|new\[\] *\{ *"Frost", *"Verdant", *"Ember"|Worlds\.Length *== *[0-9]|PrefsHighestWorld[^;]*== *[0-9]|(Rows|Worlds) *= *4\b|MaxSections *= *5|four worlds|\bfour-world |[Ww]orld[A-Za-z]* *== *3\b|CurrentIndex *== *3|SelectWorld\(3\)|KeyCode\.F4|HighestWorld\)? *== *3|Worlds\[3\]|All\[3\]|\bEmber *= *3|WorldIds *=|WorldKeys *=|PilotLoadAt(Low|High)Speed *=|Accents *=|Rows *= *4' \
    --include='*.cs' "$A/Scripts" "$A/Editor" 2>/dev/null | sed "s#$A/##" | cut -c1-190 | sort

echo
echo "== 3. art folders / files named '$REF' (runtime + staging) =="
find "$A/Art" "$A/Audio" -iname "*$REF*" -not -name '*.meta' 2>/dev/null | sed "s#$A/##" | sort | head -80

echo
echo "== 4. Unity-ignored staging folders (trailing ~) =="
find "$A/Art" "$A/Audio" -type d -name '*~' 2>/dev/null | sed "s#$A/##" | sort | grep -i "$REF" || echo "(none for $REF)"

if [ -n "$NEW" ]; then
  echo
  echo "== 5. files that mention $REF but not $NEW yet =="
  for f in $(grep -rliE "$REF" --include='*.cs' "$A/Scripts" "$A/Editor" 2>/dev/null | sort); do
    if ! grep -qiE "$NEW" "$f"; then echo "TODO  ${f#$A/}"; fi
  done
fi
