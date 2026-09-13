using System.Runtime.InteropServices;

namespace Kronikol.Reports;

/// <summary>
/// What a run executed on, as the data files record it: the operating system and the .NET runtime.
/// Deliberately two fields and no more — a report is an artifact other people download, so the machine
/// name, the user name and the environment block stay out of it (LLM_FRIENDLY_PLAN §3.3). Together with
/// <see cref="CiMetadata"/> this is what tells a downloaded <c>TestRunReport.json</c> which run it is,
/// which is what a baseline index has to key on.
/// </summary>
/// <param name="Os">
/// <see cref="RuntimeInformation.OSDescription"/> — e.g. <c>Microsoft Windows 10.0.26200</c> or
/// <c>Linux 6.8.0-1017-azure #20-Ubuntu SMP</c>. A description, not a parseable identifier.
/// </param>
/// <param name="Runtime">
/// <see cref="RuntimeInformation.FrameworkDescription"/> — e.g. <c>.NET 10.0.0</c>. The runtime that
/// produced the report, which for a test run is the runtime the tests executed on.
/// </param>
public sealed record RunEnvironment(string Os, string Runtime)
{
    /// <summary>
    /// This process's environment, computed once. Machine-varying by nature, which is why the Java port
    /// takes it from its caller rather than computing it (its parity fixtures are byte goldens).
    /// </summary>
    public static RunEnvironment Current { get; } =
        new(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription);

    /// <summary>
    /// The marker a writer is handed when nobody can say what the run executed on. The key is left out
    /// of the data file entirely rather than filled in with the writing process's own answer.
    /// </summary>
    /// <remarks>
    /// <para>Two lanes need it. <c>kronikol merge</c> runs on whichever machine collects the shards, and
    /// when they do not agree there is no single environment to report. <c>kronikol ingest</c> may be
    /// reading a suite that never ran on .NET at all - the repo's own Cucumber fixture is a node.js run -
    /// so unless the source file says, nothing does.</para>
    ///
    /// <para>Passing <c>null</c> instead means "this machine", which is what a live run wants and what
    /// the <c>kronikolVersion</c> parameter beside it already means. This is the other answer, and it
    /// has a name so that the difference is visible at the call site.</para>
    /// </remarks>
    public static RunEnvironment Unrecorded { get; } = new("", "");
}
