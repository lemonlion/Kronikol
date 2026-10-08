using Microsoft.AspNetCore.Http;
using Kronikol.Constants;

namespace Kronikol.Tracking;

/// <summary>
/// Reads test tracking context from HTTP request headers, enabling code running on the
/// WebApplicationFactory server thread to obtain the test name and ID that was propagated
/// by <see cref="TestTrackingMessageHandler"/>.
/// </summary>
public static class TestTrackingServerBridge
{
    /// <summary>
    /// Reads test name and ID from the current HTTP request headers, decoded: Kronikol sends a value that is not
    /// plain printable ASCII (or that begins or ends with a space) in the RFC 8187 form <c>UTF-8''</c> followed by
    /// its percent-encoded UTF-8 bytes, and this returns the value as the test named it.
    /// Returns null if no test context headers are present (e.g. outside a request).
    /// </summary>
    public static (string Name, string Id)? GetCurrentTestInfo(IHttpContextAccessor httpContextAccessor)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null) return null;

        var headers = context.Request.Headers;
        if (!headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestNameHeader, out var nameValues) ||
            !headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestIdHeader, out var idValues))
            return null;

        var name = TrackingHeaderValue.Decode(nameValues.FirstOrDefault());
        var id = TrackingHeaderValue.Decode(idValues.FirstOrDefault());

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id))
            return null;

        return (name, id);
    }
}
