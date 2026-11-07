using System.Text.RegularExpressions;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// The guard (plans/FAILED_CALLS_PLAN.md §5.7), read from the source. Every request a tracker builds goes through
/// <c>OpenCall.Open</c>, so the call it opens is answered once by the code that opened it, or its file is on
/// <see cref="BuiltElsewhere"/> with the reason its calls are still answered. And every extension that logs a request has a test
/// project holding a contract fact (<see cref="FailedCallContract"/>), or is on <see cref="NoContractFact"/> with the reason.
/// A new tracker that builds its requests any other way fails here until it is moved or listed.
/// </summary>
public partial class FailedCallGuardTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>Files under <c>src/</c> that build a request without <c>OpenCall.Open</c>, each with why its calls are answered.</summary>
    internal static readonly IReadOnlyDictionary<string, string> BuiltElsewhere = new Dictionary<string, string>
    {
        ["src/Kronikol/TrackingDiagramOverride.cs"] = "markers drawn on the diagram, not calls: nothing answers them",
        ["src/Kronikol/Tracking/RequestResponseLogger.cs"] =
            "LogPair writes both halves of a call that has ended, for TrackingProxy and the deferred queue, a failure among them",
        ["src/Kronikol/Tracking/PendingRequestResponseLogs.cs"] =
            "the deferred queue holds both halves of calls that have ended; its flush writes them, after a send that threw too (F28)",
        ["src/Kronikol/Tracking/MessageTracker.cs"] = "public and called by hand: the consumer writes both halves",
        ["src/Kronikol.Extensions.BigQuery/BigQueryTracker.cs"] = "public and called by hand: the consumer writes both halves",
        ["src/Kronikol.Extensions.Bigtable/BigtableTracker.cs"] = "public and called by hand: the consumer writes both halves",
        ["src/Kronikol.Extensions.Elasticsearch/ElasticsearchTrackingCallbackHandler.cs"] =
            "the transport's callback comes once the call has ended, so it writes both halves at once, a call that never reached the server among them (F22)",
        ["src/Kronikol.Extensions.MassTransit/MassTransitTracker.cs"] =
            "its observers see a message once it was handed on or faulted, and write both halves then, a fault's own call among them",
        ["src/Kronikol.Extensions.Otlp/MappedSpan.cs"] = "a span arrives once its call has ended: both halves at once, an ERROR span's reason among them",
        ["src/Kronikol.Extensions.ProxyTap/ProxyTap.cs"] = "writes both halves to its sink once the forward has ended, a failed forward among them",
        ["src/Kronikol.Extensions.TcpTap/TcpTap.cs"] =
            "a decoded exchange is written once its reply came or its connection closed before it (Q5): both halves at once",
    };

    /// <summary>Extensions that log a request with no contract fact in their test project, each with why none can apply.</summary>
    internal static readonly IReadOnlyDictionary<string, string> NoContractFact = new Dictionary<string, string>
    {
        ["Kronikol.Extensions.Bigtable"] = "BigtableTracker is called by hand: the consumer writes both halves, and it writes no failure of its own",
    };

    /// <summary>The test project an extension's facts live in, where it is not the one named after it.</summary>
    private static readonly IReadOnlyDictionary<string, string> TestProjectOf = new Dictionary<string, string>
    {
        ["Kronikol.Extensions.ClickHouse.Driver"] = "Kronikol.Tests.ClickHouse",
    };

    [Fact]
    public void Every_request_built_under_src_goes_through_OpenCall_or_is_listed()
    {
        var unlisted = RequestBuilders()
            .Where(f => f.Requests > f.Opens && !BuiltElsewhere.ContainsKey(f.Path))
            .Select(f => $"{f.Path}: {f.Requests} built, {f.Opens} through OpenCall.Open")
            .ToArray();

        Assert.True(unlisted.Length == 0,
            "These build a request that no OpenCall.Open opens. Move them onto OpenCall, or list them with the reason their calls " +
            "are still answered once:\n" + string.Join("\n", unlisted));
    }

    [Fact]
    public void Every_listed_file_still_builds_a_request()
    {
        var builders = RequestBuilders().Where(f => f.Requests > f.Opens).Select(f => f.Path).ToHashSet();

        Assert.Empty(BuiltElsewhere.Keys.Where(path => !builders.Contains(path)));
    }

    [Fact]
    public void Every_extension_that_logs_a_request_has_a_contract_fact()
    {
        var missing = Directory.GetDirectories(Path.Combine(RepoRoot, "src"), "Kronikol.Extensions.*")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(project => SourcesOf(Path.Combine(RepoRoot, "src", project)).Any(s => LogsARequest().IsMatch(s.Text)))
            .Where(project => !NoContractFact.ContainsKey(project))
            .Where(project => !HoldsAContractFact(TestProjectOf.GetValueOrDefault(project, "Kronikol.Tests." + project["Kronikol.Extensions.".Length..])))
            .ToArray();

        Assert.True(missing.Length == 0,
            "These log a request, and their test project holds no fact with [Trait(FailedCallContract.Trait, FailedCallContract.Value)] " +
            "driving a call that throws:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void The_guard_reads_a_request_logged_straight_to_the_logger()
    {
        // A scratch tracker as the plan's §6.3 describes: it builds a request and logs it, and nothing opens a call.
        const string scratch = """
            RequestResponseLogger.Log(new RequestResponseLog(name, id, HttpMethod.Get, null, uri, [], "Service", "Caller",
                RequestResponseType.Request, traceId, requestResponseId, false));
            """;

        Assert.Equal((1, 0), Count(scratch));
        Assert.Equal((1, 1), Count("var call = OpenCall.Open(new RequestResponseLog(a, b, RequestResponseType.Request, c));"));
        Assert.Equal((0, 0), Count("new RequestResponseLog(a, b, RequestResponseType.Response, c)"));
    }

    [Fact]
    public void The_guard_reads_the_trait_written_short_or_in_full()
    {
        Assert.Matches(ContractTrait(), "[Trait(FailedCallContract.Trait, FailedCallContract.Value)]");
        Assert.Matches(ContractTrait(), "[Trait(Kronikol.Tests.Tracking.FailedCallContract.Trait, Kronikol.Tests.Tracking.FailedCallContract.Value)]");
        Assert.DoesNotMatch(ContractTrait(), "[Trait(\"Category\", \"FailedCall\")]");
    }

    private static bool HoldsAContractFact(string testProject)
    {
        var directory = Path.Combine(RepoRoot, "tests", testProject);
        return Directory.Exists(directory)
               && SourcesOf(directory).Any(s => ContractTrait().IsMatch(s.Text));
    }

    private static IEnumerable<(string Path, int Requests, int Opens)> RequestBuilders() =>
        SourcesOf(Path.Combine(RepoRoot, "src"))
            .Select(s => (s.Path, Count: Count(s.Text)))
            .Where(f => f.Count.Requests > 0)
            .Select(f => (f.Path, f.Count.Requests, f.Count.Opens));

    // A request is a RequestResponseLog built with RequestResponseType.Request among its arguments, or given it as its Type.
    internal static (int Requests, int Opens) Count(string text)
    {
        var requests = 0;
        foreach (Match construction in Construction().Matches(text))
            if (RequestType().IsMatch(Arguments(text, construction.Index + construction.Length)))
                requests++;
        requests += TypeSetToRequest().Count(text);
        return (requests, OpenCallOpen().Count(text));
    }

    // The text of a call's arguments, up to the parenthesis that closes the one at start - 1.
    private static string Arguments(string text, int start)
    {
        var depth = 1;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return text[start..i];
        }
        return text[start..];
    }

    private static IEnumerable<(string Path, string Text)> SourcesOf(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(f => (Path.GetRelativePath(RepoRoot, f).Replace('\\', '/'), File.ReadAllText(f)));

    // The trait, its names written short or in full.
    [GeneratedRegex(@"\[Trait\((?:[\w.]+\.)?FailedCallContract\.Trait,\s*(?:[\w.]+\.)?FailedCallContract\.Value\)\]")]
    private static partial Regex ContractTrait();

    [GeneratedRegex(@"\bnew\s+RequestResponseLog\s*\(")]
    private static partial Regex Construction();

    [GeneratedRegex(@"RequestResponseType\.Request\b")]
    private static partial Regex RequestType();

    [GeneratedRegex(@"\bType\s*=\s*RequestResponseType\.Request\b")]
    private static partial Regex TypeSetToRequest();

    [GeneratedRegex(@"\b(?:OpenCall|_open)\.Open\(")]
    private static partial Regex OpenCallOpen();

    [GeneratedRegex(@"RequestResponseType\.Request\b|OpenCall\.Open\(|\.LogRequest[A-Za-z]*\(|SqlDiagnosticTracker|LogRedisRequest|TestTrackingMessageHandler|TrackingProxy")]
    private static partial Regex LogsARequest();
}
