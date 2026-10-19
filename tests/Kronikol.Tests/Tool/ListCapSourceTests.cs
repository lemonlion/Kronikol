using System.Reflection;
using System.Text.RegularExpressions;

namespace Kronikol.Tests.Tool;

/// <summary>
/// No list in <c>kronikol query</c> or the failures digest is cut where no one decided it may be (#161,
/// plans/LIST_CAPS_PLAN.md R2). A <c>Take</c> or <c>TakeLast</c> with a constant is a cap, and a cap that says nothing
/// reads as the whole list: until 4.29.0 twelve of them did. Each one is in <see cref="ListCapRegistry"/> with what
/// answers for it, and this reads the source, so a cap added later without an entry is refused in the file and line it
/// was written on. A cap written as a counter is outside what this can see; the census rows hold those.
/// </summary>
public class ListCapSourceTests
{
    /// <summary>A Take or TakeLast whose argument is a number or a constant's name.</summary>
    private static readonly Regex ConstantTake = new(@"\.Take(?:Last)?\(\s*(?:\d+|[A-Z]\w*)\s*\)", RegexOptions.Compiled);

    private static readonly string[] Files =
    [
        .. Directory.EnumerateFiles(Path.Combine(Source(), "Query"), "*.cs").Select(f => "Query/" + Path.GetFileName(f)),
        "Reports/FailuresDigestGenerator.cs",
    ];

    [Fact]
    public void Every_cap_in_the_query_tool_and_the_digest_is_in_the_registry()
    {
        var findings = Files.SelectMany(file => Findings(file, File.ReadAllLines(Path.Combine(Source(), file)))).ToArray();

        Assert.True(Files.Length > 20, $"read {Files.Length} files");
        Assert.True(findings.Length == 0,
            "A cap with no entry in ListCapRegistry (give it a census row, the fact that holds it, or the reason it needs none):\n  "
            + string.Join("\n  ", findings));
    }

    [Fact]
    public void Every_entry_is_on_the_lines_it_says()
    {
        // An entry whose code moved or went would cover nothing, and one whose code was copied would cover the copy.
        var wrong = ListCapRegistry.Caps
            .Select(cap => (cap, found: File.ReadAllLines(Path.Combine(Source(), cap.File)).Count(l => !Comment(l) && l.Contains(cap.Code, StringComparison.Ordinal))))
            .Where(x => x.found != x.cap.Lines)
            .Select(x => $"{x.cap}: on {x.found} line(s), not {x.cap.Lines}")
            .ToArray();

        Assert.True(wrong.Length == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void Every_entry_says_what_answers_for_it()
    {
        Assert.All(ListCapRegistry.Caps, cap => Assert.True(cap.Census is null != cap.Reason is null,
            $"{cap}: a census row, or a reason, and not both"));
        Assert.All(ListCapRegistry.Caps.Where(c => c.HeldBy is not null), cap => Assert.True(cap.Reason is not null, $"{cap}: what holds it says what it reads"));
    }

    [Fact]
    public void Every_fact_an_entry_names_is_a_fact()
    {
        var tests = typeof(ListCapSourceTests).Assembly.GetTypes();
        foreach (var cap in ListCapRegistry.Caps.Where(c => c.HeldBy is not null))
        {
            var (type, method) = (cap.HeldBy![..cap.HeldBy!.IndexOf('.')], cap.HeldBy[(cap.HeldBy.IndexOf('.') + 1)..]);
            var found = tests.Where(t => t.Name == type).Select(t => t.GetMethod(method, BindingFlags.Public | BindingFlags.Instance)).SingleOrDefault(m => m is not null);
            Assert.True(found is not null, $"{cap}: no test {cap.HeldBy}");
            Assert.True(found.GetCustomAttributes().Any(a => a is FactAttribute), $"{cap}: {cap.HeldBy} is not a fact or a theory");
        }
    }

    [Theory]
    [InlineData("foreach (var row in rows.Take(5))", true)]
    [InlineData("foreach (var point in entry.Points.TakeLast(MaxRunsListed))", true)]
    [InlineData("var first = xs.Take( 3 ).ToArray();", true)]
    [InlineData("foreach (var row in rows.Take(limit))", false)]
    [InlineData("var cut = all.Skip(head).Take(all.Count - cap).ToList();", false)]
    [InlineData("var parameters = QueryWriter.CutList(step.Parameters, 8, 8);", false)]
    [InlineData("// foreach (var row in rows.Take(5))", false)]
    public void The_rule_finds_what_it_is_for(string line, bool found) =>
        Assert.Equal(found, Findings("Query/Planted.cs", [line]).Any());

    [Fact]
    public void A_cap_planted_in_a_real_file_is_found_on_its_line()
    {
        // The control: the same file read as the fact above reads it, with one silent cap added. A rule that matched
        // nothing, or a registry that covered any line of a file, would pass the fact above and fail this.
        const string file = "Query/QueryCommand.Overview.cs";
        var lines = File.ReadAllLines(Path.Combine(Source(), file)).ToList();
        Assert.Empty(Findings(file, [.. lines]));

        var at = lines.FindIndex(l => l.Contains(".Take(MaxStatusesShown)", StringComparison.Ordinal)) + 1;
        lines.Insert(at, "            foreach (var row in rows.Take(5))");

        Assert.Equal([$"{file}:{at + 1}: foreach (var row in rows.Take(5))"], Findings(file, [.. lines]));
    }

    /// <summary>The lines of <paramref name="file"/> with a cap no entry for that file covers.</summary>
    private static IEnumerable<string> Findings(string file, string[] lines)
    {
        var covering = ListCapRegistry.Caps.Where(c => c.File == file).ToArray();
        for (var i = 0; i < lines.Length; i++)
            if (!Comment(lines[i]) && ConstantTake.IsMatch(lines[i]) && !covering.Any(c => lines[i].Contains(c.Code, StringComparison.Ordinal)))
                yield return $"{file}:{i + 1}: {lines[i].Trim()}";
    }

    private static bool Comment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronikol.sln")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Kronikol.sln not found above " + AppContext.BaseDirectory), "src", "Kronikol");
    }
}
