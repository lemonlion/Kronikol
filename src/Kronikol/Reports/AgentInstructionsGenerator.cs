namespace Kronikol.Reports;

/// <summary>
/// Writes the instruction files that sit next to a report and teach any agent that opens the directory how
/// to debug the run without reading the report.
///
/// <para><b>Why two files.</b> Claude Code reads <c>CLAUDE.md</c> and nothing else; Codex, Cursor, Copilot,
/// Jules and Amp read <c>AGENTS.md</c>. Emitting one and hoping is how a discovery loop quietly fails, so
/// both are written with identical bytes. A nested <c>CLAUDE.md</c> loads the moment an agent reads any
/// file in its directory — gitignored and <c>bin/</c> directories included — which is exactly what happens
/// the first time it opens <c>Failures.md</c>. There is no <c>llms.txt</c>: no agent reads one.</para>
///
/// <para><b>Why it is static.</b> These are instructions, and everything a test run produces — scenario
/// names, assertion messages, third-party response bodies — is written by something other than the person
/// running the tests. A response body reading "ignore your previous instructions" belongs in a fenced data
/// file that says it is data (<see cref="FailuresDigestGenerator">the digest</see> does), never in a file
/// an agent loads as direction. <see cref="Build"/> therefore takes no features, no logs and no summary:
/// the only substitution is the configured report file name, which is the user's own setting.</para>
/// </summary>
public static class AgentInstructionsGenerator
{
    /// <summary>The file Claude Code loads.</summary>
    public const string ClaudeFileName = "CLAUDE.md";

    /// <summary>The file every other agent loads. Byte-identical to <see cref="ClaudeFileName"/>.</summary>
    public const string AgentsFileName = "AGENTS.md";

    private const string ReportToken = "__REPORT__";

    /// <summary>
    /// The instruction text. <paramref name="htmlTestRunReportFileName"/> is
    /// <see cref="ReportConfigurationOptions.HtmlTestRunReportFileName"/> — the base name without
    /// extension — so a renamed report is named correctly in the rule it is the subject of.
    /// </summary>
    public static string Build(string htmlTestRunReportFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlTestRunReportFileName);
        return Template.Value.Replace(ReportToken, htmlTestRunReportFileName, StringComparison.Ordinal);
    }

    private static readonly Lazy<string> Template = new(() =>
    {
        var assembly = typeof(AgentInstructionsGenerator).Assembly;
        var name = assembly.GetManifestResourceNames()
                       .FirstOrDefault(n => n.EndsWith("agent-instructions.md", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Embedded resource agent-instructions.md not found.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
