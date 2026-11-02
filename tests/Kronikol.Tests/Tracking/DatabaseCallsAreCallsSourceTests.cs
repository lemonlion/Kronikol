using System.Reflection;
using System.Text.RegularExpressions;
using Kronikol.Constants;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// No database package writes an event (plans/CALLS_AND_EVENTS_PLAN.md 5.3). <c>Event</c> means the call moves a message,
/// and three database trackers wrote it from the day they arrived with facts that pinned it, because nothing compared a tracker's
/// kind with what its category says it is. This reads the source, in the style of <see cref="CultureInvariantSourceTests"/>,
/// so the next tracker is refused where it is written, whatever its own facts assert.
/// </summary>
public class DatabaseCallsAreCallsSourceTests
{
    private static readonly Regex CategoryName = new(@"DependencyCategories\.(?<name>[A-Za-z]+)", RegexOptions.Compiled);
    private const string EventKind = "RequestResponseMetaType.Event";

    /// <summary>Maps every category to OTel's names and reads the token to tell a statement from a message.</summary>
    private static readonly HashSet<string> Exempt = new(StringComparer.Ordinal) { "Kronikol.Extensions.Otlp" };

    private static readonly Regex Comment = new(@"(^|\s)//.*$", RegexOptions.Compiled);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronikol.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root (Kronikol.sln)");
    }

    /// <summary>A line of source as code: its comment goes, and the two slashes of a <c>spanner://</c> address do not start one.</summary>
    private static string CodeOf(string line) => Comment.Replace(line, "");

    private static bool WritesAnEvent(string source) => source.Contains(EventKind, StringComparison.Ordinal);

    /// <summary>The name of each constant whose value <see cref="DependencyCategories.IsStatementShaped"/> calls a statement.</summary>
    private static HashSet<string> StatementShapedNames() =>
        typeof(DependencyCategories).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true } && DependencyCategories.IsStatementShaped((string?)f.GetRawConstantValue()))
            .Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>A package's source without its comments: a sentence about an event writes none.</summary>
    private static string[] SourceOf(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => string.Join('\n', File.ReadLines(f).Select(CodeOf)))
            .ToArray();

    /// <summary>Each package whose source names a statement-shaped category, with the categories it names.</summary>
    private static Dictionary<string, string[]> DatabasePackages()
    {
        var statements = StatementShapedNames();
        var src = Path.Combine(RepositoryRoot(), "src");
        var folders = Directory.GetDirectories(src, "Kronikol.Extensions.*").Append(Path.Combine(src, "Kronikol", "Sql"));
        var packages = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var named = SourceOf(folder).SelectMany(text => CategoryName.Matches(text).Select(m => m.Groups["name"].Value))
                .Where(statements.Contains).Distinct(StringComparer.Ordinal).ToArray();
            if (named.Length > 0)
                packages[Path.GetRelativePath(src, folder).Replace('\\', '/')] = named;
        }
        return packages;
    }

    [Fact]
    public void No_database_package_writes_an_event()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var findings = DatabasePackages().Keys
            .Where(package => !Exempt.Contains(package))
            .Where(package => SourceOf(Path.Combine(src, package)).Any(WritesAnEvent))
            .ToArray();

        Assert.True(findings.Length == 0, $"These write {EventKind} on a database's calls, where Event means the call moves a message: {string.Join(", ", findings)}");
    }

    /// <summary>A walk that read nothing would pass the fact above: the three that once broke it have to be on its list.</summary>
    [Theory]
    [InlineData("Kronikol.Extensions.Spanner", DependencyCategories.Spanner)]
    [InlineData("Kronikol.Extensions.BigQuery", DependencyCategories.BigQuery)]
    [InlineData("Kronikol.Extensions.Bigtable", DependencyCategories.Bigtable)]
    public void The_walk_reads_the_packages_that_once_broke_the_rule(string package, string category)
    {
        var packages = DatabasePackages();

        Assert.True(packages.TryGetValue(package, out var named), $"{package} is not among {string.Join(", ", packages.Keys)}");
        Assert.Contains(category, named);
    }

    /// <summary>The guard's own test, on the lines it exists to refuse: a guard that cannot fail guards nothing.</summary>
    [Theory]
    [InlineData("            MetaType: RequestResponseMetaType.Event,", true)]
    [InlineData("var (uri, kind) = (\"spanner://x\", RequestResponseMetaType.Event);", true)]
    [InlineData("kind = RequestResponseMetaType.Event; // a statement", true)]
    [InlineData("// MetaType: RequestResponseMetaType.Event, as it once was", false)]
    [InlineData("kind = RequestResponseMetaType.Default; // never RequestResponseMetaType.Event", false)]
    public void The_guard_reads_a_line_as_code(string line, bool refused) =>
        Assert.Equal(refused, WritesAnEvent(CodeOf(line)));

    /// <summary>The word the guard looks for is the enum's: a typo there would refuse nothing.</summary>
    [Fact]
    public void The_kind_the_guard_looks_for_is_the_enums_own_name() =>
        Assert.Equal(EventKind, $"{nameof(Kronikol.Tracking.RequestResponseMetaType)}.{nameof(Kronikol.Tracking.RequestResponseMetaType.Event)}");
}
