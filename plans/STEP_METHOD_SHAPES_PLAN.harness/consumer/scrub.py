# Takes this machine's work folders out of a build's output, so a result file reads the same on any machine.
import re
import sys

for line in sys.stdin:
    line = re.sub(r"[A-Za-z]:[\\/][^ ]*?[\\/]kronikol-226-consumer[\\/][^\\/ ]*[\\/][^\\/ ]*[\\/]", "", line)
    line = re.sub(r"\[[^\]]*\.csproj[^\]]*\]", "", line)
    sys.stdout.write(line.lstrip())
