using System.Security.Cryptography;
using System.Text;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The fenced <c>$verdict</c> lines of the report scripts (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md section 3.5):
/// a report that draws no verdict attributes ships the scripts as they were before cross-run history, and one that
/// does ships 4.5.0's bytes. The pins are SHA-256 of the text with line endings normalized, taken from
/// <c>git show v3.10.0:</c> and <c>git show v4.5.0:</c> (<c>report-search-index.js</c>'s default is 4.5.0's with
/// c9c3b068 taken back out, since 512bc85a changed it later for another reason).
/// </summary>
public class VerdictFencesTests
{
    public static TheoryData<string, string, string> Pins => new()
    {
        // script, default variant (no verdicts), verdict variant
        { "advanced-search.js", "afd1116b5f7503078e8a63e50722369e3172005c172ffbbc6291d24accc40e0f", "8d36e435e27dda669ee677f7076753a2740e56c1beef21ff8b3fe1b367e4f4c5" },
        { "report-scenario-feature-map-helper.js", "7f5e95719910693ad1bd9485e5b403d76033cd3a71dd8b686e5e21fb988a005d", "e35b119ed77e70919df4f055d230ea85c629e2c8da442a7d6441dc7bcec7debf" },
        { "report-search-function.js", "dcc01b87c6ed9a72dd3c2fc4b0c0ab6ea20e95c6f107a89f9e6c9290855956ff", "657c528a4cc3eac8239fb08c8bcdcecb08f852cbed27d7b34c2256818cd059f8" },
        { "report-search-index.js", "a0f8962e2a2522ac420c3fd34581b3b18715042c9da4be98e5f527016f112a3c", "89576c647084ef6438a3af4706fcdeca53bf0b9f685d935030e9847a2a9c964f" },
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
