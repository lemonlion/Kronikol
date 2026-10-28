#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property TieredCompilationQuickJitForLoops=false

// Measurement script: prints aggregates only (counts, bucket keys, path segments, short body excerpts of
// GraphQL bodies the detector misses). Never prints a whole report, diagram, header or arbitrary body.
//
// usage: dotnet run -c Release --file measure.cs -- <outDir> <label>=<analysisFile>[;<originalFile>] ...
//   analysisFile: a report data file in any form (JSON, .krkl, zstd, gzip); analysed.
//   originalFile: optional; the same report in its original form, used for the decode/scan timings only.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Constants;
using Kronikol.History;
using Kronikol.PlantUml;
using Kronikol.Query;
using Kronikol.Reports;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var outDir = args[0];
Directory.CreateDirectory(outDir);
var inputs = args.Skip(1).Select(a =>
{
    var eq = a.IndexOf('=');
    var files = a[(eq + 1)..].Split(';');
    return (Label: a[..eq], Analysis: files[0], Original: files.Length > 1 ? files[1] : null);
}).ToList();

// Warm the engine once (JIT of the scanner and the query verbs) on the first input, so that each report's
// "warm" library timing is comparable; the cold first call is reported separately.
var summary = new StringBuilder();
var timings = new StringBuilder();
timings.AppendLine("label | file | form | bytes on disk | decoded bytes | decode ms | parse ms | walk ms | analysis ms | query cold ms | query warm ms (min of 3) | query bucket count");
var firstQuery = true;

foreach (var input in inputs)
{
    var o = new StringBuilder();
    try
    {
        Measure(input.Label, input.Analysis, input.Original, o, summary, timings, ref firstQuery);
    }
    catch (Exception e)
    {
        o.AppendLine($"!! FAILED: {e.GetType().Name}: {e.Message}");
        summary.AppendLine($"{input.Label}: FAILED {e.GetType().Name}: {e.Message}");
    }
    File.WriteAllText(Path.Combine(outDir, input.Label + ".txt"), o.ToString());
    Console.WriteLine($"done {input.Label}");
}

File.WriteAllText(Path.Combine(outDir, "_summary.txt"), summary.ToString());
File.WriteAllText(Path.Combine(outDir, "_timings.txt"), timings.ToString());
Console.WriteLine(summary.ToString());
Console.WriteLine(timings.ToString());
return;

