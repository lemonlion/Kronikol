using System.Diagnostics;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The built <c>kronikol</c> tool and the <c>dotnet</c> host, for the tests whose subject only exists when a
/// process owns it: what reaches a pipe, and what <c>dotnet run --file query.cs</c> prints.
/// </summary>
internal static class BuiltTool
{
    internal static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>The newest runnable <c>Kronikol.Tool.dll</c> under the tool's own <c>bin</c>: the built tool, not a project reference.</summary>
    internal static string Dll()
    {
        var bin = Path.Combine(RepoRoot, "src", "Kronikol.Tool", "bin");
        Assert.True(Directory.Exists(bin), $"the tool has not been built — no {bin}");

        var candidates = Directory.EnumerateFiles(bin, "Kronikol.Tool.dll", SearchOption.AllDirectories)
            .Where(f => File.Exists(Path.ChangeExtension(f, ".runtimeconfig.json")))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        Assert.True(candidates.Length > 0, $"no runnable Kronikol.Tool.dll under {bin}");
        return candidates[0];
    }

    /// <summary>What one process did: its exit code, the exact bytes it wrote to stdout, and its stderr.</summary>
    internal sealed record Outcome(int ExitCode, byte[] Stdout, string Stderr);

    /// <summary>
    /// Runs <c>dotnet</c> with <paramref name="arguments"/> in <paramref name="workingDirectory"/>. The
    /// environment is inherited, then <paramref name="environment"/> is applied on top; a null value removes
    /// a variable.
    /// </summary>
    internal static Outcome Dotnet(string workingDirectory, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null, int timeoutMilliseconds = 300_000)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        // A first-run banner or a telemetry notice is the host talking, not the command.
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
                start.Environment.Remove(name);
            else
                start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        using var stdout = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new TimeoutException($"dotnet {string.Join(' ', start.ArgumentList)} did not finish in {timeoutMilliseconds} ms");
        }

        copy.Wait();
        return new Outcome(process.ExitCode, stdout.ToArray(), stderr.Result);
    }

    private static readonly Lazy<bool> Sdk10 = new(() =>
    {
        try
        {
            var outcome = Dotnet(AppContext.BaseDirectory, ["--list-sdks"], timeoutMilliseconds: 60_000);
            return System.Text.Encoding.UTF8.GetString(outcome.Stdout)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(line => int.TryParse(line.Split('.')[0], out var major) && major >= 10);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or TimeoutException)
        {
            return false;
        }
    });

    /// <summary>Whether a .NET 10 or later SDK is on this machine: <c>dotnet run --file</c> needs one.</summary>
    internal static bool HasSdk10 => Sdk10.Value;
}
