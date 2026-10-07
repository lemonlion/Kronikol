using System.Text.RegularExpressions;

namespace Kronikol.Tests;

/// <summary>
/// No number or date in the shipped code is formatted with the machine's culture where its format puts a separator, a
/// calendar or a time separator into the text (plans/WARM_UP_PLAN.md F12, R0). Until 4.7.3 the data files, the report,
/// the query engine and the extension packages' messages each wrote their own: <c>1,234</c> seconds in XML and YAML,
/// <c>width:12,3%</c> in the timeline, <c>2569-01-01</c> as the run's start on a Thai machine. The culture facts
/// (<c>CultureInvariantOutputTests</c>, <c>CultureInvariantPipelineTests</c>, <c>QueryCultureTests</c>) read what the
/// writers produce; this one reads the source, so a format added later without the invariant culture is refused here,
/// in the file and line it was written on, before any output carries it.
/// </summary>
/// <remarks>
/// A format is allowed when the statement holding it names <c>CultureInfo.InvariantCulture</c> (a
/// <c>string.Create</c>, an <c>Append</c> or a <c>ToString</c> taking it) or is <c>FormattableString.Invariant</c>.
/// <c>src/Kronikol/Query</c> is not read: <c>QueryCommand.Run</c> runs the whole engine under the invariant culture,
/// and <c>QueryCultureTests</c> holds every verb to it. Formats that cannot hold a separator (<c>F0</c>, <c>0</c>) are
/// not read either: they print digits only for the non-negative values they are given.
/// </remarks>
public class CultureInvariantSourceTests
{
    /// <summary>An interpolation hole whose format writes a decimal or thousands separator, or a date or time.</summary>
    private static readonly Regex Hole = new(
        @"\{[^{}""]+?:(?<format>N\d+|F[1-9]\d*|P\d+|E\d+|G\d*|0\.[0#]+|#[,#0.]+|0,[0#]+|[yMdHhms][yMdHhms:./ \-'TZ]*[yMdHhms'Z])\}",
        RegexOptions.Compiled);

    /// <summary>A ToString with such a format and no provider.</summary>
    private static readonly Regex ToStringCall = new(
        @"\.ToString\(""(?<format>N\d+|F[1-9]\d*|P\d+|E\d+|G\d*|0\.[0#]+|#[,#0.]+|[yMdHhms][^""]*)""\)",
        RegexOptions.Compiled);

    private static readonly string[] Allowed =
    [
        "CultureInfo.InvariantCulture",
        "FormattableString.Invariant",
        "NumberFormatInfo.InvariantInfo",
        "DateTimeFormatInfo.InvariantInfo",
    ];

    [Fact]
    public void Every_format_with_a_separator_or_a_date_names_the_invariant_culture()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var findings = new List<string>();
        var read = 0;

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.StartsWith("Kronikol/Query/", StringComparison.Ordinal))
                continue;

            read++;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                foreach (Match match in Hole.Matches(line).Concat(ToStringCall.Matches(line)))
                {
                    if (!Statement(lines, i).Contains("$\"", StringComparison.Ordinal) && match.Value.StartsWith('{'))
                        continue; // a brace in a plain string, not an interpolation
                    if (Allowed.Any(a => Statement(lines, i).Contains(a, StringComparison.Ordinal)))
                        continue;
                    findings.Add($"{relative}:{i + 1}: {match.Value}");
                }
            }
        }

        Assert.True(read > 500, $"read {read} files under {src}");
        Assert.True(findings.Count == 0, "Formatted with the machine's culture:\n  " + string.Join("\n  ", findings));
    }

    [Theory]
    [InlineData("var s = $\"{seconds:F3}\";", true)]
    [InlineData("var s = $\"{count:N0} spans\";", true)]
    [InlineData("var s = $\"{ratio:0.##}x\";", true)]
    [InlineData("var s = $\"{at:yyyy-MM-dd}\";", true)]
    [InlineData("var s = $\"{at:HH:mm:ss}Z\";", true)]
    [InlineData("var s = $\"{run.At:yyyy-MM-dd'T'HH:mm:ss'Z'}\";", true)]
    [InlineData("var s = seconds.ToString(\"F3\");", true)]
    [InlineData("var s = at.ToString(\"yyyy-MM-ddTHH:mm:ssZ\");", true)]
    [InlineData("var s = string.Create(CultureInfo.InvariantCulture, $\"{seconds:F3}\");", false)]
    [InlineData("var s = seconds.ToString(\"F3\", CultureInfo.InvariantCulture);", false)]
    [InlineData("var s = $\"{ms:F0} ms\";", false)]
    [InlineData("var s = $\"{ms:0} ms\";", false)]
    [InlineData("var s = $\"{id:N}\";", false)]
    [InlineData("var s = value.ToString(\"G\");", true)]
    public void The_rule_finds_what_it_is_for(string line, bool found)
    {
        // The control: each shape the rule exists for is found on its own, and each allowed one is not, so a rule that
        // matched nothing could not pass the fact above.
        var matched = (Hole.IsMatch(line) || ToStringCall.IsMatch(line)) && !Allowed.Any(a => line.Contains(a, StringComparison.Ordinal));

        Assert.Equal(found, matched);
    }

    /// <summary>The line and the ones before it back to the end of the previous statement or block.</summary>
    private static string Statement(string[] lines, int index)
    {
        var start = index;
        while (start > 0)
        {
            var previous = lines[start - 1].TrimEnd();
            if (previous.EndsWith(';') || previous.EndsWith('{') || previous.EndsWith('}') || previous.Length == 0)
                break;
            start--;
        }

        return string.Join("\n", lines[start..(index + 1)]);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronikol.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Kronikol.sln not found above " + AppContext.BaseDirectory);
    }
}
