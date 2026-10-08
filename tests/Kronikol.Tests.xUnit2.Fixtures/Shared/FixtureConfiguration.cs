using System.Runtime.CompilerServices;
using Kronikol.xUnit2;

namespace Kronikol.Tests.xUnit2.Fixtures;

/// <summary>
/// What a lane asks of a run, through the environment. With neither variable set,
/// <see cref="ReportLifecycle.Options"/> is never touched, which is the case T7 measures.
/// </summary>
internal static class FixtureConfiguration
{
    /// <summary>Where the reports go: a lane points it at a file to make the write fail (T11).</summary>
    public const string ReportsFolderVariable = "KRONIKOL_FIXTURE_REPORTS_FOLDER";

    /// <summary>Milliseconds each call's note waits while the report is written, for a report that is slow to write (T10).</summary>
    public const string ReportDelayVariable = "KRONIKOL_FIXTURE_REPORT_DELAY_MS";

    /// <summary>A file that gets a line for each wait, so a lane can tell the report really was slow to write.</summary>
    public const string ReportDelayLogVariable = "KRONIKOL_FIXTURE_REPORT_DELAY_LOG";

    private static readonly object Gate = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        var folder = Environment.GetEnvironmentVariable(ReportsFolderVariable);
        var delay = int.TryParse(Environment.GetEnvironmentVariable(ReportDelayVariable), out var ms) ? ms : 0;
        if (string.IsNullOrEmpty(folder) && delay <= 0)
            return;

        var options = new ReportConfigurationOptions();
        if (!string.IsNullOrEmpty(folder))
            options.ReportsFolderPath = folder;
        if (delay > 0)
        {
            var log = Environment.GetEnvironmentVariable(ReportDelayLogVariable);
            options.RequestResponsePostProcessor = text =>
            {
                Thread.Sleep(delay);
                if (!string.IsNullOrEmpty(log))
                    lock (Gate)
                        File.AppendAllText(log, $"{DateTime.UtcNow:O}{Environment.NewLine}");
                return text;
            };
        }
        ReportLifecycle.Options = options;
    }
}
