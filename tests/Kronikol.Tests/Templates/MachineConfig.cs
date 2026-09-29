namespace Kronikol.Tests.Templates;

/// <summary>
/// A machine's own git configuration, which <see cref="RunnerJob.GitConfigGlobal"/> names for a job's steps: what a
/// self-hosted runner can hold, or the Windows image does (Git for Windows' installer defaults: <c>core.autocrlf=true</c>
/// and Git Credential Manager). The history action's scripts keep the machine's configuration and set five things of
/// their own over it (plans/HISTORY_ACTION_PLAN.md §4.9); these are the settings they must win against.
/// </summary>
internal sealed class MachineConfig
{
    private readonly string _directory;

    public MachineConfig(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, "machine.gitconfig");
        File.WriteAllText(Path, "");
    }

    /// <summary>The configuration file, for <c>GIT_CONFIG_GLOBAL</c>.</summary>
    public string Path { get; }

    /// <summary>The credential store, once <see cref="StoreCredential"/> made one.</summary>
    public string StoreFile => System.IO.Path.Combine(_directory, "credentials");

    /// <summary>The file a helper that waits for a person writes to when it is asked.</summary>
    public string AskedFile => System.IO.Path.Combine(_directory, "helper-asked");

    /// <summary>A credential helper holding another identity for the server, as a person's machine or a runner's can.</summary>
    public MachineConfig StoreCredential(string serverUrl, string user, string password)
    {
        var uri = new Uri(serverUrl);
        File.WriteAllText(StoreFile, $"{uri.Scheme}://{user}:{password}@{uri.Authority}\n");
        return Set("credential.helper", $"store --file={ChildProcess.Slashes(StoreFile)}");
    }

    /// <summary>A credential helper that waits for a person, as a credential manager's window does. It writes <see cref="AskedFile"/> when asked.</summary>
    public MachineConfig HelperThatWaits(int seconds = 8) =>
        Set("credential.helper", $"!f() {{ echo asked >> \"{ChildProcess.Slashes(AskedFile)}\"; sleep {seconds}; echo username=someone; echo password=wrong; }}; f");

    /// <summary>A hooks directory whose <c>pre-commit</c> and <c>pre-push</c> refuse everything, as a policy meant for people's commits does.</summary>
    public MachineConfig RefusingHooks()
    {
        var hooks = System.IO.Path.Combine(_directory, "hooks");
        Directory.CreateDirectory(hooks);
        foreach (var hook in new[] { "pre-commit", "pre-push" })
        {
            var path = System.IO.Path.Combine(hooks, hook);
            File.WriteAllText(path, "#!/bin/sh\necho \"policy: every commit needs a ticket number\" >&2\nexit 1\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return Set("core.hooksPath", ChildProcess.Slashes(hooks));
    }

    /// <summary>Every commit signed, with a signing program that has no key it can use.</summary>
    public MachineConfig SignsWithoutAKey()
    {
        var program = System.IO.Path.Combine(_directory, "gpg-without-a-key");
        File.WriteAllText(program, "#!/bin/sh\necho \"gpg: signing failed: No secret key\" >&2\nexit 2\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Set("gpg.program", ChildProcess.Slashes(program));
        return Set("commit.gpgsign", "true");
    }

    /// <summary>Line endings converted on checkout and commit, as Git for Windows' installer sets by default.</summary>
    public MachineConfig ConvertsLineEndings() => Set("core.autocrlf", "true");

    /// <summary>A header the machine sends to a server, as a self-hosted runner's global configuration can hold.</summary>
    public MachineConfig Header(string serverUrl, string header) => Set($"http.{serverUrl.TrimEnd('/')}/.extraheader", header);

    public MachineConfig Set(string key, string value)
    {
        BareOrigin.Git(_directory, "config", "--file", Path, "--add", key, value);
        return this;
    }
}
