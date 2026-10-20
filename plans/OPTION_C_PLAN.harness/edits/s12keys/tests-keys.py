# S12 keyboard links' tests: IflowLinkKeyboardTests (new, Playwright), copied from beside this script. Nothing existing
# changes. usage: python tests-keys.py <worktree root>
import os
import shutil
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
HERE = os.path.dirname(os.path.abspath(__file__))
target = ROOT + 'tests/Kronikol.Tests.EndToEnd/IflowLinkKeyboardTests.cs'
assert not os.path.exists(target), 'the keyboard links tests are in already'
assert os.path.isdir(os.path.dirname(target)), 'no such folder: is this a worktree root?'
shutil.copyfile(os.path.join(HERE, 'IflowLinkKeyboardTests.cs'), target)
print('ok: tests/Kronikol.Tests.EndToEnd/IflowLinkKeyboardTests.cs')
