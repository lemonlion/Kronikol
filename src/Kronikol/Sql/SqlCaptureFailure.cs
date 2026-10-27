using Kronikol.Reports;

namespace Kronikol.Sql;

/// <summary>
/// What a SQL tracker does with a command it could not record. The trackers run inside the application's own calls (a
/// DiagnosticSource event, a wrapped command, an EF Core interceptor), so an exception leaving one would fail that call:
/// the command goes unrecorded and the run's diagnostics say so, once per component, as the MongoDB subscriber has done
/// since 4.19.6.
/// </summary>
internal static class SqlCaptureFailure
{
    internal static void Record(string component, Exception ex)
    {
        try
        {
            ProcessDiagnostics.RecordOnce(
                KeyFor(component),
                DiagnosticKind.CaptureDegraded,
                $"{component} could not record a command: {ex.GetType().Name}: {ex.Message}. " +
                "The application's command was not affected; the command, and any after it that fail the same way, are missing from the diagrams.");
        }
        catch (Exception)
        {
            // Nothing a diagnostic can fail on may reach the application either.
        }
    }

    /// <summary>The key of <paramref name="component"/>'s entry; tests remove it, as they share the process with every report.</summary>
    internal static string KeyFor(string component) => $"sql-capture-failed:{component}";
}
