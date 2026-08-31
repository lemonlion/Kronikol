using System.Text.Json;
using Jint;
using Jint.Native;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// Executes the REAL shipped deep-search JS (report-search-index.js, plus the advanced-search
/// and search-function scripts it shares helpers with) via Jint, pinned to the shared
/// cross-language vectors — the same file the C# unit tests assert against. This is the drift
/// guard between the generation-side C# and the client-side JS.
/// </summary>
public class SearchIndexJintTests : IDisposable
{
    private readonly Engine _engine;

    public SearchIndexJintTests()
    {
        _engine = new Engine();
        foreach (var resource in new[] { "advanced-search.js", "report-search-function.js", "report-search-index.js" })
            _engine.Execute(LoadEmbedded(resource));
    }

    private static string LoadEmbedded(string name)
    {
        var assembly = typeof(Kronikol.Reports.ReportGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource {name} not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonDocument LoadVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "shared-vectors", "search-index-vectors.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Shipped_normalization_matches_every_vector()
    {
        using var vectors = LoadVectors();
        foreach (var v in vectors.RootElement.GetProperty("normalization").EnumerateArray())
        {
            var name = v.GetProperty("name").GetString();
            var input = v.GetProperty("input").GetString()!;
            var expected = v.GetProperty("expected").GetString()!;
            var actual = _engine.Invoke("kronNormalizeForSearch", input).AsString();
            Assert.True(expected == actual, $"shipped-JS normalization vector '{name}' diverged");
        }
    }

    [Fact]
    public void Shipped_trigram_buckets_match_every_vector()
    {
        using var vectors = LoadVectors();
        foreach (var v in vectors.RootElement.GetProperty("trigrams").EnumerateArray())
        {
            var text = v.GetProperty("text").GetString()!;
            var expected = v.GetProperty("buckets65536").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var arr = _engine.Invoke("kronTrigramBuckets", text, 65536).AsArray();
            var actual = new List<int>();
            for (uint i = 0; i < arr.Length; i++) actual.Add((int)arr[i].AsNumber());
            actual.Sort();
            Assert.Equal(expected, actual);
        }
    }

    private void LoadSerializationVectorIndex(string caseName = "serialization")
    {
        using var vectors = LoadVectors();
        var raw = Convert.FromBase64String(vectors.RootElement.GetProperty(caseName).GetProperty("rawBase64").GetString()!);
        _engine.SetValue("_rawBytes", raw.Select(b => (int)b).ToArray());
        _engine.Execute("var _ix = kronDecodeSearchIndex(Uint8Array.from(_rawBytes));");
    }

    [Fact]
    public void Shipped_decoder_reads_the_serialization_vector()
    {
        using var vectors = LoadVectors();
        var expectedAnchors = vectors.RootElement.GetProperty("serialization").GetProperty("docAnchors")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        LoadSerializationVectorIndex();
        Assert.Equal(65536, (int)_engine.Evaluate("_ix.buckets").AsNumber());
        Assert.Equal(expectedAnchors.Length, (int)_engine.Evaluate("_ix.docCount").AsNumber());
        for (var d = 0; d < expectedAnchors.Length; d++)
            Assert.Equal(expectedAnchors[d], _engine.Evaluate($"_ix.anchors[{d}]").AsString());
    }

    [Theory]
    // vector docs: 0 = "the first document body", 1 = "the second document body", 2 = "a third thing entirely"
    [InlineData("first document", new[] { 0 })]
    [InlineData("document body", new[] { 0, 1 })]
    [InlineData("entirely", new[] { 2 })]
    [InlineData("zzz-not-there", new int[0])]
    public void Shipped_candidates_match_ground_truth_on_the_vector_corpus(string query, int[] expectedDocs)
    {
        LoadSerializationVectorIndex();
        _engine.SetValue("_q", query);
        var arr = _engine.Evaluate("kronCandidateDocsForQuery(_ix, _q)").AsArray();
        var actual = new List<int>();
        for (uint i = 0; i < arr.Length; i++) actual.Add((int)arr[i].AsNumber());
        Assert.Equal(expectedDocs, actual);
    }

    [Fact]
    public void Shipped_candidates_never_prune_under_negation_or_tags()
    {
        LoadSerializationVectorIndex();
        foreach (var query in new[] { "!! first", "@sometag", "$failed", "!! first || entirely" })
        {
            _engine.SetValue("_q", query);
            var count = (int)_engine.Evaluate("kronCandidateDocsForQuery(_ix, _q).length").AsNumber();
            Assert.Equal(3, count);
        }
    }

    [Fact]
    public void Shipped_decoder_reads_the_sparse_serialization_vector()
    {
        // 20 docs (bitsetBytes = 3): this vector holds BOTH row encodings, so the decoder's
        // varint-list arm — never reachable below 17 docs — is exercised here.
        using var vectors = LoadVectors();
        var expectedAnchors = vectors.RootElement.GetProperty("serializationSparse").GetProperty("docAnchors")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        LoadSerializationVectorIndex("serializationSparse");
        Assert.Equal(20, expectedAnchors.Length);
        Assert.Equal(20, (int)_engine.Evaluate("_ix.docCount").AsNumber());
        for (var d = 0; d < expectedAnchors.Length; d++)
            Assert.Equal(expectedAnchors[d], _engine.Evaluate($"_ix.anchors[{d}]").AsString());
    }

    [Theory]
    // sparse-vector docs: "doc <d> unique-token-<d>-xyz shared warehouse phrase" for d = 0..19
    [InlineData("unique-token-7-xyz", new[] { 7 })]
    [InlineData("unique-token-13-xyz", new[] { 13 })]
    [InlineData("warehouse", new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 })]
    [InlineData("zzz-not-there", new int[0])]
    public void Shipped_candidates_match_ground_truth_on_the_sparse_vector_corpus(string query, int[] expectedDocs)
    {
        LoadSerializationVectorIndex("serializationSparse");
        _engine.SetValue("_q", query);
        var arr = _engine.Evaluate("kronCandidateDocsForQuery(_ix, _q)").AsArray();
        var actual = new List<int>();
        for (uint i = 0; i < arr.Length; i++) actual.Add((int)arr[i].AsNumber());
        Assert.Equal(expectedDocs, actual);
    }

    [Fact]
    public void Shipped_or_query_unions_candidate_sets()
    {
        LoadSerializationVectorIndex();
        _engine.SetValue("_q", "\"first document\" || entirely");
        var arr = _engine.Evaluate("kronCandidateDocsForQuery(_ix, _q)").AsArray();
        var actual = new List<int>();
        for (uint i = 0; i < arr.Length; i++) actual.Add((int)arr[i].AsNumber());
        Assert.Equal([0, 2], actual);
    }

    private bool DeepMatch(string input, string corpus, string[] tags, string status)
    {
        _engine.SetValue("_input", input);
        _engine.SetValue("_corpus", corpus);
        _engine.SetValue("_tags", tags);
        _engine.SetValue("_status", status);
        return _engine.Evaluate(
            "kronDeepMatchesItem(_input, kronNormalizeQueryText(_input), kronNormalizeForSearch(_corpus), new Set(_tags), _status)").AsBoolean();
    }

    [Fact]
    public void Deep_match_finds_payload_text_in_a_joined_corpus_piece()
    {
        var corpus = "create order some steps\n@startuml\nnote left\n{ \"widget\": \"zqxfrob-123\" }\nend note\n@enduml";
        Assert.True(DeepMatch("zqxfrob-123", corpus, [], "Passed"));
        Assert.False(DeepMatch("not-there-at-all", corpus, [], "Passed"));
    }

    [Fact]
    public void Deep_match_phrases_cannot_span_piece_boundaries()
    {
        // pieces are '\n'-joined; a phrase with a space must not match across the join
        var corpus = "ends with alpha\nbeta starts here";
        Assert.False(DeepMatch("\"alpha beta\"", corpus, [], "Passed"));
        Assert.True(DeepMatch("\"beta starts\"", corpus, [], "Passed"));
    }

    [Fact]
    public void Deep_match_negation_is_authoritative_over_the_full_corpus()
    {
        var corpus = "create order\npayload contains frobnicate";
        Assert.False(DeepMatch("order && !! frobnicate", corpus, [], "Passed"));
        Assert.True(DeepMatch("order && !! missingword", corpus, [], "Passed"));
    }

    [Fact]
    public void Deep_match_legacy_tokens_and_tags_compose()
    {
        var corpus = "create order\nsome payload text";
        Assert.True(DeepMatch("payload @smoke", corpus, ["smoke"], "Passed"));
        Assert.False(DeepMatch("payload @smoke", corpus, ["other"], "Passed"));
        Assert.True(DeepMatch("create payload", corpus, [], "Passed"));
    }

    [Fact]
    public void Deep_match_normalizes_query_like_corpus()
    {
        // corpus side is chunk-rejoined and case-folded; the query goes through the same rules
        var corpus = "note left\nAAAA\nBBBB\nend note";
        Assert.True(DeepMatch("aaaabbbb", corpus, [], "Passed"));
    }

    [Theory]
    [InlineData("ab", false)]                      // too short
    [InlineData("abc", true)]
    [InlineData("@tagonly", false)]                // metadata only
    [InlineData("$failed", false)]
    [InlineData("ab && $failed", false)]           // no >=3 text term
    [InlineData("payload && $failed", true)]
    [InlineData("\"quoted phrase\"", true)]
    [InlineData("", false)]
    public void Deep_eligibility_requires_a_text_term_of_three_or_more(string input, bool expected)
    {
        _engine.SetValue("_input", input);
        Assert.Equal(expected, _engine.Evaluate("kronIsDeepEligible(_input)").AsBoolean());
    }

    [Theory]
    // Deep results may REMOVE a shallow match only for queries that can genuinely turn negative
    // with more text — advanced queries containing !!. Everything else is add-only, so a raw
    // shallow match always survives normalization asymmetries.
    [InlineData("payload", false)]                     // legacy
    [InlineData("payload widget", false)]              // legacy multi-token
    [InlineData("payload @smoke", false)]              // legacy + tag
    [InlineData("alpha && beta", false)]               // advanced, no negation
    [InlineData("\"a phrase\" || other", false)]
    [InlineData("!! frobnicate", true)]
    [InlineData("order && !! frobnicate", true)]
    [InlineData("a || (b && !! c)", true)]             // nested negation
    [InlineData("", false)]
    public void Query_can_remove_only_under_advanced_negation(string input, bool expected)
    {
        _engine.SetValue("_input", input);
        Assert.Equal(expected, _engine.Evaluate("kronQueryCanRemove(_input)").AsBoolean());
    }

    [Fact]
    public void Flame_text_extraction_matches_generator_order()
    {
        _engine.SetValue("_json", "{\"s\":[\"SourceA\"],\"f\":[[0,\"span-one\",0,1,0,2],[0,\"span-two\",1,2,1,3]],\"m\":[[5,\"GET: /marker\"]]}");
        Assert.Equal("SourceA\nspan-one\nspan-two\nGET: /marker",
            _engine.Evaluate("kronExtractFlameText(_json)").AsString());
    }

    public void Dispose() => _engine.Dispose();
}
