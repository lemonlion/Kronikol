using System.Reflection;

namespace Kronikol.Reports;

/// <summary>
/// The name of the test suite a run belongs to — the thing that separates two projects which both have a
/// feature called <c>Cake</c> and a scenario called <c>Order a cake</c>.
///
/// <para><b>Why it has to be resolved rather than read.</b> Nothing on the model carried a suite:
/// <see cref="Feature"/> has a display name, an endpoint and its scenarios; <see cref="Scenario"/> has no
/// project or assembly field; and the only "suite" elsewhere in the codebase is CTRF's, which is defined
/// as the feature name again and so adds no discrimination. The report title is no better — it defaults
/// to the literal string <c>Test Run Report</c> for most projects.</para>
///
/// <para><b>What it uses, and why that and not something else.</b> The test assembly is the one thing that
/// is genuinely per-suite, stable across runs, and present at generation time. It is not
/// <c>Assembly.GetEntryAssembly()</c>: under VSTest that is <c>testhost</c>, which is the same string for
/// every project and would scope nothing. It is not <c>GetCallingAssembly</c> either — the generation is
/// reached through eighteen adapter entry points and, for the LightBDD adapters, through a framework
/// callback where the test assembly is not on the stack at all. What <i>is</i> reliable is the run's own
/// output directory: <c>dotnet test</c> sets <see cref="AppContext.BaseDirectory"/> to the test project's
/// build output, and exactly one <c>*.deps.json</c> sits there, named for the assembly that produced it.
/// </para>
///
/// <para><b>It degrades to nothing rather than to a guess.</b> If the probe is ambiguous or finds nothing,
/// the suite is null, and <see cref="ScenarioStableId.Compute"/> then produces exactly the id it produced
/// before suites existed. A wrong suite would silently re-key every scenario in the run; an absent one
/// costs only the cross-suite discrimination that was never there.</para>
/// </summary>
public static class RunSuite
{
    private static readonly Lazy<string?> Probed = new(Probe);

    /// <summary>
    /// The suite for this run: <see cref="ReportConfigurationOptions.SuiteName"/> when the caller set it,
    /// otherwise the probed test-assembly name, otherwise null.
    /// </summary>
    public static string? Resolve(ReportConfigurationOptions? options) =>
        !string.IsNullOrWhiteSpace(options?.SuiteName) ? options!.SuiteName!.Trim() : Probed.Value;

    /// <summary>The probed value on its own, for callers that have no options in hand.</summary>
    public static string? Current => Probed.Value;

    private static string? Probe()
    {
        try
        {
            var baseDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory)) return null;

            var deps = Directory.GetFiles(baseDirectory, "*.deps.json");
            // Exactly one, or the answer is a guess. A published output that carries several is precisely
            // the case where picking one would be wrong.
            if (deps.Length != 1) return FromEntryAssembly();

            var name = Path.GetFileName(deps[0])[..^".deps.json".Length];
            return IsNotASuite(name) ? null : name;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return FromEntryAssembly();
        }
    }

    /// <summary>
    /// The fallback, used only when the output directory cannot answer. <c>testhost</c> and its siblings
    /// are rejected by name because they are the same string for every project, which is the one thing a
    /// suite must not be.
    /// </summary>
    private static string? FromEntryAssembly()
    {
        var name = Assembly.GetEntryAssembly()?.GetName().Name;
        if (string.IsNullOrEmpty(name)) return null;

        return IsNotASuite(name) ? null : name;
    }

    /// <summary>
    /// Names that are never a suite. The runners are rejected because they are the same string for every
    /// project, which is the one thing a suite must not be. Kronikol's own tool is rejected for a
    /// different reason: <c>kronikol ingest</c> and <c>kronikol merge</c> run in the tool's directory, and
    /// scoping an ingested run's ids to <c>Kronikol.Tool</c> would key every capture ever replayed to the
    /// program that replayed it.
    /// </summary>
    private static bool IsNotASuite(string name) =>
        name is "testhost" or "testhost.x86" or "dotnet" or "vstest.console" or "ReSharperTestRunner"
            or "Kronikol.Tool";
}
