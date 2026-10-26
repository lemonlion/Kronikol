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

    // SEARCH_PLAN 4.6.3 (W3): the display text runs the same passes without the ASCII fold, the later passes matching
    // any case, so it is the search text but for the case of ASCII letters, and a range found in one is the same range
    // in the other
    [Fact]
    public void Display_text_folds_to_the_search_text_for_every_vector()
    {
        using var vectors = LoadVectors();
        foreach (var v in vectors.RootElement.GetProperty("normalization").EnumerateArray())
        {
            var name = v.GetProperty("name").GetString();
            var input = v.GetProperty("input").GetString()!;
            var expected = v.GetProperty("expected").GetString()!;
            var display = _engine.Invoke("kronNormalizeForDisplay", input).AsString();
            Assert.True(AsciiFold(display) == expected, $"the display text of vector '{name}' does not fold to its search text: {display}");
        }
    }

    [Theory]
    [InlineData("Hello WORLD MixedCase", "Hello WORLD MixedCase\n")]
    [InlineData("<COLOR:red>Flour</COLOR> and <b>Oats</B>", "Flour and Oats\n")]
    [InlineData("POST: /A\\N  B", "POST: /AB\n")]
    [InlineData("<U+0045>ND <U+0065>nd", "END end\n")]
    [InlineData("X ~*Bold~*", "X *Bold*\n")]
    [InlineData("Alpha<U+200B><U+200B>\nBeta", "Alpha Beta\n")]
    public void Display_text_keeps_the_case_the_search_text_folds(string input, string display)
    {
        Assert.Equal(display, _engine.Invoke("kronNormalizeForDisplay", input).AsString());
    }

    [Fact]
    public void A_query_s_display_text_keeps_its_tildes_as_its_search_text_does()
    {
        Assert.Equal("\"~\" ~/X ~*\n", _engine.Invoke("kronNormalizeForDisplay", "\"~\" ~/X ~*", true).AsString());
    }

    // SEARCH_PLAN 4.6.5 (H26): the same passes, keeping where each run of the text came from: what a pass writes in place
    // of a match maps to the whole match, a character a pass keeps to itself
    [Fact]
    public void kronNormalizeWithMap_is_the_search_text_and_maps_each_character_back()
    {
        using var vectors = LoadVectors();
        foreach (var v in vectors.RootElement.GetProperty("normalization").EnumerateArray())
        {
            var name = v.GetProperty("name").GetString()!;
            var input = v.GetProperty("input").GetString()!;
            var (text, runs) = WithMap(input);
            Assert.True(v.GetProperty("expected").GetString() == text, $"the mapped text of vector '{name}' is not its search text: {text}");
            AssertRunsTile(name, input, text, runs);
        }

        // a code point to its letter, an escape to what it escapes, a tab and a marked break to a space
        Assert.Equal("<U+0045>", Behind("<U+0045>ND NOTE", 0, 1));
        Assert.Equal("~*", Behind("a ~*b", 2, 3));
        Assert.Equal("\t", Behind("a\tb", 1, 2));
        Assert.Equal("<U+200B><U+200B>\n", Behind("alpha<U+200B><U+200B>\nbeta", 5, 6));

        // across passes: a decoded < that opens a tag, and a decoded backslash before an n
        Assert.Equal("flour\n", WithMap("<U+003C>color:red>flour").Text);
        Assert.Equal("flour", Behind("<U+003C>color:red>flour", 0, 5));
        Assert.Equal("get: /ab\n", WithMap("GET: /a<U+005C>nb").Text);
        Assert.Equal("GET: /a", Behind("GET: /a<U+005C>nb", 0, 7));
        Assert.Equal("b", Behind("GET: /a<U+005C>nb", 7, 8));
    }

    [Fact]
    public void A_character_a_pass_keeps_maps_to_itself_and_the_end_of_the_text_to_the_end_of_the_input()
    {
        // of two spaces the first is kept and the second goes; the newline the last pass adds came from nowhere
        Assert.Equal(" ", Behind("a  b", 1, 2));
        Assert.Equal("a  b", Behind("a  b", 0, 3));
        Assert.Equal("", Behind("a  b", 3, 4));
        // 1b writes back the character it reads before the marker
        Assert.Equal("a", Behind("a<U+200B>\nb", 0, 1));
        Assert.Equal("b", Behind("a<U+200B>\nb", 1, 2));
        // a decoded character outside the BMP is two units of the text from one code point
        Assert.Equal("<U+1F600>", Behind("x<U+1F600>y", 1, 3));
        Assert.Equal("", WithMap("").Text.TrimEnd('\n'));
    }

    // A place inside what a pass takes out goes where it was taken out: a line's start inside a tag that runs over two lines,
    // and one inside a run of spaces after the space the run keeps
    [Fact]
    public void A_place_inside_what_a_pass_takes_out_moves_to_where_it_was_taken_out()
    {
        _engine.SetValue("__in", "a <b\nc> flour\nd");
        var piece = JsonDocument.Parse(_engine.Evaluate("JSON.stringify(kronCorpusPiece(__in))").AsString()).RootElement;
        Assert.Equal("a flour\nd\n", piece.GetProperty("text").GetString());
        Assert.Equal([0, 2, 8], piece.GetProperty("rows").EnumerateArray().Select(r => r.GetInt32()));

        _engine.SetValue("__in", "a   b");
        // "a b": the place after the space kept, inside the two that go, and before b are all before b
        Assert.Equal("[0,2,2,2]", _engine.Evaluate("JSON.stringify(kronNormalizeRun(__in, false, [0, 2, 3, 4], false, false).bounds)").AsString());
    }

    // The shipped normaliser is the reference's on any text, not only on the vectors (the passes became one list); and the
    // display and the mapped texts with it
    [Fact]
    public void The_shipped_normaliser_reads_any_text_as_the_reference_does()
    {
        LoadReference();
        _engine.SetValue("__texts", RandomTexts(2000));
        var mismatch = _engine.Evaluate("""
            (function () {
                for (var i = 0; i < __texts.length; i++) {
                    var s = __texts[i];
                    if (kronNormalizeForSearch(s) !== __reference(s)) return JSON.stringify(s);
                }
                return '';
            })()
            """).AsString();
        Assert.True(mismatch == "", $"the shipped normaliser and the reference differ on {mismatch}");
    }

    [Fact]
    public void Display_text_and_the_map_read_any_text_as_the_reference_does()
    {
        LoadReference();
        _engine.SetValue("__texts", RandomTexts(2000));
        var mismatch = _engine.Evaluate("""
            (function () {
                function fold(t) { return t.replace(/[A-Z]/g, function (c) { return c.toLowerCase(); }); }
                for (var i = 0; i < __texts.length; i++) {
                    var s = __texts[i], ref = __reference(s);
                    if (fold(kronNormalizeForDisplay(s)) !== ref) return 'display ' + JSON.stringify(s);
                    if (kronNormalizeWithMap(s).text !== ref) return 'map ' + JSON.stringify(s);
                }
                return '';
            })()
            """).AsString();
        Assert.True(mismatch == "", $"differs from the reference: {mismatch}");
    }

    // A piece's lines are found after its line breaks while the passes keep them all, and followed through the passes from
    // the first that does not (a marked break, a tag over two lines, a decoded line break): either way each line starts
    // where following every line's start through every pass puts it; and the letters the fold lowered are the capitals of
    // the display text
    [Fact]
    public void A_piece_s_lines_and_letters_are_where_the_passes_put_them()
    {
        LoadReference();
        _engine.SetValue("__texts", RandomTexts(2000));
        var result = _engine.Evaluate("""
            (function () {
                var kept = 0, moved = 0;
                for (var i = 0; i < __texts.length; i++) {
                    var s = __texts[i], piece = kronCorpusPiece(s), starts = [0], capitals = [];
                    for (var at = s.indexOf('\n'); at >= 0; at = s.indexOf('\n', at + 1)) starts.push(at + 1);
                    var followed = kronNormalizeRun(s, false, starts, false, false).bounds, display = kronNormalizeForDisplay(s);
                    for (var c = 0; c < display.length; c++) if (display.charCodeAt(c) >= 65 && display.charCodeAt(c) <= 90) capitals.push(c);
                    if (piece.text !== __reference(s)) return 'text ' + JSON.stringify(s);
                    if (JSON.stringify(piece.rows) !== JSON.stringify(followed)) return 'rows ' + JSON.stringify(s);
                    if (JSON.stringify(piece.cases) !== JSON.stringify(capitals)) return 'letters ' + JSON.stringify(s);
                    if (piece.text.split('\n').length - 2 === starts.length - 1) kept++; else moved++;
                }
                return kept + ' ' + moved;
            })()
            """).AsString();
        var counts = result.Split(' ');
        Assert.True(counts.Length == 2, $"differs on {result}");
        // both ways were taken
        Assert.True(int.Parse(counts[0]) > 0 && int.Parse(counts[1]) > 0, $"line breaks kept in {counts[0]} texts and moved in {counts[1]}");
    }

    private void LoadReference()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "search-bench", "normalize.js");
        _engine.Execute("var module = { exports: {} };\n" + File.ReadAllText(path) + "\nvar __reference = module.exports.normalizeForSearch;");
    }

    // Texts made of what the passes act on, in any order: code points (a marker, a letter, a <, a backslash, a newline, one
    // outside the BMP, one that is no character), escapes, the marked breaks, tags in either case, literal \n escapes,
    // spaces and tabs, CR and LF, and letters of both cases inside and outside ASCII
    private static string[] RandomTexts(int count)
    {
        string[] pieces =
        [
            "a", "B", "z", "Q", "é", "İ", "K", " ", "  ", "\t", "\n", "\r\n", "\r", "~", "~*", "~/", "~<", "~\"",
            "<U+200B>", "<U+200B><U+200B>", "<U+0041>", "<U+0045>", "<U+003C>", "<U+005C>", "<U+000A>", "<U+1F600>",
            "<U+D800>", "<u+0041>", "<b>", "</b>", "<B>", "<color:red>", "<COLOR:#123456>", "</color>", "<i>",
            "<font color=x>", "<size:3>", "<back:red>", "\\n", "\\N", "\\n  ", "-", "\"", "#", "=", "[", ">", "<", ":"
        ];
        var random = new Random(4636);
        return Enumerable.Range(0, count)
            .Select(_ => string.Concat(Enumerable.Range(0, random.Next(1, 40)).Select(_ => pieces[random.Next(pieces.Length)])))
            .ToArray();
    }

    private static string AsciiFold(string s) => string.Concat(s.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c));

    private (string Text, int[] Runs) WithMap(string input)
    {
        _engine.SetValue("__in", input);
        var mapped = JsonDocument.Parse(_engine.Evaluate("JSON.stringify(kronNormalizeWithMap(__in))").AsString()).RootElement;
        return (mapped.GetProperty("text").GetString()!, mapped.GetProperty("runs").EnumerateArray().Select(r => r.GetInt32()).ToArray());
    }

    // the input behind the text from `from` to `to`
    private string Behind(string input, int from, int to)
    {
        _engine.SetValue("__in", input);
        var range = JsonDocument.Parse(_engine.Evaluate($"JSON.stringify(kronMapBack(kronNormalizeWithMap(__in).runs, {from}, {to}))").AsString()).RootElement;
        var start = range[0].GetInt32();
        return input.Substring(start, range[1].GetInt32() - start);
    }

    // the runs cover the text in order from its start to its end, and what they came from goes forward through the input
    private static void AssertRunsTile(string name, string input, string text, int[] runs)
    {
        Assert.True(runs.Length % 4 == 0, $"vector '{name}': runs come in fours");
        var at = 0;
        var inAt = 0;
        for (var k = 0; k < runs.Length; k += 4)
        {
            int oS = runs[k], oE = runs[k + 1], iS = runs[k + 2], iE = runs[k + 3];
            Assert.True(oS == at && oE > oS, $"vector '{name}': run {k / 4} is [{oS}, {oE}) after {at}");
            Assert.True(iS >= inAt && iE >= iS && iE <= input.Length, $"vector '{name}': run {k / 4} came from [{iS}, {iE}) after {inAt}");
            if (oE - oS == iE - iS)
                for (var j = 0; j < oE - oS; j++)
                    if (char.IsAsciiLetterOrDigit(input[iS + j]))
                        Assert.True(char.ToLowerInvariant(input[iS + j]) == text[oS + j], $"vector '{name}': {text[oS + j]} at {oS + j} is not {input[iS + j]} folded");
            at = oE;
            inAt = iE;
        }
        Assert.True(at == text.Length, $"vector '{name}': the runs end at {at} of {text.Length}");
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

    // SEARCH_PLAN 4.4: tag: and status: are tags and statuses to the index too, never text to find, and a - before them
    // is a negation; only the text beside them narrows the candidates
    [Theory]
    [InlineData("tag:sometag", new[] { 0, 1, 2 })]
    [InlineData("status:failed", new[] { 0, 1, 2 })]
    [InlineData("-tag:first", new[] { 0, 1, 2 })]
    [InlineData("-status:passed || entirely", new[] { 0, 1, 2 })]
    [InlineData("first tag:x", new[] { 0 })]
    [InlineData("entirely && -status:passed", new[] { 2 })]
    public void Shipped_candidates_never_prune_by_a_tag_or_status_qualifier(string query, int[] expectedDocs)
    {
        LoadSerializationVectorIndex();
        _engine.SetValue("_q", query);
        var arr = _engine.Evaluate("kronCandidateDocsForQuery(_ix, _q)").AsArray();
        var actual = new List<int>();
        for (uint i = 0; i < arr.Length; i++) actual.Add((int)arr[i].AsNumber());
        Assert.Equal(expectedDocs, actual);
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
    public void Deep_match_finds_captured_text_the_generator_wrote_as_code_points()
    {
        // 3.30.1: the generator writes a captured line-initial quote, a builtin call and a literal tilde as
        // code points; the engine paints the characters, and a reader types what they see.
        var corpus = "note left\n<U+0027>a', 'b'\n<U+0025>date() stays\npath \"<U+007E>\" and <U+007E>/.bashrc\nend note";
        Assert.True(DeepMatch("'a', 'b'", corpus, [], "Passed"));
        Assert.True(DeepMatch("%date()", corpus, [], "Passed"));
        Assert.True(DeepMatch("\"~\"", corpus, [], "Passed"));
        Assert.True(DeepMatch("~/.bashrc", corpus, [], "Passed"));
    }

    [Fact]
    public void A_query_keeps_its_tildes()
    {
        // A query holds none of the generator's escapes: what a reader types is what they saw.
        Assert.Equal("\"~\" ~/x ~*", _engine.Invoke("kronNormalizeQueryText", "\"~\" ~/X ~*").AsString());
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
        // corpus side is chunk-rejoined and case-folded; the query goes through the same rules.
        // The rejoin keys on the join marker the generator writes (rule 1b) rather than on the old
        // flush-left guess, so the fixture carries one — as real generated output now does.
        var corpus = "note left\nAAAA<U+200B>\nBBBB\nend note";
        Assert.True(DeepMatch("aaaabbbb", corpus, [], "Passed"));

        // And the case the flush-left guess got wrong: an UNMARKED break is the payload's own, so
        // the two lines stay two terms.
        Assert.False(DeepMatch("aaaabbbb", "note left\nAAAA\nBBBB\nend note", [], "Passed"));
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
