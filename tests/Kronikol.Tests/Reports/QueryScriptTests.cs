using System.Reflection;
using System.Text.Json;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Every run writes <c>query.cs</c> beside its report: <c>kronikol query</c> for an agent that may not
/// install the tool or reach the NuGet feed (roadmap stage 1b, <c>plans/QUERY_FALLBACK_PLAN.md</c> M2).
///
/// <para>The file restores nothing. It loads the <c>Kronikol</c> that wrote the report, from the test run's
/// own output or from the NuGet cache every consumer has, and calls <see cref="QueryCommand.Run(IReadOnlyList{string}, TextWriter, TextWriter)"/>.
/// These facts pin what it says; <c>QueryScriptEndToEndTests</c> runs it.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class QueryScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kronikol-query-script-" + Guid.NewGuid().ToString("N"));

    public QueryScriptTests()
    {
        Directory.CreateDirectory(_root);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string Dir(params string[] parts) => Path.Combine([_root, .. parts]);

    private static ReportConfigurationOptions Options(string dir) => new()
    {
        ReportsFolderPath = dir,
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
    };

    private static void Run(ReportConfigurationOptions options)
    {
        var testId = "query-script-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Pay with an expired card", testId, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");
        ReportGenerator.CreateStandardReportsWithDiagrams(
        [
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = testId, DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed, ErrorMessage = "Assert.Equal() Failure",
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "the charge succeeds", Status = ExecutionResult.Failed, FailureMessage = "declined" }]
                    }
                ]
            }
        ], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
    }

    private static string Script(string dir) => File.ReadAllText(Path.Combine(dir, "query.cs"));

    private static Assembly Library => typeof(QueryCommand).Assembly;

    [Fact]
    public void A_run_writes_query_cs_beside_its_report()
    {
        var dir = Dir("Reports");
        Run(Options(dir));

        var script = Script(dir);
        Assert.StartsWith("// Written by Kronikol ", script, StringComparison.Ordinal);
        Assert.Contains("dotnet run --file query.cs -- summary .", script, StringComparison.Ordinal);
    }

    [Fact]
    public void It_asks_for_exactly_the_version_that_wrote_the_report()
    {
        var dir = Dir("Reports");
        Run(Options(dir));

        var script = Script(dir);
        var package = ReportGenerator.KronikolVersion.Split('+')[0].ToLowerInvariant();
        Assert.Contains($"\"kronikol\", \"{package}\", \"lib\", \"net10.0\", \"Kronikol.dll\"", script, StringComparison.Ordinal);
        Assert.Contains($"\"{Library.GetName().Version}\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void It_finds_the_runs_own_engine_by_a_path_relative_to_itself_and_never_names_the_machine()
    {
        var dir = Dir("Reports");
        Run(Options(dir));

        var script = Script(dir);
        var location = Library.Location;
        var relative = Path.GetRelativePath(dir, location).Replace('\\', '/');
        Assert.Contains($"@\"{relative}\"", script, StringComparison.Ordinal);

        // An absolute path would carry a user's home directory into a folder that is often published.
        Assert.DoesNotContain("@\"" + location, script, StringComparison.Ordinal);
        Assert.DoesNotContain("@\"" + location.Replace('\\', '/'), script, StringComparison.Ordinal);
    }

    /// <summary>
    /// On Windows a path to another drive has no relative form, and <see cref="Path.GetRelativePath"/> hands the
    /// path back whole: absolute, with a home directory in it. Such a copy is not named, and the file looks in
    /// the NuGet cache alone.
    /// </summary>
    [Fact]
    public void A_copy_with_no_relative_path_is_not_named()
    {
        Assert.Equal("../Kronikol.dll", QueryScriptGenerator.Portable("..\\Kronikol.dll"));
        Assert.Equal("../../bin/Debug/net10.0/Kronikol.dll", QueryScriptGenerator.Portable("../../bin/Debug/net10.0/Kronikol.dll"));
        Assert.Null(QueryScriptGenerator.Portable("/home/someone/src/Shop.Tests/bin/Debug/net10.0/Kronikol.dll"));
    }

    /// <summary>
    /// Where a run's reports go by default - <c>Reports</c> in the test project's output - the engine is the
    /// <c>Kronikol.dll</c> one folder up, so the path says exactly that and nothing more about the machine.
    /// </summary>
    [Fact]
    public void In_the_default_layout_the_engine_is_the_Kronikol_dll_one_folder_up()
    {
        var dir = Path.Combine(Path.GetDirectoryName(Library.Location)!, "QueryScriptReports-" + Guid.NewGuid().ToString("N"));
        try
        {
            Run(Options(dir));

            Assert.Contains("System.IO.Path.Combine(here, @\"../Kronikol.dll\")", Script(dir), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void It_binds_to_the_public_Run_that_the_engine_keeps()
    {
        var dir = Dir("Reports");
        Run(Options(dir));

        var script = Script(dir);
        Assert.Contains(".GetType(\"Kronikol.Query.QueryCommand\", throwOnError: true)", script, StringComparison.Ordinal);
        Assert.Contains(
            ".GetMethod(\"Run\", new[] { typeof(System.Collections.Generic.IReadOnlyList<string>), typeof(System.IO.TextWriter), typeof(System.IO.TextWriter) })",
            script, StringComparison.Ordinal);

        var run = typeof(QueryCommand).GetMethod("Run", BindingFlags.Public | BindingFlags.Static, [typeof(IReadOnlyList<string>), typeof(TextWriter), typeof(TextWriter)]);
        Assert.NotNull(run);
        Assert.Equal(typeof(int), run!.ReturnType);
    }

    [Fact]
    public void It_keeps_a_repositorys_build_settings_out()
    {
        var dir = Dir("Reports");
        Run(Options(dir));

        var script = Script(dir);
        foreach (var property in new[]
                 {
                     "PublishAot=false", "TargetFramework=net$(BundledNETCoreAppTargetFrameworkVersion)", "OutputType=Exe",
                     "LangVersion=latest", "TreatWarningsAsErrors=false", "WarningsAsErrors=", "WarningLevel=0",
                     "RunAnalyzers=false", "NuGetAudit=false", "ImportDirectoryBuildTargets=false", "TieredCompilationQuickJitForLoops=false"
                 })
            Assert.Contains($"\n#:property {property}\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteQueryScript_false_writes_none()
    {
        var dir = Dir("Reports");
        var options = Options(dir);
        options.WriteQueryScript = false;

        Run(options);

        Assert.True(File.Exists(Path.Combine(dir, "TestRunReport.json")));
        Assert.False(File.Exists(Path.Combine(dir, "query.cs")));
    }

    [Theory]
    [InlineData(false, DataFormat.Json)]
    [InlineData(true, DataFormat.Xml)]
    [InlineData(true, DataFormat.Yaml)]
    public void Without_a_JSON_data_file_there_is_nothing_for_it_to_query(bool dataFile, DataFormat format)
    {
        var dir = Dir("Reports");
        var options = Options(dir);
        options.GenerateTestRunReportData = dataFile;
        options.TestRunReportDataFormat = format;

        Run(options);

        Assert.False(File.Exists(Path.Combine(dir, "query.cs")));
    }

    [Fact]
    public void A_query_cs_Kronikol_did_not_write_is_left_as_it_is()
    {
        var dir = Dir("Reports");
        Directory.CreateDirectory(dir);
        const string theirs = "// my own scratch file\nSystem.Console.WriteLine(1);\n";
        File.WriteAllText(Path.Combine(dir, "query.cs"), theirs);

        Run(Options(dir));

        Assert.Equal(theirs, File.ReadAllText(Path.Combine(dir, "query.cs")));
        Assert.True(File.Exists(Path.Combine(dir, "TestRunReport.json")));
    }

    /// <summary>
    /// Whose the <c>query.cs</c> already there is gets asked before the outputs, outside what keeps one failed
    /// output from stopping the rest. One that cannot be read is taken as somebody else's: left as it is,
    /// never named, and every other output written.
    /// </summary>
    [Fact]
    public void A_query_cs_that_cannot_be_read_stops_nothing()
    {
        var dir = Dir("Reports");
        Directory.CreateDirectory(dir);
        const string earlier = "// Written by Kronikol 0.0.1 beside the report in this directory\n";
        var path = Path.Combine(dir, "query.cs");
        File.WriteAllText(path, earlier);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Run(Options(dir));

        Assert.True(File.Exists(Path.Combine(dir, "TestRunReport.json")));
        Assert.DoesNotContain("query.cs", File.ReadAllText(Path.Combine(dir, "Failures.md")), StringComparison.Ordinal);
        Assert.Equal(earlier, File.ReadAllText(path));
    }

    [Fact]
    public void Its_own_earlier_query_cs_is_replaced()
    {
        var dir = Dir("Reports");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "query.cs"), "// Written by Kronikol 0.0.1 beside the report in this directory\n");

        Run(Options(dir));

        Assert.DoesNotContain("0.0.1", Script(dir), StringComparison.Ordinal);
        Assert.Contains(Library.GetName().Version!.ToString(), Script(dir), StringComparison.Ordinal);
    }

    [Fact]
    public void It_belongs_to_the_directory_and_not_to_the_run()
    {
        var dir = Dir("Reports");
        Run(Options(dir));

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "Run.json")));
        var files = manifest.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetString()).ToArray();
        Assert.Contains("TestRunReport.json", files);
        Assert.DoesNotContain("query.cs", files);
    }

    [Fact]
    public void Inside_a_project_folder_it_is_not_written_and_the_report_says_why()
    {
        File.WriteAllText(Dir("Checkout.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var dir = Dir("Reports");

        Run(Options(dir));

        Assert.False(File.Exists(Path.Combine(dir, "query.cs")), "a C# project compiles every .cs below it, so query.cs would break its build");
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "TestRunReport.json")));
        var entry = Assert.Single(report.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Where(d => d.GetProperty("kind").GetString() == "OptionNotApplied"));
        var message = entry.GetProperty("message").GetString()!;
        Assert.Contains("WriteQueryScript", message, StringComparison.Ordinal);
        Assert.Contains("Checkout.Tests.csproj", message, StringComparison.Ordinal);
        Assert.Contains("a folder whose name starts with a dot", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A project that already keeps the folder out of its build does not stop the file: nested layouts have
    /// to, or they would compile the inner project's sources, and this repository has two (the template pack,
    /// and a test project's fixtures).
    /// </summary>
    [Theory]
    [InlineData("<ItemGroup><Compile Remove=\"Reports\\**\" /></ItemGroup>")]
    [InlineData("<ItemGroup><Compile Remove=\"Reports/**/*\" /></ItemGroup>")]
    [InlineData("<ItemGroup><Compile Remove=\"**\\*\" /></ItemGroup>")]
    [InlineData("<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>")]
    [InlineData("<PropertyGroup><EnableDefaultItems>false</EnableDefaultItems></PropertyGroup>")]
    [InlineData("<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"..\\Shared\\**\\*.cs\" /></ItemGroup>")]
    public void A_project_that_already_leaves_the_folder_out_does_not_stop_it(string body)
    {
        File.WriteAllText(Dir("Packs.csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk\">{body}</Project>");
        var dir = Dir("Reports");

        Run(Options(dir));

        Assert.True(File.Exists(Path.Combine(dir, "query.cs")));
    }

    /// <summary>What cannot be read for certain is taken as a build that would compile the file.</summary>
    [Theory]
    [InlineData("<ItemGroup Condition=\"'$(Configuration)' == 'Release'\"><Compile Remove=\"Reports\\**\" /></ItemGroup>")]
    [InlineData("<ItemGroup><Compile Remove=\"**\\*\" /><Compile Include=\"**\\*.cs\" /></ItemGroup>")]
    [InlineData("<ItemGroup><Compile Remove=\"Other\\**\" /></ItemGroup>")]
    [InlineData("<ItemGroup><Compile Remove=\"Reports\\**\" /><ItemGroup>")]
    [InlineData("<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"**\\*.cs\" Exclude=\"bin\\**;obj\\**\" /></ItemGroup>")]
    public void A_removal_that_may_not_apply_is_not_trusted(string body)
    {
        File.WriteAllText(Dir("Packs.csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk\">{body}</Project>");
        var dir = Dir("Reports");

        Run(Options(dir));

        Assert.False(File.Exists(Path.Combine(dir, "query.cs")));
    }

    /// <summary>
    /// A folder whose name starts with a dot is in no project's default items: the SDK leaves <c>**/.*/**</c>
    /// out, for <c>.git</c> and <c>.vs</c>. So <c>.logs/kronikol/</c> in a test project's folder takes the file,
    /// as <c>bin</c> does; <c>QueryScriptEndToEndTests</c> builds such a project.
    /// </summary>
    [Theory]
    [InlineData(".logs", "kronikol")]
    [InlineData("TestResults", ".kronikol")]
    public void In_a_dot_folder_of_that_project_it_is_written(string outer, string inner)
    {
        File.WriteAllText(Dir("Checkout.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var dir = Dir(outer, inner);

        Run(Options(dir));

        Assert.True(File.Exists(Path.Combine(dir, "query.cs")));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "TestRunReport.json")));
        Assert.DoesNotContain(report.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("kind").GetString() == "OptionNotApplied");
    }

    /// <summary>The dot-folder rule is the default items'. A glob the project writes itself takes the file back.</summary>
    [Fact]
    public void A_dot_folder_the_project_globs_itself_is_not_trusted()
    {
        File.WriteAllText(Dir("Checkout.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>"
            + "<ItemGroup><Compile Include=\"**\\*.cs\" Exclude=\"bin\\**;obj\\**\" /></ItemGroup></Project>");
        var dir = Dir(".logs", "kronikol");

        Run(Options(dir));

        Assert.False(File.Exists(Path.Combine(dir, "query.cs")));
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    public void Under_that_projects_bin_or_obj_it_is_written(string outputFolder)
    {
        File.WriteAllText(Dir("Checkout.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var dir = Dir(outputFolder, "Debug", "net10.0", "Reports");

        Run(Options(dir));

        Assert.True(File.Exists(Path.Combine(dir, "query.cs")));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "TestRunReport.json")));
        Assert.DoesNotContain(report.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("kind").GetString() == "OptionNotApplied");
    }

    [Fact]
    public void A_path_is_written_as_a_verbatim_literal_whatever_it_holds()
    {
        var script = QueryScriptGenerator.Build("3.32.0", "3.32.0.0", ownCopy: "../we\"ird dir/Kronikol.dll");

        Assert.Contains("@\"../we\"\"ird dir/Kronikol.dll\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_its_own_copy_of_the_engine_it_looks_only_in_the_NuGet_cache()
    {
        var script = QueryScriptGenerator.Build("3.32.0-Beta.1", "3.32.0.0", ownCopy: null);

        Assert.DoesNotContain("System.IO.Path.Combine(here, @\"", script, StringComparison.Ordinal);
        // NuGet's folders are lower case, and a case-sensitive file system holds it to that.
        Assert.Contains("\"kronikol\", \"3.32.0-beta.1\", \"lib\", \"net10.0\", \"Kronikol.dll\"", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where NUGET_PACKAGES and the default miss, NuGet itself says where its cache is: a NuGet.config can move
    /// it. Only then, since asking costs a process; and both the question and the folder it names are in the file.
    /// </summary>
    [Fact]
    public void When_the_cache_is_not_where_it_usually_is_it_asks_NuGet()
    {
        var script = QueryScriptGenerator.Build("3.32.0", "3.32.0.0", ownCopy: null);

        Assert.Contains("\"nuget locals global-packages --list\"", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("QueryScript.NuGetCache(here)", StringComparison.Ordinal)
                    > script.IndexOf("foreach (string candidate in candidates)", StringComparison.Ordinal),
            "NuGet is asked only after every other place has missed");
    }
}
