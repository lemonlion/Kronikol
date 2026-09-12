using System.Text;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol init-agents</c> installs into a repository what the <c>dotnet new</c> templates ship with a
/// new one: the debugging skill, and the instruction block in <c>CLAUDE.md</c> and <c>AGENTS.md</c>.
///
/// <para>The interesting half is re-running it. An installer that appends is fine once and wrong the
/// second time - two copies of the block, or a hand edit destroyed - so the block is delimited and
/// replaced in place, and "run it again after upgrading the tool" has to be a safe instruction rather
/// than a hopeful one.</para>
/// </summary>
public class InitAgentsCommandTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-init").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string CanonicalSkillFile(params string[] parts) =>
        Path.Combine([RepoRoot, "templates", "skills", "kronikol-test-debugging", .. parts]);

    private static string CanonicalBlockFile => Path.Combine(RepoRoot, "templates", "agents", "CLAUDE.md");

    private (string Output, string Error, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = InitAgentsCommand.Run(args, output, error, _directory);
        return (output.ToString(), error.ToString(), exit);
    }

    private string Installed(params string[] parts) =>
        Path.Combine([_directory, ".claude", "skills", "kronikol-test-debugging", .. parts]);

    // ─── What it installs ──────────────────────────────────────

    [Fact]
    public void It_installs_the_skill_and_both_instruction_files()
    {
        var (output, error, exit) = Run();

        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Installed("SKILL.md")));
        Assert.True(File.Exists(Installed("references", "commands.md")));
        Assert.True(File.Exists(Installed("scripts", "query.py")));
        Assert.True(File.Exists(Path.Combine(_directory, "CLAUDE.md")));
        Assert.True(File.Exists(Path.Combine(_directory, "AGENTS.md")));
        Assert.Contains("SKILL.md", output);
    }

    [Fact]
    public void The_installed_skill_is_byte_identical_to_the_one_the_templates_ship()
    {
        Run();

        Assert.Equal(File.ReadAllBytes(CanonicalSkillFile("SKILL.md")), File.ReadAllBytes(Installed("SKILL.md")));
        Assert.Equal(File.ReadAllBytes(CanonicalSkillFile("references", "commands.md")),
            File.ReadAllBytes(Installed("references", "commands.md")));
        Assert.Equal(File.ReadAllBytes(CanonicalSkillFile("scripts", "query.py")),
            File.ReadAllBytes(Installed("scripts", "query.py")));
    }

    [Fact]
    public void A_fresh_instruction_file_is_the_block_the_templates_ship_and_nothing_else()
    {
        Run();

        Assert.Equal(File.ReadAllBytes(CanonicalBlockFile), File.ReadAllBytes(Path.Combine(_directory, "CLAUDE.md")));
    }

    [Fact]
    public void Both_instruction_files_get_the_same_bytes()
    {
        Run();

        // Claude Code reads one and Codex, Cursor and Copilot read the other. Writing one and hoping is
        // how the discovery loop quietly fails for half its audience.
        Assert.Equal(File.ReadAllBytes(Path.Combine(_directory, "CLAUDE.md")),
            File.ReadAllBytes(Path.Combine(_directory, "AGENTS.md")));
    }

    // ─── Re-running it ─────────────────────────────────────────

    [Fact]
    public void Running_it_twice_leaves_the_files_byte_identical()
    {
        Run();
        var first = File.ReadAllBytes(Path.Combine(_directory, "CLAUDE.md"));

        var (output, _, exit) = Run();

        Assert.Equal(0, exit);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(_directory, "CLAUDE.md")));
        Assert.Contains("unchanged", output);
    }

    [Fact]
    public void It_appends_to_an_existing_file_without_disturbing_what_is_there()
    {
        var existing = "# My repo\n\n## Build\n\nRun `make`.\n";
        File.WriteAllText(Path.Combine(_directory, "CLAUDE.md"), existing);

        Run();

        var updated = File.ReadAllText(Path.Combine(_directory, "CLAUDE.md"));
        Assert.StartsWith(existing, updated, StringComparison.Ordinal);
        Assert.Contains(InitAgentsCommand.BeginMarker, updated, StringComparison.Ordinal);
        Assert.Contains("Never open", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void It_replaces_the_block_in_place_rather_than_adding_a_second_one()
    {
        File.WriteAllText(Path.Combine(_directory, "CLAUDE.md"),
            "# My repo\n\n"
            + InitAgentsCommand.BeginMarker + "\nsomething an older tool wrote\n" + InitAgentsCommand.EndMarker
            + "\n\n## Build\n\nRun `make`.\n");

        var (output, _, _) = Run();

        var updated = File.ReadAllText(Path.Combine(_directory, "CLAUDE.md"));
        Assert.Equal(1, CountOf(updated, InitAgentsCommand.BeginMarker));
        Assert.DoesNotContain("something an older tool wrote", updated, StringComparison.Ordinal);
        Assert.Contains("# My repo", updated, StringComparison.Ordinal);
        Assert.Contains("Run `make`.", updated, StringComparison.Ordinal);
        Assert.Contains("updated", output);
    }

    [Fact]
    public void A_marker_mentioned_in_prose_is_not_mistaken_for_the_block()
    {
        // The wiki, the README and this command's own --help all quote the marker strings, so a repository
        // whose CLAUDE.md documents the feature will contain them too. Only a line that IS the marker opens
        // the managed region; an inline mention is text like any other.
        File.WriteAllText(Path.Combine(_directory, "CLAUDE.md"),
            "# My repo\n\nWe install the Kronikol block with `kronikol init-agents`; it goes between\n"
            + InitAgentsCommand.BeginMarker + " and " + InitAgentsCommand.EndMarker + " so it can be replaced.\n");

        var (output, _, exit) = Run();

        Assert.Equal(0, exit);
        var updated = File.ReadAllText(Path.Combine(_directory, "CLAUDE.md"));
        Assert.Contains("so it can be replaced.", updated, StringComparison.Ordinal);
        Assert.Contains("Never open", updated, StringComparison.Ordinal);
        Assert.Contains("wrote", output);
    }

    [Fact]
    public void An_unclosed_block_is_refused_rather_than_guessed_at()
    {
        // A begin with no end could mean anything, and every guess risks deleting the rest of the file.
        // Saying so and changing nothing is the only safe answer.
        var original = "# My repo\n\n" + InitAgentsCommand.BeginMarker + "\nhalf a block\n";
        File.WriteAllText(Path.Combine(_directory, "CLAUDE.md"), original);

        var (_, error, exit) = Run();

        Assert.Equal(1, exit);
        Assert.Contains("CLAUDE.md", error, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(Path.Combine(_directory, "CLAUDE.md")));
    }

    [Fact]
    public void A_block_that_is_not_the_first_thing_in_the_file_is_still_replaced_in_place()
    {
        File.WriteAllText(Path.Combine(_directory, "CLAUDE.md"),
            "# My repo\n\n## Build\n\nRun `make`.\n\n"
            + InitAgentsCommand.BeginMarker + "\nwhat-an-older-tool-wrote\n" + InitAgentsCommand.EndMarker
            + "\n\n## Deploy\n\nRun `ship`.\n");

        Run();

        var updated = File.ReadAllText(Path.Combine(_directory, "CLAUDE.md"));
        Assert.Contains("Run `make`.", updated, StringComparison.Ordinal);
        Assert.Contains("Run `ship`.", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("what-an-older-tool-wrote", updated, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(updated, InitAgentsCommand.BeginMarker));
    }

    [Fact]
    public void A_file_that_does_not_end_in_a_newline_still_gets_a_separated_block()
    {
        File.WriteAllText(Path.Combine(_directory, "CLAUDE.md"), "# My repo");

        Run();

        var updated = File.ReadAllText(Path.Combine(_directory, "CLAUDE.md"));
        Assert.Contains("# My repo\n\n" + InitAgentsCommand.BeginMarker, updated, StringComparison.Ordinal);
    }

    [Fact]
    public void It_writes_the_block_in_the_line_endings_the_file_already_uses()
    {
        File.WriteAllText(Path.Combine(_directory, "CLAUDE.md"), "# My repo\r\n\r\n## Build\r\n");

        Run();

        var updated = File.ReadAllText(Path.Combine(_directory, "CLAUDE.md"));
        Assert.DoesNotContain(updated.Replace("\r\n", ""), "\n");
        Assert.Contains(InitAgentsCommand.BeginMarker + "\r\n", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_run_over_a_CRLF_file_is_still_a_no_op()
    {
        File.WriteAllText(Path.Combine(_directory, "CLAUDE.md"), "# My repo\r\n");
        Run();
        var first = File.ReadAllBytes(Path.Combine(_directory, "CLAUDE.md"));

        var (output, _, _) = Run();

        Assert.Equal(first, File.ReadAllBytes(Path.Combine(_directory, "CLAUDE.md")));
        Assert.Contains("unchanged", output);
    }

    [Fact]
    public void A_skill_file_edited_by_hand_is_restored()
    {
        Run();
        File.WriteAllText(Installed("SKILL.md"), "I deleted the useful part");

        var (output, _, _) = Run();

        Assert.Equal(File.ReadAllBytes(CanonicalSkillFile("SKILL.md")), File.ReadAllBytes(Installed("SKILL.md")));
        Assert.Contains("updated", output);
    }

    [Fact]
    public void A_file_whose_bytes_are_not_UTF8_is_refused_rather_than_mangled()
    {
        // "Repositories that came first" is the whole audience, and an older Windows repo's CLAUDE.md may
        // be Windows-1252. Decoding it as UTF-8 turns every non-ASCII byte into U+FFFD, and re-encoding
        // writes the damage back - outside the markers, which this command promises never to touch.
        var path = Path.Combine(_directory, "CLAUDE.md");
        var original = new byte[] { 0x44, 0x6F, 0x6E, 0x92, 0x74, 0x2E, 0x20, 0x43, 0x61, 0x66, 0xE9, 0x2E, 0x0A };
        File.WriteAllBytes(path, original);

        var (_, error, exit) = Run();

        Assert.Equal(1, exit);
        Assert.Contains("UTF-8", error, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_byte_order_mark_the_file_already_had_is_kept()
    {
        var path = Path.Combine(_directory, "CLAUDE.md");
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("# My repo\n")).ToArray());

        Run();

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..], "the BOM was stripped from a file this command only appends to");
        Assert.Contains("Never open", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_holding_two_blocks_is_refused_rather_than_half_upgraded()
    {
        // Replacing only the first leaves the second - possibly written by a much older tool - as the last
        // word an agent reads, with the run reporting success.
        var block = InitAgentsCommand.BeginMarker + "\none\n" + InitAgentsCommand.EndMarker;
        var original = "# My repo\n\n" + block + "\n\n## Build\n\n" + block + "\n";
        File.WriteAllText(Path.Combine(_directory, "CLAUDE.md"), original);

        var (_, error, exit) = Run();

        Assert.Equal(1, exit);
        Assert.Contains("twice", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(Path.Combine(_directory, "CLAUDE.md")));
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_reported_rather_than_thrown()
    {
        // A read-only working copy and a file locked by an editor are ordinary conditions, and the rest of
        // this tool answers them with a message and an exit code rather than a stack trace.
        var path = Path.Combine(_directory, "CLAUDE.md");
        File.WriteAllText(path, "# My repo\n");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var (_, error, exit) = Run();

            Assert.Equal(1, exit);
            Assert.Contains("CLAUDE.md", error, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    // ─── Arguments ─────────────────────────────────────────────

    [Fact]
    public void A_named_directory_is_used_instead_of_the_working_directory()
    {
        var elsewhere = Path.Combine(_directory, "nested", "repo");
        Directory.CreateDirectory(elsewhere);

        var (_, error, exit) = Run(elsewhere);

        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Path.Combine(elsewhere, "CLAUDE.md")));
        Assert.False(File.Exists(Path.Combine(_directory, "CLAUDE.md")));
    }

    [Fact]
    public void A_directory_that_is_not_there_is_an_error_rather_than_a_new_tree()
    {
        var missing = Path.Combine(_directory, "no-such-repo");

        var (_, error, exit) = Run(missing);

        Assert.Equal(1, exit);
        Assert.Contains("no-such-repo", error);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Help_prints_the_usage_and_exits_zero()
    {
        var (output, _, exit) = Run("--help");

        Assert.Equal(0, exit);
        Assert.Contains("init-agents", output);
        Assert.False(File.Exists(Path.Combine(_directory, "CLAUDE.md")));
    }

    [Fact]
    public void An_unknown_option_is_a_usage_error()
    {
        var (_, error, exit) = Run("--recursive");

        Assert.Equal(2, exit);
        Assert.Contains("--recursive", error);
    }

    [Fact]
    public void Two_directories_is_a_usage_error()
    {
        var (_, error, exit) = Run(_directory, _directory);

        Assert.Equal(2, exit);
        Assert.Contains("one directory", error, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Nothing about the report leaks into an instruction file ───

    [Fact]
    public void The_block_never_carries_a_template_substitution_token()
    {
        // The dotnet new templates ship this same file, and the template engine rewrites SERVICE_NAME,
        // DOWNSTREAM_SERVICE, 15050, net10.0 and KronikolComponentTests in every source file it copies.
        // A token here would silently become something else in a scaffolded project.
        var block = File.ReadAllText(CanonicalBlockFile);

        foreach (var token in InitAgentsCommand.TemplateSubstitutionTokens)
            Assert.DoesNotContain(token, block, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void The_installed_files_are_written_as_UTF8_without_a_byte_order_mark()
    {
        Run();

        var bytes = File.ReadAllBytes(Path.Combine(_directory, "CLAUDE.md"));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "a BOM in CLAUDE.md would show up as a stray character in every agent that reads it");
        Assert.Contains("—", Encoding.UTF8.GetString(bytes));
    }
}
