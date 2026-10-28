using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// What a packed project asks its consumers for. NuGet packs a fixed <c>Version</c> as a floor (<c>&gt;=</c>) and a
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
    /// Packs a project this suite built, for the one framework the suites run on, and reads the version each of the
    /// nuspec's dependency groups gives the package <paramref name="id"/>: one entry per group that names it.
    /// </summary>
    public static List<string> DependencyVersions(string project, string id)
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
            var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            using var stream = nuspec.Open();
            return XDocument.Load(stream).Descendants()
                .Where(element => element.Name.LocalName == "dependency" && (string?)element.Attribute("id") == id)
                .Select(element => (string)element.Attribute("version")!)
                .ToList();
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }
}
