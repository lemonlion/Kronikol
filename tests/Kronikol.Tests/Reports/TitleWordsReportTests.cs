using System.Text.Json;
using Kronikol.Naming;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The word list over a finished model (plans/TITLES_AS_WRITTEN_PLAN.md 4.5): only the titles Kronikol made are rewritten,
/// never a name a person wrote or a row's values, and no id moves when an entry is added, edited or removed.
/// </summary>
[Collection("DiagramsFetcher")]
public class TitleWordsReportTests : IDisposable
{
    private const string Suite = "suite";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-title-words-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static Feature[] Model() =>
    [
        new Feature
        {
            DisplayName = "Acme Pay Webhook Tests",
            NameGenerated = true,
            Scenarios =
            [
                new Scenario { Id = "s1", DisplayName = "Webhook lead created via api journey", NameGenerated = true },
                new Scenario { Id = "s2", DisplayName = "Order api returns 404", NameAsWritten = true },
                new Scenario
                {
                    Id = "s3",
                    DisplayName = "Adds vat for the uk [uk]",
                    NameGenerated = true,
                    OutlineId = "Adds vat for the uk",
                    OutlineTitle = "Adds vat for the uk",
                    OutlineTitleGenerated = true,
                    ExampleValues = new Dictionary<string, string> { ["arg0"] = "uk" },
                },
                // a written row of the same theory: its outline's title is the method's, by the rule
                new Scenario
                {
                    Id = "s4",
                    DisplayName = "VAT for the uk",
                    NameAsWritten = true,
                    OutlineId = "Adds vat for the uk",
                    OutlineTitle = "Adds vat for the uk",
                    OutlineTitleGenerated = true,
                },
                // a theory whose DisplayName a person wrote
                new Scenario { Id = "s5", DisplayName = "Adds vat [eu]", NameAsWritten = true, OutlineId = "Adds vat", OutlineTitle = "Adds vat" },
                // a model a consumer made marks nothing
                new Scenario { Id = "s6", DisplayName = "Consumer api name" },
            ],
        },
        new Feature
        {
            // a Gherkin title, as written
            DisplayName = "Acme pay api flow",
            Scenarios = [new Scenario { Id = "g1", DisplayName = "The api answers", NameAsWritten = true }],
        },
    ];

    private static readonly string[] Words = ["AcmePay", "API", "VAT", "UK"];

    [Fact]
    public void Only_the_titles_Kronikol_made_are_rewritten()
    {
        var features = Model();

        new TitleWords(Words).ApplyTo(features);

        var made = features[0];
        Assert.Equal("AcmePay Webhook Tests", made.DisplayName);
        Assert.Equal("Acme Pay Webhook Tests", made.StableIdName);
        Assert.Equal(
            ["Webhook lead created via API journey", "Order api returns 404", "Adds VAT for the UK [uk]", "VAT for the uk", "Adds vat [eu]", "Consumer api name"],
            made.Scenarios.Select(s => s.DisplayName));
        // the outline's key and the row's values stay as the lane read them; the heading's words do not
        Assert.Equal("Adds vat for the uk", made.Scenarios[2].OutlineId);
        Assert.Equal("uk", made.Scenarios[2].ExampleValues!["arg0"]);
        Assert.Equal(["Adds VAT for the UK", "Adds VAT for the UK", "Adds vat"], made.Scenarios.Skip(2).Take(3).Select(s => s.OutlineTitle));
        Assert.Equal("Acme pay api flow", features[1].DisplayName);
        Assert.Equal("The api answers", features[1].Scenarios[0].DisplayName);
    }

    [Fact]
    public void A_key_is_recorded_only_where_a_title_changed()
    {
        var features = Model();

        new TitleWords(Words).ApplyTo(features);

        Assert.Equal(new ScenarioIdKey("Webhook lead created via api journey", null, null), features[0].Scenarios[0].StableIdKey);
        Assert.Equal("Adds vat for the uk [uk]", features[0].Scenarios[2].StableIdKey!.Name);
        Assert.Equal("Adds vat for the uk", features[0].Scenarios[2].StableIdKey!.OutlineId);
        Assert.Equal([null, null, null], features[0].Scenarios.Skip(3).Take(3).Select(s => s.StableIdKey));
        Assert.Null(features[1].StableIdName);
    }

