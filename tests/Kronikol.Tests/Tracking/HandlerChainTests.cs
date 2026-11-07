using Kronikol.Tracking;
using static Kronikol.Tests.Tracking.ForwardingShapes;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// #219: the walk that tells the tracking handler where a call goes (plans/WHAT_ANSWERS_PLAN.md 5.1) reads the handler an
/// invoker sends through from a field no member exposes.
/// </summary>
public class HandlerChainTests
{
    private sealed class Canned : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage());
    }

    // Fails on the first runtime that renames HttpMessageInvoker's _handler (the plan's A1): the walk then reads every
    // forwarder as an end of unknown kind, as it reads one before #219.
    [Fact]
    public void The_handler_an_invoker_sends_through_is_read_where_the_walk_reads_it()
    {
        var handler = new Canned();
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var client = new HttpClient(handler, disposeHandler: false);

        Assert.Same(handler, HandlerChain.HandlerOf(invoker));
        Assert.Same(handler, HandlerChain.HandlerOf(client));
    }

    [Fact]
    public void An_invoker_whose_handler_cannot_be_read_is_an_end_of_unknown_kind()
    {
        var chain = HandlerChain.Of(new Onward(new Canned()), _ => null);

        Assert.Null(chain.AnsweredBy);
        Assert.False(chain.TransportWritesTraceContext);
        Assert.Equal(nameof(Canned), HandlerChain.Of(new Onward(new Canned())).AnsweredBy);
    }
}
