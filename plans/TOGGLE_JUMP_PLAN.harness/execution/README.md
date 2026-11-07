# TOGGLE_JUMP_PLAN execution (#227, released as 5.34.2)

What the build ran, kept for the numbers in the plan's section 9 and the mutation table of section 6.3.

- `logs/test-red-final.log`, `logs/test-green-final.log`: the plan's facts on 5.32.1's scripts and on the first fix.
- `logs/test-red-v3.log`, `logs/test-green-v3.log`: the same once T18 and Direction were added.
- `logs/test-red-review.log`, `logs/test-red-review2.log`: the independent review's facts on the first fix (the second
  run after three facts were made to reach their fault); `logs/test-green-v4.log` to `-v6.log`: the second fix as it
  was corrected (v4 and v5 left T18's deep case 449 px off; `t18-probe.js` showed why); `logs/t18-v6.log`.
- `logs/mutations.log` (the first fix), `logs/mutations3.log` and `logs/mutations4.log` (the release; the disk was
  full during `mutations3.log`'s last eight, run again in `mutations4.log`): `mutate227.py`, checked by
  `check-mutations.py`.
- `logs/guards*.log`, `logs/linedup-fix-*.log`, `logs/linedup-orig-*.log`: section 6.2's guards, and
  `LinedUpPartsTests` on the fix and on 5.34.0's scripts (runs 1 to 5 of the fix under another session's load, 6 to 8
  on a quiet machine).
- `logs/e2e-whole.log`, `logs/core-suite.log`, `logs/test-release-5342.log`: the whole end-to-end suite, the core
  suite (before its three script-text facts were moved), and the class again on 5.34.1.
- `kit227.js` is the facts' page kit as a file, for `t18-probe.js`, which also needs a line the release does not
  carry (`window.__kpPlace = place;` in `takeReadingPlace`). `words-probe.js` showed that a label's words are one text.
- `red-green-review2.sh`, `guards-227.sh`, `final-runs-227.sh`: the runs, as scripts.
