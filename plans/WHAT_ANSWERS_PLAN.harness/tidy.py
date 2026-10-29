# Replaces a scratch directory's path, in either slash form, with <scratch> in a results file the harness keeps.
# usage: python tidy.py <results file> <scratch dir>
import sys

path, scratch = sys.argv[1], sys.argv[2]
backslash = chr(92)
text = open(path, encoding="utf-8").read()
for form in (scratch, scratch.replace("/", backslash), scratch.replace(backslash, "/")):
    text = text.replace(form, "<scratch>")
open(path, "w", encoding="utf-8").write(text)
