using System.Globalization;
using Kronikol.Constants;
using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Kronikol.Sql;
using Kronikol.Tracking;

namespace Kronikol.Extensions.EfCore.Relational;

/// <summary>
/// An EF Core <see cref="DbCommandInterceptor"/> that logs SQL operations for inclusion in test diagrams.
/// </summary>
public class SqlTrackingInterceptor : DbCommandInterceptor, ITrackingComponent
{
    private readonly SqlTrackingInterceptorOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    // Maps each DbCommand instance to its correlated IDs so that
    // LogCommandExecuted can pair with the LogCommandExecuting entry.
    private readonly ConcurrentDictionary<DbCommand, (Guid TraceId, Guid RequestResponseId)> _pendingIds = new();

    // The commands whose reader went back to EF Core as the provider made it, logged when EF Core closes it; the value
    // says whether the command is a SaveChanges batch.
    private readonly ConcurrentDictionary<DbCommand, bool> _untouched = new();

    public SqlTrackingInterceptor(SqlTrackingInterceptorOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"SqlTrackingInterceptor ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    // ─── Public methods for direct testing ──────────────────────

    public void LogCommandExecuting(DbCommand command)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var sqlOp = SqlOperationClassifier.Classify(command.CommandText, command.CommandType);

        if (effectiveVerbosity == SqlTrackingVerbosity.Summarised && sqlOp.Operation == SqlOperation.Other)
            return;

        var testInfo = GetTestInfo();
        if (testInfo is null)
            return;

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        // Store the IDs so LogCommandExecuted can retrieve them
        _pendingIds[command] = (traceId, requestResponseId);

        var label = SqlOperationClassifier.GetDiagramLabel(sqlOp, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == SqlTrackingVerbosity.Raw
            ? SqlOperationClassifier.GetRawKeyword(command.CommandText) ?? "SQL"
            : label!;

        var requestUri = BuildUri(command, sqlOp, effectiveVerbosity);
        var content = effectiveVerbosity == SqlTrackingVerbosity.Summarised ? null : command.CommandText;

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            content,
            requestUri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Request,
            traceId,
            requestResponseId,
            false,
            DependencyCategory: DependencyCategories.SQL
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => BuildVariant(command, sqlOp, v));

        RequestResponseLogger.Log(log);
    }

    public void LogCommandExecuted(DbCommand command, int? rowsAffected = null)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var sqlOp = SqlOperationClassifier.Classify(command.CommandText, command.CommandType);

        if (effectiveVerbosity == SqlTrackingVerbosity.Summarised && sqlOp.Operation == SqlOperation.Other)
            return;

        var testInfo = GetTestInfo();
        if (testInfo is null)
            return;

        // Retrieve the IDs from the executing phase, or create new ones as fallback
        if (!_pendingIds.TryRemove(command, out var ids))
            ids = (Guid.NewGuid(), Guid.NewGuid());

        var label = SqlOperationClassifier.GetDiagramLabel(sqlOp, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == SqlTrackingVerbosity.Raw
            ? SqlOperationClassifier.GetRawKeyword(command.CommandText) ?? "SQL"
            : label!;

        var requestUri = BuildUri(command, sqlOp, effectiveVerbosity);
        var content = effectiveVerbosity == SqlTrackingVerbosity.Summarised && !_options.LogResponseContent
            ? null
            : rowsAffected.HasValue ? $"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected" : null;

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            content,
            requestUri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            ids.TraceId,
            ids.RequestResponseId,
            false,
            (OneOf<System.Net.HttpStatusCode, string>)"OK",
            DependencyCategory: DependencyCategories.SQL
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => BuildResponseVariant(command, sqlOp, v, rowsAffected));

