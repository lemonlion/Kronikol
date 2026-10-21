#:project ../proto/src/Kronikol/Kronikol.csproj
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// P3: P1's capture through the prototype of section 3 (proto/ = src/Kronikol at 9d89b793 with prototype.diff applied).
// Each report is given the windows its host knows: the stack's start-up as SetupWindow, the other run as OtherRunWindows.
// Run from a folder beside proto/:   dotnet run --file p3-prototype.cs -- <P1's output folder> <empty folder>
using Kronikol.Ingestion;
using Kronikol.Tracking;

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
var p1 = Path.GetFullPath(args[0]);
var dir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(dir);
var capture = Path.Combine(p1, "capture.ndjson");
var browser = Path.Combine(p1, "browser.tests.ndjson");
var device = Path.Combine(p1, "device.tests.ndjson");
DateTimeOffset T(string time) => DateTimeOffset.Parse("2026-10-21T" + time + "Z");
var startUp = new SetupWindow(T("09:47:00.000"), T("10:00:00.000"));

Ingest("A'. browser run: the start-up kept, the device run left to its report", browser, "a",
    new() { SetupWindow = startUp, OtherRunWindows = [new OtherRunWindow("device", T("10:00:11.600"))] });
Ingest("B'. device run: the start-up kept, the browser run left to its report", device, "b",
    new() { SetupWindow = startUp, OtherRunWindows = [new OtherRunWindow("browser", T("10:00:00.000"), T("10:00:11.000"))] });
Ingest("B''. device run: the start-up and the browser run both left to the browser report", device, "b2",
    new() { OtherRunWindows = [new OtherRunWindow("browser", T("09:47:00.000"), T("10:00:11.000"))] });
Ingest("E. browser run with DropUnattributed on the database: the predicate is not asked about the start-up", browser, "e",
    new() { SetupWindow = startUp, OtherRunWindows = [new OtherRunWindow("device", T("10:00:11.600"))], Drop = r => r.ServiceName == "Database" });
Ingest("F. browser run with SeparateSetup: the start-up drawn inside the Setup partition", browser, "f",
    new() { SetupWindow = startUp, OtherRunWindows = [new OtherRunWindow("device", T("10:00:11.600"))], SeparateSetup = true });

Ingest("H. device run: everything before it declared as its setup, the browser run as another run inside that", device, "h",
    new() { SetupWindow = new SetupWindow(T("09:47:00.000"), T("10:00:11.600")), OtherRunWindows = [new OtherRunWindow("browser", T("10:00:00.000"), T("10:00:11.000"))] });

// The run with no end marker (P2 a), closed by the device run's start.
var noEnd = Path.Combine(dir, "browser-no-end.tests.ndjson");
File.WriteAllLines(noEnd, File.ReadAllLines(browser).Where(l => !l.Contains("\"status\":\"passed\",\"timestamp\":\"2026-10-21T10:00:11")));
Ingest("G. browser run that wrote no end marker: closed where the device run began", noEnd, "g",
    new() { OtherRunWindows = [new OtherRunWindow("device", T("10:00:11.600"))] });

void Ingest(string label, string tests, string outName, Shape shape)
{
    var options = IngestPipeline.DefaultOptions() with { SeparateSetup = shape.SeparateSetup };
    options.ReportsFolderPath = Path.Combine(dir, "out-" + outName);
    var asked = new List<string>();
    var result = IngestPipeline.Run(new IngestRequest
    {
        InteractionFiles = [capture], TestsFile = tests, Options = options, AttributeByTestWindow = true,
        WindowAttributionFallbackId = "session", FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"),
        DropOutsideRunWindow = true, SetupWindow = shape.SetupWindow, OtherRunWindows = shape.OtherRunWindows,
        DropUnattributed = shape.Drop is null ? null : r => { asked.Add(r.Uri.ToString()); return shape.Drop(r); },
    });
    Console.WriteLine($"== {label}");
    Console.WriteLine($"run: {result.Start:HH:mm:ss.fff} -> {result.End:HH:mm:ss.fff} ({(result.End - result.Start).TotalSeconds:0.###} s)");
    Console.WriteLine($"setup records: {result.SetupRecords}; left to other runs: {string.Join(", ", result.OtherRunRecords.Select(kv => $"{kv.Key} {kv.Value}"))}");
    foreach (var diagnostic in result.Diagnostics)
        Console.WriteLine($"  {diagnostic.Kind}: {diagnostic.Message}");
    if (shape.Drop is not null)
        Console.WriteLine($"  DropUnattributed was asked about: {string.Join(", ", asked.Distinct())}");
    var logs = RequestResponseLogger.RequestAndResponseLogs;
    foreach (var feature in result.Features)
    foreach (var scenario in feature.Scenarios)
    {
        var mine = logs.Where(l => l.TestId == scenario.Id).ToArray();
        var phases = string.Join("/", mine.Where(l => !l.IsOverrideStart && !l.IsOverrideEnd).Select(l => l.Phase.ToString()).Distinct());
        Console.WriteLine($"  [{feature.DisplayName}] {scenario.DisplayName} (id {scenario.Id}{(scenario.NotATest ? ", not a test" : "")}, {scenario.Result}{(scenario.Duration is { } d ? $", {d.TotalSeconds:0.###} s" : "")}): {mine.Length} record(s), phase {phases}");
    }
    Console.WriteLine($"  report: {options.ReportsFolderPath}");
    Console.WriteLine();
}

record Shape
{
    public SetupWindow? SetupWindow { get; init; }
    public IReadOnlyList<OtherRunWindow> OtherRunWindows { get; init; } = [];
    public Func<InteractionRecord, bool>? Drop { get; init; }
    public bool SeparateSetup { get; init; }
}
