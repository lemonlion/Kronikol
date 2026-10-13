using System.Globalization;

namespace Kronikol.Reports;

/// <summary>
/// Where the readers of a run's CI text are, as far as the run can tell: the CI run they reach it from, the workspace
/// a person finds the reports folder in, and what Kronikol itself handed to an upload
/// (plans/CI_SUMMARY_AUDIENCE_PLAN.md).
///
/// <para>None of those readers is on the runner. A GitHub job summary is read by signed-in people in a browser, and
/// by no API, <c>gh</c> command or MCP tool; the <c>::notice</c> annotation by people and by agents through
/// <c>gh run view</c>; <c>CiSummary.md</c> by whoever downloads the artifact. A hosted runner is gone once its job
/// ends, so a command naming its folder works nowhere a reader is. What the text names has to exist where they are:
/// the run's artifacts, a download of them, and the folder relative to the workspace.</para>
///
/// <para>Internal, so the CI text takes these facts without a new public member (a minor bump under the
/// repository's rule); the public builders read the process's own CI with default options.</para>
/// </summary>
internal sealed record CiReader
{
    /// <summary>Off CI: the reader is on this machine, and the text names the folder as it is.</summary>
    public static CiReader None { get; } = new();

    /// <summary>The CI the run is on.</summary>
    public CiEnvironment Provider { get; init; }

    /// <summary>The run's page (<see cref="CiMetadata.PipelineUrl"/>), or null when a variable it is built from is missing.</summary>
    public string? RunUrl { get; init; }

    /// <summary><c>GITHUB_RUN_ID</c> or <c>BUILD_BUILDID</c>.</summary>
    public string? RunId { get; init; }

    /// <summary><c>GITHUB_RUN_ATTEMPT</c>; Azure DevOps has none.</summary>
    public string? RunAttempt { get; init; }

    /// <summary><c>GITHUB_REPOSITORY</c>, as <c>owner/name</c>.</summary>
    public string? Repository { get; init; }

    /// <summary>
    /// The server's host when it is not github.com (GitHub Enterprise Server, or a GHE.com subdomain), which
    /// <c>gh --repo</c> then has to carry as <c>HOST/OWNER/REPO</c>; null on github.com.
    /// </summary>
    public string? Host { get; init; }

    /// <summary><c>GITHUB_WORKSPACE</c> or <c>BUILD_SOURCESDIRECTORY</c>: where a person finds the reports folder, in the repository.</summary>
    public string? Workspace { get; init; }

    /// <summary>
    /// The artifact Kronikol asked the CI to upload the folder as: Azure DevOps with
    /// <see cref="ReportConfigurationOptions.PublishCiArtifacts"/>, whose upload travels on the same stdout as the
    /// summary, so a reader who has one has the other. Null wherever Kronikol uploads nothing itself.
    /// </summary>
    public string? ArtifactName { get; init; }

    /// <summary>
    /// GitHub Actions with <see cref="ReportConfigurationOptions.PublishCiArtifacts"/>: the folder went to the job's
    /// upload step as <c>reports-path</c>. Kronikol does not upload it and never learns the artifact's name.
    /// </summary>
    public bool HandsFolderToUpload { get; init; }

    /// <summary>The Kronikol release that wrote the report, which <c>query.cs</c> needs in the NuGet cache off the runner.</summary>
    public string? KronikolVersion { get; init; }

    /// <summary>GitHub Actions with a run id and a repository: a reader with <c>gh</c> can download the run's artifacts.</summary>
    public bool CanDownload =>
        Provider == CiEnvironment.GitHubActions && !string.IsNullOrWhiteSpace(RunId) && !string.IsNullOrWhiteSpace(Repository);

    /// <summary>The run attempt, or 1 when the CI names none or names one that is not a number.</summary>
    public int Attempt =>
        int.TryParse(RunAttempt, NumberStyles.None, CultureInfo.InvariantCulture, out var attempt) && attempt > 0 ? attempt : 1;

    /// <summary>
    /// The reader of a run on the CI <paramref name="getEnv"/> describes, configured by <paramref name="options"/>.
    /// Values are kept as the CI gives them; the text flattens and quotes each where it prints it.
    /// </summary>
    public static CiReader Detect(Func<string, string?> getEnv, ReportConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(getEnv);
        ArgumentNullException.ThrowIfNull(options);

        var version = ReportGenerator.KronikolVersion.Split('+')[0];
        if (CiMetadataDetector.Detect(getEnv) is not { } metadata)
            return None with { KronikolVersion = version };

        var gitHub = metadata.Provider == CiEnvironment.GitHubActions;
        return new CiReader
        {
            Provider = metadata.Provider,
            RunUrl = Value(metadata.PipelineUrl),
            RunId = Value(metadata.RunId),
            RunAttempt = Value(metadata.RunAttempt),
            Repository = Value(metadata.Repository),
            Host = gitHub ? EnterpriseHost(getEnv("GITHUB_SERVER_URL")) : null,
            Workspace = Value(getEnv(gitHub ? "GITHUB_WORKSPACE" : "BUILD_SOURCESDIRECTORY")),
            ArtifactName = !gitHub && options.PublishCiArtifacts ? Value(options.CiArtifactName) : null,
            // What CiArtifactPublisher does on GitHub: nothing at all without $GITHUB_OUTPUT to write to.
            HandsFolderToUpload = gitHub && options.PublishCiArtifacts && !string.IsNullOrEmpty(getEnv("GITHUB_OUTPUT")),
            KronikolVersion = version
        };
    }

    /// <summary>The process's own CI, with default options: what the public builders read.</summary>
    public static CiReader FromProcess() => Detect(Environment.GetEnvironmentVariable, new ReportConfigurationOptions());

    private static string? Value(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// The host <c>gh</c> has to be told, from <c>GITHUB_SERVER_URL</c>: null for github.com, where <c>--repo</c> takes
    /// <c>owner/name</c> alone, and for a value that is no URL, which names no host to send it to.
    /// </summary>
    private static string? EnterpriseHost(string? serverUrl)
    {
        if (Value(serverUrl) is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;
        return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ? null : uri.Authority;
    }
}
