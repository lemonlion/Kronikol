using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Kronikol.Sql;

namespace Kronikol.Extensions.MySqlConnector;

/// <summary>
/// Tracks MySQL operations via the <c>"MySqlConnector"</c> DiagnosticSource.
/// Subscribes to <c>Command.Execute*.Start</c> and <c>Command.Execute*.Stop</c> events.
/// Zero production code changes — works automatically for all MySqlConnector connections.
/// </summary>
public sealed class MySqlDiagnosticTracker : SqlDiagnosticTracker, IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private const string DiagnosticListenerName = "MySqlConnector";
    private IDisposable? _listenerSubscription;
    private IDisposable? _allListenersSubscription;

    public MySqlDiagnosticTracker(MySqlTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
        : base(options, httpContextAccessor)
    {
    }

    public override string ComponentName => $"MySqlDiagnosticTracker ({Options.ServiceName})";

    public void Subscribe()
    {
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void Unsubscribe()
    {
        _listenerSubscription?.Dispose();
        _listenerSubscription = null;
        _allListenersSubscription?.Dispose();
        _allListenersSubscription = null;
    }

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

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        try
        {
            if (value.Key.EndsWith(".Start") || value.Key == "WriteCommandBefore")
            {
                HandleCommandStart(value.Value);
            }
            else if (value.Key.EndsWith(".Stop") || value.Key == "WriteCommandAfter")
            {
                HandleCommandEnd(value.Value);
            }
            else if (value.Key.EndsWith(".Error") || value.Key == "WriteCommandError")
            {
                HandleCommandError(value.Value);
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
        LogCommandEnd(executionId);
    }

    private void HandleCommandError(object? payload)
    {
        if (payload is null) return;
        var executionId = ExtractExecutionId(payload);
        if (executionId == Guid.Empty) return;
        var exception = ExtractException(payload);
        LogCommandEnd(executionId, exception: exception);
    }

    private static (string? CommandText, string? DataSource, string? Database, Guid ExecutionId, string? Parameters) ExtractCommandInfo(object payload)
    {
        var type = payload.GetType();
        var executionId = ExtractExecutionId(payload);

        var command = type.GetProperty("Command")?.GetValue(payload);
        string? commandText = null;
        string? dataSource = null;
        string? database = null;
        string? parameters = null;

        if (command is not null)
        {
            var cmdType = command.GetType();
            commandText = cmdType.GetProperty("CommandText")?.GetValue(command) as string;

            var connection = cmdType.GetProperty("Connection")?.GetValue(command);
            if (connection is not null)
            {
                var connType = connection.GetType();
                dataSource = connType.GetProperty("DataSource")?.GetValue(connection)?.ToString();
                database = connType.GetProperty("Database")?.GetValue(connection)?.ToString();
            }

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

        commandText ??= type.GetProperty("CommandText")?.GetValue(payload) as string;
        dataSource ??= type.GetProperty("DataSource")?.GetValue(payload)?.ToString();
        database ??= type.GetProperty("Database")?.GetValue(payload)?.ToString();

        return (commandText, dataSource, database, executionId, parameters);
    }

    private static Guid ExtractExecutionId(object payload)
    {
        var type = payload.GetType();

        var opId = type.GetProperty("OperationId")?.GetValue(payload);
        if (opId is Guid guid) return guid;

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

    private static Exception? ExtractException(object payload)
    {
        var type = payload.GetType();
        return type.GetProperty("Exception")?.GetValue(payload) as Exception;
    }
}
