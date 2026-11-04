#!/usr/bin/env bash
# The second cut 10 session: priority order from the coordinator, my own combined mutants after their parts.
cd "$(dirname "$0")"
PY=/c/Python314/python.exe
$PY mutrun.py clean-control
echo "=== P1"
$PY mutrun.py more n08a main/mut-extra
echo "=== P2"
$PY mutrun.py run n14
echo "=== P3"
$PY mutrun.py run n13 nm31b nm16b nm20 nm27 nm15c nm15d nm15e n15cde nm32
$PY mutrun.py more n12 main/mut-extra review rev4 rev5 old
$PY mutrun.py run n15ce n12cde
echo "RUN11_EXIT $?"
