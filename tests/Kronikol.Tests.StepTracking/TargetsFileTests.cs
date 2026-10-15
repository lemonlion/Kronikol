using System.Text.RegularExpressions;

namespace Kronikol.Tests.StepTracking;

/// <summary>
/// Validates that the .targets file generates valid C# source files.
/// MSBuild's WriteLinesToFile treats ';' as a list separator in the Lines property,
/// so semicolons must be escaped as %3B to produce correct output.
/// See: https://github.com/lemonlion/Kronikol/issues/46
/// </summary>
public class TargetsFileTests
{
    private static readonly string TargetsFilePath = Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Kronikol.StepTracking", "build",
            "Kronikol.StepTracking.targets"));

    [Fact]
    public void TargetsFile_WriteLinesToFile_DoesNotContainUnescapedSemicolons()
    {
        var content = File.ReadAllText(TargetsFilePath);

        // Extract all Lines="..." attribute values
        var linesPattern = new Regex(@"Lines=""([^""]+)""", RegexOptions.Singleline);
        var matches = linesPattern.Matches(content);

        Assert.True(matches.Count > 0, "Should find WriteLinesToFile Lines attributes");

        foreach (Match match in matches)
        {
            var linesValue = match.Groups[1].Value;

            // Remove valid encodings: %3B (MSBuild-escaped semicolons) and XML entities (&lt; &gt; etc.)
            var withoutEscaped = Regex.Replace(linesValue, @"%3B|&\w+;", "");
            Assert.DoesNotContain(";", withoutEscaped);
        }
    }

    [Fact]
    public void TargetsFile_GeneratedAttributes_ProduceValidCSharp()
    {
        var content = File.ReadAllText(TargetsFilePath);

        var linesPattern = new Regex(@"Lines=""([^""]+)""", RegexOptions.Singleline);
        var matches = linesPattern.Matches(content);

        foreach (Match match in matches)
        {
            var linesValue = match.Groups[1].Value;

            // Simulate what WriteLinesToFile produces: %3B → ;, &lt; → <, &gt; → >
            var decoded = linesValue
                .Replace("%3B", ";")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">");

            // Every attribute with a Description property should have valid auto-property syntax
            if (decoded.Contains("Description"))
            {
                Assert.Contains("{ get; set; }", decoded);
                Assert.DoesNotContain("{ get\n", decoded);
                Assert.DoesNotContain("{ get\r", decoded);
            }
        }
    }

    // WEAVER_SOURCE_PATHS_PLAN's audit (2026-10-15), as for the assertion weaver's targets, whose real builds show both:
    // a file written on every build is newer than the assembly, so every build compiled the project again; and the
    // attributes are C#, which a Visual Basic or F# project that imports the targets cannot compile.
    [Fact]
    public void TargetsFile_writes_each_attribute_only_when_it_changes_and_for_CSharp_only()
    {
        var targets = System.Xml.Linq.XDocument.Load(TargetsFilePath).Root!;
        var generate = targets.Elements("Target").Single(t => (string?)t.Attribute("Name") == "_GenerateStepTrackingAttributes");
        var writes = generate.Elements("WriteLinesToFile").ToList();

        Assert.NotEmpty(writes);
        Assert.All(writes, w => Assert.Equal("true", (string?)w.Attribute("WriteOnlyWhenDifferent")));
        Assert.Contains("'$(Language)' == 'C#'", (string?)generate.Attribute("Condition"));
    }
}
