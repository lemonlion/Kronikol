using System.Text;

namespace Kronikol.Tracking;

/// <summary>
/// What a call that threw leaves behind. A send that throws (a refused connection, a broken response body,
/// a client timeout) used to leave the request alone: the fingerprint read "-" for its status, the diagram
/// drew no return, and nothing said why. That is a capture gap whenever the caller's own retry hides the
/// failure. Now the response half is logged with the exception's type where the status would be, behind a
/// bang so a reader tells <c>!HttpRequestException</c> from a status word like <c>Ack</c>, and the message
/// chain in <see cref="RequestResponseLog.Error"/>; the exception itself goes on its way untouched.
/// </summary>
public static class FailedSend
{
    /// <summary>The status a thrown exception stands for: <c>!HttpRequestException</c>.</summary>
    public static string Status(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return "!" + exception.GetType().Name;
    }

    /// <summary>The message chain, outermost first, each inner one after "Caused by:".</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = new StringBuilder(exception.Message);
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            text.Append(" Caused by: ").Append(inner.Message);
        return text.ToString();
    }
}
