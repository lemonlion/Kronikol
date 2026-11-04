using System.Net;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using ProbeBddfyFluent;
using TestStack.BDDfy;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(Setup))]

namespace ProbeBddfyFluent;

// BDDfy's fluent And and But: the title keeps its "And" or "But", and the step's order gives the keyword, so the collector draws And And y.
public class Setup : IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions());
        return ValueTask.CompletedTask;
    }
}

public static class Http
{
    public static readonly HttpClient Client = new(new TestTrackingMessageHandler(
        new BDDfyTestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc", CallerName = "Tests" })
    { InnerHandler = new Stub() })
    { BaseAddress = new Uri("http://localhost/") };

    private class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
}

public class Story_fluent_and_but
{
    private const string Long = "a very long title that goes on past the point where a bar wraps its label at one hundred and ten characters or so, and a bit";

    public void Get(string path) => Http.Client.GetAsync(path).GetAwaiter().GetResult();

    [Fact]
    public void Plain_titles() =>
        this.Given(_ => Get("given"), "a user")
            .And(_ => Get("and"), "a second one")
            .But(_ => Get("but"), "not a third")
            .When(_ => Get("when"), "they order")
            .And(_ => Get("when-and"), "they pay")
            .Then(_ => Get("then"), "it ships")
            .And(_ => Get("then-and"), "it arrives")
            .BDDfy();

    [Fact]
    public void Escaped_titles() =>
        this.Given(_ => Get("given"), @"a user at C:\temp")
            .And(_ => Get("and"), @"a second one at C:\temp")
            .But(_ => Get("but"), @"not a third at C:\temp")
            .When(_ => Get("when"), @"they order at C:\temp")
            .And(_ => Get("when-and"), @"they pay at C:\temp")
            .Then(_ => Get("then"), @"it ships at C:\temp")
            .And(_ => Get("then-and"), @"it arrives at C:\temp")
            .BDDfy();

    [Fact]
    public void Long_titles() =>
        this.Given(_ => Get("given"), "given " + Long)
            .And(_ => Get("and"), "and " + Long)
            .But(_ => Get("but"), "but " + Long)
            .When(_ => Get("when"), "when " + Long)
            .And(_ => Get("when-and"), "when and " + Long)
            .Then(_ => Get("then"), "then " + Long)
            .BDDfy();

    [Fact]
    public void No_titles_with_a_path() =>
        this.Given(_ => Get("given", @"C:\temp"))
            .And(_ => Get("and", @"C:\temp"))
            .But(_ => Get("but", @"C:\temp"))
            .When(_ => Get("when", @"C:\temp"))
            .And(_ => Get("when-and", @"C:\temp"))
            .Then(_ => Get("then", @"C:\temp"))
            .And(_ => Get("then-and", @"C:\temp"))
            .BDDfy();

    public void Get(string path, string where) => Get(path);
}
