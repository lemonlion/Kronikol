using System.Text;

namespace Kronikol.Tool;

/// <summary>
/// Implements <c>kronikol init-agents</c>: install into an existing repository what the
/// <c>dotnet new kronikol-*</c> templates ship with a new one - the <c>kronikol-test-debugging</c> skill
/// under <c>.claude/skills/</c>, and the instruction block in <c>CLAUDE.md</c> and <c>AGENTS.md</c>.
///
/// <para><b>Why it exists.</b> The report is only useful to an agent that knows not to open it, and until
/// now the only way to teach one was a paragraph of the wiki telling a human to copy three files out of
/// GitHub by hand. Most repositories predate the templates, so most repositories never got the skill.</para>
///
/// <para><b>Why the block is delimited.</b> An installer that appends is right once and wrong every time
/// after: run it again after a tool upgrade and the file grows a second, stale copy. Everything between
/// <see cref="BeginMarker"/> and <see cref="EndMarker"/> is owned by this command and replaced wholesale;
/// everything outside them is the user's and is never touched. That makes "re-run it after upgrading" a
/// safe instruction rather than a hopeful one.</para>
///
/// <para>The files are embedded from <c>templates/</c>, which is the single canonical copy - the same
/// bytes the twelve templates pack and the repo's own <c>.claude/skills/</c> mirrors. <c>SkillDriftTests</c>
/// pins all four copies to it.</para>
/// </summary>
internal static class InitAgentsCommand
{
    /// <summary>Opens the region this command owns. Anything outside it belongs to the user.</summary>
    public const string BeginMarker = "<!-- kronikol:begin -->";

    /// <summary>Closes the region this command owns.</summary>
    public const string EndMarker = "<!-- kronikol:end -->";

    /// <summary>
    /// The five strings every <c>templates/kronikol-*/.template.config/template.json</c> rewrites in every
    /// file it copies. None may appear in the shipped agent files: <c>net10.0</c> in SKILL.md would arrive
    /// in a scaffolded project as <c>net8.0</c>, silently, with nothing failing.
    /// </summary>
    public static readonly string[] TemplateSubstitutionTokens =
        ["SERVICE_NAME", "DOWNSTREAM_SERVICE", "15050", "net10.0", "KronikolComponentTests"];

    /// <summary>The skill's files, as relative paths under <c>.claude/skills/kronikol-test-debugging/</c>.</summary>
    public static readonly string[] SkillFiles =
        ["SKILL.md", "references/commands.md", "scripts/query.py"];

