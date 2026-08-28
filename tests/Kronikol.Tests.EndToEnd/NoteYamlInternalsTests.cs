namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Unit-style coverage of the pure JS functions behind the note JSON ⇄ YAML
/// format toggle (reconstructor, token walk, YAML emitter, conservative
/// re-escape), exposed on <c>window._noteFormatInternals</c> and driven via
/// <c>Page.EvaluateAsync</c> against a generated report page.
/// See NOTE_YAML_TOGGLE_PLAN.md.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteYamlInternalsTests : DiagramNotePlaywrightBase
{
    public NoteYamlInternalsTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateToReport([System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        await Page.GotoAsync(GenerateReport($"YamlInternals_{testName}.html"));
        await Page.WaitForFunctionAsync("() => !!window._noteFormatInternals",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    private Task<string?> Reconstruct(string[] noteLines) =>
        Page.EvaluateAsync<string?>(
            "lines => window._noteFormatInternals.reconstructNoteJson(lines)", noteLines);

    private Task<string[]> EmitYaml(string jsonText) =>
        Page.EvaluateAsync<string[]>(
            "src => window._noteFormatInternals.jsonTextToYamlLines(src).map(l => l.t)", jsonText);

    private Task<bool[]> EmitYamlBlockFlags(string jsonText) =>
        Page.EvaluateAsync<bool[]>(
            "src => window._noteFormatInternals.jsonTextToYamlLines(src).map(l => !!l.block)", jsonText);

    private Task<string[]> EscapeLines(object[] lines) =>
        Page.EvaluateAsync<string[]>(
            "lines => window._noteFormatInternals.escapeYamlLinesForNote(lines)", lines);

    // ═══════════════════════════════════════════════════════════
    // Reconstructor — note lines → original JSON text
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Gold_vector_reconstructs_with_newline_escapes_and_int64_verbatim()
    {
        await NavigateToReport();
        var result = await Reconstruct(new[]
        {
            "<color:gray>[content-type=application/json]</color>",
            "",
            "{",
            "  \"id\": 9007199254740993,",
            "  \"query\": \"SELECT o.id,\\\\n       o.total\\\\nFROM orders o\"",
            "}"
        });
        Assert.NotNull(result);
        Assert.Contains("9007199254740993", result);
        Assert.Contains("\"SELECT o.id,\\n       o.total\\nFROM orders o\"", result);
        Assert.DoesNotContain("<color:gray>", result);
    }

    [Fact]
    public async Task Creole_escaped_double_slashes_are_restored()
    {
        await NavigateToReport();
        var result = await Reconstruct(new[]
        {
            "{",
            "  \"u\": \"https:~/~/a.com https:~/~/b.com\"",
            "}"
        });
        Assert.NotNull(result);
        Assert.Contains("https://a.com https://b.com", result);
    }

    [Fact]
    public async Task Creole_escaped_pair_markers_are_restored()
    {
        await NavigateToReport();
        var result = await Reconstruct(new[]
        {
            "{",
            "  \"md\": \"~*~*bold~*~* text\",",
            "  \"sql\": \"~-~- comment ~-~- more\",",
            "  \"link\": \"~[~[page]] end\"",
            "}"
        });
        Assert.NotNull(result);
        Assert.Contains("**bold** text", result);
        Assert.Contains("-- comment -- more", result);
        Assert.Contains("[[page]] end", result);
    }

    [Fact]
    public async Task Creole_escaped_tag_starts_are_restored()
    {
        await NavigateToReport();
        var result = await Reconstruct(new[]
        {
            "{",
            "  \"html\": \"~<b>x~</b>\"",
            "}"
        });
        Assert.NotNull(result);
        Assert.Contains("\"<b>x</b>\"", result);
    }

    [Fact]
    public async Task Wrap_broken_long_string_is_rejoined()
    {
        await NavigateToReport();
        var partA = new string('A', 100);
        var partB = new string('B', 100);
        var result = await Reconstruct(new[]
        {
            "{",
            $"  \"blob\": \"{partA}",
            $"{partB}\"",
            "}"
        });
        Assert.NotNull(result);
        Assert.Contains(partA + partB, result);
    }

    [Fact]
    public async Task Focus_emphasis_markup_is_stripped()
    {
        await NavigateToReport();
        var result = await Reconstruct(new[]
        {
            "{",
            "  <b>\"name\": \"focus\"</b>,",
            "  \"other\": <color:gray>\"dim\"</color>",
            "}"
        });
        Assert.NotNull(result);
        Assert.Contains("\"name\": \"focus\"", result);
        Assert.DoesNotContain("<b>", result);
        Assert.DoesNotContain("<color:gray>", result);
    }

    [Fact]
    public async Task Gate_rejects_truncated_graphql_plaintext_binary_and_continuation_bodies()
    {
        await NavigateToReport();
        Assert.Null(await Reconstruct(new[] { "{", "  \"a\": 1,", "…truncated (500 chars total)" }));
        Assert.Null(await Reconstruct(new[] { "query GetUser {", "  user { id }", "}" }));
        Assert.Null(await Reconstruct(new[] { "plain text response body", "not json at all" }));
        Assert.Null(await Reconstruct(new[] { "<i>[binary content]</i>" }));
        Assert.Null(await Reconstruct(new[] { "..Continued From Previous Diagram..", "\"partial\": true}" }));
        Assert.Null(await Reconstruct(new[] { "{", "\"a\": 1,", "..Continued On Next Diagram.." }));
        Assert.Null(await Reconstruct(new[] { "<color:gray>[k=v]</color>" }));
    }

    // ═══════════════════════════════════════════════════════════
    // YAML emitter — token-level, byte-faithful
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Gold_vector_emits_block_scalar_sql()
    {
        await NavigateToReport();
        var yaml = await EmitYaml(
            "{\n  \"id\": 9007199254740993,\n  \"query\": \"SELECT o.id,\\n       o.total\\nFROM orders o\"\n}");
        Assert.Equal(new[]
        {
            "id: 9007199254740993",
            "query: |-",
            "  SELECT o.id,",
            "         o.total",
            "  FROM orders o"
        }, yaml);
    }

    [Fact]
    public async Task Block_scalar_content_lines_are_flagged_as_block()
    {
        await NavigateToReport();
        var flags = await EmitYamlBlockFlags("{\"q\": \"a\\nb\"}");
        Assert.Equal(new[] { false, true, true }, flags);
    }

    [Fact]
    public async Task Nested_objects_and_arrays_emit_mappings_and_sequences()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("{\"a\":{\"b\":1,\"c\":[1,\"x\",true,null]},\"d\":[],\"e\":{}}");
        Assert.Equal(new[]
        {
            "a:",
            "  b: 1",
            "  c:",
            "    - 1",
            "    - x",
            "    - true",
            "    - null",
            "d: []",
            "e: {}"
        }, yaml);
    }

    [Fact]
    public async Task Array_of_objects_merges_first_key_onto_dash()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("[{\"a\":1,\"b\":2},{\"a\":3}]");
        Assert.Equal(new[]
        {
            "- a: 1",
            "  b: 2",
            "- a: 3"
        }, yaml);
    }

    [Fact]
    public async Task String_ending_with_newline_uses_keep_clip_header()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("{\"s\":\"line1\\nline2\\n\"}");
        Assert.Equal(new[] { "s: |", "  line1", "  line2" }, yaml);
    }

    [Fact]
    public async Task String_with_leading_space_uses_explicit_indentation_indicator()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("{\"s\":\" lead\\nnext\"}");
        Assert.Equal(new[] { "s: |2-", "   lead", "  next" }, yaml);
    }

    [Fact]
    public async Task Single_line_strings_are_quoted_only_when_yaml_requires_it()
    {
        await NavigateToReport();
        var yaml = await EmitYaml(
            "{\"plain\":\"hello world\",\"empty\":\"\",\"boolish\":\"true\",\"numish\":\"123\"," +
            "\"dateish\":\"2026-01-01\",\"lead\":\" x\",\"trail\":\"x \",\"colon\":\"a: b\"," +
            "\"hash\":\"#tag\",\"yes\":\"yes\"}");
        Assert.Equal(new[]
        {
            "plain: hello world",
            "empty: \"\"",
            "boolish: \"true\"",
            "numish: \"123\"",
            "dateish: \"2026-01-01\"",
            "lead: \" x\"",
            "trail: \"x \"",
            "colon: \"a: b\"",
            "hash: \"#tag\"",
            // a bare `yes` KEY would parse as a YAML 1.1 boolean — quoted
            "\"yes\": \"yes\""
        }, yaml);
    }

    [Fact]
    public async Task Keys_are_quoted_and_escaped_when_needed()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("{\"\":1,\"a b\":2,\"a:b\":3,\"a\\nb\":4}");
        Assert.Equal(new[]
        {
            "\"\": 1",
            "a b: 2",
            "\"a:b\": 3",
            "\"a\\nb\": 4"
        }, yaml);
    }

    [Fact]
    public async Task Unicode_escapes_decode_to_their_characters()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("{\"a\":\"caf\\u00e9\"}");
        Assert.Equal(new[] { "a: café" }, yaml);
    }

    [Fact]
    public async Task Numbers_are_emitted_verbatim_from_the_json_text()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("{\"big\":9007199254740993,\"trailing\":1.10,\"exp\":1e5}");
        Assert.Equal(new[]
        {
            "big: 9007199254740993",
            "trailing: 1.10",
            "exp: 1e5"
        }, yaml);
    }

    [Fact]
    public async Task Duplicate_keys_are_preserved()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("{\"a\":1,\"a\":2}");
        Assert.Equal(new[] { "a: 1", "a: 2" }, yaml);
    }

    [Fact]
    public async Task Integer_like_key_order_is_preserved()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("{\"2\":\"b\",\"1\":\"a\"}");
        Assert.Equal(new[] { "\"2\": b", "\"1\": a" }, yaml);
    }

    [Fact]
    public async Task Strings_a_block_scalar_cannot_represent_take_the_quoted_fallback()
    {
        await NavigateToReport();
        var longRun = new string('a', 130);
        var yaml = await EmitYaml(
            "{\"cr\":\"a\\rb\",\"ctl\":\"a\\u0001b\",\"trailws\":\"a \\nb\",\"run\":\"" + longRun + "\\nx\"}");
        Assert.Equal(new[]
        {
            "cr: \"a\\rb\"",
            "ctl: \"a\\x01b\"",
            "trailws: \"a \\nb\"",
            "run: \"" + longRun + "\\nx\""
        }, yaml);
    }

    // ═══════════════════════════════════════════════════════════
    // CRLF line breaks — Windows-captured payloads
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Uniform_crlf_string_emits_block_scalar()
    {
        await NavigateToReport();
        var yaml = await EmitYaml(
            "{\"query\":\"SELECT o.id,\\r\\n       o.total\\r\\nFROM orders o\"}");
        Assert.Equal(new[]
        {
            "query: |-",
            "  SELECT o.id,",
            "         o.total",
            "  FROM orders o"
        }, yaml);
    }

    [Fact]
    public async Task Uniform_crlf_string_ending_with_crlf_uses_keep_clip_header()
    {
        await NavigateToReport();
        var yaml = await EmitYaml("{\"s\":\"line1\\r\\nline2\\r\\n\"}");
        Assert.Equal(new[] { "s: |", "  line1", "  line2" }, yaml);
    }

    [Fact]
    public async Task Crlf_string_that_takes_the_quoted_fallback_keeps_its_cr_bytes()
    {
        await NavigateToReport();
        // Trailing whitespace before the break still forces the fallback —
        // and the quoted form must show the original \r\n bytes, not the
        // display-normalised ones.
        var yaml = await EmitYaml("{\"t\":\"a \\r\\nb\"}");
        Assert.Equal(new[] { "t: \"a \\r\\nb\"" }, yaml);
    }

    [Fact]
    public async Task Mixed_cr_lf_line_breaks_take_the_quoted_fallback()
    {
        await NavigateToReport();
        // A lone \r or a bare \n alongside \r\n means the breaks are not
        // uniform CRLF — a block scalar cannot show which was which, so the
        // exact quoted form is kept.
        var yaml = await EmitYaml("{\"m\":\"a\\r\\nb\\nc\",\"r\":\"a\\rb\\r\\nc\"}");
        Assert.Equal(new[]
        {
            "m: \"a\\r\\nb\\nc\"",
            "r: \"a\\rb\\r\\nc\""
        }, yaml);
    }

    // ═══════════════════════════════════════════════════════════
    // Conservative re-escape for splicing into the render source
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Escape_neutralises_creole_pair_markers_unconditionally()
    {
        await NavigateToReport();
        var escaped = await EscapeLines(new object[]
        {
            new { t = "url: https://x.com", block = false }
        });
        Assert.Equal(new[] { "url: https:~/~/x.com" }, escaped);
    }

    [Fact]
    public async Task Escape_doubles_backslashes()
    {
        await NavigateToReport();
        var escaped = await EscapeLines(new object[]
        {
            new { t = "a: \"x\\ny\"", block = false }
        });
        Assert.Equal(new[] { "a: \"x\\\\ny\"" }, escaped);
    }

    [Fact]
    public async Task Escape_protects_leading_bullets_and_tag_starts()
    {
        await NavigateToReport();
        var escaped = await EscapeLines(new object[]
        {
            new { t = "* item", block = true },
            new { t = "a: <div>", block = false }
        });
        Assert.Equal(new[] { "~* item", "a: ~<div>" }, escaped);
    }

    [Fact]
    public async Task Escape_wraps_long_runs_on_normal_lines_but_never_in_block_scalar_content()
    {
        await NavigateToReport();
        var longRun = new string('a', 130);
        var escaped = await EscapeLines(new object[]
        {
            new { t = longRun, block = false },
            new { t = longRun, block = true }
        });
        // Non-block line is wrapped into two physical lines; block content is not.
        Assert.Equal(3, escaped.Length);
        Assert.Equal(120, escaped[0].Length);
        Assert.Equal(10, escaped[1].Length);
        Assert.Equal(longRun, escaped[2]);
    }
}
