using System.Text.RegularExpressions;
using Kronikol.Tests.Tool;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// What a template's sample test needs to run as created (plans/ADAPTER_CAPTURE_GAPS_PLAN.md R3). Measured on 2026-10-08
/// with the published templates: the BDDfy and ReqNRoll xUnit v3 templates referenced no <c>Microsoft.NET.Test.Sdk</c>, so
/// <c>dotnet test</c> stopped at "testhost.dll was not found", and the TUnit templates' placeholder factory used
/// <c>WebHostBuilder</c>, which .NET 10 marks obsolete, so every TUnit scaffold built with an ASPDEPR004 warning. CI's
/// template job runs each scaffold's sample test; these facts fail before it does.
/// </summary>
public class TemplateTestHostTests
{
    private static readonly string TemplatesDirectory = Path.Combine(BuiltTool.RepoRoot, "templates");

    private static IEnumerable<string> ProjectFiles() =>
        Directory.GetDirectories(TemplatesDirectory, "kronikol-*")
            .SelectMany(directory => Directory.GetFiles(directory, "*.csproj"));

    private static bool References(string project, string package) =>
        Regex.IsMatch(File.ReadAllText(project), $@"<PackageReference\s+Include=""{Regex.Escape(package)}""");

    [Fact]
    public void Every_template_that_dotnet_test_runs_references_the_test_SDK()
    {
        var vsTest = ProjectFiles().Where(project => !References(project, "TUnit")).ToList();

        Assert.NotEmpty(vsTest);
        Assert.All(vsTest, project => Assert.True(References(project, "Microsoft.NET.Test.Sdk"),
            $"{Path.GetFileName(Path.GetDirectoryName(project))} references no Microsoft.NET.Test.Sdk, so dotnet test cannot start its test host"));
    }

    [Fact]
    public void No_template_builds_its_placeholder_with_the_obsolete_WebHostBuilder()
    {
        var sources = Directory.GetDirectories(TemplatesDirectory, "kronikol-*")
            .SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(sources);
        Assert.All(sources, file => Assert.DoesNotContain("new WebHostBuilder(", File.ReadAllText(file), StringComparison.Ordinal));
    }
}
