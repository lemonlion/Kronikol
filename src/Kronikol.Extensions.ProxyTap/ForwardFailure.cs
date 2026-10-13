using Kronikol.Tracking;

namespace Kronikol.Extensions.ProxyTap;

/// <summary>Why a forward failed, as far as the tap can tell (plans/PROXY_TAP_FAILED_FORWARD_PLAN.md 4.2).</summary>
internal enum ForwardFailureKind
{
    /// <summary>The upstream refused, failed or cut its answer short: anything that is not one of the others.</summary>
    Failed,

    /// <summary>No connection within <see cref="ProxyTapOptions.ConnectTimeout"/>.</summary>
    ConnectTimedOut,

    /// <summary>No answer within <see cref="ProxyTapOptions.ForwardTimeout"/>.</summary>
    TimedOut,

    /// <summary>The tap was being disposed, so the upstream is not to blame.</summary>
    Stopping,
}

/// <summary>Sorting and wording for a forward that failed.</summary>
internal static class ForwardFailure
{
    /// <summary>
    /// Sorts a forward's exception by what ended it. The forward's token, <paramref name="deadline"/>, is linked to the
    /// tap's <paramref name="lifetime"/>, so the lifetime is asked first. A cancellation that neither token caused came
    /// from inside <see cref="HttpClient"/>: <see cref="ProxyTapOptions.ConnectTimeout"/> throws that way, with a
    /// <see cref="TimeoutException"/> inside. Only a cancellation is put down to a token: a refusal that lands as the
    /// deadline passes is still the upstream's refusal.
    /// </summary>
    public static ForwardFailureKind Classify(Exception exception, CancellationToken deadline, CancellationToken lifetime)
    {
        // DisposeAsync releases the HttpClient once its drain is over, so a forward still running then throws this.
        if (lifetime.IsCancellationRequested && exception is OperationCanceledException or ObjectDisposedException)
            return ForwardFailureKind.Stopping;
        if (exception is not OperationCanceledException)
            return ForwardFailureKind.Failed;
        if (deadline.IsCancellationRequested)
            return ForwardFailureKind.TimedOut;
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is TimeoutException)
                return ForwardFailureKind.ConnectTimedOut;
        return ForwardFailureKind.Failed;
    }

    /// <summary>
    /// What went wrong, in the tap's words where the exception's own say nothing useful (a timeout's is "The operation
    /// was canceled."), with the exception's message chain after them.
    /// </summary>
    public static string Reason(ForwardFailureKind kind, Exception exception, ProxyTapOptions options)
    {
        var chain = FailedSend.Describe(exception);
        return kind switch
        {
            ForwardFailureKind.ConnectTimedOut => $"could not connect to {options.ForwardBaseUri!.Authority} within ConnectTimeout ({options.ConnectTimeout}). Caused by: {chain}",
            ForwardFailureKind.TimedOut => $"no answer from {options.ForwardBaseUri!.Authority} within ForwardTimeout ({options.ForwardTimeout}). Caused by: {chain}",
            ForwardFailureKind.Stopping => $"the tap stopped before {options.ServiceName} answered. Caused by: {chain}",
            _ => chain,
        };
    }

    /// <summary>
    /// The <c>error.type</c> of a failed client span, in the words .NET's own <see cref="HttpClient"/> spans and metrics
    /// use (System.Net.Http's <c>DiagnosticsHelper.TryGetErrorType</c>), so the tap's span reads like the services' own:
    /// an <see cref="HttpRequestException"/>'s <see cref="HttpRequestError"/>, else the exception's full type name.
    /// </summary>
    public static string ErrorType(Exception exception) => (exception as HttpRequestException)?.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => "name_resolution_error",
        HttpRequestError.ConnectionError => "connection_error",
        HttpRequestError.SecureConnectionError => "secure_connection_error",
        HttpRequestError.HttpProtocolError => "http_protocol_error",
        HttpRequestError.ExtendedConnectNotSupported => "extended_connect_not_supported",
        HttpRequestError.VersionNegotiationError => "version_negotiation_error",
        HttpRequestError.UserAuthenticationError => "user_authentication_error",
        HttpRequestError.ProxyTunnelError => "proxy_tunnel_error",
        HttpRequestError.InvalidResponse => "invalid_response",
        HttpRequestError.ResponseEnded => "response_ended",
        HttpRequestError.ConfigurationLimitExceeded => "configuration_limit_exceeded",
        _ => exception.GetType().FullName!,
    };
}
