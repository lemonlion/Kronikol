using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class CiMetadataDetectorTests
{
    [Fact]
    public void Detect_returns_null_when_not_ci()
    {
        var result = CiMetadataDetector.Detect(_ => null);
        Assert.Null(result);
    }

    [Fact]
    public void Detect_returns_github_metadata_from_env_vars()
    {
        var envVars = new Dictionary<string, string?>
        {
            ["GITHUB_ACTIONS"] = "true",
            ["GITHUB_RUN_NUMBER"] = "42",
            ["GITHUB_REF_NAME"] = "main",
            ["GITHUB_SHA"] = "abc123def456789",
            ["GITHUB_SERVER_URL"] = "https://github.com",
            ["GITHUB_REPOSITORY"] = "owner/repo",
            ["GITHUB_RUN_ID"] = "12345",
            ["GITHUB_RUN_ATTEMPT"] = "1"
        };

        var result = CiMetadataDetector.Detect(key => envVars.GetValueOrDefault(key));

        Assert.NotNull(result);
        Assert.Equal(CiEnvironment.GitHubActions, result.Provider);
        Assert.Equal("42", result.BuildNumber);
        Assert.Equal("main", result.Branch);
        Assert.Equal("abc123def456789", result.CommitSha);
        Assert.Equal("https://github.com/owner/repo/actions/runs/12345", result.PipelineUrl);
        Assert.Equal("owner/repo", result.Repository);
        Assert.Equal("12345", result.RunId);
        Assert.Equal("1", result.RunAttempt);
    }

    /// <summary>
    /// The attempt is the only part of a run's identity that cannot be reconstructed afterwards.
    /// GitHub's own reference is explicit that <c>GITHUB_RUN_ID</c> "does not change if you re-run",
    /// so a report that records the id and not the attempt collapses a re-run onto the run it retried —
    /// and re-running a failed job until it passes is exactly how a flake disappears from history.
    /// </summary>
    [Fact]
    public void A_re_run_is_told_apart_from_the_run_it_retried()
    {
        var first = CiMetadataDetector.Detect(key => new Dictionary<string, string?>
        {
            ["GITHUB_ACTIONS"] = "true", ["GITHUB_RUN_ID"] = "12345", ["GITHUB_RUN_ATTEMPT"] = "1"
        }.GetValueOrDefault(key));

        var retry = CiMetadataDetector.Detect(key => new Dictionary<string, string?>
        {
            ["GITHUB_ACTIONS"] = "true", ["GITHUB_RUN_ID"] = "12345", ["GITHUB_RUN_ATTEMPT"] = "2"
        }.GetValueOrDefault(key));

        Assert.Equal(first!.RunId, retry!.RunId);
        Assert.NotEqual(first.RunAttempt, retry.RunAttempt);
    }

    /// <summary>Azure DevOps has no equivalent variable, and a guess would be worse than a null.</summary>
    [Fact]
    public void Azure_devops_reports_no_attempt_rather_than_a_guess()
    {
        var result = CiMetadataDetector.Detect(key => new Dictionary<string, string?>
        {
            ["TF_BUILD"] = "True", ["BUILD_BUILDID"] = "77"
        }.GetValueOrDefault(key));

        Assert.Equal(CiEnvironment.AzureDevOps, result!.Provider);
        Assert.Equal("77", result.RunId);
        Assert.Null(result.RunAttempt);
    }

    [Fact]
    public void Detect_returns_azure_devops_metadata_from_env_vars()
    {
        var envVars = new Dictionary<string, string?>
        {
            ["TF_BUILD"] = "True",
            ["BUILD_BUILDNUMBER"] = "20240101.1",
            ["BUILD_SOURCEBRANCH"] = "refs/heads/main",
            ["BUILD_SOURCEVERSION"] = "abc123def456789",
            ["SYSTEM_TEAMFOUNDATIONSERVERURI"] = "https://dev.azure.com/org/",
            ["SYSTEM_TEAMPROJECT"] = "MyProject",
            ["BUILD_BUILDID"] = "999",
            ["BUILD_REPOSITORY_NAME"] = "MyRepo"
        };

        var result = CiMetadataDetector.Detect(key => envVars.GetValueOrDefault(key));

        Assert.NotNull(result);
        Assert.Equal(CiEnvironment.AzureDevOps, result.Provider);
        Assert.Equal("20240101.1", result.BuildNumber);
        Assert.Equal("refs/heads/main", result.Branch);
        Assert.Equal("abc123def456789", result.CommitSha);
        Assert.Equal("https://dev.azure.com/org/MyProject/_build/results?buildId=999", result.PipelineUrl);
        Assert.Equal("MyRepo", result.Repository);
        Assert.Equal("999", result.RunId);
    }

    [Fact]
    public void A_pull_request_build_names_the_branch_it_targets()
    {
        // GITHUB_BASE_REF is set on pull_request events and empty otherwise; Azure DevOps names the target
        // as the full ref its own runs record under. A push has no target, and neither does a machine off
        // CI, whatever is in its environment.
        string? PullRequest(string key) => key switch { "GITHUB_ACTIONS" => "true", "GITHUB_BASE_REF" => "main", _ => null };
        string? Push(string key) => key switch { "GITHUB_ACTIONS" => "true", "GITHUB_BASE_REF" => "", _ => null };
        string? Azure(string key) => key switch { "TF_BUILD" => "True", "SYSTEM_PULLREQUEST_TARGETBRANCH" => "refs/heads/main", _ => null };

        Assert.Equal("main", CiMetadataDetector.PullRequestTarget(PullRequest));
        Assert.Null(CiMetadataDetector.PullRequestTarget(Push));
        Assert.Equal("refs/heads/main", CiMetadataDetector.PullRequestTarget(Azure));
        Assert.Null(CiMetadataDetector.PullRequestTarget(_ => null));
        Assert.Null(CiMetadataDetector.PullRequestTarget(key => key == "GITHUB_BASE_REF" ? "main" : null));
    }
}