static void Measure(string label, string analysisPath, string? originalPath, StringBuilder o, StringBuilder summary, StringBuilder timings, ref bool firstQuery)
{
    o.AppendLine($"# {label}");
    o.AppendLine($"analysis file: {analysisPath} ({new FileInfo(analysisPath).Length:N0} bytes)");
    if (originalPath is not null)
        o.AppendLine($"original file: {originalPath} ({new FileInfo(originalPath).Length:N0} bytes)");

    // ---- decode + parse + walk (timed) ----
    var sw = Stopwatch.StartNew();
    var bytes = Io.ReadDecoded(analysisPath, out var form);
    var decodeMs = sw.Elapsed.TotalMilliseconds;
    sw.Restart();
    using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, MaxDepth = 512 });
    var parseMs = sw.Elapsed.TotalMilliseconds;
    sw.Restart();
    var walk = Walker.Walk(doc.RootElement);
    var walkMs = sw.Elapsed.TotalMilliseconds;
    sw.Restart();

    var reqs = walk.Requests;
    o.AppendLine($"form: {form}; decoded JSON {bytes.Length:N0} bytes");
    o.AppendLine($"root: {string.Join(", ", walk.RootScalars.Select(kv => kv.Key + "=" + kv.Value))}");
    o.AppendLine($"features {walk.Features}, scenarios {walk.Scenarios}, scenario interactions {walk.ScenarioInteractions} (requests {reqs.Count}, responses {walk.Responses}, other type {walk.OtherType}); background interactions {walk.BackgroundRequests.Count} requests (keys seen under background: {string.Join(",", walk.BackgroundKeys)})");
    o.AppendLine($"request keys present (count of {reqs.Count}): {string.Join(", ", walk.RequestKeyCounts.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}"))}");
    o.AppendLine($"content forms on requests: {string.Join(", ", reqs.GroupBy(r => r.BodyKind).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}"))}");
    o.AppendLine();

    // ---- 1. callers and services ----
    o.AppendLine("## 1. calls, callers, services");
    var byCaller = reqs.GroupBy(r => r.Caller).OrderByDescending(g => g.Count()).ToList();
    o.AppendLine($"calls (request records) {reqs.Count}; distinct callerName {byCaller.Count}; distinct serviceName {reqs.Select(r => r.Service).Distinct().Count()}");
    foreach (var g in byCaller)
    {
        var services = g.GroupBy(r => r.Service).OrderByDescending(s => s.Count()).Select(s => $"{s.Key} x{s.Count()}");
        o.AppendLine($"  caller '{g.Key}' x{g.Count()} -> {g.Select(r => r.Service).Distinct().Count()} services: {string.Join("; ", services)}");
    }
    o.AppendLine();

    // ---- 2. today's group-by path ----
    string Key(Req r, string p) => r.Service + "\u001f" + (r.Method ?? "-") + "\u001f" + p;
    var b2 = reqs.Select(r => Key(r, Paths.UriPath(r.Uri))).Distinct().Count();

    // ---- 3. templater ----
    var b3a = reqs.Select(r => Key(r, InteractionShape.Template(Paths.PathAndQuery(r.Uri)))).Distinct().Count();
    var b3b = reqs.Select(r => Key(r, InteractionShape.Template(Paths.PathOnly(r.Uri)))).Distinct().Count();
    var distinctUris = reqs.Select(r => r.Uri).Distinct().Count();
    var withQuery = reqs.Count(r => Paths.PathAndQuery(r.Uri).Contains('?'));

    o.AppendLine("## 2/3. buckets (service, method, path)");
    o.AppendLine($"distinct raw uri strings {distinctUris}; requests with a query string {withQuery}");
    o.AppendLine($"item 2  (service, method, AbsolutePath)                  : {b2}");
    o.AppendLine($"item 3a (service, method, Template(PathAndQuery))        : {b3a}");
    o.AppendLine($"item 3b (service, method, Template(path, query removed)) : {b3b}");
    foreach (var g in reqs.Where(r => Paths.PathAndQuery(r.Uri).Contains('?')).GroupBy(r => $"{r.Service} {r.Method ?? "-"} {InteractionShape.Template(Paths.PathAndQuery(r.Uri))}").OrderByDescending(g => g.Count()).Take(12))
        o.AppendLine($"  with a query string: {g.Key}  x{g.Count()}  (raw e.g. {Paths.PathAndQuery(g.First().Uri)})");

    // ---- 4. proposed segment rules ----
    var b4r0 = reqs.Select(r => Key(r, Rules.Fold(Paths.UriPath(r.Uri), Rules.R0))).Distinct().Count();
    var b4a = reqs.Select(r => Key(r, Rules.Fold(Paths.UriPath(r.Uri), s => Rules.R0(s) || Rules.OpaqueA(s)))).Distinct().Count();
    var b4b = reqs.Select(r => Key(r, Rules.Fold(Paths.UriPath(r.Uri), s => Rules.R0(s) || Rules.OpaqueB.IsMatch(s)))).Distinct().Count();
    var b4ab = reqs.Select(r => Key(r, Rules.Fold(Paths.UriPath(r.Uri), s => Rules.R0(s) || Rules.OpaqueA(s) || Rules.OpaqueB.IsMatch(s)))).Distinct().Count();
    o.AppendLine($"item 4  R0 = digits | GUID | hex 8+ (whole segment)       : {b4r0}");
    o.AppendLine($"item 4  R0 + A (20+ chars, at least one digit)           : {b4a}");
    o.AppendLine($"item 4  R0 + B (20+ chars of [A-Za-z0-9_-])              : {b4b}");
    o.AppendLine($"item 4  R0 + A + B                                       : {b4ab}");
    // Same partition as the templater? The joint key count equals both counts exactly when every bucket of one is a bucket of the other.
    foreach (var (name, rule) in new (string, Func<string, bool>)[] { ("R0", Rules.R0), ("R0+A", s => Rules.R0(s) || Rules.OpaqueA(s)), ("R0+B", s => Rules.R0(s) || Rules.OpaqueB.IsMatch(s)) })
    {
        var joint = reqs.Select(r => Key(r, Rules.Fold(Paths.UriPath(r.Uri), rule)) + "\u001e" + InteractionShape.Template(Paths.PathOnly(r.Uri))).Distinct().Count();
        var own = reqs.Select(r => Key(r, Rules.Fold(Paths.UriPath(r.Uri), rule))).Distinct().Count();
        o.AppendLine($"  {name} vs item 3b: joint keys {joint} (own {own}, templater {b3b}) -> {(joint == own && joint == b3b ? "same partition" : joint == own ? "templater merges more" : joint == b3b ? "rule merges more" : "each merges what the other splits")}");
    }
    o.AppendLine();

    // Segment census over every request's path (occurrences count requests).
    var segOcc = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var r in reqs)
        foreach (var s in Paths.UriPath(r.Uri).Split('/'))
            if (s.Length > 0)
                segOcc[s] = segOcc.GetValueOrDefault(s) + 1;

    string Templ(string seg) => InteractionShape.Template("/" + seg)[1..];
    bool Unchanged(string seg) => Templ(seg) == seg;

    var r0Digits = segOcc.Keys.Count(s => Rules.Digits.IsMatch(s));
    var r0Guid = segOcc.Keys.Count(s => !Rules.Digits.IsMatch(s) && Rules.Guid.IsMatch(s));
    var r0Hex = segOcc.Keys.Count(s => !Rules.Digits.IsMatch(s) && !Rules.Guid.IsMatch(s) && Rules.Hex8.IsMatch(s));
    var r0HexTemplaterMisses = segOcc.Keys.Where(s => Rules.R0(s) && Unchanged(s)).OrderByDescending(s => segOcc[s]).ToList();
    o.AppendLine("## 4. segment census (distinct segments over all request paths)");
    o.AppendLine($"distinct segments {segOcc.Count}; R0 folds: digits {r0Digits}, GUID {r0Guid}, hex8+ (not digits/GUID) {r0Hex}");
    o.AppendLine($"R0 segments the templater leaves unchanged: {r0HexTemplaterMisses.Count}: {string.Join(", ", r0HexTemplaterMisses.Take(40).Select(s => $"{s} x{segOcc[s]}"))}");

    foreach (var (name, rule) in new (string, Func<string, bool>)[] { ("A (20+ chars, a digit)", Rules.OpaqueA), ("B (20+ of [A-Za-z0-9_-])", s => Rules.OpaqueB.IsMatch(s)) })
    {
        var folded = segOcc.Keys.Where(s => !Rules.R0(s) && rule(s)).ToList();
        var templaterKeeps = folded.Where(Unchanged).OrderByDescending(s => segOcc[s]).ThenBy(s => s, StringComparer.Ordinal).ToList();
        var templaterPartial = folded.Where(s => !Unchanged(s)).OrderByDescending(s => segOcc[s]).ToList();
        o.AppendLine($"rule {name}: folds {folded.Count} distinct segments R0 does not ({folded.Sum(s => segOcc[s])} occurrences); templater leaves {templaterKeeps.Count} of them unchanged, changes part of {templaterPartial.Count}");
        o.AppendLine("  templater leaves unchanged (up to 40, segment xrequests):");
        foreach (var s in templaterKeeps.Take(40))
            o.AppendLine($"    {s}  x{segOcc[s]}");
        if (templaterPartial.Count > 0)
            o.AppendLine($"  templater changes part of (up to 8): {string.Join(" | ", templaterPartial.Take(8).Select(s => $"{s} -> {Templ(s)} x{segOcc[s]}"))}");
    }

    // What the templater folds that R0+A+B does not (ids inside a segment, timestamps, ...).
    var templaterOnly = segOcc.Keys.Where(s => !Unchanged(s) && !(Rules.R0(s) || Rules.OpaqueA(s) || Rules.OpaqueB.IsMatch(s))).OrderByDescending(s => segOcc[s]).ToList();
    o.AppendLine($"segments the templater changes and R0+A+B leave whole: {templaterOnly.Count} distinct; up to 15: {string.Join(" | ", templaterOnly.Take(15).Select(s => $"{s} -> {Templ(s)} x{segOcc[s]}"))}");
    o.AppendLine();

    // ---- 5. what the templater leaves ----
    o.AppendLine("## 5a. buckets under Template(path, query removed) holding the most distinct raw paths (top 20)");
    var t5 = reqs.GroupBy(r => (r.Service, M: r.Method ?? "-", T: InteractionShape.Template(Paths.PathOnly(r.Uri))))
        .Select(g => (g.Key, Raw: g.Select(r => Paths.UriPath(r.Uri)).Distinct(StringComparer.Ordinal).ToList(), Calls: g.Count()))
        .OrderByDescending(x => x.Raw.Count).ThenByDescending(x => x.Calls).ToList();
    var bucketsWithMany = t5.Count(x => x.Raw.Count > 1);
    o.AppendLine($"templated buckets {t5.Count}; holding more than one raw path {bucketsWithMany}");
    foreach (var x in t5.Take(20))
        o.AppendLine($"  [{x.Raw.Count} raw / {x.Calls} calls] {x.Key.Service} {x.Key.M} {x.Key.T}   e.g. {string.Join("  ", x.Raw.Take(3))}");
    o.AppendLine();

    o.AppendLine("## 5b. after Template(path, query removed): segment positions that still vary across templated paths (top 15, >= 3 values)");
    var families = new Dictionary<string, (string Service, string Method, string Skeleton, HashSet<string> Values, int Calls)>(StringComparer.Ordinal);
    foreach (var r in reqs)
    {
        var t = InteractionShape.Template(Paths.PathOnly(r.Uri));
        var segs = t.Split('/');
        for (var p = 0; p < segs.Length; p++)
        {
            if (segs[p].Length == 0)
                continue;
            var copy = (string[])segs.Clone();
            copy[p] = "*";
            var skeleton = string.Join('/', copy);
            var k = r.Service + "\u001f" + (r.Method ?? "-") + "\u001f" + skeleton;
            if (!families.TryGetValue(k, out var f))
                families[k] = f = (r.Service, r.Method ?? "-", skeleton, new HashSet<string>(StringComparer.Ordinal), 0);
            f.Values.Add(segs[p]);
            families[k] = f with { Calls = f.Calls + 1 };
        }
    }
    var varying = families.Values.Where(f => f.Values.Count >= 3).OrderByDescending(f => f.Values.Count).ThenByDescending(f => f.Calls).ToList();
    o.AppendLine($"families with >= 3 distinct values at one position: {varying.Count}");
    foreach (var f in varying.Take(15))
        o.AppendLine($"  [{f.Values.Count} values / {f.Calls} calls] {f.Service} {f.Method} {f.Skeleton}   * = {string.Join(", ", f.Values.Take(6))}");
    o.AppendLine();

    // ---- 6. non-HTTP ----
    o.AppendLine("## 6. non-HTTP request records (scheme other than http/https)");
    var nonHttp = reqs.Where(r => Paths.Scheme(r.Uri) is not ("http" or "https")).ToList();
    o.AppendLine($"non-http requests {nonHttp.Count} of {reqs.Count}");
    string StatementKey(Req r)
    {
        var templated = InteractionShape.Template(Paths.PathAndQuery(r.Uri));
        if (!DependencyCategories.IsStatementShaped(r.Category) || string.IsNullOrWhiteSpace(r.Body))
            return Key(r, templated);
        var head = FailureText.Truncate(InteractionShape.TemplateStatement(FailureText.Truncate(FailureText.FirstLine(r.Body), 2000)), 120);
        return Key(r, templated + " " + head);
    }
    o.AppendLine("  scheme | requests | categories | item2 buckets | item3a | item3b | statement-head buckets (history shape: path + templated first line, statement-shaped categories only)");
    foreach (var g in nonHttp.GroupBy(r => Paths.Scheme(r.Uri)).OrderByDescending(g => g.Count()))
    {
        var cats = string.Join(",", g.Select(r => r.Category is null ? "(absent)" : r.Category.Length == 0 ? "(empty)" : r.Category).Distinct());
        o.AppendLine($"  {g.Key} | {g.Count()} | {cats} | {g.Select(r => Key(r, Paths.UriPath(r.Uri))).Distinct().Count()} | {g.Select(r => Key(r, InteractionShape.Template(Paths.PathAndQuery(r.Uri)))).Distinct().Count()} | {g.Select(r => Key(r, InteractionShape.Template(Paths.PathOnly(r.Uri)))).Distinct().Count()} | {g.Select(StatementKey).Distinct().Count()}");
    }
    foreach (var g in nonHttp.GroupBy(r => Paths.Scheme(r.Uri)).OrderByDescending(g => g.Count()))
    {
        o.AppendLine($"    {g.Key} e.g. {string.Join("  ", g.Select(r => $"{r.Method} {r.Uri}").Distinct().Take(3))}");
        var heads = g.Where(r => DependencyCategories.IsStatementShaped(r.Category) && !string.IsNullOrWhiteSpace(r.Body))
            .Select(r => $"{r.Method} {Paths.UriPath(r.Uri)} :: {FailureText.Truncate(InteractionShape.TemplateStatement(FailureText.Truncate(FailureText.FirstLine(r.Body), 2000)), 120)}")
            .GroupBy(s => s).OrderByDescending(s => s.Count()).Take(4).ToList();
        if (heads.Count > 0)
            o.AppendLine($"      statement heads (top 4 of {g.Where(r => DependencyCategories.IsStatementShaped(r.Category) && !string.IsNullOrWhiteSpace(r.Body)).Select(r => FailureText.FirstLine(r.Body)).Distinct().Count()} distinct raw first lines): {string.Join(" || ", heads.Select(h => $"{h.Key} x{h.Count()}"))}");
    }
    var nb2 = nonHttp.Select(r => Key(r, Paths.UriPath(r.Uri))).Distinct().Count();
    var nb3a = nonHttp.Select(r => Key(r, InteractionShape.Template(Paths.PathAndQuery(r.Uri)))).Distinct().Count();
    var nb3b = nonHttp.Select(r => Key(r, InteractionShape.Template(Paths.PathOnly(r.Uri)))).Distinct().Count();
    var nbs = nonHttp.Select(StatementKey).Distinct().Count();
    o.AppendLine($"  total non-http: item2 {nb2}, item3a {nb3a}, item3b {nb3b}, statement-head {nbs}");
    var httpStatement = reqs.Where(r => Paths.Scheme(r.Uri) is "http" or "https").Where(r => DependencyCategories.IsStatementShaped(r.Category)).ToList();
    o.AppendLine($"  http(s) requests whose category is statement-shaped: {httpStatement.Count} ({string.Join(", ", httpStatement.GroupBy(r => r.Service + "/" + r.Category).Select(g => $"{g.Key} x{g.Count()}"))}); buckets item2 {httpStatement.Select(r => Key(r, Paths.UriPath(r.Uri))).Distinct().Count()}, item3a {httpStatement.Select(r => Key(r, InteractionShape.Template(Paths.PathAndQuery(r.Uri)))).Distinct().Count()}, item3b {httpStatement.Select(r => Key(r, InteractionShape.Template(Paths.PathOnly(r.Uri)))).Distinct().Count()}, statement-head {httpStatement.Select(StatementKey).Distinct().Count()}");
    var httpHeads = httpStatement.Where(r => !string.IsNullOrWhiteSpace(r.Body))
        .Select(r => $"{r.Method} {InteractionShape.Template(Paths.PathOnly(r.Uri))} :: {FailureText.Truncate(InteractionShape.TemplateStatement(FailureText.Truncate(FailureText.FirstLine(r.Body), 2000)), 100)}")
        .GroupBy(s => s).OrderByDescending(s => s.Count()).Take(6).ToList();
    if (httpHeads.Count > 0)
        o.AppendLine($"    statement heads (top 6): {string.Join(" || ", httpHeads.Select(h => $"{h.Key} x{h.Count()}"))}");
    o.AppendLine($"  categories over all requests: {string.Join(", ", reqs.GroupBy(r => r.Category is null ? "(absent)" : r.Category.Length == 0 ? "(empty)" : r.Category).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} x{g.Count()}"))}");
    o.AppendLine($"  non-http by (caller > service, scheme, category): {string.Join("; ", nonHttp.GroupBy(r => $"{r.Caller} > {r.Service} [{Paths.Scheme(r.Uri)}, {(r.Category is null ? "absent" : r.Category.Length == 0 ? "empty" : r.Category)}]").OrderByDescending(g => g.Count()).Select(g => $"{g.Key} x{g.Count()}"))}");
    o.AppendLine($"  metaType over all requests: {string.Join(", ", reqs.GroupBy(r => r.Meta ?? "(none)").OrderByDescending(g => g.Count()).Select(g => $"{g.Key} x{g.Count()}"))}; isUserAction {reqs.Count(r => r.UserAction)}; capturedBy {string.Join(", ", reqs.GroupBy(r => r.CapturedBy ?? "(none)").Select(g => $"{g.Key} x{g.Count()}"))}");
    o.AppendLine();

    // ---- 7. GraphQL ----
    o.AppendLine("## 7. GraphQL");
    var gql = new List<(Req R, bool PathMatch, string? LabelText)>();
    foreach (var r in reqs)
    {
        var pathMatch = Paths.UriPath(r.Uri).Contains("graphql", StringComparison.OrdinalIgnoreCase);
        var lbl = GraphQlOperationDetector.TryExtractLabel(r.Body);
        if (pathMatch || lbl is not null)
            gql.Add((r, pathMatch, lbl));
    }
    o.AppendLine($"candidates {gql.Count} (path contains graphql {gql.Count(g => g.PathMatch)}, label without graphql path {gql.Count(g => !g.PathMatch)})");
    o.AppendLine($"  by method + path + query keys + body?: {string.Join("; ", gql.GroupBy(g => $"{g.R.Method} {Paths.UriPath(g.R.Uri)}{(Paths.QueryKeys(g.R.Uri).Any() ? "?" + string.Join("&", Paths.QueryKeys(g.R.Uri)) : "")} body={(string.IsNullOrEmpty(g.R.Body) ? "no" : "yes")}").Select(x => $"{x.Key} x{x.Count()}"))}");
    o.AppendLine($"  query text starts (first 60 chars of the query value, whitespace collapsed): {string.Join(" | ", gql.Where(g => !string.IsNullOrEmpty(g.R.Body)).Select(g => Gql.QueryStart(g.R.Body!, 60)).GroupBy(s => s).Select(x => $"{x.Key} x{x.Count()}"))}");
    o.AppendLine($"methods: {string.Join(", ", gql.GroupBy(g => g.R.Method ?? "-").OrderByDescending(x => x.Count()).Select(x => $"{x.Key} x{x.Count()}"))}");
    o.AppendLine($"services: {string.Join(", ", gql.GroupBy(g => g.R.Caller + ">" + g.R.Service).OrderByDescending(x => x.Count()).Select(x => $"{x.Key} x{x.Count()}"))}");
    var withBody = gql.Count(g => !string.IsNullOrEmpty(g.R.Body));
    o.AppendLine($"body present {withBody}, absent {gql.Count - withBody}; TryExtractLabel non-null {gql.Count(g => g.LabelText is not null)}");
    var labels = gql.Where(g => g.LabelText is not null).GroupBy(g => g.LabelText!).OrderByDescending(x => x.Count()).ToList();
    o.AppendLine($"distinct labels {labels.Count}; by operation type: {string.Join(", ", gql.Where(g => g.LabelText is not null).GroupBy(g => g.LabelText!.Split(' ')[0]).Select(x => $"{x.Key} x{x.Count()}"))}; anonymous (type only) {gql.Count(g => g.LabelText is "query" or "mutation" or "subscription")}");
    foreach (var l in labels.Take(20))
        o.AppendLine($"  {l.Key}  x{l.Count()}");
    int opNameNonNull = 0, opNameNull = 0, opNameDiffersFromInline = 0, batched = 0, persistedNoQuery = 0, persistedAny = 0, notJson = 0, rawGraphQl = 0;
    var queryStringOps = new Dictionary<string, int>();
    var missed = new List<string>();
    foreach (var (r, pathMatch, lbl) in gql)
    {
        foreach (var k in Paths.QueryKeys(r.Uri))
            if (k is "query" or "operationName" or "extensions" or "variables")
                queryStringOps[k] = queryStringOps.GetValueOrDefault(k) + 1;
        if (string.IsNullOrEmpty(r.Body))
            continue;
        var looks = Gql.Looks(r.Body, out var why, out var shape);
        switch (shape)
        {
            case "array": batched++; break;
            case "notjson": notJson++; break;
        }
        if (why == "raw graphql text")
            rawGraphQl++;
        if (shape == "object" && Gql.TryParse(r.Body) is { } b)
        {
            using var owned = b;
            var e = b.RootElement;
            if (e.TryGetProperty("operationName", out var on))
            {
                if (on.ValueKind == JsonValueKind.String && on.GetString()!.Length > 0) opNameNonNull++;
                else opNameNull++;
                if (on.ValueKind == JsonValueKind.String && lbl is not null && e.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String)
                {
                    var inline = Regex.Match(q.GetString()!.TrimStart(), @"^(query|mutation|subscription)\s+(\w+)");
                    if (inline.Success && inline.Groups[2].Value != on.GetString())
                        opNameDiffersFromInline++;
                }
            }
            var hasPersisted = r.Body.Contains("persistedQuery", StringComparison.Ordinal);
            if (hasPersisted) persistedAny++;
            if (hasPersisted && !e.TryGetProperty("query", out _)) persistedNoQuery++;
        }
        if (looks && lbl is null && missed.Count < 15)
            missed.Add($"[{why}] {r.Method} {Paths.UriPath(r.Uri)}: {Gql.Excerpt(r.Body, 200)}");
    }
    // GraphQL bodies the detector misses outside the candidate set cannot exist (label null and no graphql path),
    // so also look for plainly-GraphQL bodies on requests whose path does not say graphql.
    var outside = reqs.Where(r => !Paths.UriPath(r.Uri).Contains("graphql", StringComparison.OrdinalIgnoreCase) && GraphQlOperationDetector.TryExtractLabel(r.Body) is null && !string.IsNullOrEmpty(r.Body) && Gql.Looks(r.Body, out _, out _)).ToList();
    o.AppendLine($"top-level operationName: non-empty {opNameNonNull}, null/empty {opNameNull}; operationName differs from the inline name {opNameDiffersFromInline}");
    o.AppendLine($"JSON array bodies (batched) {batched}; bodies mentioning persistedQuery {persistedAny}, of which no top-level query {persistedNoQuery}; non-JSON bodies {notJson} (raw GraphQL text {rawGraphQl})");
    o.AppendLine($"URIs carrying GraphQL parameters in the query string: {(queryStringOps.Count == 0 ? "none" : string.Join(", ", queryStringOps.Select(kv => $"{kv.Key} x{kv.Value}")))}");
    o.AppendLine($"plainly GraphQL but TryExtractLabel null (candidates): {missed.Count}{(missed.Count == 15 ? "+" : "")}");
    foreach (var m in missed)
        o.AppendLine("  " + m);
    o.AppendLine($"plainly GraphQL bodies on a path without 'graphql' that the detector misses: {outside.Count}");
    foreach (var r in outside.Take(5))
        o.AppendLine($"  {r.Method} {Paths.UriPath(r.Uri)}: {Gql.Excerpt(r.Body!, 200)}");
    o.AppendLine();

    // ---- 9. attribution ----
    o.AppendLine("## 9. attributionSource x callerName, x phase (scenario request records)");
    foreach (var g in reqs.GroupBy(r => (r.Attr, r.Caller)).OrderBy(g => g.Key.Attr, StringComparer.Ordinal).ThenByDescending(g => g.Count()))
        o.AppendLine($"  {g.Key.Attr} | {g.Key.Caller} | {g.Count()}");
    o.AppendLine("  --");
    foreach (var g in reqs.GroupBy(r => (r.Attr, r.Phase)).OrderBy(g => g.Key.Attr, StringComparer.Ordinal).ThenByDescending(g => g.Count()))
        o.AppendLine($"  {g.Key.Attr} | {g.Key.Phase} | {g.Count()}");
    if (walk.BackgroundRequests.Count > 0)
    {
        o.AppendLine("  background requests: attributionSource x callerName / x phase");
        foreach (var g in walk.BackgroundRequests.GroupBy(r => (r.Attr, r.Caller)).OrderByDescending(g => g.Count()))
            o.AppendLine($"    {g.Key.Attr} | {g.Key.Caller} | {g.Count()}");
        foreach (var g in walk.BackgroundRequests.GroupBy(r => (r.Attr, r.Phase)).OrderByDescending(g => g.Count()))
            o.AppendLine($"    {g.Key.Attr} | {g.Key.Phase} | {g.Count()}");
    }
    o.AppendLine();
    var analysisMs = sw.Elapsed.TotalMilliseconds;

    // ---- 8. timings ----
    o.AppendLine("## 8. timings");
    o.AppendLine($"analysis file: decode {decodeMs:F0} ms (form {form}), JsonDocument parse {parseMs:F0} ms, walk {walkMs:F0} ms, analysis {analysisMs:F0} ms");
    foreach (var (which, path) in new[] { ("analysis", analysisPath), ("original", originalPath) })
    {
        if (path is null)
            continue;
        // Decode only: the library's ReportFileFormat stream copied to nowhere.
        var dsw = Stopwatch.StartNew();
        var n = Io.DecodeToNull(path, out var f2);
        var dMs = dsw.Elapsed.TotalMilliseconds;
        // The library's own decode + scan + group: kronikol query interactions --group-by service,method,path --count.
        double cold = 0;
        string countText = "";
        var warm = double.MaxValue;
        for (var i = 0; i < 4; i++)
        {
            var qo = new StringWriter();
            var qe = new StringWriter();
            var qsw = Stopwatch.StartNew();
            var code = QueryCommand.Run(["interactions", path, "--group-by", "service,method,path", "--count"], qo, qe);
            var ms = qsw.Elapsed.TotalMilliseconds;
            if (i == 0)
            {
                cold = ms;
                countText = (code == 0 ? qo.ToString().Trim() : $"exit {code}: {qe.ToString().Trim()}").Replace("\n", " / ");
            }
            else
                warm = Math.Min(warm, ms);
        }
        o.AppendLine($"{which} {path}: decode-only {dMs:F0} ms ({f2}, {n:N0} bytes); query interactions --group-by service,method,path --count: first {cold:F0} ms{(firstQuery ? " (includes the process's first JIT of the engine)" : "")}, warm min {warm:F0} ms; answer: {countText}");
        timings.AppendLine($"{label} | {which} | {f2} | {new FileInfo(path).Length:N0} | {n:N0} | {dMs:F0} | {(which == "analysis" ? parseMs.ToString("F0") : "-")} | {(which == "analysis" ? walkMs.ToString("F0") : "-")} | {(which == "analysis" ? analysisMs.ToString("F0") : "-")} | {cold:F0}{(firstQuery ? "*" : "")} | {warm:F0} | {countText}");
        firstQuery = false;
    }

    summary.AppendLine($"{label} | {walk.RootScalars.GetValueOrDefault("kronikolVersion")} | fmt {walk.RootScalars.GetValueOrDefault("formatVersion")} | suite {walk.RootScalars.GetValueOrDefault("suite")} | calls {reqs.Count} | callers {byCaller.Count} | services {reqs.Select(r => r.Service).Distinct().Count()} | b2 {b2} | b3a {b3a} | b3b {b3b} | R0 {b4r0} | R0+A {b4a} | R0+B {b4b} | R0+A+B {b4ab} | non-http {nonHttp.Count} (b2 {nb2}, b3a {nb3a}, b3b {nb3b}, stmt {nbs}) | gql {gql.Count} labelled {gql.Count(g => g.LabelText is not null)} | background {walk.BackgroundRequests.Count}");
}

record Req(int Scenario, string? Method, string Uri, string Service, string Caller, string? Body, string BodyKind, string? Category, string? Meta, string? CapturedBy, bool UserAction, string Attr, string Phase);

sealed class WalkResult
{
    public List<Req> Requests { get; } = [];
    public List<Req> BackgroundRequests { get; } = [];
    public Dictionary<string, string> RootScalars { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> RequestKeyCounts { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> BackgroundKeys { get; } = new(StringComparer.Ordinal);
    public int Features, Scenarios, ScenarioInteractions, Responses, OtherType;
}

static class Walker
{
    public static WalkResult Walk(JsonElement root)
    {
        var w = new WalkResult();
        foreach (var p in root.EnumerateObject())
        {
            if (p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                var text = p.Value.ToString();
                if (p.Name is "kronikolVersion" or "formatVersion" or "mergeableFormatVersion" or "suite" or "startTime" or "endTime" or "generator" or "producer" or "tool" or "language")
                    w.RootScalars[p.Name] = text.Length > 80 ? text[..80] : text;
            }
        }
        if (root.TryGetProperty("ciMetadata", out var ci) && ci.ValueKind == JsonValueKind.Object)
            foreach (var p in ci.EnumerateObject())
                if (p.Name is "provider" or "branch" or "runId" or "repository" && p.Value.ValueKind == JsonValueKind.String)
                    w.RootScalars["ci." + p.Name] = p.Value.GetString()!;
        if (root.TryGetProperty("environment", out var env) && env.ValueKind == JsonValueKind.Object)
            foreach (var p in env.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    w.RootScalars["env." + p.Name] = p.Value.GetString()!;

        var scenarioIndex = 0;
        if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
        {
            foreach (var feature in features.EnumerateArray())
            {
                w.Features++;
                if (!feature.TryGetProperty("scenarios", out var scenarios) || scenarios.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var scenario in scenarios.EnumerateArray())
                {
                    w.Scenarios++;
                    scenarioIndex++;
                    if (!scenario.TryGetProperty("httpInteractions", out var interactions) || interactions.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var i in interactions.EnumerateArray())
                    {
                        w.ScenarioInteractions++;
                        var type = Str(i, "type");
                        if (type == "Response") { w.Responses++; continue; }
                        if (type != "Request") { w.OtherType++; continue; }
                        foreach (var p in i.EnumerateObject())
                            w.RequestKeyCounts[p.Name] = w.RequestKeyCounts.GetValueOrDefault(p.Name) + 1;
                        w.Requests.Add(Read(i, scenarioIndex));
                    }
                }
            }
        }
        if (root.TryGetProperty("background", out var background) && background.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in background.EnumerateObject())
                w.BackgroundKeys.Add(p.Name + (p.Value.ValueKind == JsonValueKind.Array ? $"[{p.Value.GetArrayLength()}]" : p.Value.ValueKind == JsonValueKind.Number ? "=" + p.Value.ToString() : ""));
            foreach (var name in new[] { "interactions", "httpInteractions" })
                if (background.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var i in list.EnumerateArray())
                        if (Str(i, "type") == "Request")
                            w.BackgroundRequests.Add(Read(i, -1));
        }
        return w;
    }

    private static Req Read(JsonElement i, int scenario)
    {
        var (body, kind) = Content(i);
        string attr = i.TryGetProperty("attributionSource", out var a) ? (a.ValueKind == JsonValueKind.String ? a.GetString()! : "(null)") : "(absent)";
        string phase = i.TryGetProperty("phase", out var ph) ? (ph.ValueKind == JsonValueKind.String ? ph.GetString()! : "(null)") : "(absent)";
        return new Req(scenario, Str(i, "method"), Str(i, "uri") ?? "", Str(i, "serviceName") ?? "", Str(i, "callerName") ?? "", body, kind,
            Str(i, "dependencyCategory"), Str(i, "metaType"), Str(i, "capturedBy"),
            i.TryGetProperty("isUserAction", out var ua) && ua.ValueKind == JsonValueKind.True, attr, phase);
    }

    private static (string?, string) Content(JsonElement i)
    {
        if (!i.TryGetProperty("content", out var c))
            return (null, "absent");
        switch (c.ValueKind)
        {
            case JsonValueKind.String: return (c.GetString(), c.GetString()!.Length == 0 ? "empty string" : "string");
            case JsonValueKind.Null: return (null, "null");
            case JsonValueKind.Object:
                if (c.TryGetProperty("$z", out var z) && z.ValueKind == JsonValueKind.String)
                    return (Io.Inflate(z.GetString()!), "compressed wrapper");
                return (c.GetRawText(), "object");
            default: return (c.GetRawText(), c.ValueKind.ToString());
        }
    }

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Null => null, _ => v.ToString() }
            : null;
}

static class Paths
{
    /// <summary>The same as QueryCommand.GroupBy's UriPath: what --group-by path buckets on today.</summary>
    public static string UriPath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return parsed.AbsolutePath;
        var question = uri.IndexOf('?');
        return question < 0 ? uri : uri[..question];
    }

    /// <summary>What InteractionShape templates: PathAndQuery for an absolute URI, the string itself otherwise.</summary>
    public static string PathAndQuery(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.PathAndQuery : uri;

    public static string PathOnly(string uri)
    {
        var pq = PathAndQuery(uri);
        var q = pq.IndexOf('?');
        return q < 0 ? pq : pq[..q];
    }

    public static string Scheme(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.Scheme : "(relative)";

    public static IEnumerable<string> QueryKeys(string uri)
    {
        var pq = PathAndQuery(uri);
        var q = pq.IndexOf('?');
        if (q < 0)
            yield break;
        foreach (var pair in pq[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            yield return Uri.UnescapeDataString(pair.Split('=', 2)[0]);
    }
}

static class Rules
{
    public static readonly Regex Digits = new(@"^[0-9]+$", RegexOptions.CultureInvariant);
    public static readonly Regex Guid = new(@"^\{?[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}?$", RegexOptions.CultureInvariant);
    public static readonly Regex Hex8 = new(@"^[0-9A-Fa-f]{8,}$", RegexOptions.CultureInvariant);
    public static readonly Regex OpaqueB = new(@"^[A-Za-z0-9_-]{20,}$", RegexOptions.CultureInvariant);

    public static bool R0(string s) => Digits.IsMatch(s) || Guid.IsMatch(s) || Hex8.IsMatch(s);

    public static bool OpaqueA(string s) => s.Length >= 20 && s.Any(char.IsAsciiDigit);

    public static string Fold(string path, Func<string, bool> isId) =>
        string.Join('/', path.Split('/').Select(s => s.Length > 0 && isId(s) ? "{id}" : s));
}

static class Gql
{
    private static readonly Regex Operation = new(@"^(query|mutation|subscription|fragment)\b|^\{", RegexOptions.CultureInvariant);

    /// <summary>Whether a body is plainly a GraphQL request, and its outer shape (object, array, notjson).</summary>
    public static bool Looks(string body, out string why, out string shape)
    {
        why = "";
        var t = body.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (t.StartsWith('{') || t.StartsWith('['))
        {
            JsonDocument? d = null;
            try { d = JsonDocument.Parse(t); }
            catch (JsonException) { shape = "notjson"; return false; }
            using (d)
            {
                var e = d.RootElement;
                shape = e.ValueKind == JsonValueKind.Array ? "array" : e.ValueKind == JsonValueKind.Object ? "object" : "other";
                if (e.ValueKind == JsonValueKind.Object)
                    return ObjectLooks(e, out why);
                if (e.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in e.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.Object && ObjectLooks(item, out var w2))
                        {
                            why = "batched array: " + w2;
                            return true;
                        }
                }
                return false;
            }
        }
        shape = "notjson";
        if (Regex.IsMatch(t, @"^(query|mutation|subscription|fragment)\b"))
        {
            why = "raw graphql text";
            return true;
        }
        return false;
    }

    private static bool ObjectLooks(JsonElement e, out string why)
    {
        why = "";
        if (e.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String && Operation.IsMatch(q.GetString()!.TrimStart()))
        {
            why = "json object with query";
            return true;
        }
        if (e.TryGetProperty("extensions", out var ex) && ex.ValueKind == JsonValueKind.Object && ex.TryGetProperty("persistedQuery", out _))
        {
            why = "persisted query (extensions.persistedQuery)";
            return true;
        }
        return false;
    }

    public static string QueryStart(string body, int n)
    {
        using var d = TryParse(body);
        if (d is null)
            return "(not json) " + Excerpt(body, n);
        var e = d.RootElement;
        if (e.ValueKind == JsonValueKind.Array)
            return "(array)";
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("query", out var q) || q.ValueKind != JsonValueKind.String)
            return "(no query string)";
        var keys = string.Join(",", e.EnumerateObject().Select(p => p.Name));
        return $"keys[{keys}] " + Excerpt(q.GetString()!, n);
    }

    public static JsonDocument? TryParse(string body)
    {
        try { return JsonDocument.Parse(body.TrimStart('﻿', ' ', '\t', '\r', '\n')); }
        catch (JsonException) { return null; }
    }

    public static string Excerpt(string body, int n)
    {
        var flat = Regex.Replace(body, @"\s+", " ");
        return flat.Length <= n ? flat : flat[..n] + "...";
    }
}

static class Io
{
    private static readonly Assembly Lib = typeof(InteractionShape).Assembly;
    private static readonly MethodInfo OpenMethod = Lib.GetType("Kronikol.Compression.ReportFileFormat", throwOnError: true)!
        .GetMethod("Open", BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo InflateMethod = Lib.GetType("Kronikol.Reports.ReportPayloads", throwOnError: true)!
        .GetMethod("Inflate", BindingFlags.NonPublic | BindingFlags.Static)!;

    public static string Inflate(string z) => (string)InflateMethod.Invoke(null, [z])!;

    /// <summary>The JSON a report file holds, through the library's own reader (ReportFileFormat.Open): every form it reads.</summary>
    public static byte[] ReadDecoded(string path, out string form)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var content = OpenMethod.Invoke(null, [file, 4L << 30])!;
        try
        {
            form = content.GetType().GetProperty("Form")!.GetValue(content)!.ToString()!;
            var stream = (Stream)content.GetType().GetProperty("Stream")!.GetValue(content)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        finally
        {
            ((IDisposable)content).Dispose();
        }
    }

    public static long DecodeToNull(string path, out string form)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var content = OpenMethod.Invoke(null, [file, 4L << 30])!;
        try
        {
            form = content.GetType().GetProperty("Form")!.GetValue(content)!.ToString()!;
            var stream = (Stream)content.GetType().GetProperty("Stream")!.GetValue(content)!;
            var buffer = new byte[128 * 1024];
            long total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                total += read;
            return total;
        }
        finally
        {
            ((IDisposable)content).Dispose();
        }
    }
}
