"""R1's wiki edits (plans/ATTACHMENT_MERGE_PLAN.md section 7), applied after publication to a wiki tree.

usage: python wiki_175.py <wiki tree> [--dry-run]
Each edit replaces text that must occur exactly once; the pages keep their own line endings.
"""
import os
import sys

tree = sys.argv[1]
dry = '--dry-run' in sys.argv

EDITS = {
    'Integration-Cucumber-Messages.md': [
        ("- the tests file still contributes what only it knows: `assertion` events (nested under the Gherkin step whose\n"
         "  time window contains them — the ✓/✗ rows and the notes in the diagram), UI actions, attachments the reporter\n"
         "  recorded, the identity, and a failure message when no Gherkin step failed (a runner-level timeout);\n",
         "- the tests file still contributes what only it knows: `assertion` events (nested under the Gherkin step whose\n"
         "  time window contains them — the ✓/✗ rows and the notes in the diagram), UI actions, the attachments the\n"
         "  reporter recorded (each joined with the messages' copy of the same artefact, so it is listed once: see\n"
         "  [Attachments](#attachments)), the identity, and a failure message when no Gherkin step failed (a runner-level\n"
         "  timeout);\n"),
        ("copied the same way. The display name gains an extension derived from the media type when it has none\n"
         "(`end-screenshot` → `end-screenshot.png`), because that extension is what makes the report render a screenshot\n"
         "inline instead of as a link. Plain-text bodies are not written out by default\n"
         "(`CucumberSynthesisOptions.WriteTextAttachments`); the `kronikol-test-id` and `kronikol-bypass` attachments\n"
         "never appear in the report, whatever that option says.\n",
         "copied the same way. `kronikol ingest` deletes its staging folder once the report is written (4.24.0), unless the\n"
         "report still links into it because a file could not be copied; a library caller that sets\n"
         "`CucumberSynthesisOptions.AttachmentsDirectory` keeps its folder and its files. The display name gains an\n"
         "extension derived from the media type when it has none (`end-screenshot` → `end-screenshot.png`). It is a label\n"
         "only: the media type decides whether the report draws an attachment inline. Plain-text bodies are not written\n"
         "out by default (`CucumberSynthesisOptions.WriteTextAttachments`); the `kronikol-test-id` and `kronikol-bypass`\n"
         "attachments never appear in the report, whatever that option says, and from 4.24.0 neither does a tests file's\n"
         "record of either for a scenario the messages own.\n"
         "\n"
         "**With `--tests` as well** (4.24.0, #175), every artefact is described twice: playwright-bdd embeds each\n"
         "attachment in the messages, and the reporter recipe records the file Playwright or the test wrote. Each attachment\n"
         "of the tests file is paired, one to one and in order, with a copy of the same name anywhere the messages put one:\n"
         "the scenario, a Gherkin step, a background step, a hook step kept by `--include-hooks`. The name may be the\n"
         "producer's own (`trace`) or the one shown with an extension (`trace.zip`), compared ignoring case, and when the\n"
         "reporter's file can be read the two must also have the same bytes, since two artefacts may share a name. The pair\n"
         "is one entry under the reporter's name, where the messages put it, so a screenshot taken inside a step is on that\n"
         "step's row. It links the reporter's file when that exists, and the messages' copy when it does not (Playwright\n"
         "empties `test-results/` on its next run), with the media type of the file it links. Two files with one name and\n"
         "different bytes are both kept, and a diagnostic counts them. Before 4.24.0 only the scenario's own list was\n"
         "compared, by the displayed name, so Playwright's `trace`, `video`, `screenshot` and `error-context` and everything\n"
         "attached inside a step were listed and stored twice.\n"),
    ],
    'Integration-Playwright.md': [
        ("Under playwright-bdd, `AfterStep` attaches one image per Gherkin step; Playwright ≥ 1.50 files attachments made "
         "inside a step on that step, and Kronikol's `step` index puts them on the matching step row.",
         "Under playwright-bdd, `AfterStep` attaches one image per Gherkin step. The recipe above writes no `step` index, so "
         "with `--tests` alone each of them is on the scenario; ingested with `--cucumber-messages` as well, each is on the "
         "step row the messages put it on, once (4.24.0, [[Integration Cucumber Messages|Integration-Cucumber-Messages#attachments]])."),
    ],
    'Ingesting-External-Captures.md': [
        ("`--tests` still contributes assertions, UI actions, attachments and the identity. Scenarios the messages do\n"
         "not own are kept as they were.",
         "`--tests` still contributes assertions, UI actions, attachments and the identity. Each attachment of the tests\n"
         "file is joined with the messages' copy of the same artefact, so it is listed once, where the messages put it\n"
         "([[Integration Cucumber Messages|Integration-Cucumber-Messages#attachments]], 4.24.0). Scenarios the messages do\n"
         "not own are kept as they were."),
    ],
    'Generated-Reports.md': [
        ("inside it one line each, attachments, and the `sN` / `sN/iN` address for each. Never a body, a header or a\n",
         "inside it one line each, its attachments wherever they sit (the scenario's own, then each step's with that\n"
         "step's `sN/k`, from 4.24.0), and the `sN` / `sN/iN` address for each. Never a body, a header or a\n"),
        ("Three fields on a failure record are worth knowing about:\n",
         "Fields on a failure record worth knowing about:\n"),
        ("| `truncated` | `true` when any free-text field on the record was cut at 4,000 characters.",
         "| `attachments` | `{ name, path }` for each of the scenario's own attachments, then `{ name, path, step }` for each step's, in the order the steps are listed (background steps, steps, kept hook steps, sub-steps), `step` being that step's address (`s0/2`, `s0/b0`). Before 4.24.0 only the scenario's own were listed, so a screenshot taken inside a step never reached the digest. `Failures.md` lists them the same way, a step's line ending with that step's address. |\n"
         "| `truncated` | `true` when any free-text field on the record was cut at 4,000 characters."),
        ("`truncated`, which the query envelope does not, and the two nest and cap `calls` differently. Since 3.7.0\n",
         "`truncated`, which the query envelope does not, and the two nest and cap `calls` differently. The query nests a\n"
         "failing step's attachments under that step (4.24.0) and lists the scenario's own first four, where the digest\n"
         "lists every one, each step's with its `step`. Since 3.7.0\n"),
    ],
    'Querying-Reports.md': [
        ("`failingSteps[]` (each with its `calls[]`), attachments | — |",
         "`failingSteps[]` (each with its `calls[]` and, from 4.24.0, the `attachments[]` that step made), attachments | — |"),
        ("For each failing scenario: the error, the failing step in context, its assertions with their messages and\n"
         "`file:line`, the calls that happened inside that step, and any attachments. Usually the entire answer.\n",
         "For each failing scenario: the error, the failing step in context, its assertions with their messages and\n"
         "`file:line`, the calls that happened inside that step, what that step attached (under it, from 4.24.0: the\n"
         "screenshot of a failure is usually there), and the scenario's own attachments. Usually the entire answer.\n"),
    ],
}

for page, edits in EDITS.items():
    path = os.path.join(tree, page)
    raw = open(path, 'rb').read()
    crlf = b'\r\n' in raw
    text = raw.decode('utf-8').replace('\r\n', '\n')
    for old, new in edits:
        count = text.count(old)
        if count != 1:
            raise SystemExit(f'{page}: expected the text once, found it {count} times:\n{old[:200]}')
        text = text.replace(old, new)
    if crlf:
        text = text.replace('\n', '\r\n')
    print(f'{page}: {len(edits)} edit(s){" (dry run)" if dry else ""}')
    if not dry:
        open(path, 'wb').write(text.encode('utf-8'))