    private const string SkillDirectory = ".claude/skills/kronikol-test-debugging";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Throws rather than substituting U+FFFD, so a file this command cannot read is refused
    /// instead of quietly rewritten with its non-ASCII bytes destroyed.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Installs the skill and the instruction block into <paramref name="workingDirectory"/> (or the
    /// directory named on the command line). The working directory is a parameter rather than a read of
    /// <see cref="Directory.GetCurrentDirectory"/> for the same reason the environment is injected
    /// elsewhere in this tool: the tests run in one parallel xunit process and must not mutate it.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error,
        string? workingDirectory = null)
    {
        string? target = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    PrintUsage(@out);
                    return 0;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error.WriteLine($"Unknown option: {arg}");
                        return 2;
                    }
                    if (target is not null)
                    {
                        error.WriteLine("init-agents takes one directory, not several.");
                        PrintUsage(error);
                        return 2;
                    }
                    target = arg;
                    break;
            }
        }

        var root = Path.GetFullPath(target ?? workingDirectory ?? Directory.GetCurrentDirectory());

        if (!Directory.Exists(root))
        {
            // Creating it would turn a mistyped path into a tree of files in a place nobody meant.
            error.WriteLine($"No such directory: {root}");
            return 1;
        }

        // A read-only working copy, a file an editor has locked and a directory sitting where a file should
        // be are all ordinary conditions. The rest of this tool answers them with a message and an exit
        // code; a stack trace here would also leave the repository half-installed with no way to tell.
        try
        {
            foreach (var relative in SkillFiles)
            {
                var destination = Path.Combine(root, Path.Combine(SkillDirectory.Split('/')),
                    Path.Combine(relative.Split('/')));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Report(@out, root, destination, WriteIfChanged(destination, Resource("skills/kronikol-test-debugging/" + relative)));
            }

            var block = Resource("agents/CLAUDE.md");
            foreach (var name in new[] { "CLAUDE.md", "AGENTS.md" })
            {
                var destination = Path.Combine(root, name);
                var (merged, problem) = Merge(destination, block);
                if (merged is null)
                {
                    error.WriteLine($"{destination}: {problem}");
                    return 1;
                }
                Report(@out, root, destination, WriteIfChanged(destination, merged));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not write into {root}: {exception.Message}");
            error.WriteLine("Some files may already have been installed; fix the permission and run this again.");
            return 1;
        }

        @out.WriteLine();
        @out.WriteLine("Re-run this after upgrading Kronikol.Tool: the block between the markers is replaced,");
        @out.WriteLine("everything else in CLAUDE.md and AGENTS.md is left alone.");
        return 0;
    }

    private enum Outcome { Wrote, Updated, Unchanged }

    private static void Report(TextWriter @out, string root, string path, Outcome outcome)
    {
        var label = outcome switch
        {
            Outcome.Wrote => "wrote    ",
            Outcome.Updated => "updated  ",
            _ => "unchanged"
        };
        @out.WriteLine($"{label} {Path.GetRelativePath(root, path).Replace('\\', '/')}");
    }

    private static Outcome WriteIfChanged(string path, byte[] content)
    {
        var existed = File.Exists(path);
        if (existed && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            return Outcome.Unchanged;

        File.WriteAllBytes(path, content);
        return existed ? Outcome.Updated : Outcome.Wrote;
    }

    /// <summary>
    /// The bytes <paramref name="path"/> should hold: the block alone when the file is new, otherwise the
    /// existing file with the delimited region replaced - or the block appended, once, when there is no
    /// region yet. A new file keeps the shipped bytes exactly; an existing one gets the block re-punctuated
    /// in whichever line ending it already uses, so a CRLF repository does not end up mixed.
    ///
    /// <para>Null, with the reason, for the three shapes there is no safe reading of: bytes that are not
    /// UTF-8, a block opened and never closed, and two blocks in one file. Every repair for those is a
    /// guess, and a wrong guess deletes or corrupts somebody's instructions - so nothing is written.</para>
    /// </summary>
    private static (byte[]? Bytes, string? Problem) Merge(string path, byte[] block)
    {
        if (!File.Exists(path))
            return (block, null);

        var raw = File.ReadAllBytes(path);
        var bom = raw is [0xEF, 0xBB, 0xBF, ..];

        string existing;
        try
        {
            // Strict, not the replacement fallback File.ReadAllText uses. An older Windows repository's
            // CLAUDE.md may be Windows-1252, and decoding-then-re-encoding it would silently replace every
            // non-ASCII byte with U+FFFD - outside the markers, in text this command promises not to touch.
            existing = StrictUtf8.GetString(raw.AsSpan(bom ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            return (null, "is not valid UTF-8, so appending to it would corrupt the bytes that are already "
                          + "there. Re-save it as UTF-8 and run this again.");
        }

        var crlf = existing.Contains("\r\n", StringComparison.Ordinal);
        var text = Encoding.UTF8.GetString(block).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (crlf)
            text = text.Replace("\n", "\r\n", StringComparison.Ordinal);

        var newLine = crlf ? "\r\n" : "\n";
        var (start, end, count) = FindRegion(existing);

        if (count > 1)
            return (null, "contains the Kronikol block twice. Upgrading only the first would leave the "
                          + "second - possibly written by a much older tool - as the last word an agent reads. "
                          + "Delete the one you do not want, then run this again.");

        string merged;
        if (start >= 0 && end > start)
        {
            merged = existing[..start] + text.TrimEnd('\r', '\n') + existing[end..];
        }
        else if (start >= 0)
        {
            return (null, $"opens a Kronikol block ({BeginMarker}) and never closes it. Every repair for "
                          + "that is a guess, and a wrong guess deletes the rest of the file. Add a "
                          + $"{EndMarker} line where the block ends, or delete the opening line.");
        }
        else
        {
            var head = existing.TrimEnd('\r', '\n');
            merged = head.Length == 0 ? text : head + newLine + newLine + text;
        }

        if (!merged.EndsWith(newLine, StringComparison.Ordinal))
            merged += newLine;

        var bytes = Utf8NoBom.GetBytes(merged);
        return (bom ? [0xEF, 0xBB, 0xBF, .. bytes] : bytes, null);
    }

    /// <summary>
    /// The half-open character range of the managed region, or <c>(-1, -1)</c> when there is none.
    ///
    /// <para>A marker counts only on a line of its own. The wiki, the README and this command's own help
    /// all quote the marker strings, so a repository whose instructions document this feature will contain
    /// them in prose - and treating a sentence about the block as the block itself would delete whatever
    /// came after it. Only the first region is considered: replacing the least text that can be right is
    /// the safe reading when a file somehow holds two.</para>
    /// </summary>
    private static (int Start, int End, int Count) FindRegion(string text)
    {
        var start = -1;
        var end = -1;
        var count = 0;
        var offset = 0;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed == BeginMarker)
            {
                count++;
                if (start < 0) start = offset;
            }
            else if (start >= 0 && end < 0 && trimmed == EndMarker)
            {
                // Just past the marker text, not past the line: the CR of a CRLF break belongs to the
                // tail that gets kept, or the replacement would leave a bare LF in a CRLF file.
                end = offset + line.TrimEnd().Length;
            }

            offset += line.Length + 1;
        }

        return (start, end, count);
    }

    private static byte[] Resource(string logicalName)
    {
        var assembly = typeof(InitAgentsCommand).Assembly;
        using var stream = assembly.GetManifestResourceStream(logicalName)
                           ?? throw new InvalidOperationException($"Embedded resource {logicalName} not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: kronikol init-agents [<dir>]");
        w.WriteLine();
        w.WriteLine("  Teaches the agents working in a repository how to debug its test runs: installs the");
        w.WriteLine("  kronikol-test-debugging skill under .claude/skills/ and adds the instruction block to");
        w.WriteLine("  CLAUDE.md and AGENTS.md. This is what `dotnet new kronikol-*` ships with a new project;");
        w.WriteLine("  init-agents is for the repositories that came first.");
        w.WriteLine();
        w.WriteLine("Arguments:");
        w.WriteLine("  <dir>        Repository root (default: the current directory). It must already exist.");
        w.WriteLine("Options:");
        w.WriteLine("  -h, --help   Show this help.");
        w.WriteLine();
        w.WriteLine("  Safe to re-run: the block between <!-- kronikol:begin --> and <!-- kronikol:end --> is");
        w.WriteLine("  replaced in place and everything else in those files is left untouched.");
        w.WriteLine();
        w.WriteLine("Example:");
        w.WriteLine("  kronikol init-agents .");
    }
}
