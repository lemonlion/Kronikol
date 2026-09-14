using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Sql;

/// <summary>
/// Shared base class for DiagnosticSource-based SQL database tracking extensions.
/// Handles command correlation, test info resolution, phase-aware tracking,
/// variant building, and request/response logging.
/// </summary>
public abstract class SqlDiagnosticTracker : ITrackingComponent
{
    private readonly SqlTrackingOptionsBase _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    private readonly ConcurrentDictionary<Guid, (Guid TraceId, Guid RequestResponseId, DateTimeOffset StartTime)> _pendingCommands = new();

    protected SqlDiagnosticTracker(SqlTrackingOptionsBase options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public abstract string ComponentName { get; }
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    /// <summary>
    /// Call when a SQL command begins execution. Returns correlation IDs if tracking is active.
    /// </summary>
    /// <param name="commandText">The SQL text being executed.</param>
    /// <param name="dataSource">The connection's DataSource (host/server).</param>
    /// <param name="database">The database name.</param>
    /// <param name="executionId">A unique identifier for this execution (from DiagnosticSource event payload).</param>
    /// <param name="parameters">Optional parameter string for Raw verbosity.</param>
    protected void LogCommandStart(string? commandText, string? dataSource, string? database, Guid executionId, string? parameters = null)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = UnifiedSqlClassifier.Classify(commandText);

        if (effectiveVerbosity == SqlTrackingVerbosityLevel.Summarised && op.Operation == UnifiedSqlOperation.Other)
            return;

        if (_options.ExcludedOperations.Contains(op.Operation))
            return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return;

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();
        _pendingCommands[executionId] = (traceId, requestResponseId, DateTimeOffset.UtcNow);

        var label = UnifiedSqlClassifier.GetDiagramLabel(op, effectiveVerbosity);
        OneOf<HttpMethod, string> method = effectiveVerbosity == SqlTrackingVerbosityLevel.Raw
            ? UnifiedSqlClassifier.GetRawKeyword(commandText) ?? "SQL"
            : label;

        var uri = BuildUri(dataSource, database, op, effectiveVerbosity);
        var content = BuildRequestContent(commandText, parameters, effectiveVerbosity);

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            content,
            uri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Request,
            traceId,
            requestResponseId,
            false,
            DependencyCategory: _options.DependencyCategory)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => BuildRequestVariant(commandText, dataSource, database, parameters, op, v));

        RequestResponseLogger.Log(log);
    }

    /// <summary>
    /// Call when a SQL command completes (success or failure).
    /// </summary>
    protected void LogCommandEnd(Guid executionId, int? rowsAffected = null, Exception? exception = null)
    {
        if (!_pendingCommands.TryRemove(executionId, out var ids))
            return;

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return;

        var responseContent = effectiveVerbosity == SqlTrackingVerbosityLevel.Summarised && !_options.LogResponseContent
            ? null
            : exception is not null
                ? exception.Message
                : rowsAffected.HasValue
                    ? $"{rowsAffected.Value} rows affected"
                    : null;

        OneOf<HttpStatusCode, string> status = exception is not null ? "Error" : "OK";

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            (OneOf<HttpMethod, string>)"",
            responseContent,
            new Uri($"{_options.UriScheme}:///"),
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            ids.TraceId,
            ids.RequestResponseId,
            false,
            status,
            DependencyCategory: _options.DependencyCategory)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vContent = v == SqlTrackingVerbosityLevel.Summarised && !_options.LogResponseContent
                    ? null
                    : exception is not null
                        ? exception.Message
                        : rowsAffected.HasValue
                            ? $"{rowsAffected.Value} rows affected"
                            : null;
                return new PhaseVariant("", new Uri($"{_options.UriScheme}:///"), vContent, [], false);
            });

        RequestResponseLogger.Log(log);
    }

    /// <summary>
    /// Simplified overload for DbConnection-wrapping extensions that don't use DiagnosticSource.
    /// Logs a complete request/response pair for a SQL command execution.
    /// </summary>
    protected internal (Guid TraceId, Guid RequestResponseId)? LogRequest(string? commandText, string? dataSource, string? database,
        System.Data.CommandType commandType = System.Data.CommandType.Text, string? parameters = null)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return null;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = UnifiedSqlClassifier.Classify(commandText, commandType);

        if (effectiveVerbosity == SqlTrackingVerbosityLevel.Summarised && op.Operation == UnifiedSqlOperation.Other)
            return null;

        if (_options.ExcludedOperations.Contains(op.Operation))
            return null;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return null;

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var label = UnifiedSqlClassifier.GetDiagramLabel(op, effectiveVerbosity);
        OneOf<HttpMethod, string> method = effectiveVerbosity == SqlTrackingVerbosityLevel.Raw
            ? UnifiedSqlClassifier.GetRawKeyword(commandText) ?? "SQL"
            : label;

        var uri = BuildUri(dataSource, database, op, effectiveVerbosity);
        var content = BuildRequestContent(commandText, parameters, effectiveVerbosity);

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            content,
            uri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Request,
            traceId,
            requestResponseId,
            false,
            DependencyCategory: _options.DependencyCategory)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => BuildRequestVariant(commandText, dataSource, database, parameters, op, v));

        RequestResponseLogger.Log(log);

        return (traceId, requestResponseId);
    }

    protected internal void LogResponse(Guid traceId, Guid requestResponseId, int? rowsAffected = null, Exception? exception = null)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return;

        var responseContent = effectiveVerbosity == SqlTrackingVerbosityLevel.Summarised && !_options.LogResponseContent
            ? null
            : exception is not null
                ? exception.Message
                : rowsAffected.HasValue
                    ? $"{rowsAffected.Value} rows affected"
                    : null;

        OneOf<HttpStatusCode, string> status = exception is not null ? "Error" : "OK";

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            (OneOf<HttpMethod, string>)"",
            responseContent,
            new Uri($"{_options.UriScheme}:///"),
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            traceId,
            requestResponseId,
            false,
            status,
            DependencyCategory: _options.DependencyCategory)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vContent = v == SqlTrackingVerbosityLevel.Summarised && !_options.LogResponseContent
                    ? null
                    : exception is not null
                        ? exception.Message
                        : rowsAffected.HasValue
                            ? $"{rowsAffected.Value} rows affected"
                            : null;
                return new PhaseVariant("", new Uri($"{_options.UriScheme}:///"), vContent, [], false);
            });

        RequestResponseLogger.Log(log);
    }

    /// <summary>
    /// Overload for DbConnection-wrapping extensions that provide pre-formatted response content
    /// (e.g. from a <see cref="TrackingDbDataReader"/> or scalar value).
    /// </summary>
    protected internal void LogResponse(Guid traceId, Guid requestResponseId, string? content, Exception? exception = null)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return;

        var responseContent = effectiveVerbosity == SqlTrackingVerbosityLevel.Summarised && !_options.LogResponseContent
            ? null
            : exception is not null
                ? exception.Message
                : content;

        OneOf<HttpStatusCode, string> status = exception is not null ? "Error" : "OK";

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            (OneOf<HttpMethod, string>)"",
            responseContent,
            new Uri($"{_options.UriScheme}:///"),
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            traceId,
            requestResponseId,
            false,
            status,
            DependencyCategory: _options.DependencyCategory)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vContent = v == SqlTrackingVerbosityLevel.Summarised && !_options.LogResponseContent
                    ? null
                    : exception is not null
                        ? exception.Message
                        : content;
                return new PhaseVariant("", new Uri($"{_options.UriScheme}:///"), vContent, [], false);
            });

        RequestResponseLogger.Log(log);
    }

    private Uri BuildUri(string? dataSource, string? database, UnifiedSqlOperationInfo op, SqlTrackingVerbosityLevel verbosity)
    {
        if (string.IsNullOrEmpty(database)) database = "unknown";
        if (string.IsNullOrEmpty(dataSource)) dataSource = "localhost";
        // SQL Server uses comma notation for ports — Uri requires colon
        dataSource = dataSource.Replace(',', ':');

        var scheme = _options.UriScheme;

        return verbosity switch
        {
            SqlTrackingVerbosityLevel.Raw => new Uri($"{scheme}://{dataSource}/{database}"),
            SqlTrackingVerbosityLevel.Summarised => op.TableName is not null
                ? new Uri($"{scheme}:///{database}/{op.TableName}")
                : new Uri($"{scheme}:///{database}"),
            _ => op.TableName is not null
                ? new Uri($"{scheme}://{dataSource}/{database}/{op.TableName}")
                : new Uri($"{scheme}://{dataSource}/{database}")
        };
    }

    private string? BuildRequestContent(string? commandText, string? parameters, SqlTrackingVerbosityLevel verbosity)
    {
        if (verbosity == SqlTrackingVerbosityLevel.Summarised)
            return null;

        if (verbosity == SqlTrackingVerbosityLevel.Raw && parameters is not null)
            return $"{commandText}\n-- Parameters: {parameters}";

        return _options.LogSqlText ? commandText : null;
    }

    private PhaseVariant BuildRequestVariant(string? commandText, string? dataSource, string? database,
        string? parameters, UnifiedSqlOperationInfo op, SqlTrackingVerbosityLevel verbosity)
    {
        var skip = verbosity == SqlTrackingVerbosityLevel.Summarised && op.Operation == UnifiedSqlOperation.Other;
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, verbosity);
        OneOf<HttpMethod, string> method = verbosity == SqlTrackingVerbosityLevel.Raw
            ? UnifiedSqlClassifier.GetRawKeyword(commandText) ?? "SQL"
            : label;
        var uri = BuildUri(dataSource, database, op, verbosity);
        var content = BuildRequestContent(commandText, parameters, verbosity);

        return new PhaseVariant(method, uri, content, [], skip);
    }

    protected SqlTrackingOptionsBase Options => _options;
}
