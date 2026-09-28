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

        /// <summary>A run's reports under a repository whose Directory.Build.props would break any ordinary build of the file.</summary>
        public string Strict => Path.Combine(Root, "strict", "tests", "Shop.Tests", "bin", "Debug", "net8.0", "Reports");

        public string EmptyCache => Path.Combine(Root, "empty-cache");

        public Reports()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(EmptyCache);
            DefaultDiagramsFetcher.Reset();
            try
            {
                Write(Run);
                WriteStrictRepository(Path.Combine(Root, "strict"));
                Write(Strict);
            }
            finally
            {
                DefaultDiagramsFetcher.Reset();
            }

            var package = ReportGenerator.KronikolVersion.Split('+')[0];
            var assembly = typeof(QueryScriptGenerator).Assembly.GetName().Version!.ToString();
            foreach (var directory in new[] { CacheOnly, Nowhere })
            {
                Directory.CreateDirectory(directory);
                File.Copy(Path.Combine(Run, "TestRunReport.json"), Path.Combine(directory, "TestRunReport.json"));
                File.WriteAllText(Path.Combine(directory, "query.cs"), QueryScriptGenerator.Build(package, assembly, ownCopy: null));
            }

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

            RequestResponseLogger.LogPair("Pay with an expired card", failing, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test",
                requestContent: """{"card":"4000 0000 0000 0069","amount":1200}""",
                responseContent: """{"status":"declined","reason":"expired_card"}""", statusCode: HttpStatusCode.PaymentRequired);
            RequestResponseLogger.LogPair("Pay with a valid card", passing, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test",
                requestContent: """{"card":"4242 4242 4242 4242","amount":1200}""",
                responseContent: """{"status":"captured","id":"ch_1"}""", statusCode: HttpStatusCode.Created);
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
                            Id = failing, DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed,
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
                            Id = passing, DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed,
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
        }
    }

    /// <summary>What an agent with no network and no packages runs.</summary>
    private BuiltTool.Outcome Script(string directory, string[] args, string? nugetPackages = null) =>
        BuiltTool.Dotnet(directory, ["run", "--file", "query.cs", "--", .. args], new Dictionary<string, string?>
        {
            ["NUGET_PACKAGES"] = nugetPackages ?? _reports.EmptyCache,
            ["HTTPS_PROXY"] = "http://127.0.0.1:9",
            ["HTTP_PROXY"] = "http://127.0.0.1:9",
            ["https_proxy"] = "http://127.0.0.1:9",
            ["http_proxy"] = "http://127.0.0.1:9",
            ["NO_PROXY"] = null,
            ["no_proxy"] = null,
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
            "query summary under a strict Directory.Build.props");
    }
}
