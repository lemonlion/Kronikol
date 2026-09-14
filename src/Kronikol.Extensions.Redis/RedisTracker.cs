using Kronikol.Constants;
using System.Net;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Redis;

/// <summary>
/// Central logging component for Redis operations. Implements <see cref="ITrackingComponent" /> with auto-registration.
/// </summary>
public class RedisTracker : ITrackingComponent
{
    private readonly RedisTrackingDatabaseOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly string _endpoint;
    private int _invocationCount;

    public RedisTracker(RedisTrackingDatabaseOptions options, string endpoint = "localhost", IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        _endpoint = endpoint;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"RedisTracker ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    public (Guid RequestResponseId, Guid TraceId) LogRedisRequest(string command, string? key, int db, string? content)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return (Guid.Empty, Guid.Empty);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = RedisOperationClassifier.Classify(command, hasResult: false, key, db);

        if (effectiveVerbosity == RedisTrackingVerbosity.Summarised && op.Operation == RedisOperation.Other)
            return (Guid.Empty, Guid.Empty);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return (Guid.Empty, Guid.Empty);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        // Request label never includes hit/miss — that's only known after the response
        var requestOp = op with { CacheResult = RedisCacheResult.None };
        var label = RedisOperationClassifier.GetDiagramLabel(requestOp, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == RedisTrackingVerbosity.Raw
            ? command.ToUpperInvariant()
            : label ?? op.Operation.ToString();

        var requestUri = BuildUri(key, db, effectiveVerbosity);
        var logContent = effectiveVerbosity == RedisTrackingVerbosity.Summarised ? null : content;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            logContent,
            requestUri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Request,
            traceId,
            requestResponseId,
            false,
            DependencyCategory: DependencyCategories.Redis
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vLabel = RedisOperationClassifier.GetDiagramLabel(requestOp, v);
                return new PhaseVariant(
                    v == RedisTrackingVerbosity.Raw ? command.ToUpperInvariant() : vLabel ?? op.Operation.ToString(),
                    BuildUri(key, db, v),
                    v == RedisTrackingVerbosity.Summarised ? null : content,
                    [],
                    v == RedisTrackingVerbosity.Summarised && op.Operation == RedisOperation.Other);
            }));

        return (requestResponseId, traceId);
    }

    public void LogRedisResponse(string command, string? key, int db, bool hasResult, Guid requestResponseId, Guid traceId, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = RedisOperationClassifier.Classify(command, hasResult, key, db);

        if (effectiveVerbosity == RedisTrackingVerbosity.Summarised && op.Operation == RedisOperation.Other)
            return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return;

        var label = RedisOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == RedisTrackingVerbosity.Raw
            ? command.ToUpperInvariant()
            : label ?? op.Operation.ToString();

        var requestUri = BuildUri(key, db, effectiveVerbosity);
        var logContent = effectiveVerbosity == RedisTrackingVerbosity.Summarised ? null : content;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            logContent,
            requestUri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            traceId,
            requestResponseId,
            false,
            (OneOf<HttpStatusCode, string>)"OK",
            DependencyCategory: DependencyCategories.Redis
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vLabel = RedisOperationClassifier.GetDiagramLabel(op, v);
                return new PhaseVariant(
                    v == RedisTrackingVerbosity.Raw ? command.ToUpperInvariant() : vLabel ?? op.Operation.ToString(),
                    BuildUri(key, db, v),
                    v == RedisTrackingVerbosity.Summarised ? null : content,
                    [],
                    v == RedisTrackingVerbosity.Summarised && op.Operation == RedisOperation.Other);
            }));
    }

    private Uri BuildUri(string? key, int db, RedisTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            RedisTrackingVerbosity.Raw => key is not null
                ? new Uri($"redis://{_endpoint}/{db}/{key}")
                : new Uri($"redis://{_endpoint}/{db}"),
            RedisTrackingVerbosity.Detailed => key is not null
                ? new Uri($"redis://db{db}/{key}")
                : new Uri($"redis://db{db}/"),
            _ => new Uri($"redis://db{db}/"), // Summarised
        };
    }
}
