using Kronikol.Extensions.MediatR;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Tests.MediatR;

/// <summary>
/// A request whose handler throws (plans/FAILED_CALLS_PLAN.md §5.1, Q2): it is answered with the exception's type and why, where it
/// was a 500 no service sent, and the sender gets the exception the handler threw, with its stack.
/// </summary>
public class MediatorFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private IMediator Mediator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PingHandler>());
        services.TrackMediatorForDiagrams(new MediatorTrackingOptions { CurrentTestInfoFetcher = () => ("Mediator Test", _testId) });
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_request_whose_handler_throws_is_answered_with_the_failure()
    {
        var failure = new InvalidOperationException("The kitchen is closed.");

        var caught = await Record.ExceptionAsync(() => Mediator().Send(new Refuse(failure), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught!, failure);
        FailedCallContract.AssertAnswered(Logs(), failure);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_request_whose_handler_throws_after_an_await_is_answered_with_the_failure()
    {
        var failure = new InvalidOperationException("The kitchen closed while it cooked.");

        var caught = await Record.ExceptionAsync(() => Mediator().Send(new RefuseLater(failure), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught!, failure);
        FailedCallContract.AssertAnswered(Logs(), failure);
    }
}

public record Refuse(Exception Failure) : IRequest<string>;

public class RefuseHandler : IRequestHandler<Refuse, string>
{
    public Task<string> Handle(Refuse request, CancellationToken cancellationToken)
    {
        FailedCallContract.Throw(request.Failure);
        return Task.FromResult("");
    }
}

public record RefuseLater(Exception Failure) : IRequest<string>;

public class RefuseLaterHandler : IRequestHandler<RefuseLater, string>
{
    public async Task<string> Handle(RefuseLater request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        FailedCallContract.Throw(request.Failure);
        return "";
    }
}
