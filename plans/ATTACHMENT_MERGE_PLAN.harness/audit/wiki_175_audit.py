"""The wiki edits of the #175 audit (plans/ATTACHMENT_MERGE_PLAN.md section 10). Usage: python -I wiki_175_audit.py <wiki dir> <version>.

Each edit is an exact, single-occurrence replacement; the pages are LF (git ls-files --eol), read and written as bytes.
"""
import sys

WIKI, VER = sys.argv[1], sys.argv[2]

EDITS = {
    "Integration-Cucumber-Messages.md": [
        ("| hook test steps | dropped, or sub-steps named by the hook with `--include-hooks` |",
         "| hook test steps | dropped, or steps named by the hook with `--include-hooks`, in the order they ran |"),
        ("| `--include-hooks` | off | Keep hook steps (`BeforeEach hook`, …) as steps in the report. |",
         "| `--include-hooks` | off | Keep hook steps (`BeforeEach hook`, …) as steps in the report, in the order they ran: a hook "
         f"before the first step comes before the steps, the others after them. Before {VER} every kept hook came after the "
         "steps, so the step markers no longer matched the steps one for one and the calls made in a step lost their step. |"),
        ("`CucumberSynthesisOptions.AttachmentsDirectory` keeps its folder and its files.",
         "`CucumberSynthesisOptions.AttachmentsDirectory` keeps its folder and its files, but for a decoded copy that "
         "`CucumberFeatureMerger.Merge` replaced with the reporter's own file, which it deletes once no entry links it."),
        ("Each attachment\nof the tests file is paired, one to one and in order, with a copy of the same name anywhere the messages put one:",
         "Each attachment\nof the tests file, the scenario's and, by its `step` index, a step's, is paired, one to one and in order, with a "
         "copy of the same name anywhere the messages put one:"),
        ("step's row. It links the reporter's file when that exists, and the messages' copy when it does not (Playwright\n"
         "empties `test-results/` on its next run), with the media type of the file it links.",
         "step's row. The reporter's files that can be read pair first; one that cannot (Playwright empties `test-results/` on\n"
         "its next run, or another process holds the file) then pairs by its name with a copy that can be read. The pair links\n"
         "the reporter's file when that can be read and the messages' copy when it cannot, with the media type of the file it\n"
         "links, and a copy the messages name by a `url` whose file is gone pairs with the reporter's file by its name."),
        ("attached inside a step were listed and stored twice.",
         "attached inside a step were listed and stored twice. Before " + VER + " a tests record with a `step` index was dropped "
         "for a scenario the messages own, a reporter's file that was gone could take the copy another file's bytes matched, "
         "and one held by another process was listed beside the copy."),
    ],
    "Generated-Reports.md": [
        ("in the order the steps are listed (background steps, steps, kept hook steps, sub-steps)",
         "in step-tree order (the background steps, then the steps, each followed by its sub-steps, a kept hook step where it ran)"),
    ],
    "Querying-Reports.md": [
        ("`file:line`, the calls that happened inside that step, what that step attached (under it, from 4.24.0: the\n"
         "screenshot of a failure is usually there), and the scenario's own attachments. Usually the entire answer.",
         "`file:line`, the calls that happened inside that step, what that step attached (under it, from 4.24.0: the\n"
         f"first six, then a line naming `steps` for the rest, and from {VER} a link printed as it is), and the scenario's own\n"
         "first four attachments. A screenshot a hook took (Playwright's own on failure, a fixture's) is one of the scenario's,\n"
         "or with `--include-hooks` on its hook step, where `Failures.md` and `steps` list it. Usually the entire answer."),
    ],
    "Merging-Parallel-Reports.md": [
        ("bypass reasons, substeps and attachments are all carried through the merge.",
         "bypass reasons, substeps and attachments are all carried through the merge. Since " + VER + " so are the attachment "
         "files: each shard's are copied into `attachments/` beside the merged report and linked there (a name two shards "
         "use is numbered, and a file with the same bytes is copied once). Before, the merged report kept each shard's own "
         "relative links, which named files that were not beside it."),
    ],
}

for page, edits in EDITS.items():
    path = f"{WIKI}/{page}"
    text = open(path, "rb").read().decode("utf-8")
    for old, new in edits:
        count = text.count(old)
        if count != 1:
            sys.exit(f"{page}: {count} occurrences of {old[:70]!r}")
        text = text.replace(old, new)
    open(path, "wb").write(text.encode("utf-8"))
    print(f"{page}: {len(edits)} edit(s)")
