using Grpc.Core;
using Grpc.Core.Interceptors;
using Kronikol.Extensions.Grpc;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The HttpContextAccessor column of <c>DiagnosticReport.html</c>, painted (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md
/// section 4.4): a group of components counts the instances that hold an accessor. Until 4.9.1 it read one instance
/// of an unordered bag and painted <c>✓ configured</c> or <c>⚠ null</c> by registration order.
/// </summary>
// Reads the process-wide component registry and log store but runs no report pipeline; each fact names its own
// components, so other facts' registrations do not reach its row.
[Collection(PlaywrightCollections.Reports)]
public class DiagnosticPageAccessorColumnTests : PlaywrightTestBase
{
    public DiagnosticPageAccessorColumnTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Two_handlers_of_one_caller_one_with_an_accessor_paint_one_of_two()
    {
        var caller = "Diagnostic column " + Guid.NewGuid().ToString("N")[..8];
        var testId = Guid.NewGuid().ToString();
        var options = new TestTrackingMessageHandlerOptions
        {
            CallerName = caller,
            FixedNameForReceivingService = "Target",
            CurrentTestInfoFetcher = () => ("Diagnostic column", testId),
        };

        // A host-side handler (an accessor, with no request current here) and a test-side one (none), both invoked.
        foreach (var accessor in new IHttpContextAccessor?[] { new HttpContextAccessor(), null })
        {
            using var invoker = new HttpMessageInvoker(new TestTrackingMessageHandler(options, accessor) { InnerHandler = new OkHandler() });
            await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://target:5000/orders"), CancellationToken.None);
        }

        await OpenDiagnosticPage(testId);

        var cell = AccessorCell($"TestTrackingMessageHandler ({caller}) (2 instances)");
        await Expect(cell).ToBeVisibleAsync();
        await Expect(cell).ToHaveTextAsync("1 of 2");
    }

    [Fact]
    public async Task A_host_side_and_a_test_side_gRPC_interceptor_paint_one_of_two()
    {
        var service = "Orders gRPC " + Guid.NewGuid().ToString("N")[..8];
        var testId = Guid.NewGuid().ToString();
        GrpcTrackingOptions Options() => new()
        {
            ServiceName = service,
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Diagnostic column", testId),
        };
        var method = new Method<string, string>(MethodType.Unary, "orders.Orders", "Get",
            Marshallers.StringMarshaller, Marshallers.StringMarshaller);

        // The host's client holds the host's accessor; the test's holds none. Both are invoked.
        foreach (var interceptor in new[] { new GrpcTrackingInterceptor(Options(), new HttpContextAccessor()), new GrpcTrackingInterceptor(Options()) })
        {
            await interceptor.AsyncUnaryCall("request", new ClientInterceptorContext<string, string>(method, null, new CallOptions()),
                (_, _) => new AsyncUnaryCall<string>(Task.FromResult("response"), Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess, () => new Metadata(), () => { })).ResponseAsync;
        }

        await OpenDiagnosticPage(testId);

        var cell = AccessorCell($"GrpcTrackingInterceptor ({service}) (2 instances)");
        await Expect(cell).ToBeVisibleAsync();
        await Expect(cell).ToHaveTextAsync("1 of 2");
    }

    /// <summary>The last cell of the Tracking Components row whose instance list is headed <paramref name="summary"/>.</summary>
    private ILocator AccessorCell(string summary) =>
        Page.Locator("tr", new() { Has = Page.Locator("summary", new() { HasTextString = summary }) })
            .Locator(":scope > td").Last;

    private async Task OpenDiagnosticPage(string testId)
    {
        var runDirectory = Path.Combine(TempDir, "run");
        Directory.CreateDirectory(runDirectory);
        using (ReportGenerator.ScopeReportsDirectory(runDirectory))
        {
            DiagnosticReportGenerator.Generate(
                RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray(), [],
                new ReportConfigurationOptions());
        }

        await Page.GotoAsync(new Uri(Path.Combine(runDirectory, "DiagnosticReport.html")).AbsoluteUri);
        await Page.Locator("h2", new() { HasTextString = "Tracking Components" }).WaitForAsync();
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("ok") });
    }
}
