#!/usr/bin/env bash
# The second pass's acceptance battery for the design the plan now specifies (breakpoint 1160,
# lower bound 768.02px, no #searchbar rule, the .toolbar-left wrap), on every shape: the nine
# synthetic pages in out-final and the three published reports in out-published-final. Run from this
# folder after:
#   for src in out-head out-stress-head out-full; do BAND_MIN=768.02 NO_SEARCH_MIN=1 CI_WRAP=1 node patch.js 1160 out-final $src; done
#   BAND_MIN=768.02 NO_SEARCH_MIN=1 CI_WRAP=1 node patch.js 1160 out-published-final out-published
# Summaries: node summ2.js final
cd "$(dirname "$0")"
TS='*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}'
VERDANA='body,button,input,select{font-family:Verdana}'
TAG=out-final-chromium-reload-10 node sweep2.js 10 out-final
SCROLLBARS=1 node sweep2.js 20 out-final
MODE=shrink node sweep2.js 20 out-final
MODE=grow node sweep2.js 20 out-final
ENGINE=firefox node sweep2.js 20 out-final
ENGINE=webkit node sweep2.js 20 out-final
INJECT_CSS="$TS" TAG=out-final-textspacing node sweep2.js 20 out-final
SCROLLBARS=1 INJECT_CSS="$TS" TAG=out-final-textspacing-sb node sweep2.js 20 out-final
INJECT_CSS="$VERDANA" TAG=out-final-verdana node sweep2.js 20 out-final
node sweep2.js 20 out-published-final
SCROLLBARS=1 node sweep2.js 20 out-published-final
