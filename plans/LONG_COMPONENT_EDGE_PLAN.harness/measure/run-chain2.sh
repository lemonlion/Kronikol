#!/bin/sh
# Everything after phase F and A, one browser at a time.
cd "$(dirname "$0")"
sh run-cdp.sh > logs/run-G-cdp.txt 2>&1
echo "cdp done $?"
node run-matrix.js B C > logs/run-B-C.txt 2>&1
echo "B C done $?"
node run-matrix.js D > logs/run-D.txt 2>&1
echo "D done $?"
node run-matrix.js E A2 > logs/run-E-A2.txt 2>&1
echo "E A2 done $?"
node run-matrix.js E2 > logs/run-E2.txt 2>&1
echo "E2 done $?"
echo "chain2 finished"
