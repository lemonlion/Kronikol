using System.Runtime.CompilerServices;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// The contract every tracker keeps for a call that throws (plans/FAILED_CALLS_PLAN.md §5.1, §5.7): one request and exactly one
/// answer, the answer saying the call failed and why, and the exception left as it was thrown. Each tracker's test project holds
/// a fact with <c>[Trait(FailedCallContract.Trait, FailedCallContract.Value)]</c> that drives the shipped tracker over an inner
/// client that throws and asserts it here; <c>FailedCallGuardTests</c> fails the build for a tracker whose project holds none.
/// Linked into every tracker's test project.
/// </summary>
internal static class FailedCallContract
{
    public const string Trait = "Contract";
    public const string Value = "FailedCall";

    /// <summary>
    /// The call's one request and its one answer, answered as <see cref="FailedSend"/> names the exception (C3), unless the
    /// tracker's service gives a failure status of its own, which <paramref name="status"/> names.
    /// </summary>
    public static (RequestResponseLog Request, RequestResponseLog Answer) AssertAnswered(
        IReadOnlyCollection<RequestResponseLog> logs, Exception thrown, string? status = null)
    {
        var calls = logs.Where(l => !l.IsDiagramMarker).ToArray();
        var request = Xunit.Assert.Single(calls, l => l.Type == RequestResponseType.Request);
        var answer = Xunit.Assert.Single(calls, l => l.Type == RequestResponseType.Response);
        Xunit.Assert.Equal(request.RequestResponseId, answer.RequestResponseId);
        Xunit.Assert.Equal(request.TraceId, answer.TraceId);
        Xunit.Assert.Equal(request.TestId, answer.TestId);
        Xunit.Assert.Equal(status ?? FailedSend.Status(thrown), answer.StatusCode?.Value?.ToString());
        Xunit.Assert.Equal(FailedSend.Describe(thrown), answer.Error);
        return (request, answer);
    }

    /// <summary>
    /// The call's one request and its one answer, for a tracker that reads a failure no exception of its own reaches (a span
    /// whose status is ERROR): failed by <paramref name="status"/>, an <see cref="System.Net.HttpStatusCode"/> or a word, with
    /// <paramref name="error"/> as its reason (C2).
    /// </summary>
    public static (RequestResponseLog Request, RequestResponseLog Answer) AssertFailed(
        IReadOnlyCollection<RequestResponseLog> logs, object status, string error)
    {
        var calls = logs.Where(l => !l.IsDiagramMarker).ToArray();
        var request = Xunit.Assert.Single(calls, l => l.Type == RequestResponseType.Request);
        var answer = Xunit.Assert.Single(calls, l => l.Type == RequestResponseType.Response);
        Xunit.Assert.Equal(request.RequestResponseId, answer.RequestResponseId);
        Xunit.Assert.Equal(request.TestId, answer.TestId);
        Xunit.Assert.Equal(status, answer.StatusCode?.Value);
        Xunit.Assert.Equal(error, answer.Error);
        return (request, answer);
    }

    /// <summary>C4: what reached the caller is the object the inner client threw, its stack still naming the frame that threw it.</summary>
    public static void AssertUntouched(Exception caught, Exception thrown)
    {
        Xunit.Assert.Same(thrown, caught);
        Xunit.Assert.Contains(nameof(Throw), caught.StackTrace ?? "", StringComparison.Ordinal);
    }

    /// <summary>Throws <paramref name="exception"/> from a frame of its own, so a caught exception's stack can be read for it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Throw(Exception exception) => throw exception;

    /// <summary>An inner handler whose send throws <paramref name="exception"/>, as a refused connection or a broken body does.</summary>
    public sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        public int Sends { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            Throw(exception);
            return null!;
        }
    }

    /// <summary>
    /// A body that breaks off as it is read. An <see cref="IOException"/> would come back wrapped by <see cref="HttpContent"/>,
    /// so the fact throws one it hands on as it is.
    /// </summary>
    public sealed class ThrowingContent(Exception exception) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            Throw(exception);
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>A response whose body breaks off as it is read.</summary>
    public static HttpResponseMessage BrokenBody(Exception exception) =>
        new(System.Net.HttpStatusCode.OK) { Content = new ThrowingContent(exception) };

    /// <summary>An inner handler that answers every send with <paramref name="response"/>.</summary>
    public sealed class AnsweringHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response());
    }

    /// <summary>A refused connection, as the socket transport reports one: the outer message and a socket error inside it.</summary>
    public static HttpRequestException Refused() =>
        new("Connection refused (localhost:1)", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
}
