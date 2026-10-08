using Kronikol.Extensions.MediatR;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Tests.MediatR;

public class MediatorTrackingExtensionsTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest() =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private static ServiceCollection ServicesWithMediator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PingHandler>());
        return services;
    }

    [Fact]
    public async Task A_sent_request_is_recorded_under_the_fetchers_test()
    {
        var services = ServicesWithMediator();
        services.TrackMediatorForDiagrams(new MediatorTrackingOptions { CurrentTestInfoFetcher = () => ("Mediator Test", _testId) });
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        Assert.Equal("pong a", await mediator.Send(new Ping("a"), TestContext.Current.CancellationToken));

        Assert.NotEmpty(GetLogsFromThisTest());
    }

    [Fact]
    public async Task TrackMediatorForDiagrams_prefers_the_options_accessor_to_the_containers()
    {
        var optionsTestId = Guid.NewGuid().ToString();
        var containerTestId = Guid.NewGuid().ToString();
        var services = ServicesWithMediator();
        services.AddSingleton<IHttpContextAccessor>(RequestHeaderAccessor.For("Container Test", containerTestId));
        services.TrackMediatorForDiagrams(new MediatorTrackingOptions
        {
            CurrentTestInfoFetcher = () => ("Mediator Test", _testId),
            HttpContextAccessor = RequestHeaderAccessor.For("Options Test", optionsTestId),
        });
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        await mediator.Send(new Ping("a"), TestContext.Current.CancellationToken);

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == optionsTestId && l.AttributionSource == AttributionSource.RequestHeader);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == containerTestId);
        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public async Task TrackMediatorForDiagrams_uses_the_containers_accessor_when_the_options_carry_none()
    {
        var containerTestId = Guid.NewGuid().ToString();
        var services = ServicesWithMediator();
        services.AddSingleton<IHttpContextAccessor>(RequestHeaderAccessor.For("Container Test", containerTestId));
        services.TrackMediatorForDiagrams(new MediatorTrackingOptions { CurrentTestInfoFetcher = () => ("Mediator Test", _testId) });
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        await mediator.Send(new Ping("a"), TestContext.Current.CancellationToken);

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == containerTestId && l.AttributionSource == AttributionSource.RequestHeader);
        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void MediatorTrackingOptions_HttpContextAccessor_defaults_to_null()
    {
        Assert.Null(new MediatorTrackingOptions().HttpContextAccessor);
    }
}

public record Ping(string Text) : IRequest<string>;

public class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("pong " + request.Text);
}
