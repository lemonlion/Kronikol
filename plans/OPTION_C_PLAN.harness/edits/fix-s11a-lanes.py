# S11a's lane check had nothing to read: a flow whose spans share one source draws one swimlane and no separator, and
# the separators are what Kronikol's SwimlaneBorderColor #ccc paints (#CCCCCC). The flow-arrows fixture gains an
# opt-in second source for its database span, and the whole-test flow theory asks for it. usage: python fix-s11a-lanes.py <root>
import sys
R = sys.argv[1].rstrip('/') + '/tests/Kronikol.Tests.EndToEnd/'

def edit(path, pairs):
    s = open(path, encoding='utf-8', newline='').read()
    for old, new in pairs:
        assert s.count(old) == 1, (path, old[:70], s.count(old))
        s = s.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(s)

edit(R + 'ReportTestHelper.cs', [
    ('''string[]? paths = null,
        int? browserFragmentMaxHeight = null)
    {
        var reportsDir = Path.Combine(tempDir, "flow-arrows-"''', '''string[]? paths = null,
        int? browserFragmentMaxHeight = null, bool twoSources = false)
    {
        var reportsDir = Path.Combine(tempDir, "flow-arrows-"'''),
    ('''        var sourceName = "Kronikol.Tests.FlowArrows.E2E." + Guid.NewGuid().ToString("N");
        using var source = new System.Diagnostics.ActivitySource(sourceName);
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,''', '''        var sourceName = "Kronikol.Tests.FlowArrows.E2E." + Guid.NewGuid().ToString("N");
        using var source = new System.Diagnostics.ActivitySource(sourceName);
        // twoSources: the database span comes from a source of its own, so a flow draws two swimlanes and the line
        // between them
        using var database = new System.Diagnostics.ActivitySource(sourceName + ".Db");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName || s.Name == database.Name,'''),
    ('''        void Span(string name, System.Diagnostics.ActivityTraceId trace, DateTimeOffset start, int ms)
        {
            System.Diagnostics.Activity.Current = null;
            var span = source.StartActivity(name, System.Diagnostics.ActivityKind.Internal,
                new System.Diagnostics.ActivityContext(trace, System.Diagnostics.ActivitySpanId.CreateRandom(), System.Diagnostics.ActivityTraceFlags.Recorded))!;
            span.SetStartTime(start.UtcDateTime);
            span.SetEndTime(start.UtcDateTime.AddMilliseconds(ms));
            spans.Add(span);
        }

        lock (WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            for (var i = 0; i < withFlow + withoutFlow; i++)''', '''        void Span(string name, System.Diagnostics.ActivityTraceId trace, DateTimeOffset start, int ms,
            System.Diagnostics.ActivitySource? from = null)
        {
            System.Diagnostics.Activity.Current = null;
            var span = (from ?? source).StartActivity(name, System.Diagnostics.ActivityKind.Internal,
                new System.Diagnostics.ActivityContext(trace, System.Diagnostics.ActivitySpanId.CreateRandom(), System.Diagnostics.ActivityTraceFlags.Recorded))!;
            span.SetStartTime(start.UtcDateTime);
            span.SetEndTime(start.UtcDateTime.AddMilliseconds(ms));
            spans.Add(span);
        }

        lock (WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            for (var i = 0; i < withFlow + withoutFlow; i++)'''),
    ('''                    Span("SELECT orders " + i, trace, start.AddMilliseconds(10), 20);''',
     '''                    Span("SELECT orders " + i, trace, start.AddMilliseconds(10), 20, twoSources ? database : null);'''),
])
edit(R + 'DiagramActivityAndComponentColoursTests.cs', [
    ('''        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, fileName, withFlow: 1, withoutFlow: 0);''',
     '''        // two sources: the flow draws two swimlanes and the line between them, which is the lane colour's only stroke
        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, fileName, withFlow: 1, withoutFlow: 0,
            twoSources: true);'''),
])
print('ok')
