using System.Text;

namespace Kronikol.Tests.Templates;

/// <summary>
/// The origins the history action's facts push to (plans/HISTORY_ACTION_PLAN.md §5.2): a bare repository that refuses
/// pushes on demand, the same repository over smart HTTP answering a token as github.com does, and a machine's
/// configuration whose stored credential really answers that server's challenge. The facts about refusals, tokens and
/// credential helpers prove something only if these behave as the real ones do.
/// </summary>
public class GitOriginTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-origin").FullName;

    public void Dispose()
    {
        try { BareOrigin.DeleteTree(_dir); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static void SkipWithoutGit() => Assert.SkipWhen(!GitProbe.IsAvailable, "git 2.31 or later not available");

    internal static string Basic(string user, string password) =>
        "AUTHORIZATION: basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));

    private ChildProcess.Result Git(string? machineConfig, string workingDirectory, params string[] arguments)
    {
        var home = Path.Combine(_dir, "home");
        Directory.CreateDirectory(home);
        var empty = Path.Combine(home, "empty.gitconfig");
        File.WriteAllText(empty, "");
        var environment = ChildProcess.BaseEnvironment(home, Path.GetTempPath(), machineConfig ?? empty);
        environment["PATH"] = string.Join(Path.PathSeparator, ChildProcess.SystemPath());
        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GIT_AUTHOR_NAME"] = environment["GIT_COMMITTER_NAME"] = "person";
        environment["GIT_AUTHOR_EMAIL"] = environment["GIT_COMMITTER_EMAIL"] = "person@example.com";
        return ChildProcess.Run(GitProbe.Executable!, arguments, environment, workingDirectory, timeout: TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void The_bare_origin_holds_main_and_a_branch_committed_to_it_starts_with_no_parent()
    {
        SkipWithoutGit();
        var origin = new BareOrigin(Path.Combine(_dir, "origin"));

        Assert.NotNull(origin.Tip("main"));
        Assert.Null(origin.Tip("kronikol-history"));

        origin.Commit("kronikol-history", new Dictionary<string, string> { ["history.jsonl"] = "{}\n" });

        Assert.Equal("{}\n", origin.Show("kronikol-history", "history.jsonl"));
        Assert.Equal([0], origin.Parents("kronikol-history"));
        Assert.Equal(["history.jsonl"], origin.Files("kronikol-history"));
    }

    [Fact]
    public void A_push_the_origin_refuses_fails_with_the_origins_message()
    {
        SkipWithoutGit();
        var origin = new BareOrigin(Path.Combine(_dir, "origin"));
        var clone = Path.Combine(_dir, "clone");
        Assert.Equal(0, Git(null, _dir, "clone", "-q", origin.ServerUrl + "/octo/app.git", clone).ExitCode);
        File.WriteAllText(Path.Combine(clone, "new.txt"), "x");
        Git(null, clone, "add", "-A");
        Git(null, clone, "commit", "-q", "-m", "change");

        origin.RefusePushesTo("main", "refused: the branch is protected by a ruleset");
        var push = Git(null, clone, "push", "-q", "origin", "HEAD:refs/heads/main");

        Assert.NotEqual(0, push.ExitCode);
        Assert.Contains("refused: the branch is protected by a ruleset", push.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void The_http_origin_serves_the_token_and_challenges_a_missing_or_wrong_one()
    {
        SkipWithoutGit();
        var origin = new BareOrigin(Path.Combine(_dir, "origin"));
        using var http = new HttpOrigin(origin);
        var url = http.ServerUrl + "/octo/app.git";

        Assert.Equal(0, Git(null, _dir, "-c", "http.extraheader=" + Basic("x-access-token", HttpOrigin.Token), "ls-remote", url).ExitCode);
        Assert.Equal(128, Git(null, _dir, "ls-remote", url).ExitCode);
        Assert.Equal(128, Git(null, _dir, "-c", "http.extraheader=" + Basic("x-access-token", "wrong"), "ls-remote", url).ExitCode);

        var statuses = http.Requests.Select(r => (r.Status, r.Who)).ToList();
        Assert.Contains((200, "token"), statuses);
        Assert.Contains((401, (string?)null), statuses);
    }

    [Fact]
    public void A_read_only_token_reads_and_its_push_is_answered_403()
    {
        SkipWithoutGit();
        var origin = new BareOrigin(Path.Combine(_dir, "origin"));
        using var http = new HttpOrigin(origin);
        var header = "http.extraheader=" + Basic("x-access-token", HttpOrigin.ReadOnlyToken);
        var clone = Path.Combine(_dir, "clone");

        Assert.Equal(0, Git(null, _dir, "-c", header, "clone", "-q", http.ServerUrl + "/octo/app.git", clone).ExitCode);
        File.WriteAllText(Path.Combine(clone, "new.txt"), "x");
        Git(null, clone, "add", "-A");
        Git(null, clone, "commit", "-q", "-m", "change");
        var push = Git(null, clone, "-c", header, "push", "-q", "origin", "HEAD:refs/heads/main");

        Assert.NotEqual(0, push.ExitCode);
        Assert.Contains(http.Requests, r => r.Status == 403 && r.Who == "read-only-token");
        Assert.Equal(origin.Tip("main"), Git(null, _dir, "--git-dir", origin.GitDir, "rev-parse", "main").Stdout.Trim());
    }

    [Fact]
    public void A_machines_stored_credential_answers_the_origins_challenge()
    {
        SkipWithoutGit();
        var origin = new BareOrigin(Path.Combine(_dir, "origin"));
        using var http = new HttpOrigin(origin);
        var machine = new MachineConfig(Path.Combine(_dir, "machine")).StoreCredential(http.ServerUrl, HttpOrigin.OtherUser, HttpOrigin.OtherPassword);

        var listed = Git(machine.Path, _dir, "ls-remote", http.ServerUrl + "/octo/app.git");

        Assert.Equal(0, listed.ExitCode);
        Assert.Contains(http.Requests, r => r.Status == 200 && r.Who == HttpOrigin.OtherUser);
    }
}
