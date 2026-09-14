using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.BigQuery;

/// <summary>
/// Central logging component for BigQuery operations. Implements <see cref="ITrackingComponent" /> with auto-registration.
/// </summary>
public class BigQueryTracker : ITrackingComponent
{
    private readonly BigQueryTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public BigQueryTracker(BigQueryTrackingMessageHandlerOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"BigQueryTracker ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    public (Guid RequestResponseId, Guid TraceId) LogRequest(
        BigQueryOperationInfo operation, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return (Guid.Empty, Guid.Empty);

        Interlocked.Increment(ref _invocationCount);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return (Guid.Empty, Guid.Empty);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var label = BigQueryOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity)
                    ?? operation.Operation.ToString();
        var uri = BuildUri(operation, effectiveVerbosity);
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var logContent = effectiveVerbosity == BigQueryTrackingVerbosity.Summarised ? null : content;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, logContent, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.BigQuery)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                BigQueryOperationClassifier.GetDiagramLabel(operation, v) ?? operation.Operation.ToString(),
                BuildUri(operation, v),
                v == BigQueryTrackingVerbosity.Summarised ? null : content,
                [], false)));

        return (requestResponseId, traceId);
    }

    public void LogResponse(
        BigQueryOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var label = BigQueryOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity)
                    ?? operation.Operation.ToString();
        var uri = BuildUri(operation, effectiveVerbosity);

        var logContent = effectiveVerbosity == BigQueryTrackingVerbosity.Summarised ? null : content;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, logContent, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            DependencyCategory: DependencyCategories.BigQuery)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                BigQueryOperationClassifier.GetDiagramLabel(operation, v) ?? operation.Operation.ToString(),
                BuildUri(operation, v),
                v == BigQueryTrackingVerbosity.Summarised ? null : content,
                [], false)));
    }

    private static Uri BuildUri(BigQueryOperationInfo op, BigQueryTrackingVerbosity effectiveVerbosity)
    {
        var resource = op.ResourceType ?? "unknown";

        return effectiveVerbosity switch
        {
            BigQueryTrackingVerbosity.Raw when op.ProjectId is not null =>
                new Uri($"bigquery:///{op.ProjectId}" +
                        (op.DatasetId is not null ? $"/{op.DatasetId}" : "") +
                        (op.ResourceName is not null ? $"/{op.ResourceName}" : "")),
            BigQueryTrackingVerbosity.Detailed =>
                new Uri($"bigquery:///{resource}" +
                        (op.ResourceName is not null ? $"/{op.ResourceName}" : "")),
            _ =>
                new Uri($"bigquery:///{resource}")
        };
    }
}
