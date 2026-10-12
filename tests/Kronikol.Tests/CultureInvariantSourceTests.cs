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
        @"\{[^{}""]+?:(?<format>[Nn]\d+|[Ff][1-9]\d*|[Pp]\d+|[Ee]\d+|G\d*|0\.[0#]+|#[,#0.]+|0,[0#]+|[yMdHhms][yMdHhms:./ \-'TZ]*[yMdHhms'Z])\}",
        RegexOptions.Compiled);

    /// <summary>A ToString with such a format and no provider.</summary>
    private static readonly Regex ToStringCall = new(
        @"\.ToString\(""(?<format>[Nn]\d+|[Ff][1-9]\d*|[Pp]\d+|[Ee]\d+|G\d*|0\.[0#]+|#[,#0.]+|[yMdHhms][^""]*)""\)",
        RegexOptions.Compiled);

    /// <summary>A call that reads format items out of a plain string, as an interpolation reads its holes.</summary>
    private static readonly Regex CompositeFormat = new(@"\b(?:[Ss]tring\.Format|AppendFormat)\(", RegexOptions.Compiled);

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
                if (!lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    findings.AddRange(Findings(lines, i).Select(found => $"{relative}:{i + 1}: {found}"));
        }

        Assert.True(read > 500, $"read {read} files under {src}");
        Assert.True(findings.Count == 0, "Formatted with the machine's culture:\n  " + string.Join("\n  ", findings));
    }

    /// <summary>
    /// The shapes the SQL and Redis captures wrote a value with, each taking the machine's culture: a scalar result's
    /// <c>ToString()</c>, a parameter's bare <c>{p.Value}</c> or <c>{val}</c>, a row count's <c>{rowsAffected.Value}</c>
    /// (ADO.NET answers -1 for a statement that changes no rows) and a Redis number's <c>ToString()</c>.
    /// </summary>
    private static readonly Regex CaptureValue = new(
        @"\bresult\.ToString\(\)|\{p\.Value\}|\{val\}|\{rowsAffected\.Value\}|\b[ld]\.ToString\(\)",
        RegexOptions.Compiled);

    private static readonly string[] ValueCapturers =
    [
        "Kronikol/Sql", "Kronikol.Extensions.SqlClient", "Kronikol.Extensions.Npgsql", "Kronikol.Extensions.MySqlConnector",
        "Kronikol.Extensions.Oracle", "Kronikol.Extensions.Sqlite", "Kronikol.Extensions.Dapper",
        "Kronikol.Extensions.EfCore.Relational", "Kronikol.Extensions.ClickHouse", "Kronikol.Extensions.ClickHouse.Client",
        "Kronikol.Extensions.ClickHouse.Driver", "Kronikol.Extensions.ClickHouse.Octonica", "Kronikol.Extensions.Spanner",
        "Kronikol.Extensions.Redis",
    ];

    [Fact]
    public void A_value_a_database_capture_writes_as_text_does_not_take_the_machines_culture()
    {
        // Until 4.16.3 SELECT 2.5 answered 2,5 on a German machine, beside rows of the same report written 2.5 (as
        // JSON); a value is written with SqlRowFormatter.ValueText, or with the invariant culture where it is a number.
        var src = Path.Combine(RepositoryRoot(), "src");
        var findings = new List<string>();
        var read = 0;

        foreach (var directory in ValueCapturers)
        foreach (var file in Directory.EnumerateFiles(Path.Combine(src, directory), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
                continue;

            read++;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (!lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    findings.AddRange(CaptureValue.Matches(lines[i]).Select(match => $"{relative}:{i + 1}: {match.Value}"));
        }

        Assert.True(read > 40, $"read {read} files under {src}");
        Assert.True(findings.Count == 0, "Written with the machine's culture:\n  " + string.Join("\n  ", findings));
    }

    [Theory]
    [InlineData("var str = result.ToString() ?? \"\";", true)]
    [InlineData(".Select(p => $\"{p.ParameterName}={p.Value}\"));", true)]
    [InlineData("paramStrs.Add($\"{name}={val}\");", true)]
    [InlineData("? $\"{rowsAffected.Value} rows affected\"", true)]
    [InlineData("double d => d.ToString(),", true)]
    [InlineData("var str = SqlRowFormatter.ValueText(result);", false)]
    [InlineData("? $\"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected\"", false)]
    [InlineData("double d => d.ToString(CultureInfo.InvariantCulture),", false)]
    public void The_capture_rule_finds_what_it_is_for(string line, bool found) => Assert.Equal(found, CaptureValue.IsMatch(line));

    /// <summary>
    /// A casing that takes the machine's culture: Turkish and Azeri give <c>i</c> a dotted capital and <c>I</c> a dotless
    /// small letter, so a name cased with them reads otherwise (<c>İt rejects…</c>) and its scenario's <c>stableId</c>
    /// with it. Until 4.16.3 the title resolver, the feature names, camel-cased names and the step weaver did.
    /// </summary>
    private static readonly Regex CultureCasing = new(
        @"\.ToUpper\(\)|\.ToLower\(\)|\bchar\.ToUpper\(|\bchar\.ToLower\(|CurrentCulture\.TextInfo|CurrentCultureIgnoreCase",
        RegexOptions.Compiled);

    [Fact]
    public void No_casing_takes_the_machines_culture()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var findings = new List<string>();
        var read = 0;

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
                continue;

            read++;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (!lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    findings.AddRange(CultureCasing.Matches(lines[i]).Select(match => $"{relative}:{i + 1}: {match.Value}"));
        }

        Assert.True(read > 500, $"read {read} files under {src}");
        Assert.True(findings.Count == 0, "Cased with the machine's culture:\n  " + string.Join("\n  ", findings));
    }

    [Theory]
    [InlineData("var title = name.ToUpper();", true)]
    [InlineData("result = result.ToLower();", true)]
    [InlineData("text = char.ToUpper(text[0]) + text.Substring(1);", true)]
    [InlineData("? $\"{char.ToLower(word[0])}{word[1..]}\"", true)]
    [InlineData(": CultureInfo.CurrentCulture.TextInfo.ToTitleCase(humanized);", true)]
    [InlineData("a.Equals(b, StringComparison.CurrentCultureIgnoreCase)", true)]
    [InlineData("var title = name.ToUpperInvariant();", false)]
    [InlineData("text = char.ToUpperInvariant(text[0]) + text.Substring(1);", false)]
    [InlineData(": CultureInfo.InvariantCulture.TextInfo.ToTitleCase(humanized);", false)]
    [InlineData("a.Equals(b, StringComparison.OrdinalIgnoreCase)", false)]
    public void The_casing_rule_finds_what_it_is_for(string line, bool found) => Assert.Equal(found, CultureCasing.IsMatch(line));

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
    [InlineData("var s = $@\"{seconds:F3} s\";", true)]
    [InlineData("var s = $\"{seconds:f3}\";", true)]
    [InlineData("var s = count.ToString(\"n0\");", true)]
    [InlineData("var s = string.Format(\"{0:F2}s\", seconds);", true)]
    [InlineData("builder.AppendFormat(\"{0:N0} calls\", count);", true)]
    [InlineData("var s = string.Format(CultureInfo.InvariantCulture, \"{0:F2}s\", seconds);", false)]
    [InlineData("var s = \"{seconds:F3}\";", false)]
    public void The_rule_finds_what_it_is_for(string line, bool found)
    {
        // The control: each shape the rule exists for is found on its own, and each allowed one is not, so a rule that
        // matched nothing could not pass the fact above.
        Assert.Equal(found, Findings([line], 0).Any());
    }

    /// <summary>
    /// The formats on line <paramref name="index"/> that take the machine's culture: a hole of an interpolated string
    /// (<c>$"</c>, <c>$@"</c> or <c>@$"</c>) or a format item of <c>string.Format</c> or <c>AppendFormat</c>, and a
    /// <c>ToString</c> with such a format, unless the statement names the invariant culture. A verbatim interpolation, a
    /// lowercase specifier (<c>f1</c> is <c>F1</c>) and a composite format went unread until 4.16.3.
    /// </summary>
    private static IEnumerable<string> Findings(string[] lines, int index)
    {
        var statement = Statement(lines, index);
        var interpolated = statement.Contains("$\"", StringComparison.Ordinal) || statement.Contains("$@\"", StringComparison.Ordinal);
        foreach (Match match in Hole.Matches(lines[index]).Concat(ToStringCall.Matches(lines[index])))
        {
            if (match.Value.StartsWith('{') && !interpolated && !CompositeFormat.IsMatch(statement))
                continue; // a brace in a plain string, not a hole or a format item
            if (Allowed.Any(a => statement.Contains(a, StringComparison.Ordinal)))
                continue;
            yield return match.Value;
        }
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
