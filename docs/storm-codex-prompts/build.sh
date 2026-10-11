#!/bin/bash
# build.sh <job> -> prints the full prompt (common preamble + job file; sounds.md stands alone)
d="$(cd "$(dirname "$0")" && pwd)"
if [ "$1" = sounds ]; then cat "$d/sounds.md"; else cat "$d/_common.md"; echo; cat "$d/$1.md"; fi
