using Kronikol.Tracking;

namespace Kronikol.Extensions.EfCore.Relational;

/// <summary>
/// Provides extension methods for configuring Entity Framework Core client options to enable test tracking.
/// </summary>
public static class SqlTrackingInterceptorOptionsExtensions
{
    /// <summary>
    /// Copies <see cref="TestTrackingMessageHandlerOptions.CurrentTestInfoFetcher"/>,
    /// <see cref="TestTrackingMessageHandlerOptions.CurrentStepTypeFetcher"/>, and
    /// <see cref="TestTrackingMessageHandlerOptions.CallerName"/> from an existing
    /// HTTP tracking options instance so that SQL and HTTP tracking share the same test context, and its
    /// <see cref="TestTrackingMessageHandlerOptions.HttpContextAccessor"/> when the SQL options carry none.
    /// </summary>
    public static SqlTrackingInterceptorOptions WithTestInfoFrom(
        this SqlTrackingInterceptorOptions sqlOptions,
        TestTrackingMessageHandlerOptions httpOptions)
    {
        sqlOptions.CurrentTestInfoFetcher = httpOptions.CurrentTestInfoFetcher;
        sqlOptions.CurrentStepTypeFetcher = httpOptions.CurrentStepTypeFetcher;
        sqlOptions.CallerName = httpOptions.CallerName;
        sqlOptions.HttpContextAccessor ??= httpOptions.HttpContextAccessor;
        return sqlOptions;
    }
}