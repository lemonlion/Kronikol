#!/usr/bin/env bash
# Keeps my patches as one diff, clears my own bin and obj, and removes only my own versions from the shared NuGet cache.
cd /c/Code/k224-review1 || exit 1
rm -rf orig; mkdir orig
git -C /c/Code/Kronikol-224rv archive --format=tar cd7412d9 -- src | tar -x -C orig
diff -ru --strip-trailing-cr --exclude=bin --exclude=obj orig/src wt/src > review-patches.diff
grep -c '^[-+][^-+]' review-patches.diff
grep -E '^(---|\+\+\+) ' review-patches.diff | grep '+++' | cut -c1-120
rm -rf orig wt/tests
find wt probes -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null
find wt probes -type d \( -name bin -o -name obj \) | wc -l
for d in "$USERPROFILE"/.nuget/packages/*/5.28.3-k60rv*; do
  [ -d "$d" ] && rm -rf "$d"
done
ls -d "$USERPROFILE"/.nuget/packages/*/5.28.3-k60rv* 2>/dev/null | wc -l
ls -la feed | awk '{s+=$5} END {print "feed bytes:", s}'
df -B1G /c | tail -1
echo FINISH_DONE
