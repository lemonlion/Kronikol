using System.Security.Cryptography;
using System.Text;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The fenced <c>$verdict</c> lines of the report scripts (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md section 3.5):
/// a report that draws no verdict attributes ships the scripts as they were before cross-run history, and one that
/// does ships 4.5.0's bytes. The pins are SHA-256 of the text with line endings normalized, taken from
/// <c>git show v3.10.0:</c> and <c>git show v4.5.0:</c> (<c>report-search-index.js</c>'s default is 4.5.0's with
/// c9c3b068 taken back out, since 512bc85a changed it later for another reason). 5.0.0 changed
/// <c>report-scenario-feature-map-helper.js</c> and <c>report-search-function.js</c> for the filter pipeline
/// (plans/OPTION_C_PLAN.md S5 and S6: the filters' flags and readout, the search's qualifiers), and S7 the search
/// function's help toggle and <c>report-search-index.js</c>'s chip, placed inside the search field, so their pins are
/// 5.0.0's text, and the Kronikol4J divergence ledger records it; the verdict lines in them are 4.5.0's.
/// plans/TAGS_PLAN.md T2 changed all four again, outside their fences but for the filter cache's flags: a scenario's
/// tags read from <c>data-tags</c> in place of <c>data-labels</c> and <c>data-categories</c>, no categories flag, and
/// <c>@"a tag"</c> for a name with spaces. 5.2.0 changed the same two as 5.0.0 did, outside their fences
/// (plans/BACK_BUTTON_NAVIGATION_PLAN.md): the helper holds the visibility pass while an address is put on the page
/// (<c>hold_visibility</c>, <c>release_visibility</c>), and the search opens a single match's feature, closes on
/// emptying only what it opened, and answers at once on Enter or when emptied; their fenced lines are unchanged.
/// plans/RUN_WINDOW_PLAN.md R1 changed the helper outside its fences: a scenario that is not a test
/// (<c>data-not-a-test</c>) is no run, the readout counts the features that hold a test, and it marks when nothing at all
/// is shown; its fenced lines are unchanged. The audit of that plan's releases changed the helper outside its fences again:
/// the readout reads as filtered while any scenario is hidden, one that is not a test included.
/// 5.2.7 changed the search function again outside its fences (the review of 5.0.0): a qualifier is read by where it
/// stands, the legacy and or or it leaves goes with it, and a tag expression reads both sides of each operator and the
/// brackets written against its tags.
/// 5.5.4 changed the search index script outside its fences (plans/HTML_COMPRESSION_PLAN.md F2): its worker builds the
/// streams it gunzips from the bytes, where it read them out of a Blob, which WebKit refuses inside a worker. 5.6.0 changed
/// it outside its fences too (plans/HTML_COMPRESSION_PLAN.md R1, 4.4): on a compressed page the worker reads a diagram's
/// source from its piece of the page's zstd frames, and every other item as it is, where it gunzipped each. 5.9.1
/// changed it outside its fences twice (plans/REPORT_UI_FIXES_PLAN.md): the chip it writes in the filter field says it is
/// matching, not searching (I9), and its normaliser drops a step bar cell's markup and reads a cell's line break (B2, B3).
/// The same release changed the advanced filter's status term in both variants (E3): `$skipped` keeps a scenario skipped
/// after a failure, as the summary counts it.
/// source from its piece of the page's zstd frames, and every other item as it is, where it gunzipped each.
/// plans/SEARCH_PLAN.md changed the search function outside its fences: emptying the search takes its marks off an
/// outline's rows and puts back a row it chose (B5), and the help no longer sets the box's help-open class, the rows
/// never folding (F5). It changed the search index script outside its fences too (4.5.7): the chip that said inside the
/// search field where deep search was went with the field, and the script tells it as a <c>kron-deep</c> event, which the
/// Search tab's bar and counts say.
/// plans/SEARCH_PLAN.md 4.4 changed <c>advanced-search.js</c> and the search function outside their fences:
/// <c>tag:</c>, <c>status:</c> and a <c>-</c> before them are tokens of the advanced search, a query is cut into spans
/// for the query builder (B4 follows 5.9.1's E3 above, so <c>status:skipped</c> finds a scenario skipped after a failure), and
/// the search function reads <c>feature:</c> and a <c>-</c> before any qualifier and names a <c>tag:</c> or
/// <c>status:</c> it cannot read. They added one fenced block, to the search function: the verdicts a report's scenarios
/// carry, which <c>status:</c> reads beside the execution results; the fenced lines that were there are unchanged.
/// plans/SEARCH_PLAN.md 4.6.2 to 4.6.5 changed the search index script outside its fences: its normaliser's passes are one
/// list the search text, the display text and the map back to the input are made from, and its worker keeps each piece of
/// a scenario's text with the places in it, reports the kinds a match hit and answers the lines asked for a result shown.
/// 5.12.0 changed three of them outside their fences, to keep the search apart from the filter field (the owner: a search
/// is no filter): the search function reads a query into flags with one matcher that the field's run and the search's
/// own run share, each keeping flags of its own; the search index script runs a deep pass for each of the two queries;
/// and the feature map helper gives every scenario the search's flags and shows or hides the scenarios by a test it is
/// handed, which the Search tab's exports use.
/// </summary>
public class VerdictFencesTests
{
    public static TheoryData<string, string, string> Pins => new()
    {
        // script, default variant (no verdicts), verdict variant
        { "advanced-search.js", "1543f8d6b072935b1b5a7f4838a4f6a3315a2b3c689026b3f099a116e55c0fb2", "e15cf8d1c84cb2cd0fc4c44bd3e237ca1dd37398d3028b0f813a1409e11a51a7" },
        { "report-scenario-feature-map-helper.js", "1fabc946b6ce384a3c0192f482831af92667c8a571c94271c17d039f823c5da2", "094f7e44413e058c7cdf5974a7484626e0af4be7cc150d6f8aa875fd953a0342" },
        { "report-search-function.js", "ca606c27299739f1eb55e6e95d27101f5a776211e71bdb179e13b93bef539614", "8a5d3099d562dd314d8881d42ed83e5ff3bb2cb77e2362bc213a401c859599c3" },
        { "report-search-index.js", "f5ee023813b98316c2ccd5b4648694a99f0d5fe6c76ec947c5e071989327e927", "3c308633251e41fd5f8a3adb4f59cf64220b71b957dbeae31d1baeaddf797e42" },
    };

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n").TrimStart('﻿'))));

