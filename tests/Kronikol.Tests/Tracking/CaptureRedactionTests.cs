using System.Net;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class CaptureRedactionTests : IDisposable
{
    public CaptureRedactionTests() => RequestResponseLogger.Redaction = null;

    public void Dispose() => RequestResponseLogger.Redaction = null;

    private static RequestResponseLog Entry(string testId, (string, string?)[] headers, string? content = null) => new(
        "Test", testId, HttpMethod.Get, content, new Uri("http://api.example.com/orders"), headers,
        "OrderApi", "Test", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);

    [Fact]
    public void Secrets_preset_replaces_credential_header_values_before_storage()
    {
        RequestResponseLogger.Redaction = CaptureRedaction.Secrets();
        var testId = Guid.NewGuid().ToString();

        RequestResponseLogger.Log(Entry(testId, [("Authorization", "Bearer top-secret"), ("Cookie", "session=abc"), ("Accept", "application/json")]));

        var stored = Assert.Single(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId));
        Assert.Equal("[REDACTED]", stored.Headers.Single(h => h.Key == "Authorization").Value);
        Assert.Equal("[REDACTED]", stored.Headers.Single(h => h.Key == "Cookie").Value);
        Assert.Equal("application/json", stored.Headers.Single(h => h.Key == "Accept").Value);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId),
            l => l.Headers.Any(h => h.Value?.Contains("top-secret") == true));
    }

    [Fact]
    public void Header_matching_is_case_insensitive_and_drop_mode_removes_the_header()
    {
        RequestResponseLogger.Redaction = new CaptureRedaction(["x-api-key"]) { DropHeaders = true };
        var testId = Guid.NewGuid().ToString();

        RequestResponseLogger.Log(Entry(testId, [("X-API-KEY", "k-123"), ("Accept", "*/*")]));

        var stored = Assert.Single(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId));
        Assert.DoesNotContain(stored.Headers, h => h.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase));
        Assert.Single(stored.Headers);
    }

    [Fact]
    public void Content_patterns_scrub_tokens_inside_bodies()
    {
        RequestResponseLogger.Redaction = CaptureRedaction.Secrets().RedactContent(@"Bearer\s+[A-Za-z0-9\-_\.]+", "Bearer [REDACTED]");
        var testId = Guid.NewGuid().ToString();

        RequestResponseLogger.Log(Entry(testId, [], """{"auth":"Bearer abc.def.ghi","ok":true}"""));

        var stored = Assert.Single(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId));
        Assert.Equal("""{"auth":"Bearer [REDACTED]","ok":true}""", stored.Content);
    }

    [Fact]
    public void Custom_hook_can_drop_an_entry_entirely()
    {
        RequestResponseLogger.Redaction = new CaptureRedaction([]) { Custom = log => log.Uri.AbsolutePath.Contains("health") ? null : log };
        var testId = Guid.NewGuid().ToString();

        RequestResponseLogger.Log(Entry(testId, []) with { Uri = new Uri("http://api.example.com/health") });
        RequestResponseLogger.Log(Entry(testId, []));

        Assert.Single(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId));
    }

    [Fact]
    public void Redaction_runs_before_content_truncation()
    {
        RequestResponseLogger.Redaction = CaptureRedaction.Secrets().RedactContent("secret", "***");
        RequestResponseLogger.MaxContentLength = 8;
        try
        {
            var testId = Guid.NewGuid().ToString();
            RequestResponseLogger.Log(Entry(testId, [], "secret-value-that-is-long"));
            var stored = Assert.Single(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId));
            Assert.StartsWith("***-valu", stored.Content);
            Assert.Contains("truncated", stored.Content);
        }
        finally
        {
            RequestResponseLogger.MaxContentLength = null;
        }
    }

    [Fact]
    public void No_redaction_configured_stores_entries_verbatim()
    {
        var testId = Guid.NewGuid().ToString();
        RequestResponseLogger.Log(Entry(testId, [("Authorization", "Bearer raw")]));
        var stored = Assert.Single(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId));
        Assert.Equal("Bearer raw", stored.Headers.Single().Value);
    }

    [Fact]
    public void Phase_variants_are_redacted_too()
    {
        var redaction = CaptureRedaction.Secrets();
        var log = Entry("t", [("Authorization", "x")]) with
        {
            SetupVariant = new PhaseVariant(HttpMethod.Get, new Uri("http://a/b"), "body", [("Authorization", "setup-secret")], false),
        };

        var result = redaction.Apply(log)!;

        Assert.Equal("[REDACTED]", result.SetupVariant!.Headers.Single().Value);
        Assert.Equal("[REDACTED]", result.Headers.Single().Value);
    }

    [Fact]
    public void Secret_never_reaches_the_TestRunReport_json_when_redaction_is_on()
    {
        RequestResponseLogger.Redaction = CaptureRedaction.Secrets();
        var testId = "redact-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Secret test", testId, HttpMethod.Get, new Uri("http://api/x"), "Api", "Test");
        // LogPair has no headers; log a raw entry with a secret header too.
        RequestResponseLogger.Log(Entry(testId, [("Authorization", "Bearer never-on-disk")]) with { Type = RequestResponseType.Request });

        var dir = Path.Combine(Path.GetTempPath(), "kronikol-redact-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new ReportConfigurationOptions
            {
                ReportsFolderPath = dir,
                InternalFlowTracking = false,
                GenerateComponentDiagram = false,
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
            };
            DefaultDiagramsFetcher.Reset();
            Kronikol.Reports.ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Kronikol.Reports.Feature { DisplayName = "F", Scenarios = [new Kronikol.Reports.Scenario { Id = testId, DisplayName = "Secret test", Result = Kronikol.Reports.ExecutionResult.Passed }] }],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
            DefaultDiagramsFetcher.Reset();

            var json = File.ReadAllText(Path.Combine(dir, "TestRunReport.json"));
            Assert.DoesNotContain("never-on-disk", json);
            Assert.Contains("[REDACTED]", json);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}
