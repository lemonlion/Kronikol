using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kronikol;
using Kronikol.Constants;
using Kronikol.History;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;

// Q18 (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): how much a history fragment grows when it carries the run's raw calls.
// usage: dotnet run -c Release -- <label>=<report file> ...   Prints counts and byte sizes only, never a path, a body or a name.
Console.WriteLine("label | scenarios | calls | distinct templated (v8) | distinct raw | fragment B | fragment gz B | raw block B | raw gz B (added) | raw block share | heads | head chars | p7 | h7 | max raw call B | written without B | written with B | written added B | written added gz B");
foreach (var arg in args)
{
    var eq = arg.IndexOf('=');
    var label = arg[..eq];
    var path = arg[(eq + 1)..];
    try
    {
        Measure(label, path);
    }
    catch (Exception e)
    {
        Console.WriteLine($"{label} | FAILED {e.GetType().Name}: {e.Message}");
    }
}
return;

static void Measure(string label, string path)
{
    var (features, logs, suite) = Load(path);
    var report = new { Features = features };
    var options = new HistoryBuildOptions { Durations = true, Shapes = true, ErrorKeys = true, Bridges = true };
    var built = HistoryRunBuilder.Build(features, logs, suite, null, new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero), options);
    var fragment = HistoryFragment.Write(built.Roster, built.Run, "5.17.0", built.Shapes);
    var fragmentBytes = Encoding.UTF8.GetByteCount(fragment);
    var fragmentGz = Gzip(fragment);

    // The raw calls, by scenario in roster order, as the builder attributes them (by test id).
    var scenarios = report.Features.OrderBy(f => f.DisplayName).SelectMany(f => f.Scenarios ?? []).ToArray();
    var byTest = logs.Where(l => l is not null).GroupBy(l => l!.TestId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    var distinct = new Dictionary<string, int>(StringComparer.Ordinal);
    var perScenario = new List<int[]>();
    int calls = 0, heads = 0, headChars = 0, p7 = 0, h7 = 0, maxRaw = 0;
    foreach (var scenario in scenarios)
    {
        var own = byTest.TryGetValue(scenario.Id, out var list) ? list : [];
        var indices = new List<int>();
        foreach (var raw in RawCalls(own))
        {
            calls++;
            var json = JsonSerializer.Serialize(raw, Json);
            if (!distinct.TryGetValue(json, out var at))
            {
                at = distinct.Count;
                distinct[json] = at;
                maxRaw = Math.Max(maxRaw, Encoding.UTF8.GetByteCount(json));
                if (raw.TryGetValue("h", out var h) && h is string head) { heads++; headChars += head.Length; }
                if (raw.ContainsKey("p7")) p7++;
                if (raw.ContainsKey("h7")) h7++;
            }
            indices.Add(at);
        }
        perScenario.Add(indices.ToArray());
    }

    // The block: the distinct raw calls once, and each position's calls in order as indices into them.
    var block = new StringBuilder();
    block.Append("{\"t\":\"raw\",\"calls\":[").AppendJoin(',', distinct.OrderBy(p => p.Value).Select(p => p.Key)).Append("],\"order\":[");
    block.AppendJoin(',', perScenario.Select(s => "[" + string.Join(',', s) + "]")).Append("]}\n");
    var blockText = block.ToString();
    var blockBytes = Encoding.UTF8.GetByteCount(blockText);
    var withGz = Gzip(fragment + blockText);
    var templated = built.Shapes?.Calls.Count ?? 0;
    // The fragment as R1 writes it, with and without its raw calls.
    var written = HistoryFragment.Write(built.Roster, built.Run, "5.17.0", built.Shapes, built.RawCalls);
    var writtenBytes = Encoding.UTF8.GetByteCount(written);
    Console.WriteLine($"{label} | {scenarios.Length} | {calls} | {templated} | {distinct.Count} | {fragmentBytes:N0} | {fragmentGz:N0} | {blockBytes:N0} | {withGz - fragmentGz:N0} | {(double)blockBytes / fragmentBytes:P0} | {heads} | {headChars:N0} | {p7} | {h7} | {maxRaw:N0} | {fragmentBytes:N0} | {writtenBytes:N0} | {writtenBytes - fragmentBytes:N0} | {Gzip(written) - fragmentGz:N0}");
}

