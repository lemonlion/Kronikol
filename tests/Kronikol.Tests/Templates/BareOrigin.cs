namespace Kronikol.Tests.Templates;

/// <summary>
/// A bare repository standing for the repository on github.com (plans/HISTORY_ACTION_PLAN.harness/common.sh, made into
/// C#): <c>octo/app.git</c> under a project root, holding a <c>main</c> with two files. It serves blobless and by-id
/// fetches as github.com does (<c>uploadpack.allowFilter</c>, <c>uploadpack.allowAnySHA1InWant</c>), and a
/// <c>pre-receive</c> hook refuses pushes on demand, as a ruleset or a server hook would.
/// Reached through <see cref="ServerUrl"/> as <c>file://</c>, or over HTTP through <see cref="HttpOrigin"/>.
/// </summary>
internal sealed class BareOrigin
{
    public const string Repository = "octo/app";

    public BareOrigin(string root)
    {
        Root = root;
        GitDir = Path.Combine(root, "octo", "app.git");
        Directory.CreateDirectory(GitDir);
        Git(root, "init", "-q", "--bare", "-b", "main", GitDir);
        Git(GitDir, "config", "uploadpack.allowFilter", "true");
        Git(GitDir, "config", "uploadpack.allowAnySHA1InWant", "true");
        Commit("main", new Dictionary<string, string> { ["README.md"] = "# app\n", ["src/App.cs"] = "class App {}\n" }, "init");
    }

    /// <summary>The directory <c>octo/app.git</c> sits in: <c>git http-backend</c>'s project root.</summary>
    public string Root { get; }

    public string GitDir { get; }

    /// <summary>What <c>github.server_url</c> would be for this origin reached as a file.</summary>
    public string ServerUrl => new Uri(Root).AbsoluteUri.TrimEnd('/');

    /// <summary>The branch's tip, or null when the origin has no such branch.</summary>
    public string? Tip(string branch)
    {
        var result = TryGit(GitDir, "rev-parse", "--verify", "-q", "refs/heads/" + branch);
        return result.ExitCode == 0 ? result.Stdout.Trim() : null;
    }

    /// <summary>A file on a branch, or null when the branch or the file is not there.</summary>
    public string? Show(string branch, string path)
    {
        var result = TryGit(GitDir, "show", $"refs/heads/{branch}:{path}");
        return result.ExitCode == 0 ? result.Stdout : null;
    }

    /// <summary>The files a branch's tip holds.</summary>
    public IReadOnlyList<string> Files(string branch) =>
        Git(GitDir, "ls-tree", "-r", "--name-only", "refs/heads/" + branch).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The commit messages of a branch, newest first.</summary>
    public IReadOnlyList<string> Messages(string branch) =>
        Git(GitDir, "log", "--format=%s", "refs/heads/" + branch).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The number of parents each commit of a branch has, newest first: an orphan root has none.</summary>
    public IReadOnlyList<int> Parents(string branch) =>
        Git(GitDir, "log", "--format=%P", "refs/heads/" + branch).Split('\n').SkipLast(1).Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).ToList();

    /// <summary>Commits these files onto a branch as a person would, making the branch (with no parent) when it is not there.</summary>
    public string Commit(string branch, IReadOnlyDictionary<string, string> files, string message = "seed")
    {
        var work = Path.Combine(Root, "work-" + Guid.NewGuid().ToString("N"));
        Git(Root, "init", "-q", "-b", branch, work);
        if (Tip(branch) is not null)
        {
            Git(work, "fetch", "-q", GitDir, "refs/heads/" + branch);
            Git(work, "reset", "-q", "--hard", "FETCH_HEAD");
        }

        foreach (var (path, content) in files)
        {
            var full = Path.Combine(work, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        Git(work, "add", "-A");
        Git(work, "commit", "-q", "--no-verify", "-m", message);
        Git(work, "push", "-q", "--no-verify", GitDir, "HEAD:refs/heads/" + branch);
        var tip = Git(work, "rev-parse", "HEAD").Trim();
        DeleteTree(work);
        return tip;
    }

    /// <summary>
    /// Every push to <paramref name="branch"/> is refused from now on, with this message, as a ruleset or a server hook
    /// refuses. Each refusal is counted in <see cref="Refusals"/>.
    /// </summary>
    public void RefusePushesTo(string branch, string message)
    {
        var hook = Path.Combine(GitDir, "hooks", "pre-receive");
        File.WriteAllText(hook,
            "#!/bin/sh\n" +
            "while read old new ref; do\n" +
            $"  if [ \"$ref\" = \"refs/heads/{branch}\" ]; then echo refused >> refusals.log; echo \"{message}\" >&2; exit 1; fi\n" +
            "done\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>How many pushes <see cref="RefusePushesTo"/>'s hook has refused.</summary>
    public int Refusals => File.Exists(Path.Combine(GitDir, "refusals.log")) ? File.ReadAllLines(Path.Combine(GitDir, "refusals.log")).Length : 0;

    /// <summary>Runs git for the fixture itself: no machine configuration, and an identity of its own.</summary>
    public static string Git(string workingDirectory, params string[] arguments)
    {
        var result = TryGit(workingDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} exited {result.ExitCode}: {result.Stderr}");
        return result.Stdout;
    }

    public static ChildProcess.Result TryGit(string workingDirectory, params string[] arguments)
    {
        var home = Path.Combine(Path.GetTempPath(), "kronikol-origin-home");
        Directory.CreateDirectory(home);
        var empty = Path.Combine(home, "empty.gitconfig");
        if (!File.Exists(empty))
            File.WriteAllText(empty, "");
        var environment = ChildProcess.BaseEnvironment(home, Path.GetTempPath(), empty);
        environment["PATH"] = string.Join(Path.PathSeparator, ChildProcess.SystemPath());
        environment["GIT_AUTHOR_NAME"] = environment["GIT_COMMITTER_NAME"] = "fixture";
        environment["GIT_AUTHOR_EMAIL"] = environment["GIT_COMMITTER_EMAIL"] = "fixture@example.com";
        return ChildProcess.Run(GitProbe.Executable!, arguments, environment, workingDirectory, timeout: TimeSpan.FromMinutes(2));
    }

    /// <summary>Deletes a tree git wrote, whose object files are read-only on Windows.</summary>
    public static void DeleteTree(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }
}
