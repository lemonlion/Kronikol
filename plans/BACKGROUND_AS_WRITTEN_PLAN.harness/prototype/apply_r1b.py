"""The two further edits the evidence called for, on top of apply_r1.py: python -I apply_r1b.py <checkout>."""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, *pairs):
    p = root / path
    t = p.read_text(encoding="utf-8")
    for old, new in pairs:
        assert old in t, f"{path}: not found: {old[:70]!r}"
        t = t.replace(old, new, 1)
    p.write_text(t, encoding="utf-8", newline="\n")


# A list visited twice is placed twice, and the second visit holds objects the first pass never saw: KeyNotFoundException, and no
# report. No Kronikol lane shares a step once the detector stops, but Feature and ScenarioStep are public, and a caller's may.
edit(
    "src/Kronikol/Reports/AttachmentPlacement.cs",
    (
        """    internal static void ForEachList(IEnumerable<Scenario> scenarios, Func<FileAttachment[], FileAttachment[]> replace)
    {
        foreach (var scenario in scenarios)""",
        """    internal static void ForEachList(IEnumerable<Scenario> scenarios, Func<FileAttachment[], FileAttachment[]> replace)
    {
        // Each step once, however many scenarios hold it: a list replaced twice would hand replace the copies it made.
        var visited = new HashSet<ScenarioStep>(ReferenceEqualityComparer.Instance);
        foreach (var scenario in scenarios)""",
    ),
    (
        """            foreach (var step in steps ?? [])
            {
                if (step.Attachments is { Length: > 0 } attachments)
                    step.Attachments = replace(attachments);""",
        """            foreach (var step in steps ?? [])
            {
                if (!visited.Add(step))
                    continue;
                if (step.Attachments is { Length: > 0 } attachments)
                    step.Attachments = replace(attachments);""",
    ),
)

# The separate section starts closed by default, and a failed Background step must not be hidden in it: every other list holding a
# failed step starts open, and a Background that fails is the whole of what its scenario ran.
edit(
    "src/Kronikol/Reports/ReportGenerator.cs",
    (
        """                body.Append($\"\"\"<details class="scenario-background"{(backgroundStepsOpen ? " open" : "")}>\"\"\");""",
        """                body.Append($\"\"\"<details class="scenario-background"{(backgroundStepsOpen || background.Any(b => b.Status == ExecutionResult.Failed || HasAnyFailed(b)) ? " open" : "")}>\"\"\");""",
    ),
)
print("applied")
