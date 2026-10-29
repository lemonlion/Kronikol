using System.Data;
using System.Globalization;
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
        try
        {
            CommandStarted(commandText, dataSource, database, executionId, parameters);
        }
        catch (Exception ex)
        {
            // A command whose request could not be logged is dropped whole: its end finds nothing pending.
            _pendingCommands.TryRemove(executionId, out _);
            NotRecorded(ex);
        }
    }

    private void CommandStarted(string? commandText, string? dataSource, string? database, Guid executionId, string? parameters)
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
            Phase = TestPhaseContext.Current,
            RecordedBefore8 = Before8(commandText, CommandType.Text, dataSource, database, effectiveVerbosity),
            RecordedBefore10 = Before10(method, uri, dataSource, database, op, effectiveVerbosity)
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => BuildRequestVariant(commandText, dataSource, database, BuildRequestContent(commandText, parameters, v), op, v));

        RequestResponseLogger.Log(log);
    }

    /// <summary>
    /// Call when a SQL command completes (success or failure).
    /// </summary>
    protected void LogCommandEnd(Guid executionId, int? rowsAffected = null, Exception? exception = null)
    {
        try
        {
            CommandEnded(executionId, rowsAffected, exception);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    private void CommandEnded(Guid executionId, int? rowsAffected, Exception? exception)
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
                    ? $"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected"
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
                            ? $"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected"
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
        => LogRequestCore(commandText, dataSource, database, commandType,
            v => BuildRequestContent(commandText, parameters, v), timestamp: null);

    /// <summary>
    /// Logs the request of a call whose body is known only once the call has returned, such as the rows a bulk insert
    /// sent, which the driver reads when it sends them. <paramref name="body"/> builds the request's content at Raw and
    /// Detailed verbosity (a SQL request has none at Summarised), and <paramref name="startedAt"/> is when the call
    /// began, so the record keeps its place in time although it is logged after the call.
    /// </summary>
    internal (Guid TraceId, Guid RequestResponseId)? LogRequestAfterCall(string? commandText, string? dataSource, string? database,
        Func<string?> body, DateTimeOffset startedAt)
    {
        // The body is built only for a record that is made, and once for the record and its variants.
        var built = new Lazy<string?>(body);
        return LogRequestCore(commandText, dataSource, database, System.Data.CommandType.Text,
            v => v == SqlTrackingVerbosityLevel.Summarised ? null : built.Value, startedAt);
    }

    private (Guid TraceId, Guid RequestResponseId)? LogRequestCore(string? commandText, string? dataSource, string? database,
        System.Data.CommandType commandType, Func<SqlTrackingVerbosityLevel, string?> contentFor, DateTimeOffset? timestamp)
    {
        try
        {
            return RequestLogged(commandText, dataSource, database, commandType, contentFor, timestamp);
        }
        catch (Exception ex)
        {
            // No ids, so the wrapper logs no response for a request that is not there.
            NotRecorded(ex);
            return null;
        }
    }

    private (Guid TraceId, Guid RequestResponseId)? RequestLogged(string? commandText, string? dataSource, string? database,
        System.Data.CommandType commandType, Func<SqlTrackingVerbosityLevel, string?> contentFor, DateTimeOffset? timestamp)
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
        var content = contentFor(effectiveVerbosity);

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
            Phase = TestPhaseContext.Current,
            Timestamp = timestamp,
            RecordedBefore8 = Before8(commandText, commandType, dataSource, database, effectiveVerbosity),
            RecordedBefore10 = Before10(method, uri, dataSource, database, op, effectiveVerbosity)
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => BuildRequestVariant(commandText, dataSource, database, contentFor(v), op, v));

        RequestResponseLogger.Log(log);

        return (traceId, requestResponseId);
    }

    protected internal void LogResponse(Guid traceId, Guid requestResponseId, int? rowsAffected = null, Exception? exception = null)
    {
        try
        {
            ResponseLogged(traceId, requestResponseId, rowsAffected, exception);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    private void ResponseLogged(Guid traceId, Guid requestResponseId, int? rowsAffected, Exception? exception)
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
                    ? $"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected"
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
                            ? $"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected"
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
        try
        {
            ResponseLoggedWithContent(traceId, requestResponseId, _ => content, exception);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    /// <summary>
    /// Logs the rows a query read, such as a <see cref="TrackingDbDataReader"/>'s, kept at
    /// <see cref="SqlResponseDetailResolver.ResolveKept(SqlTrackingOptionsBase)"/>: the call's note and each phase's
    /// are made at the detail their own verbosity resolves to.
    /// </summary>
    internal void LogRowsResponse(Guid traceId, Guid requestResponseId, Func<SqlResponseDetail, string?> rowsAt)
    {
        try
        {
            ResponseLoggedWithContent(traceId, requestResponseId,
                verbosity => rowsAt(SqlResponseDetailResolver.Resolve(
                    _options.ResponseDetail, verbosity == SqlTrackingVerbosityLevel.Summarised)),
                exception: null);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    private void ResponseLoggedWithContent(Guid traceId, Guid requestResponseId,
        Func<SqlTrackingVerbosityLevel, string?> contentAt, Exception? exception)
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
                : contentAt(effectiveVerbosity);

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
                        : contentAt(v);
                return new PhaseVariant("", new Uri($"{_options.UriScheme}:///"), vContent, [], false);
            });

        RequestResponseLogger.Log(log);
    }

    private Uri BuildUri(string? dataSource, string? database, UnifiedSqlOperationInfo op, SqlTrackingVerbosityLevel verbosity)
    {
        // SqlEndpoint reads a host and a database out of any data source: "host,1433", "(localdb)\MSSQLLocalDB",
        // "tcp:host", a host list, and Oracle's EZConnect strings and connect descriptors, whose database ODP.NET does
        // not report (plans/CLIENT_FLOORS_PLAN.md F26).
        var (host, named) = SqlEndpoint.Address(dataSource, database);
        return UriOf(host, SqlEndpoint.Segment(named), op, verbosity);
    }

    /// <summary>
    /// The address as it was written before F26: the data source read as a host alone and the database as the
    /// connection reports it, so an Oracle call named <c>localhost</c> and <c>unknown</c>. What history's earlier rules
    /// recorded, so their bridges are made from it.
    /// </summary>
    private Uri BuildUriBeforeAddress(string? dataSource, string? database, UnifiedSqlOperationInfo op, SqlTrackingVerbosityLevel verbosity)
        => UriOf(SqlEndpoint.Host(dataSource), SqlEndpoint.Segment(database), op, verbosity);

    private Uri UriOf(string host, string db, UnifiedSqlOperationInfo op, SqlTrackingVerbosityLevel verbosity)
    {
        var scheme = _options.UriScheme;

        return SqlEndpoint.Build(verbosity switch
        {
            SqlTrackingVerbosityLevel.Raw => $"{scheme}://{host}/{db}",
            SqlTrackingVerbosityLevel.Summarised => op.TableName is not null
                ? $"{scheme}:///{db}/{op.TableName}"
                : $"{scheme}:///{db}",
            _ => op.TableName is not null
                ? $"{scheme}://{host}/{db}/{op.TableName}"
                : $"{scheme}://{host}/{db}"
        }, scheme);
    }

    private void NotRecorded(Exception ex) => SqlCaptureFailure.Record(ComponentName, ex);

    /// <summary>
    /// The call as history's rules 6 to 9 recorded it, when that differs (<see cref="RequestResponseLog.RecordedBefore10"/>):
    /// the data source read as a host alone and the database as the connection reports it, so an Oracle call drawn
    /// against <c>localhost</c> and <c>unknown</c> (plans/CLIENT_FLOORS_PLAN.md F26).
    /// </summary>
    private RecordedBefore? Before10(OneOf<HttpMethod, string> method, Uri uri, string? dataSource, string? database,
        UnifiedSqlOperationInfo op, SqlTrackingVerbosityLevel verbosity)
    {
        var before = BuildUriBeforeAddress(dataSource, database, op, verbosity);
        return before == uri ? null : new RecordedBefore(method, before);
    }

    /// <summary>
    /// The call as 5.16.1 recorded it, when that differs, for history's rules 6 and 7 (<see cref="RequestResponseLog.RecordedBefore8"/>):
    /// a batch opening with two SET statements was classified by neither, so it was <c>Other</c> with no table, and was
    /// not recorded at Summarised or where <c>Other</c> is excluded.
    /// </summary>
    private RecordedBefore? Before8(string? commandText, CommandType commandType, string? dataSource, string? database, SqlTrackingVerbosityLevel verbosity)
    {
        if (UnifiedSqlClassifier.ClassifiedBefore8(commandText, commandType) is not { } before)
            return null;
        if ((verbosity == SqlTrackingVerbosityLevel.Summarised && before.Info.Operation == UnifiedSqlOperation.Other)
            || _options.ExcludedOperations.Contains(before.Info.Operation))
            return RecordedBefore.NotRecorded;
        OneOf<HttpMethod, string> method = verbosity == SqlTrackingVerbosityLevel.Raw
            ? before.RawKeyword ?? "SQL"
            : UnifiedSqlClassifier.GetDiagramLabel(before.Info, verbosity);
        return new RecordedBefore(method, BuildUriBeforeAddress(dataSource, database, before.Info, verbosity));
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
        string? content, UnifiedSqlOperationInfo op, SqlTrackingVerbosityLevel verbosity)
    {
        var skip = verbosity == SqlTrackingVerbosityLevel.Summarised && op.Operation == UnifiedSqlOperation.Other;
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, verbosity);
        OneOf<HttpMethod, string> method = verbosity == SqlTrackingVerbosityLevel.Raw
            ? UnifiedSqlClassifier.GetRawKeyword(commandText) ?? "SQL"
            : label;
        var uri = BuildUri(dataSource, database, op, verbosity);

        return new PhaseVariant(method, uri, content, [], skip);
    }

    protected SqlTrackingOptionsBase Options => _options;
}
