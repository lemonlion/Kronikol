using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.Tests.Tool;
using static Kronikol.Tests.Packaging.FloorLedger;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// Every packable <c>src/</c> project held to <see cref="FloorLedger"/> (<c>plans/CLIENT_FLOORS_PLAN.md</c> 5.2): no
/// dependency a consumer gets floats unless the ledger marks it still floating, every fixed one is in the ledger at its
/// version, and the still-floating and unmeasured marks only go. A reference a consumer does not get is left out: one
/// with <c>PrivateAssets</c> <c>all</c>, which StepTracking and AssertionTracking use for the Mono.Cecil they bundle.
/// Test projects may float; they are never packed.
/// </summary>
public class FloorLedgerTests
{
    /// <summary>A consumer-facing reference in a packable project.</summary>
    private sealed record Reference(string Project, string Package, string Version);

    private static readonly Lazy<List<Reference>> References = new(Read);

    private static List<Reference> Read()
    {
        var src = Path.Combine(BuiltTool.RepoRoot, "src");
        var references = new List<Reference>();
        foreach (var path in Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories))
        {
            if (Regex.IsMatch(path, @"[\\/](bin|obj)[\\/]"))
                continue;
            var project = XDocument.Load(path);
            if (project.Descendants("IsPackable").Any(p => p.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)))
                continue;
            foreach (var reference in project.Descendants("PackageReference"))
            {
                var privateAssets = (string?)reference.Attribute("PrivateAssets") ?? (string?)reference.Element("PrivateAssets") ?? "";
                if (privateAssets.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
                    continue;
                var package = (string?)reference.Attribute("Include")
                    ?? throw new InvalidOperationException($"{Path.GetFileName(path)}: a PackageReference with no Include");
                var version = (string?)reference.Attribute("Version") ?? (string?)reference.Element("Version")
                    ?? throw new InvalidOperationException($"{Path.GetFileName(path)}: {package} has no Version");
                references.Add(new Reference(Path.GetFileNameWithoutExtension(path), package, version));
            }
        }
        return references;
    }

    private static Entry? EntryFor(Reference reference) =>
        Entries.SingleOrDefault(e => e.Project == reference.Project && e.Package == reference.Package);

    [Fact]
    public void No_packable_project_floats_a_dependency_unless_the_ledger_says_which_batch_measures_it()
    {
        var floats = References.Value.Where(r => r.Version.Contains('*')).ToList();

        var unmarked = floats
            .Where(r => EntryFor(r) is not { Basis: Basis.StillFloating } entry || entry.Version != r.Version)
            .Select(r => $"{r.Project}: {r.Package} {r.Version}")
            .ToList();
        Assert.True(unmarked.Count == 0, "floating, and not marked still floating in FloorLedger:\n" + string.Join("\n", unmarked));
    }

    [Fact]
    public void Every_fixed_dependency_a_consumer_gets_is_in_the_ledger_at_its_version()
    {
        var pins = References.Value.Where(r => !r.Version.Contains('*')).ToList();

        Assert.NotEmpty(pins);
        var missing = pins
            .Where(r => EntryFor(r) is not { } entry || entry.Version != r.Version || entry.Basis == Basis.StillFloating)
            .Select(r => $"{r.Project}: {r.Package} {r.Version} (ledger: {EntryFor(r)?.Version ?? "none"})")
            .ToList();
        Assert.True(missing.Count == 0, "a fixed reference the ledger does not hold at its version:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_entry_names_a_reference_its_project_has()
    {
        var stale = Entries
            .Where(e => !References.Value.Any(r => r.Project == e.Project && r.Package == e.Package))
            .Select(e => $"{e.Project}: {e.Package}")
            .ToList();

        Assert.True(stale.Count == 0, "in the ledger but in no packable csproj:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void A_project_references_a_package_once_and_the_ledger_holds_it_once()
    {
        var twice = References.Value.GroupBy(r => (r.Project, r.Package)).Where(g => g.Count() > 1).Select(g => $"{g.Key.Project}: {g.Key.Package}")
            .Concat(Entries.GroupBy(e => (e.Project, e.Package)).Where(g => g.Count() > 1).Select(g => $"ledger {g.Key.Project}: {g.Key.Package}"))
            .ToList();

        Assert.True(twice.Count == 0, "named twice:\n" + string.Join("\n", twice));
    }

    [Fact]
    public void The_still_floating_and_unmeasured_marks_only_go()
    {
        Assert.InRange(Entries.Count(e => e.Basis == Basis.StillFloating), 0, StillFloatingAtMost);
        Assert.InRange(Entries.Count(e => e.Basis == Basis.Unmeasured), 0, UnmeasuredAtMost);
    }

    [Fact]
    public void Each_entry_says_what_its_version_rests_on()
    {
        var bad = Entries
            .Where(e => e.Basis switch
            {
                Basis.Measured => string.IsNullOrWhiteSpace(e.Evidence) || e.Version.Contains('*'),
                Basis.StillFloating => !Regex.IsMatch(e.Evidence, @"^R2[a-c]$") || !e.Version.Contains('*'),
                _ => !Regex.IsMatch(e.Evidence, @"^R3[a-c]$") || e.Version.Contains('*')
            })
            .Select(e => $"{e.Project}: {e.Package} {e.Version} {e.Basis} '{e.Evidence}'")
            .ToList();

        Assert.True(bad.Count == 0, "an entry whose version and basis disagree, or with no evidence or batch:\n" + string.Join("\n", bad));
    }
}
