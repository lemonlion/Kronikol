"""Probe only, never for a release: on the two prototypes built as one, writes per record what published's walk says and what the stamp says.

Usage: apply_probe_one.py <worktree>. Reads KRONIKOL_PROBE_ATTRIBUTION at run time for the file to append to. differential.py reads the result.

The walk here is published's, over the same tree: the nth bar opens the nth step if the old text check passes. So a disagreement is the stamp
against what today's report would say of the same record, in the same run, with no second run's timing in it.
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")

pairs = [
    ("""            var stepMarkerCount = 0;
            var interactionIndex = 0;

            foreach (var log in perTest)
            {
                if (!log.IsDiagramMarker)
                {
""",
     """            var stepMarkerCount = 0;
            var interactionIndex = 0;
            string? probeWalk = null;
            var probeMarkers = 0;
            var probeFile = Environment.GetEnvironmentVariable("KRONIKOL_PROBE_ATTRIBUTION");

            foreach (var log in perTest)
            {
                if (log.IsDiagramMarker && log.IsOverrideStart && log.PlantUml is not null && log.MarkerKind == DiagramMarkerKind.Step)
                {
                    probeWalk = probeMarkers < ordered.Count && ProbePublishedMatch(log.PlantUml, ordered[probeMarkers].Text) ? ordered[probeMarkers].Path : null;
                    probeMarkers++;
                }

                if (!log.IsDiagramMarker && probeFile is { Length: > 0 })
                {
                    var probeStamp = log.OpenStep is not { } probeOpen ? "unstamped"
                        : collected is null ? "unmapped"
                        : collected.TryGetValue(probeOpen, out var probePath) ? (probePath ?? "none") : "unmapped";
                    lock (ProbeLock)
                        File.AppendAllText(probeFile, $"{perTest.Key}\\t{log.Type}\\t{log.RequestResponseId}\\t{probeWalk ?? "-"}\\t{probeStamp}\\t{log.Method.Value}\\t{log.Uri}\\n");
                }

                if (!log.IsDiagramMarker)
                {
"""),
    ("""    private static bool StepMarkerMatches(string plantUml, string stepText)
    {
""",
     """    private static readonly object ProbeLock = new();

    private static bool ProbePublishedMatch(string plantUml, string stepText)
    {
        if (string.IsNullOrWhiteSpace(stepText))
            return true;

        var marker = plantUml.Replace('\\n', ' ').Trim();
        return marker.Contains(stepText, StringComparison.OrdinalIgnoreCase)
               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StepMarkerMatches(string plantUml, string stepText)
    {
"""),
]
for old, new in pairs:
    assert s.count(old) == 1, old[:70]
    s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("probe applied")
