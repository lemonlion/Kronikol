using System.Net;
using System.Text;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The <c>query.cs</c> every run writes beside its report, run the way an agent runs it:
/// <c>dotnet run --file query.cs -- &lt;verb&gt; .</c>, against the built tool, byte for byte
/// (roadmap stage 1b, <c>plans/QUERY_FALLBACK_PLAN.md</c> §6.2).
///
/// <para>Every run of the script here has the network cut (a proxy nothing listens on) and an empty NuGet
/// cache, unless the case is about the cache: the point of the file is that it needs neither. The plan's
/// first design, a <c>#:package</c> directive, failed exactly that, twice, and nothing short of running it
/// would have shown it.</para>
///
/// <para>Skipped, visibly, where no .NET 10 SDK is installed: <c>dotnet run --file</c> needs one, and a
/// smoke test that is silently skipped cannot be told from one that never worked.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class QueryScriptEndToEndTests : IClassFixture<QueryScriptEndToEndTests.Reports>
{
    private readonly Reports _reports;

    public QueryScriptEndToEndTests(Reports reports) => _reports = reports;

    /// <summary>
    /// One report written by the real pipeline - so the <c>query.cs</c> beside it is the one a consumer
    /// gets - plus the three odd places a script has to cope with.
    /// </summary>
    public sealed class Reports : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "kronikol-query-e2e-" + Guid.NewGuid().ToString("N"));

        /// <summary>A run's reports directory: its <c>query.cs</c> finds the run's own <c>Kronikol.dll</c>.</summary>
        public string Run => Path.Combine(Root, "Reports");

        /// <summary>A <c>query.cs</c> whose only engine is a NuGet cache holding <c>Kronikol.dll</c> and nothing else.</summary>
        public string CacheOnly => Path.Combine(Root, "CacheOnly");

        public string Cache => Path.Combine(Root, "nuget-cache");

        /// <summary>A <c>query.cs</c> with no engine anywhere.</summary>
        public string Nowhere => Path.Combine(Root, "Nowhere");

        /// <summary>A run's reports under a repository whose Directory.Build.props and .targets would break any ordinary build of the file.</summary>
        public string Strict => Path.Combine(Root, "strict", "tests", "Shop.Tests", "bin", "Debug", "net8.0", "Reports");

        /// <summary>A test project whose run writes its reports into a dot-folder of the project's own folder.</summary>
        public string DottedProject => Path.Combine(Root, "dotted", "Shop.Tests");

        /// <summary>That run's reports directory: <c>.logs/kronikol/</c>, which the SDK's default items leave out.</summary>
        public string Dotted => Path.Combine(DottedProject, ".logs", "kronikol");

        /// <summary>A test project with a <c>query.cs</c> in a folder of its own that is not <c>bin</c>, <c>obj</c> or a dot-folder.</summary>
        public string PlainProject => Path.Combine(Root, "plain", "Shop.Tests");

        /// <summary>A <c>query.cs</c> whose engine is only in a cache a <c>NuGet.config</c> above it moved.</summary>
        public string Configured => Path.Combine(Root, "configured", "Reports");

        /// <summary>A user profile with nothing in it: no <c>~/.nuget/packages</c>.</summary>
        public string EmptyHome => Path.Combine(Root, "empty-home");

        public string EmptyCache => Path.Combine(Root, "empty-cache");

        public Reports()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(EmptyCache);
            Directory.CreateDirectory(EmptyHome);
            WriteProject(DottedProject);
            WriteProject(PlainProject);
            DefaultDiagramsFetcher.Reset();
            try
            {
                Write(Run);
                WriteStrictRepository(Path.Combine(Root, "strict"));
                Write(Strict);
                Write(Dotted);
            }
            finally
            {
                DefaultDiagramsFetcher.Reset();
            }

            var package = ReportGenerator.KronikolVersion.Split('+')[0];
            var assembly = typeof(QueryScriptGenerator).Assembly.GetName().Version!.ToString();
            foreach (var directory in new[] { CacheOnly, Nowhere, Configured })
            {
                Directory.CreateDirectory(directory);
                File.Copy(Path.Combine(Run, "TestRunReport.json"), Path.Combine(directory, "TestRunReport.json"));
                File.WriteAllText(Path.Combine(directory, "query.cs"), QueryScriptGenerator.Build(package, assembly, ownCopy: null));
            }

            // What the run never writes, because the project would compile it: the premise of the directory rule.
            Directory.CreateDirectory(Path.Combine(PlainProject, "TestResults"));
            File.WriteAllText(Path.Combine(PlainProject, "TestResults", "query.cs"), QueryScriptGenerator.Build(package, assembly, ownCopy: null));

            // The repository's own NuGet.config moves the cache, relative to itself, as NuGet reads it.
            File.WriteAllText(Path.Combine(Root, "configured", "NuGet.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="../nuget-cache" />
                  </config>
                </configuration>
                """);

            // Kronikol.dll alone, where NuGet would have put it: none of the packages it depends on.
            var lib = Path.Combine(Cache, "kronikol", package.ToLowerInvariant(), "lib", "net10.0");
            Directory.CreateDirectory(lib);
            File.Copy(typeof(QueryScriptGenerator).Assembly.Location, Path.Combine(lib, "Kronikol.dll"));
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { /* best effort */ }
        }

        private static void Write(string reportsDirectory)
        {
            var failing = "e2e-" + Guid.NewGuid().ToString("N");
            var passing = "e2e-" + Guid.NewGuid().ToString("N");
            var browsing = "e2e-" + Guid.NewGuid().ToString("N");

            // Timed, so the run's first POST /charge (600 ms against a later 5 ms) is a first-call warm-up and the answers
            // below include the lines that say so (plans/WARM_UP_PLAN.md T34).
            var at = DateTimeOffset.UtcNow.AddMinutes(-1);
            RequestResponseLogger.LogPair("Pay with an expired card", failing, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test",
                """{"card":"4000 0000 0000 0069","amount":1200}""", """{"status":"declined","reason":"expired_card"}""", HttpStatusCode.PaymentRequired,
                TestPhase.Unknown, null, null, requestAt: at, responseAt: at.AddMilliseconds(600));
            RequestResponseLogger.LogPair("Pay with a valid card", passing, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test",
                """{"card":"4242 4242 4242 4242","amount":1200}""", """{"status":"captured","id":"ch_1"}""", HttpStatusCode.Created,
                TestPhase.Unknown, null, null, requestAt: at.AddSeconds(1), responseAt: at.AddSeconds(1).AddMilliseconds(5));
            RequestResponseLogger.LogPair("Browse the catalogue", browsing, HttpMethod.Get, new Uri("http://catalogue/items?page=1"), "catalogue", "Test",
                responseContent: """{"items":[{"sku":"A1","price":1200}]}""", statusCode: HttpStatusCode.OK);

            ReportGenerator.CreateStandardReportsWithDiagrams(
            [
                new Feature
                {
                    DisplayName = "Checkout",
                    Scenarios =
                    [
                        new Scenario
                        {
                            Id = failing, DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(0.9),
                            ErrorMessage = "Assert.Equal() Failure: Values differ\nExpected: captured\nActual:   declined",
                            Steps =
                            [
                                new ScenarioStep { Keyword = "Given", Text = "an expired card", Status = ExecutionResult.Passed },
                                new ScenarioStep { Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Passed },
                                new ScenarioStep { Keyword = "Then", Text = "the charge is captured", Status = ExecutionResult.Failed, FailureMessage = "declined" }
                            ]
                        },
                        new Scenario
                        {
                            Id = passing, DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(0.4),
                            Steps = [new ScenarioStep { Keyword = "Then", Text = "the charge is captured", Status = ExecutionResult.Passed }]
                        }
                    ]
                },
                new Feature
                {
                    DisplayName = "Catalogue",
                    Scenarios = [new Scenario { Id = browsing, DisplayName = "Browse the catalogue", Result = ExecutionResult.Passed }]
                }
            ], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, new ReportConfigurationOptions
            {
                ReportsFolderPath = reportsDirectory,
                InternalFlowTracking = false,
                GenerateComponentDiagram = false,
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
            });
        }

        /// <summary>
        /// Settings a real repository has, every one of which broke or polluted a probe of the file before its
        /// properties were added: a net8.0 target, warnings and nullable warnings as errors, every analyzer, an
        /// old language version, no implicit usings, a library output type, and central package management.
        /// And a <c>Directory.Build.targets</c> that comes after the file's properties and fails any project
        /// that does not pass the repository's policy.
        /// </summary>
        private static void WriteStrictRepository(string root)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutputType>Library</OutputType>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <WarningsAsErrors>$(WarningsAsErrors);nullable;CS0219</WarningsAsErrors>
                    <WarningLevel>9999</WarningLevel>
                    <AnalysisLevel>latest-all</AnalysisLevel>
                    <AnalysisMode>All</AnalysisMode>
                    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
                    <GenerateDocumentationFile>true</GenerateDocumentationFile>
                    <LangVersion>9.0</LangVersion>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                    <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), """
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                </Project>
                """);

            // Imported after the file's own properties, so it overrides them, and its targets run in its build: a
            // policy every project of the repository has to pass.
            File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), """
                <Project>
                  <PropertyGroup>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <WarningLevel>9999</WarningLevel>
                  </PropertyGroup>
                  <Target Name="RepositoryPolicy" BeforeTargets="CoreCompile">
                    <Error Text="Every project in this repository sets RepositoryPolicy." Condition="'$(RepositoryPolicy)' != 'true'" />
                  </Target>
                </Project>
                """);
        }

        /// <summary>A test project's file, as small as one gets: what the directory rule is about is its folder.</summary>
        private static void WriteProject(string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Shop.Tests.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
        }
    }

    /// <summary>
    /// What an agent with no network and no packages runs. With <paramref name="home"/>, that is the whole user
    /// profile and <c>NUGET_PACKAGES</c> is unset, so where the cache is comes from NuGet's own settings.
    /// </summary>
    private BuiltTool.Outcome Script(string directory, string[] args, string? nugetPackages = null, string? home = null)
    {
        var environment = new Dictionary<string, string?>
        {
            ["NUGET_PACKAGES"] = home is null ? nugetPackages ?? _reports.EmptyCache : null,
            ["HTTPS_PROXY"] = "http://127.0.0.1:9",
            ["HTTP_PROXY"] = "http://127.0.0.1:9",
            ["https_proxy"] = "http://127.0.0.1:9",
            ["http_proxy"] = "http://127.0.0.1:9",
            ["NO_PROXY"] = null,
            ["no_proxy"] = null,
        };
        if (home is not null)
        {
            environment["HOME"] = home;
            environment["USERPROFILE"] = home;
        }

        return BuiltTool.Dotnet(directory, ["run", "--file", "query.cs", "--", .. args], environment);
    }

    /// <summary>A project built the way its owner builds it, offline.</summary>
    private BuiltTool.Outcome Build(string project) =>
        BuiltTool.Dotnet(project, ["build", "--nologo"], new Dictionary<string, string?>
        {
            ["NUGET_PACKAGES"] = _reports.EmptyCache,
            ["HTTPS_PROXY"] = "http://127.0.0.1:9",
            ["HTTP_PROXY"] = "http://127.0.0.1:9",
            ["https_proxy"] = "http://127.0.0.1:9",
            ["http_proxy"] = "http://127.0.0.1:9",
        });

    private static BuiltTool.Outcome Tool(string directory, string[] args) =>
        BuiltTool.Dotnet(directory, [BuiltTool.Dll(), "query", .. args]);

    private static void AssertSame(BuiltTool.Outcome tool, BuiltTool.Outcome script, string what)
    {
        Assert.True(tool.ExitCode == script.ExitCode,
            $"{what}: the tool exited {tool.ExitCode} and query.cs {script.ExitCode}\nquery.cs stderr:\n{script.Stderr}\nquery.cs stdout:\n{Encoding.UTF8.GetString(script.Stdout)}");
        Assert.Equal(Encoding.UTF8.GetString(tool.Stdout), Encoding.UTF8.GetString(script.Stdout));
        Assert.Equal(tool.Stdout, script.Stdout);
        Assert.Equal(tool.Stderr, script.Stderr);
    }

    private static void RequireSdk10()
    {
        if (!BuiltTool.HasSdk10)
            Assert.Skip("dotnet run --file needs a .NET 10 SDK, and none is installed here: query.cs is not exercised");
    }

    public static TheoryData<string> Invocations =>
    [
        "summary .",
        "summary . --json",
        "scenarios .",
        "scenarios . --result Failed --json",
        "services .",
        "failures .",
        "failures . --json",
        "repro .",
        "steps . s0",
        "assertions .",
        "flow . s0",
        "interactions .",
        "http . s0/i0 --body",
        "values . --path $.status",
        "grep . declined",
        "compare . s0 s1",
        "history .",
        "--describe",
        "--help",
        "",
        "no-such-verb .",
        "summary no-such-report.json",
    ];

    [Theory]
    [MemberData(nameof(Invocations))]
    public void It_answers_exactly_as_the_tool_does(string invocation)
    {
        RequireSdk10();
        var args = invocation.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        AssertSame(Tool(_reports.Run, args), Script(_reports.Run, args), $"query {invocation}");
    }

    [Fact]
    public void The_report_answers_with_the_first_call_warm_up()
    {
        // Guards the theory above: it holds the script to the tool over the lines the warm-up adds (summary, flow,
        // interactions) only while the report carries one.
        RequireSdk10();
        var summary = System.Text.Encoding.UTF8.GetString(Tool(_reports.Run, ["summary", "."]).Stdout);

        Assert.Contains("First-call warm-up:", summary);
        Assert.Contains("warm-up", System.Text.Encoding.UTF8.GetString(Tool(_reports.Run, ["interactions", "."]).Stdout));
    }

    /// <summary>
    /// A net8.0 or net9.0 test project's own <c>Kronikol.dll</c> carries no engine, so the file goes to the
    /// NuGet cache, where the package's <c>lib/net10.0</c> is whatever the project targets. Only
    /// <c>Kronikol.dll</c> is there: the engine needs none of the packages <c>Kronikol</c> depends on.
    /// </summary>
    [Fact]
    public void The_NuGet_cache_alone_is_enough()
    {
        RequireSdk10();

        AssertSame(Tool(_reports.CacheOnly, ["failures", "."]), Script(_reports.CacheOnly, ["failures", "."], nugetPackages: _reports.Cache),
            "query failures, engine from the cache");
    }

    [Fact]
    public void With_no_engine_anywhere_it_says_where_it_looked_and_how_else_to_ask()
    {
        RequireSdk10();

        var outcome = Script(_reports.Nowhere, ["summary", "."]);

        Assert.Equal(1, outcome.ExitCode);
        Assert.Empty(outcome.Stdout);
        var version = ReportGenerator.KronikolVersion.Split('+')[0];
        Assert.Contains($"query.cs: Kronikol {version}, which wrote this report, is not on this machine.", outcome.Stderr, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_reports.EmptyCache, "kronikol", version.ToLowerInvariant(), "lib", "net10.0", "Kronikol.dll"), outcome.Stderr, StringComparison.Ordinal);
        Assert.Contains($"dnx Kronikol.Tool@{version} query summary .", outcome.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repositorys_strict_build_settings_do_not_reach_it()
    {
        RequireSdk10();

        AssertSame(Tool(_reports.Strict, ["summary", "."]), Script(_reports.Strict, ["summary", "."]),
            "query summary under a strict Directory.Build.props and Directory.Build.targets");
    }

    /// <summary>
    /// The directory rule, built for real. A <c>query.cs</c> in a dot-folder of a project's folder is in no build,
    /// because the SDK's default items leave <c>**/.*/**</c> out, so a run writes one there and the project still
    /// builds. Anywhere else in the project's folder but <c>bin</c> and <c>obj</c> the project compiles it, and its
    /// <c>#:</c> lines are error CS9298: why a run writes none there.
    /// </summary>
    [Fact]
    public void A_project_builds_with_query_cs_in_its_dot_folder_and_breaks_with_it_elsewhere()
    {
        RequireSdk10();

        Assert.True(File.Exists(Path.Combine(_reports.Dotted, "query.cs")), "the run writes query.cs into the project's .logs/kronikol");
        var dotted = Build(_reports.DottedProject);
        Assert.True(dotted.ExitCode == 0, "the project with query.cs in .logs/kronikol does not build:\n" + Encoding.UTF8.GetString(dotted.Stdout));

        var plain = Build(_reports.PlainProject);
        Assert.NotEqual(0, plain.ExitCode);
        Assert.Contains("CS9298", Encoding.UTF8.GetString(plain.Stdout), StringComparison.Ordinal);
    }

    [Fact]
    public void In_a_projects_dot_folder_it_answers_exactly_as_the_tool_does()
    {
        RequireSdk10();

        AssertSame(Tool(_reports.Dotted, ["failures", "."]), Script(_reports.Dotted, ["failures", "."]),
            "query failures in a project's .logs/kronikol");
    }

    /// <summary>
    /// A <c>NuGet.config</c> can move the cache (<c>globalPackagesFolder</c>), and then neither
    /// <c>NUGET_PACKAGES</c> nor <c>~/.nuget/packages</c> says where the engine is. When those miss, the file
    /// asks NuGet itself, from its own folder, so the answer is the one the test run restored under.
    /// </summary>
    [Fact]
    public void The_cache_a_NuGet_config_moved_is_looked_in()
    {
        RequireSdk10();

        AssertSame(Tool(_reports.Configured, ["failures", "."]), Script(_reports.Configured, ["failures", "."], home: _reports.EmptyHome),
            "query failures, engine from the cache a NuGet.config names");
    }
}
