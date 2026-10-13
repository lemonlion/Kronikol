using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using MongoDB.Driver;

namespace Kronikol.Tests.MongoDB;

/// <summary>
/// The driver version a packed extension asks for. A floating reference (<c>3.*</c>) is packed as the newest version of
/// the day, so each release raised the floor every consumer had to meet, and a consumer that pins an older driver met
/// NuGet's downgrade error (#168 F21, <c>plans/MONGODB_DETAILED_PAYLOADS_PLAN.md</c> Q18). Each extension pins the
/// oldest driver its suite passes on whose dependencies carry no known advisory, and this reads the floor back from the
/// nuspec <c>dotnet pack</c> writes. Moving a floor is a decision: run the suite on the new one first.
/// </summary>
public class MongoDbPackageTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void The_packed_extension_asks_for_its_measured_driver_floor()
    {
        // This file runs in both suites, each on its own driver line, so the driver it runs on names the extension.
        var major = typeof(MongoClient).Assembly.GetName().Version!.Major;
        var (project, floor) = major switch
        {
            // 3.9.0: older 3.x drivers allow Snappier 1.0.0 (a high advisory) or SharpCompress 0.30.1 (a moderate one).
            3 => ("src/Kronikol.Extensions.MongoDB/Kronikol.Extensions.MongoDB.csproj", "3.9.0"),
            // 2.28.0: the first strong-named 2.x, so a consumer on a later 2.x can compile against it (this suite, on the
            // newest 2.x, could not against an unsigned 2.21); every 2.x up to 2.18.0 carries a high advisory of its own.
            2 => ("src/Kronikol.Extensions.MongoDB.V2/Kronikol.Extensions.MongoDB.V2.csproj", "2.28.0"),
            _ => throw new InvalidOperationException($"No extension is built on driver {major}.x.")
        };

        var versions = PackedDependencyVersions(project, "MongoDB.Driver");

        Assert.NotEmpty(versions);
        Assert.All(versions, version => Assert.Equal(floor, version));
    }

    /// <summary>
    /// Packs the extension this suite built, for the one framework the suite runs on, and reads the version each of the
    /// nuspec's dependency groups gives the package.
    /// </summary>
    private static List<string> PackedDependencyVersions(string project, string id)
    {
        // bin/<configuration>/<framework>/: the configuration that built this assembly built the extension it references.
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
            Assert.True(process.WaitForExit(TimeSpan.FromMinutes(3)), "dotnet pack did not finish in three minutes");
            Assert.True(process.ExitCode == 0, $"dotnet pack failed:\n{stdout.Result}\n{stderr.Result}");

            var package = Assert.Single(output.GetFiles("*.nupkg"));
            using var archive = ZipFile.OpenRead(package.FullName);
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