    [Theory]
    [MemberData(nameof(Pins))]
    public void Each_variant_is_the_bytes_it_was_before(string script, string withoutVerdicts, string withVerdicts)
    {
        Assert.Equal(withoutVerdicts, Sha256(ReportGenerator.LoadScript(script, verdicts: false)));
        Assert.Equal(withVerdicts, Sha256(ReportGenerator.LoadScript(script, verdicts: true)));
    }

    [Theory]
    [MemberData(nameof(Pins))]
    public void No_variant_ships_a_marker_or_a_stand_in_line(string script, string withoutVerdicts, string withVerdicts)
    {
        _ = (withoutVerdicts, withVerdicts);
        foreach (var verdicts in new[] { false, true })
        {
            var lines = ReportGenerator.LoadScript(script, verdicts).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            Assert.DoesNotContain(lines, l => l.StartsWith("// kron:", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.StartsWith(VerdictFences.ElsePrefix, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Every_embedded_script_has_balanced_fences()
    {
        var assembly = typeof(ReportGenerator).Assembly;
        var scripts = assembly.GetManifestResourceNames().Where(n => n.EndsWith(".js", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(scripts);
        var fenced = 0;
        foreach (var name in scripts)
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            var text = reader.ReadToEnd();
            if (text.Contains(VerdictFences.Open, StringComparison.Ordinal))
                fenced++;
            // Throws on an unbalanced fence.
            VerdictFences.Variant(text, verdicts: true);
            VerdictFences.Variant(text, verdicts: false);
        }

        Assert.Equal(4, fenced);
    }

    [Fact]
    public void A_block_ships_its_lines_with_verdicts_and_its_stand_ins_without()
    {
        const string source = "a\n// kron:verdicts\nb(v)\n// kron:else\n//|b()\n// kron:/verdicts\nc\n// kron:verdicts\nd\n// kron:/verdicts\ne\n";

        Assert.Equal("a\nb(v)\nc\nd\ne\n", VerdictFences.Variant(source, verdicts: true));
        Assert.Equal("a\nb()\nc\ne\n", VerdictFences.Variant(source, verdicts: false));
    }

    [Fact]
    public void Line_endings_are_kept_and_markers_are_read_through_them()
    {
        const string source = "a\r\n// kron:verdicts\r\nb(v)\r\n// kron:else\r\n//|b()\r\n// kron:/verdicts\r\nc\r\n";

        Assert.Equal("a\r\nb(v)\r\nc\r\n", VerdictFences.Variant(source, verdicts: true));
        Assert.Equal("a\r\nb()\r\nc\r\n", VerdictFences.Variant(source, verdicts: false));
    }

    [Theory]
    [InlineData("a\n// kron:verdicts\nb\n", "never closed")]
    [InlineData("a\n// kron:/verdicts\nb\n", "a close without an open")]
    [InlineData("a\n// kron:else\n//|b\n", "an else outside a fence")]
    [InlineData("// kron:verdicts\n// kron:verdicts\nb\n// kron:/verdicts\n", "inside another")]
    [InlineData("// kron:verdicts\nb\n// kron:else\nb()\n// kron:/verdicts\n", "does not start //|")]
    public void An_unbalanced_fence_is_refused_whichever_variant_is_asked_for(string source, string reason)
    {
        foreach (var verdicts in new[] { false, true })
            Assert.Contains(reason, Assert.Throws<InvalidOperationException>(() => VerdictFences.Variant(source, verdicts)).Message);
    }
}
