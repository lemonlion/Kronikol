#!/usr/bin/env bash
# The conditions battery (deep2-final.sh's conditions) on the 3.29.3 build's nine shapes.
cd "$(dirname "$0")"
TS='*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}'
VERDANA='body,button,input,select{font-family:Verdana}'
TAG=audit-chromium-reload-10 node sweep2.js 10 out-audit
SCROLLBARS=1 TAG=audit-sb node sweep2.js 20 out-audit
MODE=shrink TAG=audit-shrink node sweep2.js 20 out-audit
MODE=grow TAG=audit-grow node sweep2.js 20 out-audit
SCROLLBARS=1 MODE=shrink TAG=audit-sb-shrink node sweep2.js 20 out-audit
ENGINE=firefox TAG=audit-firefox node sweep2.js 20 out-audit
ENGINE=webkit TAG=audit-webkit node sweep2.js 20 out-audit
INJECT_CSS="$TS" TAG=audit-textspacing node sweep2.js 20 out-audit
SCROLLBARS=1 INJECT_CSS="$TS" TAG=audit-textspacing-sb node sweep2.js 20 out-audit
INJECT_CSS="$VERDANA" TAG=audit-verdana node sweep2.js 20 out-audit
SCROLLBARS=1 INJECT_CSS="$VERDANA" TAG=audit-verdana-sb node sweep2.js 20 out-audit
