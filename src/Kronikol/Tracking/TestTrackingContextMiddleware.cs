using Microsoft.AspNetCore.Http;
using Kronikol.Constants;

namespace Kronikol.Tracking;

/// <summary>
/// ASP.NET Core middleware that reads test-tracking headers from incoming requests and
/// sets <see cref="TestIdentityScope.Current"/> for the duration of the request.
/// This ensures test identity flows into background tasks spawned by <c>Task.Run</c>
/// or <c>ThreadPool.QueueUserWorkItem</c> via <see cref="AsyncLocal{T}"/>.
/// <para>
/// Registered by <c>services.AddTestTrackingContextPropagation()</c>, which adds it ahead of the host's own
/// middleware through <see cref="TestTrackingContextStartupFilter"/>, or by hand with
/// <c>app.UseMiddleware&lt;TestTrackingContextMiddleware&gt;()</c>. Header values in the <c>UTF-8''</c> form that
/// Kronikol's writers use for a value that is not plain ASCII are decoded.
/// </para>
/// </summary>
public class TestTrackingContextMiddleware
{
    private readonly RequestDelegate _next;

    public TestTrackingContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestNameHeader, out var name) &&
            context.Request.Headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestIdHeader, out var id) &&
            name.Count > 0 && id.Count > 0)
        {
            using (TestIdentityScope.Begin(TrackingHeaderValue.Decode(name[0]!), TrackingHeaderValue.Decode(id[0]!)))
            {
                await _next(context);
            }
        }
        else
        {
            await _next(context);
        }
    }
}
