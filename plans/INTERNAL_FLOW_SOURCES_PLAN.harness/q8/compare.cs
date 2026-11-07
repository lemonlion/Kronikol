// #138 Q8: what release A changes in a report's internal flow. Reads the iflow-segments element of each report page, decoded first (`kronikol decode <page> -o <plain>`),
// (a gzip + base64 map of segment key -> popup data) and prints counts only: segments, "Unknown" and
// "System.Net.Http" swimlanes, and each popup's span count (from its "Internal Flow (N spans" title), so two runs of
// one suite, before and after, can be compared without reading either report.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var runs = args.Select(path => (Path: path, Segments: Load(path))).ToArray();
foreach (var run in runs)
{
    var s = run.Segments;
    var counts = s.Values.Select(SpanCount).Where(c => c >= 0).OrderBy(c => c).ToArray();
    Console.WriteLine($"{run.Path}");
    Console.WriteLine($"  segments={s.Count} withUnknownLane={s.Values.Count(v => v.Contains("|Unknown|"))} " +
                      $"unknownLanes={s.Values.Sum(v => Occurrences(v, "|Unknown|"))} httpLanes={s.Values.Sum(v => Occurrences(v, "|System.Net.Http|"))} " +
                      $"grpcLanes={s.Values.Sum(v => Occurrences(v, "|Grpc.Net.Client|"))} spans={counts.Sum()}");
    Console.WriteLine($"  span counts: {string.Join(" ", counts.GroupBy(c => c).Select(g => $"{g.Key}x{g.Count()}"))}");
}

if (runs.Length == 2)
{
    var before = runs[0].Segments.Values.Select(SpanCount).Where(c => c >= 0).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
    var after = runs[1].Segments.Values.Select(SpanCount).Where(c => c >= 0).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
    var changed = before.Keys.Union(after.Keys).OrderBy(k => k)
        .Where(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k))
        .Select(k => $"{k} spans: {before.GetValueOrDefault(k)} -> {after.GetValueOrDefault(k)} popups");
    Console.WriteLine("popups by span count that differ: " + string.Join("; ", changed.DefaultIfEmpty("none")));
}

static Dictionary<string, string> Load(string path)
{
    var html = File.ReadAllText(path);
    var match = Regex.Match(html, "<script id=\"iflow-segments\" type=\"application/json\">(?<json>[^<]*)</script>");
    if (!match.Success)
        return [];
    using var element = JsonDocument.Parse(match.Groups["json"].Value);
    var z = element.RootElement.GetProperty("z").GetString()!;
    var json = z.TrimStart().StartsWith('{') ? z : Gunzip(Convert.FromBase64String(z));
    using var map = JsonDocument.Parse(json);
    return map.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
}

static string Gunzip(byte[] bytes)
{
    using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
    using var reader = new StreamReader(gzip, Encoding.UTF8);
    return reader.ReadToEnd();
}

static int SpanCount(string segment)
{
    var match = Regex.Match(segment, @"Internal Flow \((\d+) span");
    return match.Success ? int.Parse(match.Groups[1].Value) : -1;
}

static int Occurrences(string text, string needle)
{
    var count = 0;
    for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        count++;
    return count;
}
