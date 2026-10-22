using System.Net;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Reports for <c>plans/LONG_NOTE_SCROLL_PLAN.md</c>, written by the real generator from captured calls as a BrowserJs run
/// writes them (the page splits, the system-ui look). A source written by hand would miss the generator's prefix
/// (<c>skinparam wrapWidth</c>, the teoz pragma, the style block), and that prefix decides how a note paints: under
/// <c>wrapWidth</c> the engine draws every word as its own <c>&lt;text&gt;</c>.
/// </summary>
internal static class LongPayloadReports
{
    private static readonly DateTimeOffset At = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly (string Key, string? Value)[] JsonHeaders = [("Content-Type", "application/json")];

    /// <summary>One GET call, its response carrying <paramref name="responseBody"/> and <paramref name="responseHeaders"/>
    /// (a JSON content type unless given).</summary>
    internal static RequestResponseLog[] Call(string testId, int index, string? responseBody, string service = "Orders API",
        (string Key, string? Value)[]? responseHeaders = null)
    {
        var pairId = Guid.NewGuid();
        var trace = Guid.NewGuid();
        var uri = new Uri($"http://orders.internal/api/items?page={index}");
        var request = new RequestResponseLog(testId, testId, HttpMethod.Get, null, uri, JsonHeaders, service, "Caller",
            RequestResponseType.Request, trace, pairId, false) { Timestamp = At.AddSeconds(index) };
        var response = new RequestResponseLog(testId, testId, HttpMethod.Get, responseBody, uri, responseHeaders ?? JsonHeaders,
            service, "Caller", RequestResponseType.Response, trace, pairId, false, HttpStatusCode.OK)
            { Timestamp = At.AddSeconds(index).AddMilliseconds(40) };
        return [request, response];
    }

    /// <summary>
    /// Writes a report holding one scenario per entry, each drawn from its calls by the generator, and returns its URL.
    /// </summary>
    internal static string Write(string tempDir, string outputDir, string fileName,
        IReadOnlyList<(string Id, string Name, RequestResponseLog[] Logs)> scenarios, ResolvedToggleDefaults? toggleDefaults = null) =>
        WriteEdited(tempDir, outputDir, fileName, scenarios, source => source, toggleDefaults);

