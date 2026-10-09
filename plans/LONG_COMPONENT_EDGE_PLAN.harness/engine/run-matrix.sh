#!/usr/bin/env bash
# Follow-up matrix: edges of loop / colourbar / kseqlink / plain on the unobfuscated master build, per fix variant,
# at (A) node's default stack with the JIT on and (B) --stack-size=400 with the optimizing compilers off.
cd "$(dirname "$0")"
N=/c/Code/Kronikol/tools/render-bench/core-oct-master-noobf.js
SHAPES=loop,colourbar,kseqlink,plain
export STDLIB_DIR=/c/Code/plantuml/src/main/resources/teavm
run() { # tag flags envs...
  local tag=$1 flags=$2; shift 2
  env "$@" NODEFLAGS="$flags" CONC=${CONC:-6} REPS=${REPS:-1} node bisect.js $N "$tag" $SHAPES 60 30000 > /dev/null 2>&1
}
# B: 400 KB, optimizers off (deterministic): two variants at a time
run fu-B-base "--stack-size=400 --no-opt --no-maglev" X=1 &
run fu-B-labels "--stack-size=400 --no-opt --no-maglev" PATCH_LABELS=1 &
wait
run fu-B-teavm "--stack-size=400 --no-opt --no-maglev" PATCH_TEAVM=1 &
run fu-B-teavmlazy "--stack-size=400 --no-opt --no-maglev" PATCH_TEAVM=1 PATCH_TEAVM_LAZY=1 &
wait
# A: default stack, JIT on: one variant at a time, two renders per verdict
export REPS=2 CONC=6
run fu-A-base "" X=1
run fu-A-labels "" PATCH_LABELS=1
run fu-A-teavm "" PATCH_TEAVM=1
run fu-A-teavmlazy "" PATCH_TEAVM=1 PATCH_TEAVM_LAZY=1
echo done
