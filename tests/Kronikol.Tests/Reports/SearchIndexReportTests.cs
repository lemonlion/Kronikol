using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Reports.SearchIndex;

namespace Kronikol.Tests.Reports;

/// <summary>
/// SEARCH_INDEX_PLAN §9.2: the deep-search index blob is emitted by default, absent on opt-out,
/// decodes to a doc table matching the scenario anchor ids in <c>allScenarios</c> order, and its
/// rows map corpus-only strings (note payloads, message text, example values) to the right doc.
/// The decoder here is an INDEPENDENT implementation of the §4.2 layout — it cross-checks the
/// serializer rather than trusting it.
/// </summary>
public class SearchIndexReportTests
{
    // ---------- independent §4.2 v1 decoder (test-side on purpose) ----------

    internal sealed record DecodedIndex(int Buckets, string[] DocAnchors, Dictionary<int, int[]> Rows)
    {
        internal int[] Candidates(string term)
        {
            var normalized = SearchNormalizer.Normalize(term).TrimEnd('\n');
            var buckets = new HashSet<int>();
            SearchIndexBuilder.AddTrigramBuckets(normalized, buckets);
            IEnumerable<int> candidates = Enumerable.Range(0, DocAnchors.Length);
            foreach (var b in buckets)
                candidates = Rows.TryGetValue(b, out var row) ? candidates.Intersect(row) : [];
            return candidates.ToArray();
        }
    }

    internal static string? ExtractBlobBase64(string html)
    {
        var m = Regex.Match(html, "<script id=\"kron-search-index\" type=\"application/json\">\"([^\"]*)\"</script>");
        return m.Success ? m.Groups[1].Value : null;
    }

    internal static DecodedIndex DecodeBlob(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        gzip.CopyTo(buffer);
        var raw = buffer.ToArray();

        Assert.Equal("KSI1", Encoding.ASCII.GetString(raw, 0, 4));
        Assert.Equal(1, raw[4]);
        var pos = 5;
        var buckets = (int)ReadU32Le(raw, ref pos);
        var docCount = (int)ReadU32Le(raw, ref pos);

        var anchors = new string[docCount];
        for (var d = 0; d < docCount; d++)
        {
            var len = (int)ReadVarint(raw, ref pos);
            anchors[d] = Encoding.UTF8.GetString(raw, pos, len);
            pos += len;
        }

        var bitsetBytes = (docCount + 7) >> 3;
        var rows = new Dictionary<int, int[]>();
        for (var b = 0; b < buckets; b++)
        {
            var payloadLen = (int)ReadVarint(raw, ref pos);
            if (payloadLen == 0) continue;
            var payloadEnd = pos + payloadLen;
            var encoding = raw[pos++];
            if (encoding == 1)
            {
                var ids = new List<int>();
                for (var d = 0; d < docCount; d++)
                    if ((raw[pos + (d >> 3)] & (1 << (d & 7))) != 0)
                        ids.Add(d);
                pos += bitsetBytes;
                rows[b] = ids.ToArray();
            }
            else
            {
                Assert.Equal(2, encoding);
                var count = (int)ReadVarint(raw, ref pos);
                var ids = new int[count];
                var prev = 0;
                for (var i = 0; i < count; i++)
                {
                    var v = (int)ReadVarint(raw, ref pos);
                    ids[i] = i == 0 ? v : prev + v;
                    prev = ids[i];
                }
                rows[b] = ids;
            }
            Assert.Equal(payloadEnd, pos);
        }
        Assert.Equal(raw.Length, pos);
        return new DecodedIndex(buckets, anchors, rows);
    }

    private static uint ReadU32Le(byte[] raw, ref int pos)
    {
        var v = (uint)(raw[pos] | (raw[pos + 1] << 8) | (raw[pos + 2] << 16) | (raw[pos + 3] << 24));
        pos += 4;
        return v;
    }

    private static uint ReadVarint(byte[] raw, ref int pos)
    {
        uint v = 0;
        var shift = 0;
        while ((raw[pos] & 128) != 0)
        {
            v |= (uint)(raw[pos++] & 127) << shift;
            shift += 7;
        }
        v |= (uint)raw[pos++] << shift;
        return v;
    }

