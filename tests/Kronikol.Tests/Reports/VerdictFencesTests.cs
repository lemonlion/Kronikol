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
/// </summary>
public class VerdictFencesTests
{
    public static TheoryData<string, string, string> Pins => new()
    {
        // script, default variant (no verdicts), verdict variant
        { "advanced-search.js", "bd95ec17e218564784d4868f54985392751c51772ed5ee593cb63fbb8f2c9d34", "f8e26b59e269fcebd3bfc36c251ac06264946049b46c8157cd24740a98fc0c7c" },
        { "report-scenario-feature-map-helper.js", "d3d00e12dfb1ae86fce149860f5ffbe970a43af27dd8b80c0a39f3438a187202", "235ffa5649c597fa43429959c0eacc0d90f7bbcdb5f7a740b6636a5ecd076caa" },
        { "report-search-function.js", "dbdfcbe5ea893e6dd89a1412c8e5dd1ff423afc22dccc927ff686c1544e84daf", "e8a0c76ac338519d83d2f84163d29f1761deb1c5e9f2fba880fc0239925401ac" },
        { "report-search-index.js", "7efeaa109b0ac98cb76f510dc9ea0ddeb0324428bd6c9169e08f8bf85b77028a", "58b3e65874a3a7bce211f85517f802d69f5d27f6b0649e925ead354f26a33bbb" },
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
