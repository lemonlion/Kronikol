using System.Diagnostics;
using System.Text;

namespace Kronikol.Tests.Tool;

/// <summary>
/// What the tool prints has to survive being piped. Every other tool test calls
/// <c>QueryCommand.Run(args, new StringWriter(), …)</c>, and a <see cref="StringWriter"/> has no encoding
/// at all — it is UTF-16 in memory — so the one thing that can go wrong here is invisible to all of them.
///
/// <para>On Windows <c>Console.Out</c> encodes through <c>Console.OutputEncoding</c>, which comes from the
/// console's code page — 437 or 1252 on a default machine, neither of which has <c>·</c>, <c>›</c>,
/// <c>✗</c>, <c>…</c> or <c>—</c>. Those are not decoration: <c>·</c> separates the fields of a footer,
/// <c>›</c> joins a feature to its scenario, and <c>✗</c> marks the failing step. Encoded through a
/// code page that lacks them they become <c>?</c>, so the addresses the tool tells a reader to feed back
/// to it arrive mangled, and the run-end pointer says to pipe this output to an agent.</para>
///
/// <para>Measured rather than reasoned about: with the console code page forced to 437, the tool without
/// the fix wrote 0xFA for <c>·</c> — a byte that is not valid UTF-8 at all, so a consumer decoding the pipe
/// does not get a wrong character, it gets an exception. With the fix it wrote C2 B7 under the same code
/// page. This machine and most CI runners report 65001, which is exactly why it went unnoticed: the test
/// below is decisive on a host that does not, and on one that does it still pins that the bytes are valid
/// UTF-8, carry no BOM, and were not degraded to <c>?</c>.</para>
///
/// <para>The budget makes it worse rather than cosmetic. <c>QueryWriter</c> charges every line against
/// <c>--max-bytes</c> using <c>Encoding.UTF8.GetByteCount</c>, so a byte budget is being enforced in an
/// encoding the bytes are not in.</para>
/// </summary>
public class ToolStdoutEncodingTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>
    /// The built tool, not a project reference: the defect lives in the process's own stdout stream, which
    /// only exists when a process owns it.
    /// </summary>
    private static string ToolDll()
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

    [Fact]
    public void Tool_stdout_round_trips_through_strict_UTF8()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Read the raw bytes, not .NET's decode of them: the question is what reached the pipe.
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
        };
        start.ArgumentList.Add(ToolDll());
        start.ArgumentList.Add("query");
        start.ArgumentList.Add("--help");

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "the tool did not exit");
        Assert.True(process.ExitCode is 0 or 2, $"exit {process.ExitCode}\n{stderr}");

        // A BOM would be the other way to get this wrong: valid UTF-8 that every consumer of a text
        // stream then has to strip, and that a byte-budget check counts three times over.
        Assert.False(stdout.StartsWith('﻿'), "stdout begins with a BOM");

        // Non-vacuity first: a help text with no non-ASCII in it would pass any encoding check going.
        Assert.Contains(stdout, c => c > 127);

        // U+FFFD is what a lossy decode leaves behind; '?' is what a lossy ENCODE leaves behind, and it is
        // the one that actually happens here, so both are checked.
        Assert.DoesNotContain('�', stdout);
        foreach (var glyph in new[] { '·', '—', '…' })
            Assert.True(!stdout.Contains(glyph + "?", StringComparison.Ordinal), $"{glyph} survived but its neighbour did not");

        // The decisive one: every character the tool meant to print is still the character it meant.
        Assert.Contains('·', stdout);
    }
}
