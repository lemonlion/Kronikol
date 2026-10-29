using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// What a packed project asks its consumers for, and what it carries. NuGet packs a fixed <c>Version</c> as a floor (<c>&gt;=</c>) and a
/// floating one as the newest version of the day, so the nuspec, not the csproj, is what a consumer's restore meets
/// (<c>plans/CLIENT_FLOORS_PLAN.md</c>). Linked into each test project that reads its package's floor back; it throws
/// rather than asserts, so a suite on any test framework can use it.
/// </summary>
internal static class PackedNuspec
{
    /// <summary>The repository root, from <c>tests/&lt;project&gt;/bin/&lt;configuration&gt;/&lt;framework&gt;/</c>.</summary>
    public static string RepoRoot { get; } = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>
    /// Where the packed <c>src/<paramref name="project"/></c> and <see cref="FloorLedger"/> disagree, one line each:
    /// every dependency the ledger gives the project must be in every one of the nuspec's dependency groups at the
    /// ledger's version, so moving a floor means moving it in the csproj and the ledger alike. Empty when they agree.
    /// </summary>
    public static List<string> DisagreementsWithLedger(string project)
    {
        var entries = FloorLedger.Entries.Where(e => e.Project == project).ToList();
        if (entries.Count == 0)
            return [$"{project}: the ledger has no entry for it"];
        var packed = Dependencies($"src/{project}/{project}.csproj");
        // A nuspec with no dependency group at all still owes each entry its version.
        var groups = packed.Select(d => d.Group).Distinct().DefaultIfEmpty("(none)").ToList();
        var lines = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Basis != FloorLedger.Basis.Measured)
                lines.Add($"{entry.Package}: the ledger marks {entry.Version} {entry.Basis} ({entry.Evidence}), not a measured floor");
            foreach (var group in groups)
            {
                var versions = packed.Where(d => d.Group == group && d.Id == entry.Package).Select(d => d.Version).ToList();
                if (versions.Count != 1 || versions[0] != entry.Version)
                    lines.Add($"{entry.Package}: the nuspec's {group} group asks for {string.Join(", ", versions.DefaultIfEmpty("nothing"))}, the ledger for {entry.Version}");
            }
        }
        return lines;
    }

    /// <summary>
    /// Every file the packed <c>src/<paramref name="project"/></c> carries, by its path in the package
    /// (<c>lib/net10.0/...</c>).
    /// </summary>
    public static List<string> Files(string project) =>
        Pack($"src/{project}/{project}.csproj", archive => archive.Entries.Select(entry => entry.FullName).ToList());

    /// <summary>One dependency of a packed nuspec: its group's target framework, the package and the version.</summary>
    private sealed record Dependency(string Group, string Id, string Version);

    /// <summary>
    /// Packs a project this suite built, for the one framework the suites run on, and reads every dependency of every
    /// group in the nuspec it writes.
    /// </summary>
    private static List<Dependency> Dependencies(string project) =>
        Pack(project, archive =>
        {
            var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            using var stream = nuspec.Open();
            return XDocument.Load(stream).Descendants()
                .Where(element => element.Name.LocalName == "dependency")
                .Select(element => new Dependency(
                    (string?)element.Parent?.Attribute("targetFramework") ?? "",
                    (string)element.Attribute("id")!,
                    (string)element.Attribute("version")!))
                .ToList();
        });

    /// <summary>Packs a project this suite built, for the one framework the suites run on, and reads the package.</summary>
    private static T Pack<T>(string project, Func<ZipArchive, T> read)
    {
        // bin/<configuration>/<framework>/: the configuration that built this assembly built the project it references.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var output = Directory.CreateTempSubdirectory("kronikol-pack-");
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[]
                     {
                         "pack", Path.Combine(RepoRoot, project), "--no-build", "--no-restore", "--nologo",
                         "-c", configuration, "-p:TargetFrameworks=net10.0", "-o", output.FullName
                     })
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
                throw new TimeoutException($"dotnet pack {project} did not finish in three minutes");
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"dotnet pack {project} failed:\n{stdout.Result}\n{stderr.Result}");

            var packages = output.GetFiles("*.nupkg");
            if (packages.Length != 1)
                throw new InvalidOperationException($"dotnet pack {project} wrote {packages.Length} packages, not one");
            using var archive = ZipFile.OpenRead(packages[0].FullName);
            return read(archive);
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }
}
