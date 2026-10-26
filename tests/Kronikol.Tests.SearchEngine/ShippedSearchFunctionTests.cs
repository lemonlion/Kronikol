using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;
using Kronikol.Reports;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// The search's words and its matching, run in the shipped scripts (SEARCH_PLAN B8). Until then SearchFunctionTests
/// tested them on SearchFunction, a C# port of the search that the page never ran, so they passed whatever the shipped
/// script did. The words are parseSearchTokensIncludingQuotes's, which the instant search and the deep search's worker
/// both call. A match is the instant search's own: run_search_scenarios runs over the scenarios of a report the
/// generator writes, each with the attributes it was written with. It reads a scenario's data-search, which holds its
/// feature's and its own names, descriptions, rule, endpoint, tags, error message, steps and example values, and from its
/// diagrams only the participants' names and the HTTP calls' paths; the rest of a diagram only the deep search reads
/// (DeepSearchTests). A plain query takes the text test over data-search, as every query here does, and one with an
/// operator advancedSearchMatch, which IntegrationTests and the classes beside it cover. What a search then does to the
/// page is SearchFunctionPageTests'.
/// </summary>
public class ShippedSearchFunctionTests : IDisposable
{
    private const string Cake = "scenario-making-chocolate-cake";
    private const string Bread = "scenario-proving-bread";
    private const string Cream = "scenario-whipping-cream";
    private const string Flour = "scenario-ordering-flour";

    // The page as far as the instant search's matching reads it: the scenarios, each with the attributes the generator
    // wrote, in one feature, and the search field. A fact reads which scenarios the search leaves showing; what it does to
    // the rest of a page (rules, features, opening a match) needs a browser.
    private const string MatchingPage = """
        var window = globalThis;
        var __bar = { value: '', classList: { toggle: function () {} }, setAttribute: function () {}, removeAttribute: function () {} };
        var __scenarios = [];
        var __feature = { style: {}, getElementsByClassName: function () { return __scenarios; } };
        var document = {
            getElementById: function (id) { return id === 'searchbar' ? __bar : null; },
            getElementsByClassName: function (name) { return name === 'scenario' ? __scenarios : name === 'feature' ? [__feature] : []; },
            querySelector: function () { return null; },
            querySelectorAll: function () { return []; },
            addEventListener: function () {},
            dispatchEvent: function () {}
        };
        function CustomEvent() {}
        function update_url_hash() {}
        function __scenario(attributes) {
            return {
                style: {},
                getAttribute: function (n) { return Object.prototype.hasOwnProperty.call(attributes, n) ? attributes[n] : null; },
                hasAttribute: function (n) { return Object.prototype.hasOwnProperty.call(attributes, n); },
                setAttribute: function (n, v) { attributes[n] = String(v); },
                classList: { contains: function (c) { return (attributes['class'] || '').split(' ').indexOf(c) >= 0; } },
                closest: function () { return null; },
                querySelector: function () { return null; }
            };
        }
        function __shown(query, scenarios) {
            _filterCache = null;
            __scenarios = JSON.parse(scenarios).map(__scenario);
            __bar.value = query;
            run_search_scenarios();
            return JSON.stringify(__scenarios.filter(function (s) { return s.style.display !== 'none'; }).map(function (s) { return s.getAttribute('id'); }));
        }
        """;

    // The kitchen's scenarios as the generator writes them, read once: each one's attributes, as JSON
    private static readonly Lazy<string> Kitchen = new(GenerateKitchen);

    private readonly Engine _engine = new();

    public ShippedSearchFunctionTests()
    {
        _engine.Execute(MatchingPage);
        foreach (var resource in new[] { "advanced-search.js", "report-scenario-feature-map-helper.js", "report-search-function.js" })
            _engine.Execute(Load(resource));
    }

