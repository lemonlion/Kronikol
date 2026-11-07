namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// Tests that make real <see cref="HttpClient"/> calls and check what the call sends or what the span listener keeps of
/// it, run here, after every parallel collection.
/// </summary>
/// <remarks>
/// Several test classes hear every source through a listener that samples every span. While one of them runs, every
/// <see cref="HttpClient"/> call in the process starts a span from <c>System.Net.Http</c> and sends a <c>traceparent</c>,
/// parent or not, so a call outside a trace would carry one for a reason that has nothing to do with Kronikol's listener.
/// <c>DisableParallelization</c> runs this collection after every parallel one, so no such listener is alive beside its
/// calls.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HttpClientSpansCollection
{
    public const string Name = "HttpClientSpans";
}
