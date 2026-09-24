#!/usr/bin/env bash
# The second pass's battery (2026-09-24): every folder, HEAD and prototype, under the conditions the
# first pass held fixed. Run from this folder after gen.cs (out-head, out-stress-head, out-full) and
# patch.js (the -patched folders). Appends every summary to deep2-log.txt; the JSON lands in
# sweep2-results-*.json.
cd "$(dirname "$0")"
LOG=deep2-log.txt
FOLDERS="out-head out-head-patched out-stress-head out-stress-head-patched out-full out-full-patched"
say() { echo; echo "######## $*"; }
{
  say "1. classic scrollbars (chromium without --hide-scrollbars), reload"
  for d in $FOLDERS; do SCROLLBARS=1 node sweep2.js 20 $d; done
  say "2. no reload: narrowed from 1400 (a window snapped to half the screen)"
  for d in $FOLDERS; do MODE=shrink node sweep2.js 20 $d; done
  say "3. no reload: widened from 320 (a phone or small tablet rotated)"
  for d in $FOLDERS; do MODE=grow node sweep2.js 20 $d; done
  say "4. firefox, reload"
  for d in $FOLDERS; do ENGINE=firefox node sweep2.js 20 $d; done
  say "5. webkit, reload"
  for d in $FOLDERS; do ENGINE=webkit node sweep2.js 20 $d; done
  say "6. WCAG 1.4.12 text spacing on the prototype, chromium, reload"
  for d in out-head-patched out-stress-head-patched out-full-patched; do
    INJECT_CSS='*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}' TAG=$d-textspacing node sweep2.js 20 $d
  done
  say "7. the variant without #searchbar { min-width: 0 }, chromium and firefox"
  for d in out-head-patched-nosearch out-full-patched-nosearch; do node sweep2.js 20 $d; ENGINE=firefox node sweep2.js 20 $d; done
} 2>&1 | grep -v '^$' | tee -a $LOG
