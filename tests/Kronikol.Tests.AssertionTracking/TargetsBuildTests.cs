using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kronikol.AssertionTracking;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// The weaver's own <c>build/</c> props and targets, run by a real <c>dotnet build</c> (WEAVER_SOURCE_PATHS_PLAN section
/// 5.2, F10): nothing ran them before, so no fact could see that a build with deterministic source paths
/// (<c>ContinuousIntegrationBuild</c>) labelled every assertion by its line (#165), or that the documented off switch
/// broke the build (F7). The fixture imports the targets as a package reference would, with the task loaded from this
/// test's build of it, references the core library and Shouldly, and declares a source root for its own directory, so
/// the facts need no git repository.
/// </summary>
public class TargetsBuildTests
{
    private static readonly string Tfm = $"net{Environment.Version.Major}.0";

    private static readonly string WeaverBuildDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Kronikol.AssertionTracking", "build"));

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "KRONIKOL_TargetsTests", Guid.NewGuid().ToString("N")[..8]);

    // The task as MSBuild loads it from a package's tasks/ folder: the assembly and Mono.Cecil beside it.
    private static readonly Lazy<string> TaskDirectory = new(() =>
    {
        var directory = Path.Combine(Root, "tasks");
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                     .Where(f => Path.GetFileName(f) is var n && (n == "Kronikol.AssertionTracking.dll" || n.StartsWith("Mono.Cecil", StringComparison.Ordinal))))
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        return directory + Path.DirectorySeparatorChar;
    });

    // The probe's #163 statements (WEAVER_SOURCE_PATHS_PLAN harness probe/Scenarios/IssueFeature.cs), three of them
    // disabled by pragma comments.
    private const string Statements = """
        using System.Collections.Generic;
        using System.Linq;
        using FluentAssertions;
        using Shouldly;

        [assembly: Kronikol.Tracking.TrackAssertions]

        public enum Colour { Red, Blue }

        public record Order(string Id, Colour Colour, List<string> Lines);

        public record Split(string SplitId);

        public static class IssueCases
        {
            public static void Statements()
            {
                var actual = new Order("A1", Colour.Blue, new List<string> { "x", "y" });
                var expected = new Order("A1", Colour.Blue, new List<string> { "x", "y" });
                var splits = new List<Split> { new("S1") };
                var result = new Split("S1");

                actual.Id.ShouldBe(expected.Id);
                actual.Colour.ToString().ShouldBe(expected.Colour.ToString());
                actual.Lines.First().ShouldBe(expected.Lines.First());
                actual.Colour.ToString().ToLowerInvariant().ShouldBe(expected.Colour.ToString().ToLowerInvariant());
                actual.Colour.ToString().Should().Be(expected.Colour.ToString());
                result.SplitId.ShouldBe(splits.First().SplitId);
                actual.Id.Should().Be(expected.Id);
                actual.Id.Should().Be("A1"); // pragma:TrackAssertions:disable
                // pragma:TrackAssertions:disable
                actual.Lines.Count.ShouldBe(2);
                expected.Id.Should().Be("A1");
                // pragma:TrackAssertions:enable
                actual.Lines.Last().ShouldBe("y");
            }
        }
        """;

    private const int Woven = 8;

    private static readonly Lazy<WovenRun.Note[]> LocalNotes = new(() =>
    {
        var build = Build("Local", []);
        return Notes(build.Assembly);
    });

    private sealed record FixtureBuild(string Directory, string Assembly, string Output);

    /// <summary>Builds a copy of the fixture in <c>{name}</c> (under <paramref name="parent"/> when given) with the
    /// properties given, and <paramref name="extraProject"/> placed before the SDK's targets, and returns its woven
    /// assembly and the build's output.</summary>
    private static FixtureBuild Build(string name, IReadOnlyList<string> properties, string? parent = null, string? assemblyName = null,
        string source = Statements, string extraProject = "", System.Text.Encoding? sourceEncoding = null)
    {
        var directory = Path.Combine(parent ?? Root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
        // UTF-8 without a byte order mark, as an editor writes a file.
        File.WriteAllText(Path.Combine(directory, "IssueCases.cs"), source, sourceEncoding ?? new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "Fixture.csproj"), $"""
            <Project>
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
              <Import Project="{Path.Combine(WeaverBuildDirectory, "Kronikol.AssertionTracking.props")}" />
              <PropertyGroup>
                <TargetFramework>{Tfm}</TargetFramework>
                <AssemblyName>{assemblyName ?? "TargetsFixture" + name}</AssemblyName>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsPackable>false</IsPackable>
                <NoWarn>$(NoWarn);CS8602;CS8604</NoWarn>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="FluentAssertions" Version="8.9.0" />
                <PackageReference Include="Shouldly" Version="4.3.0" />
                <Reference Include="Kronikol" HintPath="{typeof(Kronikol.Tracking.Track).Assembly.Location}" />
                <!-- The fixture's own source root, as Source Link names a repository's: no git needed. -->
                <SourceRoot Include="$(MSBuildProjectDirectory){Path.DirectorySeparatorChar}" />
              </ItemGroup>
            {extraProject}
              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
              <Import Project="{Path.Combine(WeaverBuildDirectory, "Kronikol.AssertionTracking.targets")}" />
            </Project>
            """);

        var output = Run(directory, ["build", "Fixture.csproj", "-c", "Debug", "-nodeReuse:false", "-v:n",
            $"-p:_AssertionTrackingTaskDir={TaskDirectory.Value}", .. properties]);
        var assembly = Path.Combine(directory, "bin", "Debug", Tfm, (assemblyName ?? "TargetsFixture" + name) + ".dll");
        return new FixtureBuild(directory, assembly, output);
    }

    private static string Run(string directory, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        // The test host's own MSBuild settings would point the inner build at the outer SDK's pieces.
        foreach (var variable in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath" })
            start.Environment.Remove(variable);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(5)), $"dotnet {string.Join(' ', arguments)} did not finish");
        var output = stdout.Result + stderr.Result;
        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', arguments)} failed:\n{output}");
        return output;
    }

    private static WovenRun.Note[] Notes(string assemblyPath)
    {
        var (thrown, notes) = WovenRun.Run(assemblyPath, "IssueCases", "Statements");
        Assert.Null(thrown);
        return notes;
    }

    private static void AssertDrawnAsLocally(FixtureBuild build)
    {
        Assert.Contains($"Kronikol.AssertionTracking: Instrumented {Woven} assertion(s)", build.Output);
        var notes = Notes(build.Assembly);
        Assert.DoesNotContain(notes, n => n.Label.StartsWith("Assertion at line", StringComparison.Ordinal));
        Assert.Equal(LocalNotes.Value, notes);
    }

    // Fact 19.
    [Theory]
    [InlineData("portable")]
    [InlineData("embedded")]
    public void A_ci_build_draws_its_assertions_as_a_local_build_does(string debugType)
    {
        var build = Build($"Ci{debugType}", ["-p:ContinuousIntegrationBuild=true", $"-p:DebugType={debugType}"]);

        AssertDrawnAsLocally(build);
    }

    // Fact 19, a build that sets CodePage: the compiler decodes every file without a byte order mark in that code page,
    // and so does the weave, in MSBuild's own process, which has no code pages but the Unicode ones unless one is
    // registered.
    [Fact]
    public void A_build_with_a_code_page_labels_its_statements_as_the_compiler_read_them()
    {
        var build = Build("CodePage", ["-p:ContinuousIntegrationBuild=true", "-p:CodePage=1252"], source: """
            using FluentAssertions;

            [assembly: Kronikol.Tracking.TrackAssertions]

            public static class IssueCases
            {
                public static void Statements()
                {
                    var name = "café";
                    name.Should().Be("café");
                }
            }
            """);

        // The file is UTF-8 without a byte order mark; read as Windows-1252, its é is the two characters Ã and ©.
        Assert.Equal("Name should be \"cafÃ©\"", Assert.Single(Notes(build.Assembly)).Label);
    }

    // The audit of #165 (2026-10-15): the attributes' files were written again on every build, newer than the assembly,
    // so every build compiled the project again; since 4.20.4 writes them with the switch off too, so did a build that
    // weaves nothing.
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void A_second_build_with_nothing_changed_compiles_nothing(string enabled)
    {
        var build = Build("Incremental" + enabled, [$"-p:TrackAssertionsEnabled={enabled}"]);
        var compiled = Path.Combine(build.Directory, "obj", "Debug", Tfm, "TargetsFixtureIncremental" + enabled + ".dll");
        var first = File.GetLastWriteTimeUtc(compiled);

        var again = Run(build.Directory, ["build", "Fixture.csproj", "-c", "Debug", "-nodeReuse:false", "-v:n",
            $"-p:_AssertionTrackingTaskDir={TaskDirectory.Value}", $"-p:TrackAssertionsEnabled={enabled}"]);

        Assert.Equal(first, File.GetLastWriteTimeUtc(compiled));
        Assert.DoesNotContain("Instrumented", again, StringComparison.Ordinal);
    }

    // The audit: from 4.20.4 the attributes' C# files were added to every project with the switch off, so a Visual Basic
    // or F# project that imports the targets (the package carries them in buildTransitive) no longer built.
    [Fact]
    public void A_visual_basic_project_that_imports_the_targets_builds()
    {
        var directory = Path.Combine(Root, "VisualBasic");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "Answers.vb"), "Public Module Answers\n    Public Function Answer() As Integer\n        Return 42\n    End Function\nEnd Module\n");
        File.WriteAllText(Path.Combine(directory, "Fixture.vbproj"), $"""
            <Project>
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
              <Import Project="{Path.Combine(WeaverBuildDirectory, "Kronikol.AssertionTracking.props")}" />
              <PropertyGroup>
                <TargetFramework>{Tfm}</TargetFramework>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
              <Import Project="{Path.Combine(WeaverBuildDirectory, "Kronikol.AssertionTracking.targets")}" />
            </Project>
            """);

        foreach (var enabled in new[] { "false", "true" })
            Run(directory, ["build", "Fixture.vbproj", "-c", "Debug", "-nodeReuse:false", "-v:n",
                $"-p:_AssertionTrackingTaskDir={TaskDirectory.Value}", $"-p:TrackAssertionsEnabled={enabled}"]);
    }

    // The audit (2026-10-15): a Windows-1252 file with no byte order mark and no CodePage, read by the compiler with the
    // machine's fallback, Windows-1252 on Windows and UTF-8 with U+FFFD for the byte it cannot read on Linux, where CI
    // runs this class. The weave read Windows-1252 everywhere, so on Linux the label showed é and the compiled text U+FFFD.
    [Fact]
    public void A_file_that_is_not_utf8_is_labelled_as_the_compiler_read_it()
    {
        var build = Build("Ansi", ["-p:ContinuousIntegrationBuild=true"], source: """
            using FluentAssertions;

            [assembly: Kronikol.Tracking.TrackAssertions]

            public static class IssueCases
            {
                public static void Statements()
                {
                    var name = "café";
                    name.Should().Be("café");
                }
            }
            """, sourceEncoding: System.Text.CodePagesEncodingProvider.Instance.GetEncoding(1252));

        Assert.Equal(OperatingSystem.IsWindows() ? "Name should be \"café\"" : "Name should be \"caf\uFFFD\"",
            Assert.Single(Notes(build.Assembly)).Label);
    }

    // Fact 20.
    [Fact]
    public void A_projects_own_PathMap_draws_its_assertions_as_a_local_build_does()
    {
        var directory = Path.Combine(Root, "PathMap");
        var build = Build("PathMap", [$"-p:PathMap={directory}{Path.DirectorySeparatorChar}=/src/"]);

        AssertDrawnAsLocally(build);
    }

    // Fact 21.
    [Fact]
    public void A_ci_build_that_embeds_its_sources_draws_its_assertions_as_a_local_build_does()
    {
        var build = Build("CiEmbedAll", ["-p:ContinuousIntegrationBuild=true", "-p:EmbedAllSources=true"]);

        AssertDrawnAsLocally(build);
    }

    // Fact 22: the off switch the wiki documents ("Disabling the Weaver") failed CS0234 in a project that declares the
    // attribute, since the attributes were generated only when the switch was on (F7).
    [Fact]
    public void The_off_switch_builds_a_project_that_declares_the_attribute_and_weaves_nothing()
    {
        var build = Build("Off", ["-p:TrackAssertionsEnabled=false"]);

        Assert.DoesNotContain("Kronikol.AssertionTracking: Instrumented", build.Output);
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(build.Assembly);
        Assert.DoesNotContain(module.CustomAttributes, a => a.AttributeType.Name == "__AssertionTrackingWeaved__");
    }

    // Fact 23: the woven calls carry the names the compiler wrote, so two clones at different paths weave the same
    // bytes, and with the labels read from the files.
    [Fact]
    public void Two_ci_builds_at_different_paths_weave_the_same_bytes_with_the_statements_labels()
    {
        var first = Build("Clone", ["-p:ContinuousIntegrationBuild=true"], Path.Combine(Root, "a"), "TargetsFixtureClone");
        var second = Build("Clone", ["-p:ContinuousIntegrationBuild=true"], Path.Combine(Root, "b", "deeper", "still"), "TargetsFixtureClone");

        Assert.Equal(Hash(first.Assembly), Hash(second.Assembly));
        Assert.Equal(Hash(Path.ChangeExtension(first.Assembly, ".pdb")), Hash(Path.ChangeExtension(second.Assembly, ".pdb")));
        AssertDrawnAsLocally(first);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // Fact 24 (R2): a statement whose source no candidate reaches. The fixture moves its source away between the
    // compiler and the weave, so neither the file the map leads back to nor the PDB's own name is there: the build warns
    // once, KRONIKOL002, naming the document and the paths tried; <NoWarn> silences it; and a build that reads every
    // document has none.
    private const string HideSource = """
          <Target Name="_HideIssueCases" BeforeTargets="_WeaveAssertionTracking">
            <Move SourceFiles="IssueCases.cs" DestinationFolder="$(MSBuildProjectDirectory)-hidden" />
          </Target>
        """;

    [Fact]
    public void A_statement_whose_source_cannot_be_read_warns_once()
    {
        var unread = Build("Unread", ["-p:ContinuousIntegrationBuild=true"], extraProject: HideSource);

        // The SDK numbers the source roots it maps, and outside a repository the NuGet folders come first: the fixture's
        // own root is /_/ on one machine and /_2/ on another.
        var file = Path.Combine(unread.Directory, "IssueCases.cs");
        Assert.Matches(Regex.Escape("warning KRONIKOL002: Kronikol.AssertionTracking: the source of 11 assertion statement(s) "
            + "in 1 file(s) could not be read, so they are labelled by line number, show no values, and their pragma comments "
            + "were not read. First: ") + @"(?<name>/_\d*/)IssueCases\.cs; tried " + Regex.Escape(file) + @", \k<name>IssueCases\.cs\.",
            unread.Output);
        Assert.Contains("    1 Warning(s)", unread.Output);
        Assert.All(Notes(unread.Assembly), n => Assert.StartsWith("Assertion at line", n.Label, StringComparison.Ordinal));

        var silenced = Build("UnreadNoWarn", ["-p:ContinuousIntegrationBuild=true"],
            extraProject: HideSource + "<PropertyGroup><NoWarn>$(NoWarn);KRONIKOL002</NoWarn></PropertyGroup>");
        Assert.DoesNotContain("warning KRONIKOL002", silenced.Output);
        Assert.Contains("Kronikol.AssertionTracking: Could not read the source of 1 file(s)", silenced.Output);

        var read = Build("ReadAll", ["-p:ContinuousIntegrationBuild=true"]);
        Assert.DoesNotContain("KRONIKOL002", read.Output);
        Assert.DoesNotContain("Could not read the source", read.Output);
    }
}