    private static string Load(string name)
    {
        var assembly = typeof(ReportGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames().First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resourceName)!);
        return reader.ReadToEnd();
    }

    private static string GenerateKitchen()
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Kitchen",
                Scenarios =
                [
                    new() { Id = "s1", DisplayName = "Making CHOCOLATE Cake", Result = ExecutionResult.Passed },
                    new()
                    {
                        Id = "s2", DisplayName = "Proving bread", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "the dough has caraway seeds", Status = ExecutionResult.Passed }]
                    },
                    new() { Id = "s3", DisplayName = "Whipping cream", Description = "Covers the clotted path", Result = ExecutionResult.Passed },
                    new() { Id = "s4", DisplayName = "Ordering flour", Result = ExecutionResult.Passed },
                    new() { Id = "s5", DisplayName = "Washing up", Result = ExecutionResult.Passed }
                ]
            }
        ];
        // a participant the diagram names, and an HTTP call's path, which is all the instant search reads of a diagram
        var diagram = "@startuml\nparticipant \"Granary Service\" as granary\nbaker -> granary : GET: /api/sacks\n@enduml";
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("s4", "", diagram)], features,
            DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(Path.GetTempPath(), $"kronikol-shipped-search-{Guid.NewGuid():N}.html"), "Kitchen", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        try
        {
            var html = HtmlReportFile.ReadPlain(path);
            var scenarios = Regex.Matches(html, "<details class=\"scenario[\\s\"][^>]*>")
                .Select(tag => Regex.Matches(tag.Value, "([\\w-]+)=\"([^\"]*)\"")
                    .ToDictionary(a => a.Groups[1].Value, a => WebUtility.HtmlDecode(a.Groups[2].Value)))
                .ToArray();
            Assert.Equal(5, scenarios.Length);
            return JsonSerializer.Serialize(scenarios);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // the ids of the kitchen's scenarios the instant search leaves showing
    private string[] Shown(string query) =>
        JsonSerializer.Deserialize<string[]>(_engine.Invoke("__shown", query, Kitchen.Value).AsString())!;

    private string[] Words(string search) =>
        _engine.Invoke("parseSearchTokensIncludingQuotes", search).AsArray().Select(w => w.AsString()).ToArray();

    // ---- the words of a search ----

    [Theory]
    [InlineData("chocolate", "chocolate")]
    [InlineData("chocolate cake", "chocolate", "cake")]
    [InlineData("\"chocolate cake\"", "chocolate cake")]
    [InlineData("\"chocolate cake\" vanilla", "chocolate cake", "vanilla")]
    [InlineData("\"chocolate cake\" \"vanilla ice cream\"", "chocolate cake", "vanilla ice cream")]
    public void A_search_is_split_into_its_words_and_quoted_phrases(string search, params string[] words)
    {
        Assert.Equal(words, Words(search));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_search_has_no_words(string search)
    {
        Assert.Empty(Words(search));
    }

    // ---- what a search matches ----

    [Fact]
    public void A_word_of_a_scenario_s_name_finds_it()
    {
        Assert.Equal([Cake], Shown("chocolate"));
    }

    [Fact]
    public void A_word_of_its_steps_finds_it()
    {
        Assert.Equal([Bread], Shown("caraway"));
    }

    [Fact]
    public void A_word_of_its_description_finds_it()
    {
        Assert.Equal([Cream], Shown("clotted"));
    }

    // SearchFunctionTests read the whole PlantUML source as the scenario's text; the instant search reads only the
    // participants' names and the calls' paths, here with internal-flow tracking off (SEARCH_PLAN B11), and a word
    // anywhere else in a diagram is found by the deep search
    [Fact]
    public void A_participant_s_name_or_a_call_s_path_in_its_diagram_finds_it()
    {
        Assert.Equal([Flour], Shown("granary"));
        Assert.Equal([Flour], Shown("/api/sacks"));
    }

    [Theory]
    [InlineData("chocolate")]
    [InlineData("Chocolate")]
    [InlineData("CHOCOLATE")]
    public void A_word_finds_a_scenario_whatever_the_case_of_either(string query)
    {
        Assert.Equal([Cake], Shown(query));
    }

    [Fact]
    public void A_word_no_scenario_holds_finds_none()
    {
        Assert.Empty(Shown("vanilla"));
    }

    [Fact]
    public void Every_word_must_be_found_in_the_one_scenario()
    {
        Assert.Equal([Cake], Shown("chocolate cake"));
        Assert.Equal([Bread], Shown("bread caraway"));
        Assert.Empty(Shown("chocolate vanilla"));
        // each word is in some scenario, never both in one
        Assert.Empty(Shown("chocolate caraway"));
    }

    public void Dispose() => _engine.Dispose();
}