        RequestResponseLogger.Log(log);
    }

    // A command that fails still gets its response, with the error: without one the diagram shows a call that
    // never answered, and the command stayed in _pendingIds for the life of the interceptor.
    private void LogCommandFailed(DbCommand command, Exception exception)
    {
        if (!_pendingIds.TryRemove(command, out var ids))
            return;

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var sqlOp = SqlOperationClassifier.Classify(command.CommandText, command.CommandType);

        var testInfo = GetTestInfo();
        if (testInfo is null)
            return;

        var label = SqlOperationClassifier.GetDiagramLabel(sqlOp, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == SqlTrackingVerbosity.Raw
            ? SqlOperationClassifier.GetRawKeyword(command.CommandText) ?? "SQL"
            : label!;

        var content = effectiveVerbosity == SqlTrackingVerbosity.Summarised && !_options.LogResponseContent
            ? null
            : exception.Message;

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            content,
            BuildUri(command, sqlOp, effectiveVerbosity),
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            ids.TraceId,
            ids.RequestResponseId,
            false,
            (OneOf<System.Net.HttpStatusCode, string>)"Error",
            DependencyCategory: DependencyCategories.SQL
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => BuildResponseVariantWithContent(command, sqlOp, v, exception.Message));

        RequestResponseLogger.Log(log);
    }

    // ─── EF Core DbCommandInterceptor overrides ────────────────
    //
    // Each hook runs inside EF Core's own call, so whatever recording a command fails on is caught: the command goes
    // on, and the run's diagnostics name what was not recorded (5.11.1).

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        Safe(() => LogCommandFailed(command, eventData.Exception));
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Safe(() => LogCommandFailed(command, eventData.Exception));
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Safe(() => LogCommandExecuting(command));
        return base.ReaderExecuting(command, eventData, result);
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        => base.ReaderExecuted(command, eventData, OnReaderExecuted(command, eventData, result));

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Safe(() => LogCommandExecuting(command));
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Safe(() => LogCommandExecuted(command, result));
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Safe(() => LogCommandExecuting(command));
        return base.ScalarExecuting(command, eventData, result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Safe(() => LogCommandExecutedWithContent(command, FormatScalar(result)));
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Safe(() => LogCommandExecuting(command));
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        => base.ReaderExecutedAsync(command, eventData, OnReaderExecuted(command, eventData, result), cancellationToken);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Safe(() => LogCommandExecuting(command));
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Safe(() => LogCommandExecuted(command, result));
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Safe(() => LogCommandExecuting(command));
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Safe(() => LogCommandExecutedWithContent(command, FormatScalar(result)));
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult DataReaderClosing(DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
    {
        LogUntouchedReader(command, eventData, closed: false);
        return base.DataReaderClosing(command, eventData, result);
    }

    public override ValueTask<InterceptionResult> DataReaderClosingAsync(DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
    {
        LogUntouchedReader(command, eventData, closed: false);
        return base.DataReaderClosingAsync(command, eventData, result);
    }

    public override InterceptionResult DataReaderDisposing(DbCommand command, DataReaderDisposingEventData eventData, InterceptionResult result)
    {
        LogUntouchedReader(command, eventData, closed: true);
        return base.DataReaderDisposing(command, eventData, result);
    }

    // ─── The reader handed back to EF Core ─────────────────────

    // The provider's reader is replaced by a TrackingDbDataReader only where nothing EF Core runs on it needs the
    // provider's own type (plans/SQL_RESPONSE_CAPTURE_PLAN.md F1, F2). Npgsql's update pipeline casts the reader of every
    // SaveChanges batch to NpgsqlDataReader (#193), and EF Core's shaper casts it for a column read through a provider
    // reader method. Those readers go back to EF Core as they are, and their response is logged when EF Core closes them.
    private DbDataReader OnReaderExecuted(DbCommand command, CommandExecutedEventData? eventData, DbDataReader result)
    {
        try
        {
            if (_options.LogResponseContent)
            {
                if (KeepsProvidersReader(eventData))
                {
                    _untouched[command] = eventData!.CommandSource == CommandSource.SaveChanges;
                    return result;
                }
                if (WrapReaderIfTracked(command, result) is { } wrapped)
                    return wrapped;
            }
            LogCommandExecuted(command);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
        return result;
    }

    // A direct call with no event data, as the interceptor's own tests make, replaces the reader as before.
    private bool KeepsProvidersReader(CommandExecutedEventData? eventData)
    {
        if (eventData is null) return false;
        var context = eventData.Context;
        if (eventData.CommandSource == CommandSource.SaveChanges &&
            !UpdatePipelineTakesAnyReader(context is null ? null : ProviderNameOf(context)))
            return true;
        if (context is null || ProviderReaderMethods.FirstUse(context) is not { } firstUse) return false;

        NotCaptured(context, firstUse);
        return true;
    }

    /// <summary>
    /// Whether the update pipeline of the EF Core provider named (as <c>DbContext.Database.ProviderName</c> names it)
    /// reads a SaveChanges batch's reader through <see cref="DbDataReader"/>'s own members only, so a TrackingDbDataReader
    /// may stand in for it. The IL scan of plans/SQL_RESPONSE_CAPTURE_PLAN.md cleared these six; Npgsql's casts the reader
    /// to NpgsqlDataReader, and a provider not named here is not assumed to be safe.
    /// </summary>
    internal static bool UpdatePipelineTakesAnyReader(string? providerName) => providerName is
        "Microsoft.EntityFrameworkCore.SqlServer" or
        "Microsoft.EntityFrameworkCore.Sqlite" or
        "Pomelo.EntityFrameworkCore.MySql" or
        "MySql.EntityFrameworkCore" or
        "FirebirdSql.EntityFrameworkCore.Firebird" or
        "Oracle.EntityFrameworkCore";

    /// <summary>The provider a context runs on, as EF Core names it; the facts, which run on SQLite, name another.</summary>
    internal Func<DbContext, string?> ProviderNameOf { get; init; } = static context => context.Database.ProviderName;

    private void LogUntouchedReader(DbCommand command, DataReaderEventData eventData, bool closed)
    {
        // SqlClient sets RecordsAffected only once the reader is closed, so SaveChanges is logged at Disposing; a query is
        // logged at Closing, while the reader can still say whether it stands on a row. EF Core calls Closing before
        // Disposing every time, and when an earlier interceptor's Closing throws it calls neither here.
        try
        {
            if (!_untouched.TryGetValue(command, out var saveChanges) || saveChanges != closed) return;
            _untouched.TryRemove(command, out _);

            string? content;
            if (saveChanges)
            {
                var affected = eventData.DataReader.RecordsAffected;
                content = affected >= 0 ? $"{affected.ToString(CultureInfo.InvariantCulture)} rows affected" : null;
            }
            else
            {
                // ReadCount counts the Read() that found no row too, so a reader still on a row has read one fewer.
                var readCount = ((DataReaderClosingEventData)eventData).ReadCount;
                var rows = Math.Max(0, readCount - (IsOnRow(eventData.DataReader) ? 0 : 1));
                content = $"{rows.ToString(CultureInfo.InvariantCulture)} rows";
            }
            LogCommandExecutedWithContent(command, content);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    private static bool IsOnRow(DbDataReader reader)
    {
        try
        {
            if (reader.IsClosed || reader.FieldCount == 0) return false;
            _ = reader.IsDBNull(0);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Once per context type: every query on it keeps the provider's reader, and its note gives the row count.
    private void NotCaptured(DbContext context, string firstUse)
    {
        Kronikol.Reports.ProcessDiagnostics.RecordOnce(
            NotCapturedKey(ComponentName, context.GetType()),
            Kronikol.Reports.DiagnosticKind.CaptureDegraded,
            $"{ComponentName}: rows of queries on {context.GetType().Name} are not captured: {firstUse}, which needs the " +
            "provider's own reader, so EF Core keeps it and each query's note gives its row count.");
    }

    /// <summary>The key of that entry; tests remove it, as they share the process with every report.</summary>
    internal static string NotCapturedKey(string component, Type contextType) => $"sql-provider-reader:{component}:{contextType.FullName}";

    private void Safe(Action log)
    {
        try
        {
            log();
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    private void NotRecorded(Exception ex) => SqlCaptureFailure.Record(ComponentName, ex);

    // ─── Test identity resolution ────────────────────────────────

    private TestIdentity? GetTestInfo()
        => TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);

    // ─── URI construction ──────────────────────────────────────

    // SqlEndpoint makes a host of any data source ("(localdb)\MSSQLLocalDB", Npgsql's "tcp://host:5432", "host,1433"),
    // which until 5.11.1 went into the address as it was: on LocalDB every command failed with a UriFormatException.
    // A connection that cannot name its data source or database is drawn against localhost and "unknown".
    private static Uri BuildUri(DbCommand command, SqlOperationInfo op, SqlTrackingVerbosity verbosity)
    {
        var database = Named(() => command.Connection?.Database) ?? "unknown";
        var host = SqlEndpoint.Host(Named(() => command.Connection?.DataSource));

        return SqlEndpoint.Build(verbosity switch
        {
            SqlTrackingVerbosity.Raw => $"sql://{host}/{SqlEndpoint.Segment(database)}",
            SqlTrackingVerbosity.Summarised => op.TableName is not null
                ? $"sql://{SqlEndpoint.Host(database)}/{op.TableName}"
                : $"sql://{SqlEndpoint.Host(database)}",
            _ => op.TableName is not null // Detailed
                ? $"sql://{host}/{SqlEndpoint.Segment(database)}/{op.TableName}"
                : $"sql://{host}/{SqlEndpoint.Segment(database)}",
        });
    }

    private static string? Named(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ─── Phase variant helpers ──────────────────────────────────

    private static PhaseVariant BuildVariant(DbCommand command, SqlOperationInfo sqlOp, SqlTrackingVerbosity verbosity)
    {
        var skip = verbosity == SqlTrackingVerbosity.Summarised && sqlOp.Operation == SqlOperation.Other;
        var label = SqlOperationClassifier.GetDiagramLabel(sqlOp, verbosity);
        OneOf<HttpMethod, string> method = verbosity == SqlTrackingVerbosity.Raw
            ? SqlOperationClassifier.GetRawKeyword(command.CommandText) ?? "SQL"
            : label!;
        var uri = BuildUri(command, sqlOp, verbosity);
        var content = verbosity == SqlTrackingVerbosity.Summarised ? null : command.CommandText;

        return new PhaseVariant(method, uri, content, [], skip);
    }

    private PhaseVariant BuildResponseVariant(DbCommand command, SqlOperationInfo sqlOp, SqlTrackingVerbosity verbosity, int? rowsAffected)
    {
        var skip = verbosity == SqlTrackingVerbosity.Summarised && sqlOp.Operation == SqlOperation.Other;
        var label = SqlOperationClassifier.GetDiagramLabel(sqlOp, verbosity);
        OneOf<HttpMethod, string> method = verbosity == SqlTrackingVerbosity.Raw
            ? SqlOperationClassifier.GetRawKeyword(command.CommandText) ?? "SQL"
            : label!;
        var uri = BuildUri(command, sqlOp, verbosity);
        var content = verbosity == SqlTrackingVerbosity.Summarised && !_options.LogResponseContent
            ? null
            : rowsAffected.HasValue ? $"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected" : null;

        return new PhaseVariant(method, uri, content, [], skip);
    }

    private PhaseVariant BuildResponseVariantWithContent(DbCommand command, SqlOperationInfo sqlOp, SqlTrackingVerbosity verbosity, string? responseContent)
    {
        var skip = verbosity == SqlTrackingVerbosity.Summarised && sqlOp.Operation == SqlOperation.Other;
        var label = SqlOperationClassifier.GetDiagramLabel(sqlOp, verbosity);
        OneOf<HttpMethod, string> method = verbosity == SqlTrackingVerbosity.Raw
            ? SqlOperationClassifier.GetRawKeyword(command.CommandText) ?? "SQL"
            : label!;
        var uri = BuildUri(command, sqlOp, verbosity);
        var content = verbosity == SqlTrackingVerbosity.Summarised && !_options.LogResponseContent ? null : responseContent;

        return new PhaseVariant(method, uri, content, [], skip);
    }

    // ─── Response content helpers ───────────────────────────────

    public void LogCommandExecutedWithContent(DbCommand command, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var sqlOp = SqlOperationClassifier.Classify(command.CommandText, command.CommandType);

        if (effectiveVerbosity == SqlTrackingVerbosity.Summarised && sqlOp.Operation == SqlOperation.Other)
            return;

        var testInfo = GetTestInfo();
        if (testInfo is null)
            return;

        if (!_pendingIds.TryRemove(command, out var ids))
            ids = (Guid.NewGuid(), Guid.NewGuid());

        var label = SqlOperationClassifier.GetDiagramLabel(sqlOp, effectiveVerbosity);
        OneOf<HttpMethod, string> method = effectiveVerbosity == SqlTrackingVerbosity.Raw
            ? SqlOperationClassifier.GetRawKeyword(command.CommandText) ?? "SQL"
            : label!;

        var requestUri = BuildUri(command, sqlOp, effectiveVerbosity);
        var responseContent = effectiveVerbosity == SqlTrackingVerbosity.Summarised && !_options.LogResponseContent ? null : content;

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            responseContent,
            requestUri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            ids.TraceId,
            ids.RequestResponseId,
            false,
            (OneOf<System.Net.HttpStatusCode, string>)"OK",
            DependencyCategory: DependencyCategories.SQL
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => BuildResponseVariantWithContent(command, sqlOp, v, content));

        RequestResponseLogger.Log(log);
    }

    private TrackingDbDataReader? WrapReaderIfTracked(DbCommand command, DbDataReader result)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return null;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var sqlOp = SqlOperationClassifier.Classify(command.CommandText, command.CommandType);

        if (effectiveVerbosity == SqlTrackingVerbosity.Summarised && sqlOp.Operation == SqlOperation.Other)
            return null;

        var testInfo = GetTestInfo();
        if (testInfo is null)
            return null;

        if (!_pendingIds.TryRemove(command, out var ids))
            ids = (Guid.NewGuid(), Guid.NewGuid());

        // Capture IDs in the closure — LogCommandExecutedWithContent would fail to
        // find them in _pendingIds since we already removed them above.
        var capturedIds = ids;
        var detail = SqlResponseDetailResolver.Resolve(
            _options.ResponseDetail, effectiveVerbosity == SqlTrackingVerbosity.Summarised);
        return new TrackingDbDataReader(
            result, detail, _options.MaxResponseRows, _options.MaxValueDisplayLength,
            content => LogCommandExecutedWithContentDirect(command, capturedIds.TraceId, capturedIds.RequestResponseId, content));
    }

    private string? FormatScalar(object? result) =>
        _options.LogResponseContent ? SqlRowFormatter.ScalarText(result, _options.MaxValueDisplayLength) : null;

    private void LogCommandExecutedWithContentDirect(DbCommand command, Guid traceId, Guid requestResponseId, string? content)
    {
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var sqlOp = SqlOperationClassifier.Classify(command.CommandText, command.CommandType);

        if (effectiveVerbosity == SqlTrackingVerbosity.Summarised && sqlOp.Operation == SqlOperation.Other)
            return;

        var testInfo = GetTestInfo();
        if (testInfo is null)
            return;

        var label = SqlOperationClassifier.GetDiagramLabel(sqlOp, effectiveVerbosity);
        OneOf<HttpMethod, string> method = effectiveVerbosity == SqlTrackingVerbosity.Raw
            ? SqlOperationClassifier.GetRawKeyword(command.CommandText) ?? "SQL"
            : label!;

        var requestUri = BuildUri(command, sqlOp, effectiveVerbosity);
        // Same rule as BuildResponseVariantWithContent and the shared SqlDiagnosticTracker:
        // Summarised suppresses content only when LogResponseContent is off.
        var responseContent = effectiveVerbosity == SqlTrackingVerbosity.Summarised && !_options.LogResponseContent
            ? null
            : content;

        var log = new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            responseContent,
            requestUri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            traceId,
            requestResponseId,
            false,
            (OneOf<System.Net.HttpStatusCode, string>)"OK",
            DependencyCategory: DependencyCategories.SQL
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        };

        log.AttachVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => BuildResponseVariantWithContent(command, sqlOp, v, content));

        RequestResponseLogger.Log(log);
    }
}
