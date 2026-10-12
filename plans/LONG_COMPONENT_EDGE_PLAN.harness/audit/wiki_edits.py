# The audit of LONG_COMPONENT_EDGE_PLAN (plan §11, 2026-10-12): its wiki edits, applied to an LF checkout of the wiki
# once the release number was known:   python wiki_edits.py <version> [wiki checkout, default ../Kronikol.wiki]
import sys, pathlib

VER = sys.argv[1]
ROOT = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else pathlib.Path(__file__).resolve().parents[4] / 'Kronikol.wiki'


def edit(page, pairs):
    p = ROOT / page
    t = p.read_bytes().decode('utf-8')
    assert '\r\n' not in t, page
    for old, new in pairs:
        n = t.count(old)
        assert n == 1, (page, old[:90], n)
        t = t.replace(old, new.replace('__VER__', VER))
    p.write_bytes(t.encode('utf-8'))
    print('edited', page)


edit('Large-Response-and-Diagram-Handling.md', [
    # An over-long opener draws Syntax Error? (measured 2026-10-12 in node at the default stack and at 400 KB, and on
    # 2026-10-10 in the worker); only the coloured bar produces no SVG.
    ("- an over-long **coloured note bar**, and since the 1.2026.8beta1 build an over-long **block opener** too, overflows "
     "the engine's own JavaScript stack (`RangeError: Maximum call stack size exceeded`) and produces no SVG at all (on the "
     "1.2026.6 build block openers were a parse limit like messages). The same stack runs out on a long **component diagram "
     "edge label**, **participant name**, **activity action** or **swimlane**: the engine draws its `RangeError` picture, or "
     "`Syntax Error?` where the overflow happens while it chooses a command, in place of the whole diagram (capped since 4.14.5).",
     "- an over-long **coloured note bar** overflows the engine's own JavaScript stack (`RangeError: Maximum call stack size "
     "exceeded`) and produces no SVG at all, only the error's text. The same stack runs out, since the 1.2026.8beta1 build, on "
     "a long **block opener** (on the 1.2026.6 build openers were a parse limit like messages), and on a long **component "
     "diagram edge label**, **participant name**, **activity action** or **swimlane**: the engine draws its `RangeError` "
     "picture, or `Syntax Error?` where the overflow happens while it chooses a command (an opener, a name or an action), in "
     "place of the whole diagram (capped since 4.14.5)."),
    # The link row said the main thread draws every length; on a component edge the whole label binds first (row below).
    ("from 980 characters in a worker whose JIT has not warmed up and from 475 with V8's optimizing compilers off; the "
     "page's main thread, node, Firefox and WebKit draw every length measured | `MaxLinkedLabelChars` = 350 |",
     "from 980 characters in a worker whose JIT has not warmed up and from 475 with V8's optimizing compilers off; the "
     "page's main thread, node, Firefox and WebKit draw every length measured. On a component edge the engine walks the "
     "whole label, link or not, so the edge's own cap, in the next row, binds first | `MaxLinkedLabelChars` = 350 |"),
    ("\nA truncated statement ends with `…`.\n",
     "\nA truncated statement ends with `…`. A cut, and a name or label broken onto display lines, never falls between the "
     "two halves of a character written as two UTF-16 code units, such as an emoji, which would draw as `�` (until __VER__ "
     "they could).\n"),
])

edit('PlantUML-Browser-Rendering.md', [
    ("The internal-flow popup does the same.",
     "The internal-flow popup does the same, and since __VER__ it also names an over-long statement under the engine's "
     "`Syntax Error?` picture, as the report's own diagrams do: an activity action past its length fails that way."),
])

edit('Merging-Parallel-Reports.md', [
    ("diagnostics section lists the shards' diagnostics (`MergeableReport.Diagnostics`), which it dropped\nbefore 4.6.0.\n",
     "diagnostics section lists the shards' diagnostics (`MergeableReport.Diagnostics`), which it dropped\nbefore 4.6.0. "
     "Since __VER__ its component diagram takes `DependencyColors` from the options when `ComponentDiagramOptions` names none, "
     "as a run's does; it drew the default palette before.\n"),
])

edit('Component-Diagrams.md', [
    ("A longer method list keeps its first entries, in order, ends `…, +N more`,",
     "A longer method list keeps its first entries in ordinal order (since __VER__; before it, in the order of the "
     "machine's culture, so two machines could keep different entries), ends `…, +N more`,"),
    ("A service name is cut at 200 characters, and two names that share their first 200 keep a component each.",
     "A service name is cut at 200 characters, and two names that share their first 200 keep a component each; so, since "
     "__VER__, do two names written in a script other than Latin, whose letters an alias cannot keep."),
])

edit('Large-Response-and-Diagram-Handling.md', [
    ("so two services whose names share their first 200 characters keep a participant each.",
     "so two services whose names share their first 200 characters keep a participant each. Since __VER__ a name holding "
     "any character outside ASCII takes the hash too: an alias keeps only ASCII letters, digits and underscores, so two "
     "names of one length written in another script came out as the same run of underscores and drew as one participant."),
])
