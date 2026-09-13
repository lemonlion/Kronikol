using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>Failures.md</c> ends every worked failure with "open in the report", pointing at
/// <c>TestRunReport.html#sid-…</c>. The digest baked that link whatever the configuration said, while the
/// query tool gates the same link on the HTML actually being there — so the two surfaces disagreed about
/// whether a report exists, and the one that persists to disk was the one that guessed.
///
/// <para>A dangling link in a file an agent is told to read first is worse than no link: it is the digest
/// answering "where do I see this in context?" with a path, and the reader spends its next step finding
/// out the path is not a file. <c>GenerateTestRunReport = false</c> with the digest on is not an odd
/// combination — it is what someone wanting the failures and the data without a 10 MB page asks for.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class DigestDeepLinkGatingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-link-" + Guid.NewGuid().ToString("N"));

    public DigestDeepLinkGatingTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private void Run(bool html)
    {
        var testId = "link-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Pay", testId, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");
        ReportGenerator.CreateStandardReportsWithDiagrams(
            [
                new Feature
                {
                    DisplayName = "Checkout",
                    Scenarios = [new Scenario { Id = testId, DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "Assert.Equal() Failure" }]
                }
            ],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            new ReportConfigurationOptions
            {
                ReportsFolderPath = _dir,
                InternalFlowTracking = false,
                GenerateComponentDiagram = false,
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
                GenerateTestRunReport = html
            });
    }

    [Fact]
    public void A_digest_written_without_the_html_report_links_to_nothing()
    {
        Run(html: false);

        Assert.False(File.Exists(Path.Combine(_dir, "TestRunReport.html")));

        var markdown = File.ReadAllText(Path.Combine(_dir, "Failures.md"));
        Assert.DoesNotContain("#sid-", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("open in the report", markdown, StringComparison.Ordinal);

        // The jsonl says absent rather than guessing, so a script can branch on it.
        var failure = Assert.Single(FailuresJsonl.Failures(File.ReadAllText(Path.Combine(_dir, "Failures.jsonl"))));
        Assert.Equal(JsonValueKind.Null, failure.GetProperty("deepLink").ValueKind);

        // What the failure IS addressed by does not depend on the HTML, and must still be there.
        Assert.Contains("stableId", markdown, StringComparison.Ordinal);
        Assert.NotEmpty(failure.GetProperty("stableId").GetString()!);
    }

    [Fact]
    public void A_digest_written_beside_the_html_report_still_links_to_it()
    {
        Run(html: true);

        var markdown = File.ReadAllText(Path.Combine(_dir, "Failures.md"));
        var failure = Assert.Single(FailuresJsonl.Failures(File.ReadAllText(Path.Combine(_dir, "Failures.jsonl"))));
        var stableId = failure.GetProperty("stableId").GetString();

        Assert.Contains($"TestRunReport.html#sid-{stableId}", markdown, StringComparison.Ordinal);
        Assert.Equal($"TestRunReport.html#sid-{stableId}", failure.GetProperty("deepLink").GetString());

        // ...and the thing it names is really in the page, which is the half a link test usually skips.
        // `#sid-` is not an element id: the page carries `data-stable-id` and a hash handler resolves the
        // one to the other, so both halves have to be there for the link to land.
        var page = File.ReadAllText(Path.Combine(_dir, "TestRunReport.html"));
        Assert.Contains($"data-stable-id=\"{stableId}\"", page, StringComparison.Ordinal);
        Assert.Contains("sid-", page, StringComparison.Ordinal);
    }
}
