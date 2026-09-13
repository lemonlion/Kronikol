using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A failing CI job used to say nothing about how to debug itself unless somebody had turned on
/// <c>WriteCiSummary</c> — which generates the full <c>CiSummary.md</c>, rendered diagrams and all, at a
/// measured 48&#160;KB. Four lines of advice and a 48&#160;KB artifact were one switch, so almost nobody
/// had the four lines.
///
/// <para>This is a <b>new</b> option defaulting to on, not a change to either existing default: flipping a
/// default so existing code behaves differently without being touched is a MAJOR bump under this
/// repository's own rule, and there is no major release to put it in.</para>
///
/// <para><b>Both channels, because neither is enough.</b> Content written only to
/// <c>$GITHUB_STEP_SUMMARY</c> is absent from <c>gh run view --log</c>, the command an agent reaches for.
/// And the console is not reliable either — measured on .NET&#160;10, <c>dotnet test</c> under NUnit&#160;4
/// let a library's stderr through while swallowing its stdout, and under xUnit&#160;2 swallowed both. So
/// the block goes to stdout <em>and</em> the job summary, and the files beside the report stay the channel
/// that always works.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class CiDebugSectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cidebug-" + Guid.NewGuid().ToString("N"));
    private readonly string _summary;
    private readonly string? _previousGitHub;
    private readonly string? _previousSummary;

    public CiDebugSectionTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
        _summary = Path.Combine(_dir, "step-summary.md");

        _previousGitHub = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        _previousSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GITHUB_ACTIONS", _previousGitHub);
        Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", _previousSummary);
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string Run(bool onCi, bool failing, Action<ReportConfigurationOptions>? configure = null)
    {
        Environment.SetEnvironmentVariable("GITHUB_ACTIONS", onCi ? "true" : null);
        Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", onCi ? _summary : null);

        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = _dir,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
        };
        configure?.Invoke(options);

        var testId = "cidebug-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Pay", testId, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");

        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [
                    new Feature
                    {
                        DisplayName = "Checkout",
                        Scenarios =
                        [
                            new Scenario
                            {
                                Id = testId, DisplayName = "Pay",
                                Result = failing ? ExecutionResult.Failed : ExecutionResult.Passed,
                                ErrorMessage = failing ? "Assert.Equal() Failure" : null
                            }
                        ]
                    }
                ],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
        }
        finally
        {
            Console.SetOut(original);
        }

        return captured.ToString();
    }

    private string StepSummary() => File.Exists(_summary) ? File.ReadAllText(_summary) : "";

    [Fact]
    public void A_failing_ci_run_says_how_to_debug_itself_on_both_channels()
    {
        var console = Run(onCi: true, failing: true);

        Assert.Contains("Debug this run", console, StringComparison.Ordinal);
        Assert.Contains("kronikol query failures", console, StringComparison.Ordinal);

        // ...and the same block in the job summary, which is where a person looks.
        Assert.Contains("Debug this run", StepSummary(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_green_ci_run_says_nothing()
    {
        // A block that speaks on every run is a block people learn to skip, and there is nothing to debug.
        var console = Run(onCi: true, failing: false);

        Assert.DoesNotContain("Debug this run", console, StringComparison.Ordinal);
        Assert.DoesNotContain("Debug this run", StepSummary(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failing_local_run_says_nothing()
    {
        // Off CI the run-end pointer is right there and reliable; a second block would be noise.
        var console = Run(onCi: false, failing: true);

        Assert.DoesNotContain("Debug this run", console, StringComparison.Ordinal);
    }

    [Fact]
    public void It_can_be_turned_off()
    {
        var console = Run(onCi: true, failing: true, o => o.WriteCiDebugSection = false);

        Assert.DoesNotContain("Debug this run", console, StringComparison.Ordinal);
        Assert.DoesNotContain("Debug this run", StepSummary(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_full_ci_summary_is_still_one_block_not_two()
    {
        // WriteCiSummary already appends this same section to the same place. Both on must not double it.
        Run(onCi: true, failing: true, o => o.WriteCiSummary = true);

        var occurrences = StepSummary().Split("## Debug this run").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Neither_existing_default_moved()
    {
        // The reason this is a new option at all. Changing either of these would be a MAJOR bump.
        var fresh = new ReportConfigurationOptions();
        Assert.False(fresh.WriteCiSummary);
        Assert.False(fresh.PublishCiArtifacts);
        Assert.True(fresh.WriteCiDebugSection);
    }
}