    [Fact]
    public void A_key_the_lane_recorded_is_kept()
    {
        var key = new ScenarioIdKey("Webhook lead created via api journey [x]", "Webhook lead created via api journey", null);
        var feature = new Feature
        {
            DisplayName = "Acme Pay Webhook Tests",
            StableIdName = "Acme pay webhook tests",
            NameGenerated = true,
            Scenarios = [new Scenario { Id = "s1", DisplayName = "Webhook lead created via api journey", NameGenerated = true, StableIdKey = key }],
        };

        new TitleWords(Words).ApplyTo([feature]);

        Assert.Same(key, feature.Scenarios[0].StableIdKey);
        Assert.Equal("Acme pay webhook tests", feature.StableIdName);
    }

    [Fact]
    public void No_id_moves_when_an_entry_is_added_edited_or_removed()
    {
        static string[] Ids(params string[] words)
        {
            var features = Model();
            new TitleWords(words).ApplyTo(features);
            return features.SelectMany(f => f.Scenarios.Select(s => ScenarioStableId.For(Suite, f, s))).ToArray();
        }

        var none = Ids();
        Assert.Equal(none, Ids("API"));
        Assert.Equal(none, Ids("AcmePay", "API", "VAT", "UK"));
        Assert.Equal(none, Ids("ACMEPAY", "Api", "vat"));
    }

    private JsonElement[] Generate(string name, Feature[] features, params string[] words)
    {
        var directory = Path.Combine(_dir, name);
        ReportGenerator.CreateStandardReportsWithDiagrams(features, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc), new ReportConfigurationOptions
            {
                ReportsFolderPath = directory,
                InternalFlowTracking = false,
                CompressTestRunReport = false,
                TitleWords = words,
            });
        using var data = JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(directory, "TestRunReport.json")));
        return data.RootElement.GetProperty("features").EnumerateArray().Select(f => f.Clone()).ToArray();
    }

    private static Feature[] LowerCaseModel() =>
    [
        new Feature
        {
            DisplayName = "lightbdd api feature",
            NameGenerated = true,
            Scenarios = [new Scenario { Id = "l1", DisplayName = "calls the api", NameGenerated = true }],
        },
    ];

    [Fact]
    public void The_list_runs_after_titles_are_capitalised_so_a_lower_case_name_keeps_its_id()
    {
        var without = Assert.Single(Generate("without", LowerCaseModel()));
        var with = Assert.Single(Generate("with", LowerCaseModel(), "API"));

        var scenarioWithout = without.GetProperty("scenarios")[0];
        var scenarioWith = with.GetProperty("scenarios")[0];
        Assert.Equal("Calls the api", scenarioWithout.GetProperty("name").GetString());
        Assert.Equal("Calls the API", scenarioWith.GetProperty("name").GetString());
        Assert.Equal("Calls the api", scenarioWith.GetProperty("stableIdName").GetString());
        Assert.Equal(scenarioWithout.GetProperty("stableId").GetString(), scenarioWith.GetProperty("stableId").GetString());
        Assert.Equal("Lightbdd API feature", with.GetProperty("name").GetString());
        Assert.Equal("Lightbdd api feature", with.GetProperty("stableIdName").GetString());
    }

    [Fact]
    public void The_data_file_marks_a_feature_whose_name_its_lane_made()
    {
        var features = Generate("marks", Model(), Words);

        var made = features.Single(f => f.GetProperty("name").GetString() == "AcmePay Webhook Tests");
        var written = features.Single(f => f.GetProperty("name").GetString() == "Acme pay api flow");
        Assert.True(made.GetProperty("nameGenerated").GetBoolean());
        Assert.False(written.TryGetProperty("nameGenerated", out _));
    }

    [Fact]
    public void A_parameter_column_s_header_reads_the_list_when_it_is_titleized()
    {
        Feature[] Rows() =>
        [
            new Feature
            {
                DisplayName = "Pricing",
                NameGenerated = true,
                Scenarios =
                [
                    new Scenario { Id = "p1", DisplayName = "Prices(vatRate: 20, country: uk)", NameGenerated = true, OutlineId = "Prices", ExampleValues = new() { ["vatRate"] = "20", ["country"] = "uk" } },
                    new Scenario { Id = "p2", DisplayName = "Prices(vatRate: 5, country: uk)", NameGenerated = true, OutlineId = "Prices", ExampleValues = new() { ["vatRate"] = "5", ["country"] = "uk" } },
                ],
            },
        ];
        Generate("headers", Rows(), "VAT");

        var html = ReportHtml.Read(Path.Combine(_dir, "headers", "TestRunReport.html"));
        var headers = System.Text.RegularExpressions.Regex.Matches(html, "<th class=\"sub-header\"[^>]*>([^<]*)</th>").Select(m => m.Groups[1].Value).Distinct();
        Assert.Equal(["VAT Rate", "Country"], headers);
    }
}
