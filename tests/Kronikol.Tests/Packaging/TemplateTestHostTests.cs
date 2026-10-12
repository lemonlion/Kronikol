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

    [Fact]
    public void No_TUnit_template_references_the_test_SDK()
    {
        // TUnit runs on Microsoft.Testing.Platform, whose entry point the test SDK's generated one would replace.
        var tunit = ProjectFiles().Where(project => References(project, "TUnit")).ToList();

        Assert.NotEmpty(tunit);
        Assert.All(tunit, project => Assert.False(References(project, "Microsoft.NET.Test.Sdk"),
            $"{Path.GetFileName(Path.GetDirectoryName(project))} references Microsoft.NET.Test.Sdk"));
    }

    [Fact]
    public void Every_template_takes_the_Mvc_Testing_version_for_the_framework_it_was_created_for()
    {
        // Microsoft.AspNetCore.Mvc.Testing ships a major version per .NET version, for its own framework only, so a pin
        // of 10.x failed restore in every scaffold made with --Framework net8.0 or net9.0. The template's Framework option
        // rewrites every "net10.0" in the project, so no condition may name it.
        var projects = ProjectFiles().ToList();

        Assert.NotEmpty(projects);
        Assert.All(projects, project =>
        {
            var text = File.ReadAllText(project);
            var name = Path.GetFileName(Path.GetDirectoryName(project));
            Assert.True(Regex.IsMatch(text, @"<PackageReference\s+Include=""Microsoft\.AspNetCore\.Mvc\.Testing""\s+Version=""\$\(MvcTestingVersion\)"""),
                $"{name} pins Microsoft.AspNetCore.Mvc.Testing instead of taking $(MvcTestingVersion)");
            Assert.Matches(@"<MvcTestingVersion Condition=""\$\(TargetFramework\.StartsWith\('net8'\)\)"">8\.", text);
            Assert.Matches(@"<MvcTestingVersion Condition=""\$\(TargetFramework\.StartsWith\('net9'\)\)"">9\.", text);
            Assert.Matches(@"<MvcTestingVersion>10\.", text);
        });
    }

    [Fact]
    public void Every_placeholder_factory_refuses_a_Program_that_is_not_the_placeholders()
    {
        // With Program.cs deleted and the API referenced, Program is the API's and the factory still compiled, hosting the
        // placeholder in the API's place, so every test that checks only a status code passed without reaching the API.
        var factories = Directory.GetDirectories(TemplatesDirectory, "kronikol-*")
            .SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => File.ReadAllText(file).Contains("class PlaceholderApiFactory", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(Directory.GetDirectories(TemplatesDirectory, "kronikol-*").Length, factories.Count);
        Assert.All(factories, file => Assert.Contains("if (typeof(Program).Assembly != typeof(PlaceholderApiFactory).Assembly)",
            File.ReadAllText(file), StringComparison.Ordinal));
    }
}
