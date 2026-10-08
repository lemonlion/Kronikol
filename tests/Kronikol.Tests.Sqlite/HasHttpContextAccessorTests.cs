using Kronikol.Extensions.Sqlite;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kronikol.Tests.Sqlite;

/// <summary>
/// The component reports the accessor it holds, so the diagnostic page's HttpContextAccessor column counts it
/// (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md section 4.4, T29). Until this release it took one and reported none.
/// </summary>
public class HasHttpContextAccessorTests
{
    [Fact]
    public void HasHttpContextAccessor_reports_the_accessor_from_the_constructor_or_the_options()
    {
        IHttpContextAccessor accessor = new StubAccessor();

        Assert.True(((ITrackingComponent)new TrackingSqliteConnection(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), new SqliteTrackingOptions(), accessor)).HasHttpContextAccessor);
        Assert.True(((ITrackingComponent)new TrackingSqliteConnection(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), new SqliteTrackingOptions { HttpContextAccessor = new StubAccessor() })).HasHttpContextAccessor);
        Assert.False(((ITrackingComponent)new TrackingSqliteConnection(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), new SqliteTrackingOptions())).HasHttpContextAccessor);
    }

    private sealed class StubAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
