#!/usr/bin/env bash
# Packs r1 and r1-r2 from the one worktree, then puts it back at r1-r2-r3.
R="C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/31780689-26c4-4084-b1b5-c4efb9a6fd42/scratchpad/review4"
cd "$R/wt" || exit 1
for stage in r1 r1-r2; do
  git -c core.autocrlf=false -c core.eol=lf checkout -- src
  git -c core.autocrlf=false -c core.eol=lf apply "$R/patches/$stage.patch" && echo "applied $stage"
  suffix=rev4$(echo $stage | tr -d '-')
  "$R/probes/pack-rev4.sh" "$R/wt" "$suffix"
done
git -c core.autocrlf=false -c core.eol=lf checkout -- src
git -c core.autocrlf=false -c core.eol=lf apply "$R/patches/r1-r2-r3.patch" && echo "applied full"
git status --short | wc -l
echo DONE-STAGES
