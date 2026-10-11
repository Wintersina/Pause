#!/bin/bash
# build.sh <job> -> prints the full prompt (common preamble + job file)
d="$(cd "$(dirname "$0")" && pwd)"; cat "$d/_common.md"; echo; cat "$d/$1.md"
