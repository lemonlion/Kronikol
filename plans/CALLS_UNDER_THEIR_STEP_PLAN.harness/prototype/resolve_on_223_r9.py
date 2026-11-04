"""Settles the one conflict of this plan's layer on #223's prototype at 10bbbe81: its R8 keeps a bypass off a frame, which the flow's reading has to do too.

Usage: resolve_on_223_r9.py <worktree>
"""
import pathlib
import re
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Tracking/StepCollector.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
block = re.compile(r"<<<<<<< ours\n.*?=======\n.*?>>>>>>> theirs\n", re.S)
assert len(block.findall(s)) == 1
s = block.sub("""            var bypassed = NearestOpenInFlow(state) ?? LastOpenInItsCaller(state);
            // Only CompleteFrame closes a frame: a second bypass of a step already gone must not take the one under it
            if (bypassed is null or { IsFrame: true })
                return null;
""", s)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("settled on 10bbbe81")
