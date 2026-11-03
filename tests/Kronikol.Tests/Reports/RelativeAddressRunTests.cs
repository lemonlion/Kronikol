using System.Text.RegularExpressions;
using Kronikol.ComponentDiagram;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A record may hold a relative address: <see cref="MessageTracker"/> and <see cref="RequestResponseLog"/> take any
/// <see cref="Uri"/>. <see cref="Uri"/> refuses most of its members on one with an <see cref="InvalidOperationException"/>,
/// and the three readers on a run's way asked anyway, so a single such record cost the scenario's diagram, and with internal flow on, its default,
/// <c>TestRunReport.html</c> and <c>Specifications.html</c> as well (plans/CALLS_AND_EVENTS_PLAN.md F25).
/// </summary>
[Collection("DiagramsFetcher")]
public class RelativeAddressRunTests : IDisposable
{
    private const string Test = "relative-address-scenario";

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-relative-address").FullName;

    public RelativeAddressRunTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Probe",
            Scenarios = [new Scenario { Id = Test, DisplayName = "A publish", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }]
        }
    ];

    private static void Publish(Uri address) =>
        new MessageTracker(new MessageTrackerOptions { CallerName = "Test", ServiceName = "Bus", CurrentTestInfoFetcher = () => ("A publish", Test) })
            .TrackSendEvent("Publish", "Bus", address, new { Id = 1 });

    private static RequestResponseLog[] CallRecords(Uri address)
    {
        var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;
        return
        [
            new RequestResponseLog("A publish", Test, HttpMethod.Post, null, address, [], "Bus", "Test",
                RequestResponseType.Request, trace, id, false) { Timestamp = at },
            new RequestResponseLog("A publish", Test, HttpMethod.Post, null, address, [], "Bus", "Test",
                RequestResponseType.Response, trace, id, false, System.Net.HttpStatusCode.OK) { Timestamp = at.AddMilliseconds(5) }
        ];
    }

    private static void Call(Uri address)
    {
        foreach (var record in CallRecords(address))
            RequestResponseLogger.Log(record);
    }

    /// <summary>One whole run of <paramref name="record"/> in a folder of its own: the files it wrote, and what it said.</summary>
    private (string Folder, string[] Files, string Console) Run(string name, Action record, Action<ReportConfigurationOptions> configure)
    {
        var folder = Path.Combine(_dir, name);
        var options = new ReportConfigurationOptions { ReportsFolderPath = folder, WriteRunSummaryToConsole = false };
        configure(options);
        using var isolation = RequestResponseLogger.IsolateForTests();
        using var console = new ThreadScopedConsole();
        DefaultDiagramsFetcher.Reset();
        record();
        ReportGenerator.CreateStandardReportsWithDiagrams(Features(), DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow, options);
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        return (folder, files, console.Text);
    }

    public static TheoryData<string> Configurations => ["default", "no-flow", "mergeable", "data-only"];

    private static void Configure(string configuration, ReportConfigurationOptions options)
    {
        if (configuration == "no-flow")
            options.InternalFlowTracking = false;
        if (configuration is "mergeable" or "data-only")
            options.GenerateMergeableData = true;
        if (configuration == "data-only")
        {
            options.GenerateTestRunReport = false;
            options.GenerateSpecificationsReport = false;
            options.GenerateSpecificationsData = false;
            options.GenerateComponentDiagram = false;
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void A_relative_address_costs_a_run_no_file(string configuration)
    {
        var control = Run("control", () => Publish(new Uri("event://orders")), o => Configure(configuration, o));
        var relative = Run("relative", () => Publish(new Uri("orders", UriKind.Relative)), o => Configure(configuration, o));

        Assert.Equal(control.Files, relative.Files);
        Assert.DoesNotContain("relative URI", relative.Console, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_on_a_relative_address_costs_a_run_no_file()
    {
        var control = Run("control", () => Call(new Uri("http://orders/orders")), _ => { });
        var relative = Run("relative", () => Call(new Uri("orders?page=1", UriKind.Relative)), _ => { });

        Assert.Equal(control.Files, relative.Files);
        Assert.DoesNotContain("relative URI", relative.Console, StringComparison.Ordinal);
    }

    /// <summary>The address as it was written, query and all, is what the diagram's arrow names.</summary>
    [Fact]
    public void The_diagram_names_a_relative_address_as_it_was_written()
    {
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(CallRecords(new Uri("orders?page=1", UriKind.Relative)))
            .Single().PlantUmls.Single().PlainText;

        Assert.Contains("POST: orders?page=1", source, StringComparison.Ordinal);
    }

    /// <summary>The call ordering's path: a relative address up to its query or fragment, as an absolute one gives its path.</summary>
    [Theory]
    [InlineData("orders?page=1", "orders")]
    [InlineData("orders#top", "orders")]
    [InlineData("orders", "orders")]
    [InlineData("http://orders/orders?page=1", "/orders")]
    public void The_call_ordering_reads_an_address_of_either_shape(string written, string path)
    {
        var ordering = ComponentFlowSegmentBuilder.BuildCallOrdering(CallRecords(new Uri(written, UriKind.RelativeOrAbsolute))).Single();

        Assert.Equal(path, ordering.Entries.Single().Path);
    }

    /// <summary>Repeated calls on one relative address collapse as they do on an absolute one, and two addresses stay two.</summary>
    [Fact]
    public void Repeated_calls_on_a_relative_address_collapse()
    {
        var (same, other) = (new Uri("orders?page=1", UriKind.Relative), new Uri("orders?page=2", UriKind.Relative));
        var logs = CallRecords(same).Concat(CallRecords(same)).Concat(CallRecords(same)).Concat(CallRecords(other)).ToArray();

        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, collapseConsecutiveIdenticalCalls: true, collapseThreshold: 2)
            .Single().PlantUmls.Single().PlainText;

        Assert.Contains("loop ×3", source, StringComparison.Ordinal);
        Assert.Contains("POST: orders?page=1", source, StringComparison.Ordinal);
        Assert.Contains("POST: orders?page=2", source, StringComparison.Ordinal);
    }

    /// <summary>The same record through a shard and a merge, whose reader builds the address back from the file.</summary>
    /// <summary>As it was written, an end space too: <see cref="Uri"/> trims one from an absolute address, not from a relative one, so a merge has nothing to keep.</summary>
    [Theory]
    [InlineData("orders")]
    [InlineData("orders ")]
    public void A_merge_reads_a_shard_holding_a_relative_address(string address)
    {
        var run = Run("shard", () => Publish(new Uri(address, UriKind.Relative)), o => Configure("mergeable", o));
        var output = Path.Combine(_dir, "merged", "Combined.html");
        var error = new StringWriter();

        Assert.True(MergeCommand.Run([run.Folder, "-o", output], new StringWriter(), error, _ => null) == 0, error.ToString());

        Assert.True(File.Exists(output));
        Assert.Contains(MergeableReportReader.ReadFile(Path.ChangeExtension(output, ".krkl")).Interactions, call => call.Uri.ToString() == address);
    }

    /// <summary>
    /// What stops the next reader: a record's address asked for a member a relative one refuses, with no
    /// <c>IsAbsoluteUri</c> on the line or the one before it, where a wrapped condition writes it. <c>RecordedUri</c> answers for both shapes.
    /// </summary>
    [Fact]
    public void No_reader_asks_a_recorded_address_for_what_a_relative_one_refuses()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kronikol.sln")))
            root = root.Parent;
        var src = Path.Combine(root!.FullName, "src", "Kronikol");
        var read = 0;
        var findings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            read++;
            var number = 0;
            var before = "";
            foreach (var line in File.ReadLines(file))
            {
                number++;
                if (AsksWhatARelativeAddressRefuses(line, before))
                    findings.Add($"{Path.GetRelativePath(root.FullName, file)}:{number}: {line.Trim()}");
                before = line;
            }
        }

        Assert.True(read > 200, $"read {read} files under {src}");
        Assert.True(findings.Count == 0, "Use RecordedUri, as a record's address may be relative:\n" + string.Join('\n', findings));
    }

    private static readonly Regex Refused = new(
        @"\b(?:\w*\.)?(?:Uri|effectiveUri)[?!]?\.(?:AbsolutePath|AbsoluteUri|PathAndQuery|Host|DnsSafeHost|IdnHost|Port|IsDefaultPort|Scheme|Authority|Query|Fragment|Segments|LocalPath|UserInfo|HostNameType|IsLoopback|IsFile|IsUnc|GetLeftPart|GetComponents|MakeRelativeUri|MakeRelative)\b",
        RegexOptions.Compiled);

    private static readonly Regex Comment = new(@"(^|\s)//.*$", RegexOptions.Compiled);

    /// <summary>
    /// The guard's reading of one line, given the one before it. It sees <c>Uri</c> and <c>effectiveUri</c>, as a record's address is named,
    /// so a lower-case local is out of its sight, and it reads <c>src/Kronikol</c>, where a run's readers live.
    /// </summary>
    private static bool AsksWhatARelativeAddressRefuses(string line, string before)
    {
        var code = Comment.Replace(line, "");
        return Refused.IsMatch(code) && !code.Contains("IsAbsoluteUri", StringComparison.Ordinal) && !Comment.Replace(before, "").Contains("IsAbsoluteUri", StringComparison.Ordinal);
    }

    /// <summary>The guard's own test, on the lines it exists to refuse and those it must let by: a pattern that cannot match guards nothing.</summary>
    [Theory]
    [InlineData("Path: l.Uri.AbsolutePath)).ToArray();", "", true)]
    [InlineData("var fullPathAndQuery = effectiveUri.PathAndQuery;", "", true)]
    [InlineData("request.Uri.PathAndQuery,", "", true)]
    [InlineData("request.Uri?.PathAndQuery,", "", true)]
    [InlineData("request.Uri!.Host,", "", true)]
    [InlineData("log.Uri.GetLeftPart(UriPartial.Path)", "", true)]
    [InlineData("log.Uri.GetComponents(UriComponents.Path, UriFormat.Unescaped)", "", true)]
    [InlineData("var path = r.Uri.IsAbsoluteUri ? r.Uri.PathAndQuery : r.Uri.OriginalString;", "", false)]
    [InlineData("    ? r.Uri.PathAndQuery", "var path = r.Uri.IsAbsoluteUri", false)]
    [InlineData("    ? r.Uri.PathAndQuery", "// r.Uri.IsAbsoluteUri", true)]
    [InlineData("// r.Uri.PathAndQuery", "", false)]
    [InlineData("var text = r.Uri.ToString();", "", false)]
    public void The_guard_reads_the_lines_it_was_written_for(string line, string before, bool matches)
    {
        Assert.Equal(matches, AsksWhatARelativeAddressRefuses(line, before));
    }

    /// <summary>On Linux <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> takes <c>/orders?page=1</c> for a file, and escapes its query into the path.</summary>
    [Theory]
    [InlineData("http://svc/foo?x=1", true)]
    [InlineData("kafka:///orders", true)]
    [InlineData("orders", false)]
    [InlineData("/orders?page=1", false)]
    [InlineData("mailto:a@b", false)]
    public void An_address_is_absolute_when_it_says_so(string address, bool absolute)
    {
        Assert.Equal(absolute, RecordedUri.TryAbsolute(address, out var uri));
        Assert.Equal(absolute, uri is not null);
    }
}
