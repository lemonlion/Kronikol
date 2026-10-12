using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Kronikol.Sql;

namespace Kronikol.Extensions.Npgsql;

/// <summary>
/// Tracks PostgreSQL (Npgsql) SQL operations via the <c>"Npgsql"</c> DiagnosticSource.
/// Subscribes to <c>BeforeExecuteCommand</c>, <c>AfterExecuteCommand</c>, and
/// <c>CommandExecutionException</c> events.
/// Zero production code changes — works automatically for all Npgsql connections.
/// </summary>
public sealed class NpgsqlDiagnosticTracker : SqlDiagnosticTracker, IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private const string DiagnosticListenerName = "Npgsql";
    private IDisposable? _listenerSubscription;
    private IDisposable? _allListenersSubscription;

    public NpgsqlDiagnosticTracker(NpgsqlTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
        : base(options, httpContextAccessor)
    {
    }

    public override string ComponentName => $"NpgsqlDiagnosticTracker ({Options.ServiceName})";

    /// <summary>
    /// Subscribes to <see cref="DiagnosticListener.AllListeners"/> to intercept the Npgsql DiagnosticSource.
    /// </summary>
    public void Subscribe()
    {
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    /// <summary>
    /// Unsubscribes from all diagnostic listeners.
    /// </summary>
    public void Unsubscribe()
    {
        _listenerSubscription?.Dispose();
        _listenerSubscription = null;
        _allListenersSubscription?.Dispose();
        _allListenersSubscription = null;
    }

    // IObserver<DiagnosticListener>

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener value)
    {
        if (value.Name == DiagnosticListenerName)
        {
            _listenerSubscription?.Dispose();
            _listenerSubscription = value.Subscribe(this);
        }
    }

    void IObserver<DiagnosticListener>.OnCompleted() { }
    void IObserver<DiagnosticListener>.OnError(Exception error) { }

    // IObserver<KeyValuePair<string, object?>>

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        try
        {
            switch (value.Key)
            {
                case "Npgsql.Command.CommandExecuting" or "BeforeExecuteCommand":
                    HandleCommandStart(value.Value);
                    break;
                case "Npgsql.Command.CommandExecuted" or "AfterExecuteCommand":
                    HandleCommandEnd(value.Value);
                    break;
                case "Npgsql.Command.CommandError" or "CommandExecutionException":
                    HandleCommandError(value.Value);
                    break;
            }
        }
        catch
        {
            // DiagnosticSource event handling must never throw
        }
    }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }
    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }

    private void HandleCommandStart(object? payload)
    {
        if (payload is null) return;

        var (commandText, dataSource, database, executionId, parameters) = ExtractCommandInfo(payload);
        if (executionId == Guid.Empty) return;

        LogCommandStart(commandText, dataSource, database, executionId, parameters);
    }

    private void HandleCommandEnd(object? payload)
    {
        if (payload is null) return;

        var executionId = ExtractExecutionId(payload);
        if (executionId == Guid.Empty) return;

        var rowsAffected = ExtractRowsAffected(payload);
        LogCommandEnd(executionId, rowsAffected);
    }

    private void HandleCommandError(object? payload)
    {
        if (payload is null) return;

        var executionId = ExtractExecutionId(payload);
        if (executionId == Guid.Empty) return;

        var exception = ExtractException(payload);
        LogCommandEnd(executionId, exception: exception);
    }

    // Npgsql DiagnosticSource uses anonymous types; extract via reflection
    private static (string? CommandText, string? DataSource, string? Database, Guid ExecutionId, string? Parameters) ExtractCommandInfo(object payload)
    {
        var type = payload.GetType();

        var executionId = ExtractExecutionId(payload);
        var commandText = type.GetProperty("CommandText")?.GetValue(payload) as string
                       ?? type.GetProperty("CommandText")?.GetValue(payload)?.ToString();

        // Try to get connection info from the Command property
        var command = type.GetProperty("Command")?.GetValue(payload);
        string? dataSource = null;
        string? database = null;
        string? parameters = null;

        if (command is not null)
        {
            var cmdType = command.GetType();
            commandText ??= cmdType.GetProperty("CommandText")?.GetValue(command) as string;

            var connection = cmdType.GetProperty("Connection")?.GetValue(command);
            if (connection is not null)
            {
                var connType = connection.GetType();
                dataSource = connType.GetProperty("Host")?.GetValue(connection)?.ToString();
                var port = connType.GetProperty("Port")?.GetValue(connection);
                if (dataSource is not null && port is not null)
                    dataSource = $"{dataSource}:{port}";
                database = connType.GetProperty("Database")?.GetValue(connection)?.ToString();
            }

            // Extract parameters if present
            var paramCollection = cmdType.GetProperty("Parameters")?.GetValue(command);
            if (paramCollection is System.Collections.IEnumerable enumerable)
            {
                var paramStrs = new List<string>();
                foreach (var p in enumerable)
                {
                    var pType = p.GetType();
                    var name = pType.GetProperty("ParameterName")?.GetValue(p)?.ToString();
                    var val = pType.GetProperty("Value")?.GetValue(p);
                    if (name is not null)
                        paramStrs.Add($"{name}={(val is null or DBNull ? "" : Kronikol.Sql.SqlRowFormatter.ValueText(val))}");
                }
                if (paramStrs.Count > 0)
                    parameters = string.Join(", ", paramStrs);
            }
        }

        // Fallback: try direct properties on payload
        dataSource ??= type.GetProperty("DataSource")?.GetValue(payload)?.ToString()
                     ?? type.GetProperty("Host")?.GetValue(payload)?.ToString();
        database ??= type.GetProperty("Database")?.GetValue(payload)?.ToString();

        return (commandText, dataSource, database, executionId, parameters);
    }

    private static Guid ExtractExecutionId(object payload)
    {
        var type = payload.GetType();

        // Npgsql uses OperationId or CommandId
        var opId = type.GetProperty("OperationId")?.GetValue(payload);
        if (opId is Guid guid) return guid;

        var cmdId = type.GetProperty("CommandId")?.GetValue(payload);
        if (cmdId is Guid cmdGuid) return cmdGuid;

        // Fallback: look for any Guid-typed property
        foreach (var prop in type.GetProperties())
        {
            if (prop.PropertyType == typeof(Guid))
            {
                var val = (Guid)prop.GetValue(payload)!;
                if (val != Guid.Empty) return val;
            }
        }

        return Guid.Empty;
    }

    private static int? ExtractRowsAffected(object payload)
    {
        var type = payload.GetType();
        var prop = type.GetProperty("RowsAffected") ?? type.GetProperty("Rows");
        if (prop?.GetValue(payload) is int rows)
            return rows;
        return null;
    }

    private static Exception? ExtractException(object payload)
    {
        var type = payload.GetType();
        return type.GetProperty("Exception")?.GetValue(payload) as Exception;
    }
}
