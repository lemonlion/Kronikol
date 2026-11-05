"""What 5.29.4 changes in the wiki sections 5.29.3 wrote, which r1_docs.py was written before: a step left running, a call between steps with the bars hidden, a call with no step.

Usage: r1_wiki_sections.py <wiki worktree> <version>
"""
import pathlib
import sys

root, version = pathlib.Path(sys.argv[1]), sys.argv[2]


def edit(path, reps):
    p = root / path
    s = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in s
    s = s.replace("\r\n", "\n")
    for old, new in reps:
        assert s.count(old) == 1, (path, old[:80], s.count(old))
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


edit("Step-Tracking.md", [
    ("""A step method that is not awaited can still be running when its framework step ends. It keeps its place under that step and reads as passed with no duration until it finishes, and every later step keeps its own row. A call it makes later reads the step open by then, and two sub-steps that overlap can swap their end times and outcomes (#225).""",
     f"""A step method that is not awaited can still be running when its framework step ends. It keeps its place under that step, is listed with no result until it finishes, and every later step keeps its own row. A call it makes reads the step it is in, and two sub-steps that overlap keep their own end times and outcomes (since {version}, #225)."""),
    ("""- **A call names the step open when it was logged**, so it reads that step whether or not bars are drawn.""",
     """- **A call names the step its flow was in when it was logged**, so it reads that step whether or not bars are drawn."""),
    ("""A call made between steps reads the step before it, and with no bar to say a step with no calls ran, that can be an earlier step than with the bars drawn.""",
     f"""A call made between steps reads the step before it, with the bars hidden or drawn, and since {version} whether or not that step made a call."""),
])
edit("Generated-Reports.md", [
    ("""`null` before the first step, and in a step the report does not list (a BDDfy example table's later rows, a BDDfy tear down).""",
     f"""`null` before the first step, in a step the report does not list (a BDDfy example table's later rows, a BDDfy tear down), and from {version} for a record from a service's own thread while several steps ran that share none, which waits on no call of the test's."""),
])
print("applied")