static (Feature[] Features, RequestResponseLog?[] Logs, string? Suite) Load(string path)
{
    try
    {
        var report = MergeableReportReader.ReadFile(path);
        return (report.Features, report.Interactions.Cast<RequestResponseLog?>().ToArray(), report.Suite);
    }
    catch (FormatException)
    {
    }

    // Not mergeable: the scenarios and their calls as the query engine's index reads them.
    using var index = Kronikol.Query.ReportScanner.Scan(path);
    using var bodies = new Kronikol.Query.BodyCache(index);
    var logs = new List<RequestResponseLog?>();
    var features = new List<Feature>();
    foreach (var group in index.Scenarios.GroupBy(s => s.FeatureName))
    {
        var scenarios = new List<Scenario>();
        foreach (var entry in group)
        {
            var id = string.IsNullOrEmpty(entry.Id) ? entry.StableId : entry.Id;
            scenarios.Add(new Scenario { Id = id, DisplayName = entry.Name, Result = Enum.TryParse<ExecutionResult>(entry.Result, out var r) ? r : ExecutionResult.Passed });
            foreach (var i in entry.Interactions)
            {
                var type = i.Type == "Response" ? RequestResponseType.Response : RequestResponseType.Request;
                var content = i.BodyHash is { } hash ? bodies.Raw(hash) : null;
                var uri = Uri.TryCreate(i.Uri, UriKind.RelativeOrAbsolute, out var u) ? u : new Uri("http://unknown/");
                OneOf<System.Net.HttpStatusCode, string>? status = i.StatusCode is { } code
                    ? int.TryParse(code, out var n) ? (System.Net.HttpStatusCode)n : code
                    : null;
                var pair = Guid.TryParse(i.RequestResponseId, out var g) ? g : Guid.NewGuid();
                var method = i.Method is { } m ? (OneOf<HttpMethod, string>)m : (OneOf<HttpMethod, string>)"";
                logs.Add(new RequestResponseLog(entry.Name, id, method, content, uri, [], i.ServiceName, i.CallerName, type, Guid.Empty, pair, TrackingIgnore: false,
                    StatusCode: status, DependencyCategory: i.DependencyCategory));
            }
        }
        features.Add(new Feature { DisplayName = group.Key, Scenarios = scenarios.ToArray() });
    }
    return (features.ToArray(), logs.ToArray(), index.Suite);
}

static IEnumerable<Dictionary<string, object?>> RawCalls(IEnumerable<RequestResponseLog?> logs)
{
    var responses = new Dictionary<Guid, RequestResponseLog>();
    var requests = new List<RequestResponseLog>();
    foreach (var log in logs)
    {
        if (log is null || log.TrackingIgnore || log.IsDiagramMarker)
            continue;
        if (log.Type == RequestResponseType.Response)
            responses.TryAdd(log.RequestResponseId, log);
        else
            requests.Add(log);
    }

    foreach (var request in requests)
    {
        responses.TryGetValue(request.RequestResponseId, out var response);
        var (code, text) = InteractionStatus.Split(response?.StatusCode);
        var status = code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? text;
        var method = request.Method.Value switch { HttpMethod http => http.Method, var other => other?.ToString() ?? "" };
        var uri = request.Uri.ToString();
        var raw = new Dictionary<string, object?> { ["c"] = request.CallerName, ["s"] = request.ServiceName, ["m"] = method, ["p"] = CallOperation.PathOf(uri, request.DependencyCategory, queryKeys: true) };
        var statement = DependencyCategories.IsStatementShaped(request.DependencyCategory) && !string.IsNullOrWhiteSpace(request.Content);
        if (statement)
            raw["h"] = Head8(request.Content!);
        else if (Suffix(method, uri, request.Content) is { } suffix)
            raw["q"] = suffix;
        if (status is not null)
            raw["r"] = status;

        // Under 6 and 7: the escaped path, and the statement's raw first line, where they differ from 8's.
        var escaped = request.Uri.IsAbsoluteUri ? request.Uri.PathAndQuery : request.Uri.OriginalString;
        if (!string.Equals(escaped, (string)raw["p"]!, StringComparison.Ordinal))
            raw["p7"] = escaped;
        if (statement)
        {
            var head7 = FailureText.Truncate(FailureText.FirstLine(request.Content), 2000);
            if (!string.Equals(head7, (string)raw["h"]!, StringComparison.Ordinal))
                raw["h7"] = head7;
        }
        yield return raw;
    }
}

static string Head8(string content)
{
    // CallOperation.Head's input before the rules: the document's query string, or the statement past a SET prologue.
    var method = typeof(CallOperation).GetMethod("DocumentQuery", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    var query = (string?)method.Invoke(null, [content]);
    var without = typeof(CallOperation).GetMethod("WithoutSetPrologue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    var statement = query ?? (string)without.Invoke(null, [content])!;
    return FailureText.Truncate(FailureText.FirstLine(statement), 2000);
}

static string? Suffix(string method, string uri, string? content)
{
    if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        return GraphQlOperationReader.FromQueryString(uri)?.Label;
    if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        return null;
    if (GraphQlOperationReader.FromBody(content) is { } operation)
        return operation.Label;
    return GraphQlOperationReader.IsBatch(content) ? "batch" : null;
}

static int Gzip(string text)
{
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        gzip.Write(Encoding.UTF8.GetBytes(text));
    return (int)output.Length;
}

partial class Program
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
