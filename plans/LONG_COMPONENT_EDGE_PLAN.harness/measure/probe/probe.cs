#:package Kronikol@4.14.4
#:property PublishAot=false
#:property InvariantGlobalization=true
// The issue #162 probe, extended: probe.cs <outRoot> <workers> <N> [<N> ...]
// Writes <outRoot>/w<workers>/n<N>/ComponentDiagram.html (+ source.puml, edge.txt) with Kronikol 4.14.4.
using Kronikol; using Kronikol.ComponentDiagram; using Kronikol.Tracking;
var outRoot = Path.GetFullPath(args[0]); var workers = int.Parse(args[1]);
Console.WriteLine($"Kronikol assembly: {typeof(ComponentDiagramReportGenerator).Assembly.GetName().Version} {typeof(ComponentDiagramReportGenerator).Assembly.Location}");
foreach (var methods in args.Skip(2).Select(int.Parse))
{
    var outDir = Path.Combine(outRoot, $"w{workers}", $"n{methods}");
    var logs = Enumerable.Range(1, methods).Select(k => new RequestResponseLog(TestName: "Places an order", TestId: "t1", Method: $"INSERT INTO orders_archive_{k:000}", Content: null, Uri: new Uri("clickhouse://warehouse/default"), Headers: [], ServiceName: "Warehouse", CallerName: "Caller", Type: RequestResponseType.Request, TraceId: Guid.NewGuid(), RequestResponseId: Guid.NewGuid(), TrackingIgnore: false, DependencyCategory: "ClickHouse")).ToList();
    var result = ComponentDiagramReportGenerator.GenerateComponentDiagramReport(logs, new ReportConfigurationOptions { ReportsFolderPath = outDir, BrowserRenderWorkers = workers });
    var edge = result.PlantUml.Split('\n').Single(l => l.StartsWith("caller ", StringComparison.Ordinal)).TrimEnd();
    File.WriteAllText(Path.Combine(outDir, "source.puml"), result.PlantUml);
    File.WriteAllText(Path.Combine(outDir, "edge.txt"), edge);
    Console.WriteLine($"{methods} statements: edge line {edge.Length} chars, {edge.Split(@"\n").Length} display lines, {result.HtmlFilePath}");
}
