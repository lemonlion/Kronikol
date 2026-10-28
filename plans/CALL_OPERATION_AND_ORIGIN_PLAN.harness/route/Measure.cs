extern alias K;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using K::Kronikol.Compression;
using K::Kronikol.Constants;
using K::Kronikol.History;
using K::Kronikol.Query;
using K::Kronikol.Reports;

namespace RouteProbe;

/// <summary>One request record of a scenario, as the data file holds it. Body is the decoded text (a compressed wrapper
/// inflated); StoredBytes is the UTF-8 size of the content value as the file writes it (escaped string or wrapper).</summary>
sealed record Req(int Scenario, string? Method, string Uri, string Service, string Caller, string? Body, int StoredBytes, string BodyForm, string? Category);

static class Measure
{
    private const char Sep = '\u001f';

    public static int Run(string outDir, IReadOnlyList<string> specs)
    {
        Directory.CreateDirectory(outDir);
        var inputs = specs.Select(a =>
        {
            var eq = a.IndexOf('=');
            var files = a[(eq + 1)..].Split(';');
            return (Label: a[..eq], Analysis: files[0], Engine: files.Length > 1 ? files[1] : files[0]);
        }).ToList();

        // Warm the JIT once over the first input (walk, key, scan, query), so the first report's timings are not the process's start-up.
        Warm(inputs[0].Analysis, inputs[0].Engine);

        var summary = new StringBuilder();
        summary.AppendLine("label | version | calls | callers | services | today path | tpl keys kept | tpl route (3.6) | tpl PathOf | +head | +gql = route | caller,route | strict route | alt head route | gql routes | body reads (distinct) / all bodies (distinct) | read KB / all KB");
        var done = new List<Analysed>();
        foreach (var input in inputs)
        {
            var o = new StringBuilder();
            try
            {
                done.Add(One(input.Label, input.Analysis, input.Engine, o, summary));
            }
            catch (Exception e)
            {
                o.AppendLine($"!! FAILED: {e.GetType().Name}: {e.Message}");
                summary.AppendLine($"{input.Label} | FAILED {e.GetType().Name}: {Cut.To(e.Message, 100)}");
            }
            File.WriteAllText(Path.Combine(outDir, input.Label + ".txt"), o.ToString());
            Console.WriteLine($"done {input.Label}");
        }

        // The timings in a pass of their own, after everything above has run once and tier-1 code has had time to land.
        for (var round = 0; round < 3; round++)
        {
            foreach (var a in done.Take(4))
                TimeOne(a, runs: 3);
            Thread.Sleep(1000);
        }
        var timings = new StringBuilder();
        timings.AppendLine("label | records | path only ms | + head ms | route ms (us/record) | strict ms | route + buckets ms | engine baseline ms | engine route ms (scan + key loop, of which reads) | delta ms | reads | opens | engine buckets = walk");
        foreach (var a in done)
        {
            var t = TimeOne(a, runs: 9);
            timings.AppendLine($"{a.Label} | {a.Reqs.Count} | {t.Path:F2} | {t.Head:F2} | {t.Route:F2} ({1000 * t.Route / Math.Max(1, a.Reqs.Count):F1}) | {t.Strict:F2} | {t.Buckets:F2} | {t.Baseline:F1} | {t.Engine.TotalMs:F1} ({t.Engine.ScanMs:F1} + {t.Engine.KeyMs:F1}, {t.Engine.ReadMs:F2}) | {t.Engine.TotalMs - t.Baseline:F1} | {t.Engine.Reads} | {t.Engine.Opens} | {(t.Engine.Buckets == a.Route && t.Engine.CallerBuckets == a.CallerRoute ? "yes" : $"NO {t.Engine.Buckets}/{a.Route}")}");
        }
        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), summary.ToString());
        File.WriteAllText(Path.Combine(outDir, "_timings.txt"), timings.ToString());
        Console.WriteLine(summary.ToString());
        Console.WriteLine(timings.ToString());
        return 0;
    }

    private sealed record Analysed(string Label, List<Req> Reqs, string Engine, int Route, int CallerRoute);

    private readonly record struct Timed(double Path, double Head, double Route, double Strict, double Buckets, double Baseline, EngineResult Engine);

    private static Timed TimeOne(Analysed a, int runs)
    {
        var reqs = a.Reqs;
        var tPath = MinMs(runs, () => { var s = 0; foreach (var r in reqs) s += Key.TemplatedPath(r.Uri).Length; return s; });
        var tHead = MinMs(runs, () => { var s = 0; foreach (var r in reqs) s += Key.PathAndHead(r.Uri, r.Category, r.Body).Length; return s; });
        var tRoute = MinMs(runs, () => { var s = 0; foreach (var r in reqs) s += Key.Route(r.Method, r.Uri, r.Category, r.Body, GqlRule.Plan, out _).Length; return s; });
        var tStrict = MinMs(runs, () => { var s = 0; foreach (var r in reqs) s += Key.Route(r.Method, r.Uri, r.Category, r.Body, GqlRule.Strict, out _).Length; return s; });
        var tBuckets = MinMs(runs, () =>
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in reqs)
                set.Add(r.Service + Sep + (r.Method ?? "-") + Sep + Key.Route(r.Method, r.Uri, r.Category, r.Body, GqlRule.Plan, out _));
            return set.Count;
        });
        // Baseline and route interleaved, so drift on a shared machine lands on both.
        var baseline = double.MaxValue;
        EngineResult best = default;
        for (var k = 0; k < runs; k++)
        {
            var sw = Stopwatch.StartNew();
            QueryCommand.Run(["interactions", a.Engine, "--group-by", "service,method,path", "--count"], new StringWriter(), new StringWriter());
            baseline = Math.Min(baseline, sw.Elapsed.TotalMilliseconds);
            var e = EngineRoute(a.Engine);
            if (k == 0 || e.TotalMs < best.TotalMs)
                best = e;
        }
        return new Timed(tPath, tHead, tRoute, tStrict, tBuckets, baseline, best);
    }

    // ─── 3b: two kinds of statement head that hide the statement ─────────────────────────────────────────────────────

    /// <summary>A JSON object whose top-level <c>query</c> is a string (a Cosmos DB query spec, BigQuery's jobs.query): that string.</summary>
    private static string? DocumentQuery(string body)
    {
        if (Key.FirstNonWhite(body) != '{')
            return null;
        var t = TopLevel.Read(body);
        return t.IsObject && t.QueryToken == JsonTokenType.String ? t.Query : null;
    }

    /// <summary>What follows a prologue of SET session statements (EF Core's SQL Server batches open with two), or null when there is none.</summary>
    private static string? AfterSessionPrologue(string body)
    {
        var lines = body.Split('\n');
        var k = 0;
        while (k < lines.Length && (lines[k].Trim().Length == 0 || lines[k].TrimStart().StartsWith("SET ", StringComparison.OrdinalIgnoreCase)))
            k++;
        return k == 0 || k >= lines.Length ? null : string.Join('\n', lines[k..]);
    }

    /// <summary>The SET lines a body opens with, joined by ' | ', for the tally.</summary>
    private static string Prologue(string body) =>
        string.Join(" | ", body.Split('\n').Select(l => l.Trim()).TakeWhile(l => l.Length == 0 || l.StartsWith("SET ", StringComparison.OrdinalIgnoreCase)).Where(l => l.Length > 0));

    /// <summary>The alternative head: the query a document carries, templated as a statement; else the statement after a SET prologue; else the plan's head.</summary>
    private static string AltHead(string body) =>
        DocumentQuery(body) is { } query ? Key.Head(query) : AfterSessionPrologue(body) is { } rest ? Key.Head(rest) : Key.Head(body);

    private static string AltRoute(Req r, string planRoute)
    {
        if (!DependencyCategories.IsStatementShaped(r.Category) || string.IsNullOrWhiteSpace(r.Body))
            return planRoute;
        var head = Key.Head(r.Body);
        var withHead = Key.TemplatedPath(r.Uri) + " " + head;
        var tail = planRoute.Length > withHead.Length ? planRoute[withHead.Length..] : "";
        return Key.TemplatedPath(r.Uri) + " " + AltHead(r.Body) + tail;
    }

    private static void Warm(string analysis, string engine)
    {
        var w = Walk(analysis, out _, out _);
        foreach (var r in w)
        {
            Key.Route(r.Method, r.Uri, r.Category, r.Body, GqlRule.Plan, out _);
            Key.Route(r.Method, r.Uri, r.Category, r.Body, GqlRule.Strict, out _);
        }
        EngineRoute(engine);
        QueryCommand.Run(["interactions", engine, "--group-by", "service,method,path", "--count"], new StringWriter(), new StringWriter());
    }

    // ─── Reading ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static List<Req> Walk(string path, out string form, out Dictionary<string, string> root)
    {
        byte[] bytes;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var content = ReportFileFormat.Open(file))
        {
            form = content.Form.ToString();
            using var ms = new MemoryStream();
            content.Stream.CopyTo(ms);
            bytes = ms.ToArray();
        }
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 512 });
        root = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Name is "kronikolVersion" or "formatVersion" or "suite" && p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                root[p.Name] = p.Value.ToString();
        root["decodedBytes"] = bytes.Length.ToString("N0");

        var list = new List<Req>();
        var scenarioIndex = 0;
        if (!doc.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("scenarios", out var scenarios) || scenarios.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var scenario in scenarios.EnumerateArray())
            {
                scenarioIndex++;
                if (!scenario.TryGetProperty("httpInteractions", out var interactions) || interactions.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var i in interactions.EnumerateArray())
                {
                    if (Str(i, "type") != "Request")
                        continue;
                    string? body = null;
                    var stored = 0;
                    var bodyForm = "absent";
                    if (i.TryGetProperty("content", out var c))
                    {
                        stored = Encoding.UTF8.GetByteCount(c.GetRawText());
                        switch (c.ValueKind)
                        {
                            case JsonValueKind.String:
                                body = c.GetString();
                                bodyForm = body!.Length == 0 ? "empty" : "string";
                                break;
                            case JsonValueKind.Object when c.TryGetProperty("$z", out var z) && z.ValueKind == JsonValueKind.String:
                                body = ReportPayloads.Inflate(z.GetString()!);
                                bodyForm = "compressed";
                                break;
                            case JsonValueKind.Null:
                                bodyForm = "null";
                                stored = 0;
                                break;
                            default:
                                body = c.GetRawText();
                                bodyForm = c.ValueKind.ToString();
                                break;
                        }
                    }
                    list.Add(new Req(scenarioIndex, Str(i, "method"), Str(i, "uri") ?? "", Str(i, "serviceName") ?? "", Str(i, "callerName") ?? "",
                        body, stored, bodyForm, Str(i, "dependencyCategory")));
                }
            }
        }
        return list;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Null => null, _ => v.ToString() }
            : null;

    // ─── The paths 3.6 measured, for the comparison ───────────────────────────────────────────────────────────────────

    /// <summary>What --group-by path buckets on today (QueryCommand.GroupBy's UriPath).</summary>
    private static string UriPath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return parsed.AbsolutePath;
        var question = uri.IndexOf('?');
        return question < 0 ? uri : uri[..question];
    }

    /// <summary>What 3.6's templater columns templated: Uri.PathAndQuery (escaped) for an absolute URI, the string otherwise.</summary>
    private static string PathAndQuery(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.PathAndQuery : uri;

    private static string PathOnly(string uri)
    {
        var pq = PathAndQuery(uri);
        var q = pq.IndexOf('?');
        return q < 0 ? pq : pq[..q];
    }

    // ─── One report ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static Analysed One(string label, string analysisPath, string enginePath, StringBuilder o, StringBuilder summary)
    {
        var reqs = Walk(analysisPath, out var form, out var root);
        o.AppendLine($"# {label}");
        o.AppendLine($"analysis file {analysisPath} ({form}, decoded {root.GetValueOrDefault("decodedBytes")} bytes); engine file {enginePath}");
        o.AppendLine($"kronikolVersion {root.GetValueOrDefault("kronikolVersion")}, formatVersion {root.GetValueOrDefault("formatVersion")}, suite {root.GetValueOrDefault("suite")}");
        var callers = reqs.Select(r => r.Caller).Distinct().Count();
        var services = reqs.Select(r => r.Service).Distinct().Count();
        o.AppendLine($"request records {reqs.Count}; callers {callers}; services {services}; content forms: {string.Join(", ", reqs.GroupBy(r => r.BodyForm).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}");
        o.AppendLine();

        // The key, per record, once (the timings below recompute it).
        var plan = new string[reqs.Count];
        var strict = new string[reqs.Count];
        var traces = new KeyTrace[reqs.Count];
        var strictTraces = new KeyTrace[reqs.Count];
        for (var n = 0; n < reqs.Count; n++)
        {
            var r = reqs[n];
            plan[n] = Key.Route(r.Method, r.Uri, r.Category, r.Body, GqlRule.Plan, out traces[n]);
            strict[n] = Key.Route(r.Method, r.Uri, r.Category, r.Body, GqlRule.Strict, out strictTraces[n]);
        }
        string K3(Req r, string p) => r.Service + Sep + (r.Method ?? "-") + Sep + p;
        string K4(Req r, string p) => r.Caller + Sep + K3(r, p);
        int Buckets(Func<int, string> key) => Enumerable.Range(0, reqs.Count).Select(key).Distinct(StringComparer.Ordinal).Count();

        var bToday = Buckets(n => K3(reqs[n], UriPath(reqs[n].Uri)));
        var bKeys = Buckets(n => K3(reqs[n], InteractionShape.Template(PathAndQuery(reqs[n].Uri))));
        var b36 = Buckets(n => K3(reqs[n], InteractionShape.Template(PathOnly(reqs[n].Uri))));
        var bPathOf = Buckets(n => K3(reqs[n], Key.TemplatedPath(reqs[n].Uri)));
        var bHead = Buckets(n => K3(reqs[n], Key.PathAndHead(reqs[n].Uri, reqs[n].Category, reqs[n].Body)));
        var bRoute = Buckets(n => K3(reqs[n], plan[n]));
        var bCaller = Buckets(n => K4(reqs[n], plan[n]));
        var bStrict = Buckets(n => K3(reqs[n], strict[n]));
        var bStrictCaller = Buckets(n => K4(reqs[n], strict[n]));

        o.AppendLine("## 1. buckets of (service, method, X), request records");
        o.AppendLine($"  X = today's path (Uri.AbsolutePath)                    {bToday,5}   (3.6 col 1)");
        o.AppendLine($"  X = Template(Uri.PathAndQuery), query keys kept        {bKeys,5}   (3.6 col 2)");
        o.AppendLine($"  X = Template(Uri.PathAndQuery less query)              {b36,5}   (3.6 col 3, 'route' there)");
        o.AppendLine($"  X = Template(PathOf(data file uri))   [4.1 steps 1-2]  {bPathOf,5}");
        o.AppendLine($"    + statement head                    [4.1 step 3]     {bHead,5}");
        o.AppendLine($"    + GraphQL operation = route         [4.1 step 4]     {bRoute,5}   <- the prototype's route");
        o.AppendLine($"  (caller, service, method, route)                       {bCaller,5}");
        o.AppendLine($"  route with the strict GraphQL reader                   {bStrict,5}   (with caller {bStrictCaller})");
        var lengths = plan.Distinct(StringComparer.Ordinal).Select(p => p.Length).OrderBy(l => l).ToArray();
        if (lengths.Length > 0)
            o.AppendLine($"  distinct route values {lengths.Length}: length median {lengths[lengths.Length / 2]}, p90 {lengths[(int)(lengths.Length * 0.9)]}, max {lengths[^1]}; over 100 characters {lengths.Count(l => l > 100)} " +
                         $"(--group-by pads every row to the widest value: service {reqs.Max(r => r.Service.Length)} + method {reqs.Max(r => (r.Method ?? "-").Length)} + route {lengths[^1]} characters)");
        o.AppendLine();

        // ── PathOf against Uri.PathAndQuery ──
        var pathDiff = reqs.Where(r => Key.TemplatedPath(r.Uri) != InteractionShape.Template(PathOnly(r.Uri))).ToList();
        o.AppendLine($"## 2. records whose templated PathOf differs from the templated Uri.PathAndQuery path: {pathDiff.Count}");
        foreach (var g in pathDiff.GroupBy(r => (Old: InteractionShape.Template(PathOnly(r.Uri)), New: Key.TemplatedPath(r.Uri))).OrderByDescending(g => g.Count()).Take(8))
            o.AppendLine($"  x{g.Count()}  {Cut.To(g.Key.Old, 52)}  ->  {Cut.To(g.Key.New, 52)}");
        o.AppendLine();

        // ── Statement heads ──
        o.AppendLine("## 3. statement-shaped calls: by category, calls (with content), buckets path only -> with head; top routes");
        foreach (var g in reqs.Select((r, n) => (r, n)).Where(x => DependencyCategories.IsStatementShaped(x.r.Category)).GroupBy(x => x.r.Category!).OrderByDescending(g => g.Count()))
        {
            var contentCount = g.Count(x => !string.IsNullOrWhiteSpace(x.r.Body));
            var pathOnly = g.Select(x => K3(x.r, Key.TemplatedPath(x.r.Uri))).Distinct().Count();
            var withHead = g.Select(x => K3(x.r, Key.PathAndHead(x.r.Uri, x.r.Category, x.r.Body))).Distinct().Count();
            var rawLines = g.Where(x => !string.IsNullOrWhiteSpace(x.r.Body)).Select(x => FailureText.FirstLine(x.r.Body)).Distinct().Count();
            o.AppendLine($"  {g.Key}: calls {g.Count()} (content {contentCount}); buckets {pathOnly} -> {withHead}; distinct raw first lines {rawLines}; schemes {string.Join(",", g.Select(x => Uri.TryCreate(x.r.Uri, UriKind.Absolute, out var u) ? u.Scheme : "rel").Distinct())}");
            var top = g.GroupBy(x => (x.r.Service, M: x.r.Method ?? "-", Route: plan[x.n]))
                .Select(b => (b.Key, Calls: b.Count(), Raw: b.Where(x => x.r.Body is not null).Select(x => FailureText.FirstLine(x.r.Body)).Distinct().Count()))
                .OrderByDescending(b => b.Calls).ToList();
            var show = g.Key is "CosmosDB" or "BigQuery" or "SQL" ? 8 : 3;
            foreach (var b in top.Take(show))
                o.AppendLine($"    x{b.Calls,-3} raw {b.Raw,-3} {Cut.To($"{b.Key.M} {b.Key.Route}", 104)}");
            // The head buckets that fold the most distinct statements.
            foreach (var b in top.Where(b => b.Raw > 1).OrderByDescending(b => b.Raw).Take(g.Key is "CosmosDB" or "BigQuery" or "SQL" ? 3 : 1))
            {
                var examples = g.Where(x => (x.r.Service, x.r.Method ?? "-", plan[x.n]) == b.Key && x.r.Body is not null).Select(x => FailureText.FirstLine(x.r.Body)).Distinct().Take(3);
                o.AppendLine($"      folds {b.Raw} raw first lines into '{Cut.To(b.Key.Route, 60)}', e.g. {string.Join(" || ", examples.Select(e => Cut.To(e, 70)))}");
            }
        }
        o.AppendLine();

        // ── 3b. heads that hide the statement, against the alternative head ──
        var alt = Enumerable.Range(0, reqs.Count).Select(n => AltRoute(reqs[n], plan[n])).ToArray();
        var bAlt = Buckets(n => K3(reqs[n], alt[n]));
        var docQuery = Enumerable.Range(0, reqs.Count).Where(n => DependencyCategories.IsStatementShaped(reqs[n].Category) && reqs[n].Body is { } b && DocumentQuery(b) is not null).ToList();
        var prologue = Enumerable.Range(0, reqs.Count).Where(n => DependencyCategories.IsStatementShaped(reqs[n].Category) && reqs[n].Body is { } b && DocumentQuery(b) is null && AfterSessionPrologue(b) is not null).ToList();
        o.AppendLine($"## 3b. heads that hide the statement: route buckets {bRoute} with the plan's head, {bAlt} with the alternative head (a document's query string templated as the statement; the statement after a SET prologue)");
        foreach (var (name, set) in new[] { ("documents with a top-level query string", docQuery), ("statements after a SET prologue", prologue) })
        {
            if (set.Count == 0)
            {
                o.AppendLine($"  {name}: none");
                continue;
            }
            o.AppendLine($"  {name}: calls {set.Count} ({string.Join(", ", set.GroupBy(n => reqs[n].Category).Select(g => $"{g.Key} {g.Count()}"))}); plan buckets {set.Select(n => K3(reqs[n], plan[n])).Distinct().Count()} -> alternative {set.Select(n => K3(reqs[n], alt[n])).Distinct().Count()}; distinct raw statements {set.Select(n => DocumentQuery(reqs[n].Body!) ?? AfterSessionPrologue(reqs[n].Body!)).Select(s => FailureText.FirstLine(s)).Distinct().Count()}");
            foreach (var g in set.GroupBy(n => K3(reqs[n], plan[n])).OrderByDescending(g => g.Count()).Take(3))
            {
                var r = reqs[g.First()];
                o.AppendLine($"    plan  x{g.Count(),-3} {Cut.To($"{r.Method} {plan[g.First()]}", 100)}");
                foreach (var a in g.GroupBy(n => alt[n]).OrderByDescending(a => a.Count()).Take(6))
                    o.AppendLine($"     alt  x{a.Count(),-3} {Cut.To($"{r.Method} {a.Key}", 100)}");
            }
        }
        if (prologue.Count > 0)
            o.AppendLine($"  the prologues: {string.Join("; ", prologue.GroupBy(n => Prologue(reqs[n].Body!)).OrderByDescending(g => g.Count()).Select(g => $"'{Cut.To(g.Key, 70)}' x{g.Count()}"))}; " +
                         $"methods {string.Join(", ", prologue.GroupBy(n => reqs[n].Method).Select(g => $"{g.Key} {g.Count()}"))}; paths {string.Join(", ", prologue.GroupBy(n => Key.TemplatedPath(reqs[n].Uri)).Select(g => $"{g.Key} {g.Count()}"))}");
        o.AppendLine();

        // ── GraphQL ──
        var looked = Enumerable.Range(0, reqs.Count).Where(n => traces[n].GqlBodyLooked || traces[n].GqlGetLooked).ToList();
        var found = looked.Where(n => traces[n].Gql?.Operation is not null).ToList();
        o.AppendLine($"## 4. GraphQL: POST JSON-object bodies read {looked.Count(n => traces[n].GqlBodyLooked)}, GETs with GraphQL parameters {looked.Count(n => traces[n].GqlGetLooked)}; operations found {found.Count}");
        o.AppendLine($"  how (plan): {string.Join("; ", looked.GroupBy(n => traces[n].Gql!.How).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}");
        var gqlRoutes = found.GroupBy(n => K3(reqs[n], plan[n])).OrderByDescending(g => g.Count()).ToList();
        o.AppendLine($"  distinct GraphQL routes {gqlRoutes.Count} (calls {found.Count}); the detector's labels on the same bodies: {string.Join(", ", found.GroupBy(n => K::Kronikol.PlantUml.GraphQlOperationDetector.TryExtractLabel(reqs[n].Body) ?? "(none)").Select(g => $"{g.Key} x{g.Count()}"))}");
        foreach (var g in gqlRoutes)
        {
            var r = reqs[g.First()];
            o.AppendLine($"    x{g.Count(),-3} {Cut.To($"{r.Caller} > {r.Service} {r.Method} {plan[g.First()]}", 108)}");
        }
        var notGraphqlPath = found.Count(n => !reqs[n].Uri.Contains("graphql", StringComparison.OrdinalIgnoreCase));
        o.AppendLine($"  operations on a path without 'graphql' {notGraphqlPath}; strict reader disagrees on {looked.Count(n => plan[n] != strict[n])} records");
        foreach (var n in looked.Where(n => plan[n] != strict[n]).Take(5))
            o.AppendLine($"    plan {Cut.To(plan[n], 50)} | strict {Cut.To(strict[n], 50)}");
        var notGql = looked.Where(n => traces[n].Gql?.Operation is null).ToList();
        o.AppendLine($"  POST JSON-object bodies that are not GraphQL {notGql.Count}, by (service, method, templated path): {string.Join("; ", notGql.GroupBy(n => $"{reqs[n].Service} {reqs[n].Method} {Key.TemplatedPath(reqs[n].Uri)}").OrderByDescending(g => g.Count()).Take(6).Select(g => $"{Cut.To(g.Key, 60)} x{g.Count()}"))}");
        o.AppendLine();

        // ── Caller ──
        var multiCaller = Enumerable.Range(0, reqs.Count).GroupBy(n => K3(reqs[n], plan[n])).Where(g => g.Select(n => reqs[n].Caller).Distinct().Count() > 1).ToList();
        o.AppendLine($"## 5. (service, method, route) buckets with more than one caller: {multiCaller.Count} (so caller adds {bCaller - bRoute} buckets)");
        foreach (var g in multiCaller.OrderByDescending(g => g.Count()).Take(10))
        {
            var r = reqs[g.First()];
            o.AppendLine($"    {Cut.To($"{r.Service} {r.Method} {plan[g.First()]}", 70)} <- {string.Join(", ", g.GroupBy(n => reqs[n].Caller).Select(c => $"{c.Key} x{c.Count()}"))}");
        }
        o.AppendLine();

        // ── Body reads ──
        var withContent = reqs.Where(r => r.Body is not null).ToList();
        var nonEmpty = withContent.Where(r => r.Body!.Length > 0).ToList();
        var needed = reqs.Where(r => Key.NeedsBody(r.Method, r.Category, r.Body is not null)).ToList();
        var neededStatement = needed.Count(r => DependencyCategories.IsStatementShaped(r.Category));
        long Utf8(IEnumerable<Req> rs) => rs.Sum(r => (long)Encoding.UTF8.GetByteCount(r.Body!));
        long Stored(IEnumerable<Req> rs) => rs.Sum(r => (long)r.StoredBytes);
        IEnumerable<Req> Distinct(IEnumerable<Req> rs) => rs.GroupBy(r => r.Body!, StringComparer.Ordinal).Select(g => g.First());
        o.AppendLine("## 6. request bodies the key reads (4.4: a statement-shaped call's, and every POST's), against all request bodies");
        o.AppendLine($"  all request records {reqs.Count}; with content {withContent.Count} (non-empty {nonEmpty.Count}); distinct non-empty {Distinct(nonEmpty).Count()}; " +
                     $"decoded bytes {Utf8(nonEmpty):N0} over records, {Utf8(Distinct(nonEmpty)):N0} distinct; stored {Stored(Distinct(nonEmpty)):N0} distinct");
        o.AppendLine($"  read by the key: records {needed.Count} (statement-shaped {neededStatement}, other POSTs {needed.Count - neededStatement}); distinct {Distinct(needed).Count()}; " +
                     $"decoded bytes {Utf8(needed):N0} over records, {Utf8(Distinct(needed)):N0} distinct; stored {Stored(Distinct(needed)):N0} distinct");
        var posts = needed.Where(r => !DependencyCategories.IsStatementShaped(r.Category)).ToList();
        o.AppendLine($"  the POSTs read: first character {string.Join(", ", posts.GroupBy(r => r.Body!.Length == 0 ? "(empty)" : Key.FirstNonWhite(r.Body).ToString()).OrderByDescending(g => g.Count()).Select(g => $"'{g.Key}' {g.Count()}"))}; " +
                     $"by service {string.Join(", ", posts.GroupBy(r => r.Service).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} {g.Count()}"))}");
        var readOnlyIfGraphqlPath = reqs.Count(r => Key.IsPost(r.Method) && r.Body is not null && r.Uri.Contains("graphql", StringComparison.OrdinalIgnoreCase) && !DependencyCategories.IsStatementShaped(r.Category));
        o.AppendLine($"  for comparison, POSTs with content on a path holding 'graphql' {readOnlyIfGraphqlPath} (a rule keyed on the endpoint's name would read only these)");
        o.AppendLine();

        // ── Cross-checks ──
        var pathMismatch = reqs.Count(r => Key.PathOf(r.Uri) != Key.PathOfInternal(r.Uri));
        var heads = reqs.Where(r => DependencyCategories.IsStatementShaped(r.Category) && !string.IsNullOrWhiteSpace(r.Body)).ToList();
        var headMismatch = heads.Count(r => Key.Head(r.Body!) != Key.HeadInternal(r.Body!));
        o.AppendLine($"## 7. cross-checks: PathOf reproduction vs WarmUpCalls.PathOf mismatches {pathMismatch} of {reqs.Count}; statement head reproduction vs InteractionShape.StatementHead mismatches {headMismatch} of {heads.Count}");
        o.AppendLine();

        summary.AppendLine($"{label} | {Cut.To(root.GetValueOrDefault("kronikolVersion"), 6)} | {reqs.Count} | {callers} | {services} | {bToday} | {bKeys} | {b36} | {bPathOf} | {bHead} | {bRoute} | {bCaller} | {bStrict} | {bAlt} | {gqlRoutes.Count} | " +
                           $"{needed.Count} ({Distinct(needed).Count()}) / {nonEmpty.Count} ({Distinct(nonEmpty).Count()}) | {Utf8(Distinct(needed)) / 1024.0:F0} / {Utf8(Distinct(nonEmpty)) / 1024.0:F0}");
        return new Analysed(label, reqs, enginePath, bRoute, bCaller);
    }

    private static double MinMs(int runs, Func<int> work)
    {
        var best = double.MaxValue;
        var sink = 0;
        for (var k = 0; k < runs; k++)
        {
            var sw = Stopwatch.StartNew();
            sink += work();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        GC.KeepAlive(sink);
        return best;
    }

    private struct EngineResult
    {
        public double ScanMs, KeyMs, ReadMs, TotalMs;
        public int Reads, Opens, Buckets, CallerBuckets;
        public long ReadChars;
    }

    /// <summary>What R2's route dimension would do in the engine: the index as every command builds it, then each request's
    /// key, its body read through the command's one BodyCache only when the key needs it.</summary>
    private static EngineResult EngineRoute(string path)
    {
        var result = new EngineResult();
        var total = Stopwatch.StartNew();
        using var index = ReportScanner.Scan(path);
        result.ScanMs = total.Elapsed.TotalMilliseconds;
        var keyClock = Stopwatch.StartNew();
        var readClock = new Stopwatch();
        var buckets = new HashSet<string>(StringComparer.Ordinal);
        var callerBuckets = new HashSet<string>(StringComparer.Ordinal);
        var read = new HashSet<string>(StringComparer.Ordinal);
        using (var cache = new BodyCache(index))
        {
            foreach (var (_, request, _) in QueryCommand.AllInteractions(index))
            {
                string? body = null;
                if (request.BodyHash is { } hash && Key.NeedsBody(request.Method, request.DependencyCategory, true))
                {
                    readClock.Start();
                    body = cache.Raw(hash);
                    readClock.Stop();
                    if (read.Add(hash))
                        result.ReadChars += body?.Length ?? 0;
                }
                var route = Key.Route(request.Method, request.Uri, request.DependencyCategory, body, GqlRule.Plan, out _);
                var k3 = request.ServiceName + Sep + (request.Method ?? "-") + Sep + route;
                buckets.Add(k3);
                callerBuckets.Add(request.CallerName + Sep + k3);
            }
        }
        result.KeyMs = keyClock.Elapsed.TotalMilliseconds;
        result.ReadMs = readClock.Elapsed.TotalMilliseconds;
        result.TotalMs = total.Elapsed.TotalMilliseconds;
        result.Reads = read.Count;
        result.Opens = index.PayloadOpens;
        result.Buckets = buckets.Count;
        result.CallerBuckets = callerBuckets.Count;
        return result;
    }
}
