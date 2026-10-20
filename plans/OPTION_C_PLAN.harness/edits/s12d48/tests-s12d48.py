# S12 D48's tests: two new classes, copied from this folder. Nothing existing changes: no test presses Tab today, and
# the focus a script gives, which the existing tests use, is left where it lands.
#   tests/Kronikol.Tests/Reports/FocusScriptTests.cs              (core, new)
#   tests/Kronikol.Tests.EndToEnd/FocusClearOfTheBarTests.cs      (Playwright, new)
# usage: python tests-s12d48.py <worktree root>
import os
import shutil
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
HERE = os.path.dirname(os.path.abspath(__file__))

copies = {
    'FocusScriptTests.cs': 'tests/Kronikol.Tests/Reports/FocusScriptTests.cs',
    'FocusClearOfTheBarTests.cs': 'tests/Kronikol.Tests.EndToEnd/FocusClearOfTheBarTests.cs',
}
for source, target in copies.items():
    assert os.path.isfile(os.path.join(HERE, source)), ('not beside this script', source)
    assert not os.path.exists(ROOT + target), ('already there: D48 tests are in already', target)
    assert os.path.isdir(os.path.dirname(ROOT + target)), ('no such folder: is this a worktree root?', target)

for source, target in copies.items():
    shutil.copyfile(os.path.join(HERE, source), ROOT + target)
print('ok:', ', '.join(copies.values()))