    /// <summary>
    /// As <see cref="Write"/>, each diagram's source passed through <paramref name="edit"/> first: for a fact that needs
    /// a note in the form the generator gives it on another platform (its line ends decide where FocusFields' tags go).
    /// </summary>
    internal static string WriteEdited(string tempDir, string outputDir, string fileName,
        IReadOnlyList<(string Id, string Name, RequestResponseLog[] Logs)> scenarios, Func<string, string> edit,
        ResolvedToggleDefaults? toggleDefaults = null)
    {
        var diagrams = new List<DiagramAsCode>();
        foreach (var scenario in scenarios)
        {
            var perTest = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(scenario.Logs, maxEncodedDiagramLength: 8000,
                clientSideSplitting: true, systemUiFont: true);
            foreach (var plantUml in perTest.SelectMany(t => t.PlantUmls))
                diagrams.Add(new DiagramAsCode(scenario.Id, "", edit(plantUml.PlainText)));
        }

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Long payloads",
                Scenarios = scenarios.Select(s => new Scenario
                {
                    Id = s.Id, DisplayName = s.Name, IsHappyPath = true, Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "the items are listed", Status = ExecutionResult.Passed }]
                }).ToArray()
            }
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams.ToArray(), features,
            At.UtcDateTime, At.UtcDateTime.AddSeconds(30),
            null, Path.Combine(tempDir, fileName), "Test Run Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: toggleDefaults);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>One SQL call whose query is <paramref name="query"/>, as a ClickHouse client's capture draws it.</summary>
    internal static RequestResponseLog[] SqlCall(string testId, int index, string query)
    {
        var trace = Guid.NewGuid();
        var pairId = Guid.NewGuid();
        var uri = new Uri("clickhouse://insights/location_performance_weekly");
        return
        [
            new(testId, testId, "SELECT", query, uri, [], "ClickHouse", "DataInsights", RequestResponseType.Request, trace, pairId,
                TrackingIgnore: false, DependencyCategory: Kronikol.Constants.DependencyCategories.SQL) { Timestamp = At.AddSeconds(index) },
            new(testId, testId, "SELECT", "[{\"period\":\"2026-09-07\"}]", uri, [], "ClickHouse", "DataInsights", RequestResponseType.Response,
                trace, pairId, TrackingIgnore: false, DependencyCategory: Kronikol.Constants.DependencyCategories.SQL)
                { Timestamp = At.AddSeconds(index).AddMilliseconds(25) },
        ];
    }

    /// <summary>
    /// One scenario of <paramref name="calls"/> SQL calls whose queries are one long line each, which the note width wraps:
    /// with 80 calls the page draws the diagram in parts (taller than <c>BrowserFragmentMaxHeight</c>), with 4 whole. The
    /// first <paramref name="shortFirst"/> queries are short, so a first part drawn from them is narrower than the rest
    /// (plan F3).
    /// </summary>
    internal static string WideQueries(string tempDir, string outputDir, string fileName, int calls, NoteWidthMode noteWidth,
        int shortFirst = 0)
    {
        var query = "SELECT " + string.Join(", ", Enumerable.Range(0, 70).Select(i => $"metric_column_{i:D2}")) + " FROM sales_weekly";
        var logs = Enumerable.Range(0, calls)
            .SelectMany(i => SqlCall("wide-queries", i, i < shortFirst ? "SELECT count() FROM sales_weekly" : query)).ToArray();
        return Write(tempDir, outputDir, fileName, [("wide-queries", $"{calls} wide queries", logs)],
            ResolvedToggleDefaults.BuiltIn with { NoteWidth = noteWidth });
    }

    /// <summary>
    /// A JSON object the generator draws in <paramref name="lines"/> lines: its two braces and one property a line, each
    /// key starting with <paramref name="key"/> so a fact finds its note by it. The property numbered
    /// <paramref name="wideAt"/> holds the widest value, so it decides how wide the whole note is drawn.
    /// </summary>
    internal static string JsonOfLines(int lines, string key, int wideAt = -1) =>
        System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1, lines - 2).ToDictionary(
            i => $"{key}_{i:D4}",
            i => i == wideAt
                ? "the widest value of this payload, deep in its body, which sets how wide the whole note is drawn"
                : $"value {i}"));

    /// <summary>
    /// One form post to <paramref name="uri"/>, its body the form-url-encoded <paramref name="fields"/>, which the generator
    /// draws a field to a line with a grey <c>&amp;</c> after each; a URL too long for the arrow's label also adds its
    /// "[Full path]" lines under the body.
    /// </summary>
    internal static RequestResponseLog[] FormPost(string testId, string fields, Uri uri)
    {
        var pairId = Guid.NewGuid();
        var trace = Guid.NewGuid();
        (string Key, string? Value)[] headers = [("Content-Type", "application/x-www-form-urlencoded")];
        var request = new RequestResponseLog(testId, testId, HttpMethod.Post, fields, uri, headers, "Orders API", "Caller",
            RequestResponseType.Request, trace, pairId, false) { Timestamp = At };
        var response = new RequestResponseLog(testId, testId, HttpMethod.Post, null, uri, JsonHeaders, "Orders API", "Caller",
            RequestResponseType.Response, trace, pairId, false, HttpStatusCode.OK) { Timestamp = At.AddMilliseconds(40) };
        return [request, response];
    }

    /// <summary>One message published to <paramref name="topic"/>, its body <paramref name="body"/>.</summary>
    internal static RequestResponseLog[] Message(string testId, int index, string body, string topic = "order-events")
    {
        var trace = Guid.NewGuid();
        return
        [
            new(testId, testId, "Publish", body, new Uri($"kafka://{topic}"), JsonHeaders, "Kafka", "Caller",
                RequestResponseType.Request, trace, Guid.NewGuid(), false, MetaType: RequestResponseMetaType.Event)
                { Timestamp = At.AddSeconds(index) },
        ];
    }

    /// <summary>
    /// The scroll box's report (<c>plans/LONG_NOTE_SCROLL_PLAN.md</c> 7.1): one scenario that opens with a database call
    /// (two notes the Databases toggle hides), then JSON responses of 12, 40,
    /// 41, 150 and 1,500 body lines (keys <c>r12</c>, <c>r40</c>, <c>r41</c>, <c>r150</c>, <c>r1500</c>, the last one's widest
    /// line its 700th), a message of 60 lines (<c>m60</c>) and a 50-line response drawn with FocusFields emphasis on
    /// <c>focus_0003</c>, whose widest line painted (<c>focus_0045</c>, wide letters, past the 40 lines it draws) is not its longest in characters; and, with <paramref name="manyCalls"/>, a second scenario of 300 calls with a 60-line response
    /// each (<c>many</c>), drawn in parts.
    /// </summary>
    internal static string ScrollMix(string tempDir, string outputDir, string fileName, ResolvedToggleDefaults toggles,
        bool manyCalls = false)
    {
        // its widest line by its painted width is not its longest by characters: wide letters against more narrow ones,
        // both past the 40 lines the note draws, so the clear row alone keeps the note as wide as the wide one
        var focusBody = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1, 48).ToDictionary(
            i => $"focus_{i:D4}",
            i => i == 45 ? new string('W', 40) : i == 46 ? new string('i', 70) : $"value {i}"));
        var focus = Call("scroll-mix", 6, focusBody);
        focus[1].FocusFields = ["focus_0003"];
        var logs = SqlCall("scroll-mix", -1, "SELECT count() FROM orders")
            .Concat(Call("scroll-mix", 0, JsonOfLines(12, "r12")))
            .Concat(Call("scroll-mix", 1, JsonOfLines(40, "r40")))
            .Concat(Call("scroll-mix", 2, JsonOfLines(41, "r41")))
            .Concat(Call("scroll-mix", 3, JsonOfLines(150, "r150")))
            .Concat(Call("scroll-mix", 4, JsonOfLines(1500, "r1500", wideAt: 700)))
            .Concat(Message("scroll-mix", 5, JsonOfLines(60, "m60")))
            .Concat(focus)
            .Concat(Call("scroll-mix", 7, MarkedUpBody()))
            .ToArray();
        var scenarios = new List<(string Id, string Name, RequestResponseLog[] Logs)> { ("scroll-mix", "Long payloads of every size", logs) };
        if (manyCalls)
            scenarios.Add(("scroll-many", "Three hundred calls",
                Enumerable.Range(0, 300).SelectMany(i => Call("scroll-many", i, JsonOfLines(60, "many"))).ToArray()));
        return Write(tempDir, outputDir, fileName, scenarios, toggles);
    }

    /// <summary>
    /// A 60-line body (keys <c>r60x</c>) whose third value is a run of 300 characters with no space, which the generator
    /// breaks at 120 and marks, and whose fourth looks like creole markup, which the generator escapes: what a box shows
    /// of it is what "Copy box text" copies, the breaks joined and the escapes undone.
    /// </summary>
    internal static string MarkedUpBody() =>
        System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1, 58).ToDictionary(
            i => $"r60x_{i:D4}",
            i => i == 3 ? new string('x', 300)
                : i == 4 ? "creole: **not bold** //not italic// --not struck-- __not underlined__ <b>not a tag</b> ~tilde [[not a link]]"
                : $"value {i}"));

    /// <summary>
    /// The 40 calls of the plan's measurement M1b: four responses of 1,500 lines (<c>big</c>), twelve of 150 (<c>mid</c>)
    /// and twenty-four of 12 (<c>small</c>), in one scenario.
    /// </summary>
    internal static string FortyCalls(string tempDir, string outputDir, string fileName, ResolvedToggleDefaults toggles)
    {
        var logs = Enumerable.Range(0, 40).SelectMany(i => Call("forty-calls", i,
            i % 10 == 0 ? JsonOfLines(1500, "big") : i % 10 <= 3 ? JsonOfLines(150, "mid") : JsonOfLines(12, "small"))).ToArray();
        return Write(tempDir, outputDir, fileName, [("forty-calls", "Forty calls", logs)], toggles);
    }

    /// <summary>
    /// One call whose JSON response holds a string with fifteen lines: four lines as JSON and eighteen in the YAML view the
    /// report starts in (the string becomes a block, a line each), so the body is short as JSON and long as YAML at the
    /// default 10-line limit (plan F1).
    /// </summary>
    internal static string YamlLongerThanJson(string tempDir, string outputDir, string fileName, int scenarios = 1)
    {
        var query = "SELECT\n" + string.Join("\n", Enumerable.Range(1, 14).Select(i => $"  column_{i:D2}")) + "\nFROM sales";
        var body = System.Text.Json.JsonSerializer.Serialize(new { query, rows = 3 });
        return Write(tempDir, outputDir, fileName, Enumerable.Range(1, scenarios)
            .Select(n => ($"yaml-longer-{n}", $"A string with line breaks {n}", Call($"yaml-longer-{n}", 0, body))).ToList());
    }
}