    // ---------- fixtures ----------

    private static Feature[] TwoScenarioFeature() =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "Create order", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid request", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "s2", DisplayName = "Cancel order", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "an existing order", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private const string PayloadNeedle = "zqxfrobwidget-7734";
    private const string MessageNeedle = "vlurpqzt-endpoint";

    private static DefaultDiagramsFetcher.DiagramAsCode[] NeedleDiagrams() =>
    [
        new("s1", "", "@startuml\nA -> B : POST: /api/" + MessageNeedle + "\nnote left\n{\n  \"widget\": \"" + PayloadNeedle + "\"\n}\nend note\n@enduml"),
        new("s2", "", "@startuml\nA -> B : GET: /api/other\n@enduml")
    ];

    private static string Generate(string fileName,
        DefaultDiagramsFetcher.DiagramAsCode[] diagrams,
        Feature[] features,
        PlantUmlRendering rendering = PlantUmlRendering.BrowserJs,
        bool inlineSvg = false,
        bool fullSearchIndex = true)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: rendering,
            inlineSvgRendering: inlineSvg, fullSearchIndex: fullSearchIndex);
        return File.ReadAllText(path);
    }

    // ---------- facts ----------

    [Fact]
    public void Index_blob_is_emitted_by_default()
    {
        var html = Generate("SearchIndexDefault.html", NeedleDiagrams(), TwoScenarioFeature());
        Assert.NotNull(ExtractBlobBase64(html));
    }

    [Fact]
    public void Index_blob_is_absent_when_opted_out()
    {
        var html = Generate("SearchIndexOptOut.html", NeedleDiagrams(), TwoScenarioFeature(), fullSearchIndex: false);
        Assert.Null(ExtractBlobBase64(html));
        Assert.DoesNotContain("kron-search-index", html);
    }

    [Fact]
    public void Doc_table_matches_scenario_anchor_ids_in_document_order()
    {
        var html = Generate("SearchIndexDocTable.html", NeedleDiagrams(), TwoScenarioFeature());
        var decoded = DecodeBlob(ExtractBlobBase64(html)!);

        Assert.Equal(["scenario-create-order", "scenario-cancel-order"], decoded.DocAnchors);
        Assert.Equal(SearchIndexBuilder.BucketCount, decoded.Buckets);
    }

    [Fact]
    public void Payload_only_string_maps_to_the_right_doc()
    {
        var html = Generate("SearchIndexPayload.html", NeedleDiagrams(), TwoScenarioFeature());
        var decoded = DecodeBlob(ExtractBlobBase64(html)!);

        var candidates = decoded.Candidates(PayloadNeedle);
        Assert.Contains(0, candidates);
        Assert.DoesNotContain(1, candidates);
    }

    [Fact]
    public void Message_only_string_maps_to_the_right_doc()
    {
        var html = Generate("SearchIndexMessage.html", NeedleDiagrams(), TwoScenarioFeature());
        var decoded = DecodeBlob(ExtractBlobBase64(html)!);

        var candidates = decoded.Candidates(MessageNeedle);
        Assert.Contains(0, candidates);
        Assert.DoesNotContain(1, candidates);
    }

    [Fact]
    public void Img_mode_indexes_the_code_behind()
    {
        var html = Generate("SearchIndexImg.html", NeedleDiagrams(), TwoScenarioFeature(), rendering: PlantUmlRendering.Server);
        var decoded = DecodeBlob(ExtractBlobBase64(html)!);

        Assert.Contains(0, decoded.Candidates(PayloadNeedle));
    }

    [Fact]
    public void InlineSvg_mode_indexes_the_code_behind()
    {
        var html = Generate("SearchIndexInlineSvg.html", NeedleDiagrams(), TwoScenarioFeature(), rendering: PlantUmlRendering.Local, inlineSvg: true);
        var decoded = DecodeBlob(ExtractBlobBase64(html)!);

        Assert.Contains(0, decoded.Candidates(PayloadNeedle));
    }

    [Fact]
    public void Serialization_is_byte_stable_for_a_fixed_corpus()
    {
        var a = ExtractBlobBase64(Generate("SearchIndexStableA.html", NeedleDiagrams(), TwoScenarioFeature()));
        var b = ExtractBlobBase64(Generate("SearchIndexStableB.html", NeedleDiagrams(), TwoScenarioFeature()));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Index_is_not_emitted_for_a_report_with_no_scenarios()
    {
        var html = Generate("SearchIndexEmpty.html", [], [new Feature { DisplayName = "F1", Scenarios = [] }]);
        Assert.Null(ExtractBlobBase64(html));
    }

    [Fact]
    public void Parameterized_rows_are_docs_and_carry_the_group_search_text()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Withdraw $200", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$200" },
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "the account has funds", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Withdraw $500", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$500" },
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "the account has funds", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };
        var diagrams = new DefaultDiagramsFetcher.DiagramAsCode[]
        {
            new("s1", "", "@startuml\nA -> B : POST: /pay\nnote left\n{ \"tag\": \"" + PayloadNeedle + "\" }\nend note\n@enduml"),
            new("s2", "", "@startuml\nA -> B : POST: /pay\n@enduml")
        };

        var html = Generate("SearchIndexParam.html", diagrams, features);
        var decoded = DecodeBlob(ExtractBlobBase64(html)!);

        Assert.Equal(2, decoded.DocAnchors.Length);
        // payload needle lives only in s1's diagram -> only doc 0
        var candidates = decoded.Candidates(PayloadNeedle);
        Assert.Contains(0, candidates);
        Assert.DoesNotContain(1, candidates);
    }

    [Fact]
    public void Merge_path_flame_text_is_indexed_from_precomputed_html()
    {
        // Q-E: merged reports receive whole-test-flow only as precomputed HTML strings; the
        // flame text (source names, span names, marker labels) must still be indexed.
        var flameJson = "{\"s\":[\"Zorblatt.ActivitySource\"],\"f\":[[0,\"qmxspan-frobnicate\",0,100,0,5]],\"m\":[[0,\"GET: /api/qwyjibo-marker\"]]}";
        var compressed = typeof(Kronikol.InternalFlow.InternalFlowHtmlGenerator)
            .GetMethod("CompressToBase64", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [flameJson]) as string;
        var flameHtml = $"<div class=\"iflow-flame\" data-diagram-type=\"flamechart\" data-flame-z=\"{compressed}\"></div>";
        var precomputed = new Dictionary<string, Kronikol.Reports.Merge.WholeTestFlowFragment>
        {
            ["s1"] = new("", flameHtml, 1)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            [], TwoScenarioFeature(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "SearchIndexMergeFlame.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true,
            wholeTestVisualization: WholeTestFlowVisualization.FlameChart,
            precomputedWholeTestContent: precomputed);
        var html = File.ReadAllText(path);
        var decoded = DecodeBlob(ExtractBlobBase64(html)!);

        foreach (var needle in new[] { "qmxspan-frobnicate", "zorblatt.activitysource", "qwyjibo-marker" })
        {
            var candidates = decoded.Candidates(needle);
            Assert.Contains(0, candidates);
            Assert.DoesNotContain(1, candidates);
        }
    }

    [Fact]
    public void Example_values_are_searchable_via_group_data_search()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Row one", Result = ExecutionResult.Passed,
                        OutlineId = "grp",
                        ExampleValues = new Dictionary<string, string> { ["Code"] = "xkcd-9931-unique" },
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "a value", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Row two", Result = ExecutionResult.Passed,
                        OutlineId = "grp",
                        ExampleValues = new Dictionary<string, string> { ["Code"] = "other" },
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "a value", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };

        var html = Generate("SearchIndexExampleValues.html", [], features);

        // §1.2 coverage fix: example values appear in the group data-search AND the row data-row-search
        var dataSearch = Regex.Matches(html, @"data-search=""([^""]*)""").Select(m => m.Groups[1].Value)
            .FirstOrDefault(v => v.Contains("row one"));
        Assert.NotNull(dataSearch);
        Assert.Contains("xkcd-9931-unique", dataSearch);

        var rowSearches = Regex.Matches(html, @"data-row-search=""([^""]*)""").Select(m => m.Groups[1].Value).ToArray();
        Assert.Contains(rowSearches, v => v.Contains("xkcd-9931-unique"));
    }
}
