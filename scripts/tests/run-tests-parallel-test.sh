#!/bin/bash
# Tests for the pure parts of scripts/run-tests-parallel.sh (suite list parsing,
# duration balancing, log parsing, --plan). No Unity is started.
#
#   scripts/tests/run-tests-parallel-test.sh        (a few seconds; exit 0 = all passed)

set -u
HERE="$(cd "$(dirname "$0")" && pwd -P)"
SCRIPT="$HERE/../run-tests-parallel.sh"
T="$(mktemp -d /tmp/pause-parallel-test.XXXXXX)"
trap 'rm -rf "${T:?}"' EXIT
PASS=0; FAIL=0
ok() { PASS=$((PASS + 1)); echo "  ok   $1"; }
bad() { FAIL=$((FAIL + 1)); echo "  FAIL $1"; }
check() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (got '$2', want '$3')"; fi; }

PT_SOURCE_ONLY=1 source "$SCRIPT"

# ---- list_suites
cat > "$T/AllTests.cs" <<'CS'
    static readonly (string name, Func<int> execute)[] Suites =
    {
        ("AlphaTest", AlphaTest.Execute),
        // ("CommentedTest", CommentedTest.Execute),
        ("BetaSliceTest", BetaTest.ExecuteSlice1),
        ("GammaTest", GammaTest.Execute),
    };
CS
check "list_suites reads the table, skips comments, accepts Class.Method" "$(list_suites "$T/AllTests.cs" | paste -sd, -)" "AlphaTest,BetaSliceTest,GammaTest"
real="$(list_suites "$HERE/../../Pause/Assets/Editor/Tests/AllTests.cs" | wc -l | tr -d ' ')"
[ "$real" -gt 100 ] && ok "the real AllTests.cs lists $real suites" || bad "the real AllTests.cs lists $real suites"

# ---- balance
printf 'A 100\nB 60\nC 50\nD 40\nE 30\nF 20\n' > "$T/d1"
out="$(balance 2 < "$T/d1")"
load() { echo "$out" | awk -v s="$1" '$1 == s { t += $3 } END { print t + 0 }'; }
check "2 shards: longest-first loads are 160 / 140 (optimal 150; LPT)" "$(load 1)/$(load 2)" "160/140"
check "every suite is assigned exactly once" "$(echo "$out" | awk '{print $2}' | sort | paste -sd, -)" "A,B,C,D,E,F"
out="$(balance 3 < "$T/d1")"
check "3 shards: loads 100 / 110 / 90" "$(load 1)/$(load 2)/$(load 3)" "100/110/90"
printf 'Big 300\nS1 1\nS2 1\nS3 1\n' > "$T/d2"
out="$(balance 2 < "$T/d2")"
check "a dominant suite gets a shard to itself" "$(load 1)/$(load 2)" "300/3"
printf 'A 10\nB -\nC 30\nD 20\n' > "$T/d3"
out="$(balance 1 < "$T/d3")"
check "unknown duration gets the median (20)" "$(echo "$out" | awk '$2 == "B" {print $3}')" "20.0"
printf 'A -\nB -\nC -\nD -\n' > "$T/d4"
out="$(balance 2 < "$T/d4")"
check "all unknown: an equal split" "$(load 1)/$(load 2)" "2/2"
check "deterministic (same input, same plan)" "$(balance 2 < "$T/d1")" "$(balance 2 < "$T/d1")"
check "one shard takes everything" "$(balance 1 < "$T/d1" | awk '$1 != 1' | wc -l | tr -d ' ')" "0"

# ---- parse_log
cat > "$T/shard.log" <<'LOG'
[ALL] RunSuites: 3 suites; editor startup took 7.6s
[X] PASS  fine
[ALL] OneTest: PASS (1.5s, 1200 MB)
[SHOP] FAIL  the first thing broke
[SHOP] FAIL  the second thing broke
[ALL] TwoTest: FAIL (2) (3.2s, 1300 MB)
[ALL] ThrowTest threw: System.Exception
[ALL] ThreeTest: FAIL (1) (0.4s, 1300 MB)
[ALL] 3 suites, 3 failures, tests 5.0s (startup 7.6s)
[ALL] RESULT: FAIL failures=3 failed=TwoTest,ThreeTest
LOG
res="$(parse_log < "$T/shard.log")"
check "result lines: names, status, failure count, seconds" "$(echo "$res" | awk '$1 == "R" { printf "%s:%s:%s:%s;", $2, $3, $4, $5 }')" "OneTest:PASS:0:1.5;TwoTest:FAIL:2:3.2;ThreeTest:FAIL:1:0.4;"
check "failing check lines attach to the suite that logged them" "$(echo "$res" | awk '$1 == "C" && $2 == "TwoTest"' | wc -l | tr -d ' ')" "2"
check "a thrown suite's line attaches to the next result line" "$(echo "$res" | awk '$1 == "C" && $2 == "ThreeTest"' | wc -l | tr -d ' ')" "1"
check "the summary lines are not taken for suites" "$(echo "$res" | awk '$1 == "R"' | wc -l | tr -d ' ')" "3"

# ---- --plan end to end on the fixture
printf 'AlphaTest 50\nBetaSliceTest 30\nGammaTest 20\n' > "$T/dur.txt"
plan="$(PT_ALLTESTS="$T/AllTests.cs" PT_DURATIONS="$T/dur.txt" UNITY_BATCH_LOGDIR="$T/nologs" "$SCRIPT" --plan -j 2)"
check "--plan: shard 1 = Alpha, shard 2 = Beta + Gamma" "$(echo "$plan" | sed -E 's/, ~[0-9]+ s//' | paste -sd'|' -)" "shard 1: 1 suites: AlphaTest|shard 2: 2 suites: BetaSliceTest,GammaTest"
plan="$(PT_ALLTESTS="$T/AllTests.cs" PT_DURATIONS="$T/dur.txt" UNITY_BATCH_LOGDIR="$T/nologs" "$SCRIPT" --plan -j 2 --suites gamma,AlphaTest)"
check "--plan --suites: names match with or without the Test suffix" "$(echo "$plan" | sed -E 's/, ~[0-9]+ s//' | paste -sd'|' -)" "shard 1: 1 suites: AlphaTest|shard 2: 1 suites: GammaTest"
PT_ALLTESTS="$T/AllTests.cs" PT_DURATIONS="$T/dur.txt" UNITY_BATCH_LOGDIR="$T/nologs" "$SCRIPT" --plan --suites Nope >/dev/null 2>&1
check "--plan: an unknown suite is an error (exit 2)" "$?" "2"

echo "$PASS passed, $FAIL failed"
[ "$FAIL" = 0 ]
