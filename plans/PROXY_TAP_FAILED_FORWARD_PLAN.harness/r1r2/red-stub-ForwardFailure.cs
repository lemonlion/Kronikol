namespace Kronikol.Extensions.ProxyTap;

// R1/R2 red-step stubs: the facts compile against 4.19.3 and fail on their own assertions.
internal enum ForwardFailureKind
{
    Failed,
    ConnectTimedOut,
    TimedOut,
    Stopping,
}

internal static class ForwardFailure
{
    public static ForwardFailureKind Classify(Exception exception, CancellationToken deadline, CancellationToken lifetime) => ForwardFailureKind.Failed;

    public static string Reason(ForwardFailureKind kind, Exception exception, ProxyTapOptions options) => "";

    public static string ErrorType(Exception exception) => "";
}
