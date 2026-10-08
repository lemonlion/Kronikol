using System.Net;
using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;

namespace Kronikol.Tests.MongoDB;

/// <summary>
/// An accessor holding one request that carries the two test-tracking headers. Each instance keeps its own request:
/// <see cref="HttpContextAccessor"/> keeps one per async flow, so two of those in one test would share it.
/// </summary>
internal sealed class HeaderAccessor(HttpContext? httpContext) : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; } = httpContext;

    public static HeaderAccessor For(string testName, string testId)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = testName;
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = testId;
        return new HeaderAccessor(context);
    }

    /// <summary>A find on <c>testdb.orders</c>, as the driver raises it before sending the command.</summary>
    public static CommandStartedEvent FindStarted() =>
        new("find", new BsonDocument("find", "orders"), new DatabaseNamespace("testdb"), 1L, 1,
            new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))));
}
