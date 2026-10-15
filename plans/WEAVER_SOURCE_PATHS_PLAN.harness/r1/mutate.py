# WEAVER_SOURCE_PATHS_PLAN section 5.5: each mutant is applied in a worktree of its own (MUT), the test project rebuilt
# for net10.0, the facts named for it run, and the files restored from the release tree (SRC). A mutant is caught when
# at least one of its facts fails; one that does not apply or does not build is reported, never counted as caught.
import json, os, re, shutil, subprocess, sys, time

SRC = r"C:\Code\Kronikol-165"
MUT = r"C:\Code\Kronikol-165-mut"
BS = chr(92)
NL = chr(10)

WEAVER = r"src\Kronikol.AssertionTracking"
SD = WEAVER + r"\SourceDocuments.cs"
AW = WEAVER + r"\AssertionWeaver.cs"
TASK = WEAVER + r"\WeaveAssertionsTask.cs"
TARGETS = WEAVER + r"\build\Kronikol.AssertionTracking.targets"
EDP = r"src\Kronikol\Reports\ErrorDiffParser.cs"
FT = r"src\Kronikol\Reports\FailureText.cs"

WT = r"tests\Kronikol.Tests.AssertionTracking\Kronikol.Tests.AssertionTracking.csproj"
CORE = r"tests\Kronikol.Tests\Kronikol.Tests.csproj"

def r(*parts):
    return "".join(parts)

MUTANTS = [
    ("M1", "ignore PathMap", [(TASK, "var map = SourceDocuments.ParsePathMap(PathMap, refused);", "var map = SourceDocuments.ParsePathMap(null, refused);")],
     WT, ["The_task_reads_a_document_a_projects_own_PathMap_named", "A_projects_own_PathMap_draws_its_assertions_as_a_local_build_does"]),
    ("M2", "ignore SourceRoots", [(TASK, "foreach (var root in SourceRoots)", "foreach (var root in Array.Empty<ITaskItem>())")],
     WT, ["The_task_reads_a_mapped_document_through_the_source_roots"]),
    ("M3", "skip embedded source", [(SD, "if (EmbeddedSource(document) is { } embedded)", "if (EmbeddedSource(document) is { } embedded && embedded.Length < 0)")],
     WT, ["Embedded_source_is_read_when_no_file_is_there"]),
    ("M4", "drop the checksum", [(SD, "if (checksum is null || checksum.Matches(bytes))", "if (checksum is null || checksum.Matches(bytes) || true)")],
     WT, ["Two_directories_mapped_to_one_name_are_told_apart_by_the_checksum", "Overlapping_mapped_values_are_told_apart_by_the_checksum"]),
    ("M5", "read ,, as a separator", [(SD, "if (i < text.Length && text[i] == separator)", "if (i < text.Length && text[i] == separator && false)")],
     WT, ["The_PathMap_is_parsed_as_the_compiler_parses_it"]),
    ("M6", "pass the resolved local path as callerFilePath", [
        (SD, "private readonly List<string> _found = new();", "private readonly List<string> _found = new(); internal readonly Dictionary<string, string> Resolved = new(StringComparer.Ordinal);"),
        (SD, '_found.Add($"Read the source of {name} from {candidate}");', '{ _found.Add($"Read the source of {name} from {candidate}"); Resolved[name] = candidate; }'),
        (AW, r("        var filePath = assertion.SequencePoint.Document.Url;", NL, "        var lineNumber = assertion.SequencePoint.StartLine;", NL, NL, "        var firstInstr"),
             r("        var filePath = _sources.Resolved.TryGetValue(assertion.SequencePoint.Document.Url, out var resolved) ? resolved : assertion.SequencePoint.Document.Url;", NL, "        var lineNumber = assertion.SequencePoint.StartLine;", NL, NL, "        var firstInstr")),
     ], WT, ["The_woven_calls_and_the_pdb_keep_the_compilers_names", "Two_ci_builds_at_different_paths_weave_the_same_bytes_with_the_statements_labels"]),
    ("M7", "IsPragmaDisabled reads the name as written", [(AW, r("        var lines = _sources.Lines(sp.Document);", NL, "        if (lines == null || sp.StartLine < 1"),
        r("        var lines = File.Exists(sp.Document.Url) ? File.ReadAllLines(sp.Document.Url) : null;", NL, "        if (lines == null || sp.StartLine < 1"))],
     WT, ["A_mapped_build_reads_the_pragma_comments"]),
    ("M8", "the attributes generated only with the switch on", [(TARGETS, r('<Target Name="_GenerateAssertionTrackingAttributes"', NL, '          BeforeTargets="CoreCompile">'),
        r('<Target Name="_GenerateAssertionTrackingAttributes"', NL, '          BeforeTargets="CoreCompile"', NL, '          Condition="', "'$(TrackAssertionsEnabled)' == 'true'", '">'))],
     WT, ["The_off_switch_builds_a_project_that_declares_the_attribute_and_weaves_nothing"]),
    ("M9", "drop the parser's sourceless pattern", [(EDP, "{4}but was not(?=", "{4}but was never(?=")],
     CORE, ["An_unquoted_failure_has_an_expected_and_actual", "Expected_and_actual_are_parsed_out_of_Shouldlys_text_without_its_source"]),
    ("M10", "key the sourceless form on its first line", [(FT, 'return CollapseWhitespace(second) + " " + CollapseWhitespace(sourceless.Groups["expected"].Value);', 'return first + " " + CollapseWhitespace(second);')],
     CORE, ["Shouldly_failures_without_the_source_cluster_by_the_check_and_its_expected_value", "Shouldly_failures_without_the_source_expecting_different_values_cluster_apart"]),
    ("M12", "break lines at CR and LF only", [(SD, r("if (c != '", BS, "r' && c != '", BS, "n' && c != '", BS, "u0085' && c != '", BS, "u2028' && c != '", BS, "u2029')"),
        r("if (c != '", BS, "r' && c != '", BS, "n')"))],
     WT, ["A_statement_after_a_unicode_line_break_is_labelled_with_its_own_text"]),
    ("M13a", "decode a non-UTF-8 file as UTF-8", [(SD, r("        catch (DecoderFallbackException)", NL, "        {", NL, "            return Windows1252(bytes);"),
        r("        catch (DecoderFallbackException)", NL, "        {", NL, "            return Encoding.UTF8.GetString(bytes);"))],
     WT, ["A_bom_less_windows_1252_file_is_read_as_the_compiler_reads_it"]),
    ("M13b", "ignore CodePage", [(SD, "        if (codePage is not null)" + NL + "            return codePage.GetString(bytes);", "        if (codePage is not null && codePage is null)" + NL + "            return codePage.GetString(bytes);")],
     WT, ["A_build_code_page_decodes_a_bom_less_utf8_file_as_the_compiler_does", "The_task_takes_the_builds_PathMap_source_roots_and_code_page", "A_build_with_a_code_page_labels_its_statements_as_the_compiler_read_them"]),
    ("M13c", "try UTF-8 before the code page", [(SD, "        if (codePage is not null)" + NL + "            return codePage.GetString(bytes);",
        "        try { return StrictUtf8.GetString(bytes); } catch (DecoderFallbackException) { }" + NL + "        if (codePage is not null)" + NL + "            return codePage.GetString(bytes);")],
     WT, ["A_build_code_page_decodes_a_bom_less_utf8_file_as_the_compiler_does", "The_task_takes_the_builds_PathMap_source_roots_and_code_page", "A_build_with_a_code_page_labels_its_statements_as_the_compiler_read_them"]),
    ("M14", "keep the TypeDefinitionDocuments records", [(AW, "            DropTypeDefinitionDocuments(assembly.MainModule);", "            // DropTypeDefinitionDocuments(assembly.MainModule);")],
     WT, ["The_woven_pdb_holds_no_type_record_that_names_another_types_document"]),
    ("M15", "read embedded source before the files", [(SD, "        // 1. A file the compiler's map leads back to",
        '        if (EmbeddedSource(document) is { } early) { _found.Add($"Read the source of {name} from the source the PDB embeds"); return SplitLines(Decode(early, _codePage)); }' + NL + "        // 1. A file the compiler's map leads back to")],
     WT, ["A_file_with_the_checksum_is_read_and_the_embedded_blob_stays_the_compilers"]),
    ("M16", "take a mapped candidate that fails the checksum", [(SD, "if (checksum is null || checksum.Matches(bytes))", "if (checksum is null || checksum.Matches(bytes) || !string.Equals(candidate, name, StringComparison.Ordinal))")],
     WT, ["Embedded_source_wins_over_a_file_that_fails_the_checksum"]),
    ("M17", "ignore a byte order mark", [(SD, "        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)",
        "        if (codePage is null) { try { return StrictUtf8.GetString(bytes); } catch (DecoderFallbackException) { return Windows1252(bytes); } }" + NL + "        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)")],
     WT, ["A_file_in_a_unicode_encoding_is_read_as_before"]),
    ("M18", "answer an empty text when nothing is readable", [(AW, "            if (lines == null)" + NL + '                return $"assertion at line {sp.StartLine}";', "            if (lines == null)" + NL + '                return "";')],
     WT, ["A_document_nothing_reaches_falls_back_and_is_recorded"]),
    ("M19", "take the shorter matched value first", [(SD, ".OrderByDescending(e => e.Value.Length)", ".OrderBy(e => e.Value.Length)")],
     WT, ["A_document_without_a_checksum_is_read_through_the_longer_mapped_value"]),
    ("M20", "leave the rest's separators as the name has them (red on Linux only)", [(SD, "var candidate = entry.Key + WithSeparatorsOf(entry.Key, name.Substring(entry.Value.Length));", "var candidate = entry.Key + name.Substring(entry.Value.Length);")],
     WT, ["A_mapped_value_written_with_the_other_separator_is_read_back"]),
    ("M21", "read every check that ends but was not", [(EDP, r("{4}should be", BS, "r?", BS, "n(?<expected>"), r("{4}should be[^", BS, "r", BS, "n]*", BS, "r?", BS, "n(?<expected>"))],
     CORE, ["A_check_that_is_not_an_equality_has_no_expected_and_actual"]),
    ("M22", "key should not be by its check and value", [(FT, "{4}but was not(?=", "{4}but was(?: not)?(?=")],
     CORE, ["Shouldly_should_not_be_without_the_source_keeps_its_first_line_key"]),
    ("M23", "drop the name as written, unchecked", [(SD, "        if (unverified is not null)", "        if (unverified is not null && unverified.Length < 0)")],
     WT, ["A_file_under_its_own_name_that_fails_the_checksum_is_still_read"]),
    ("M24", "take 0 as a code page", [(SD, "        if (codePage == 0)" + NL + "            return null;", "")],
     WT, ["The_builds_code_page_is_read_as_the_compiler_reads_it"]),
]

def run(cmd, cwd, timeout=3600):
    p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    return p.returncode, p.stdout + p.stderr

def restore(files):
    for f in files:
        shutil.copyfile(os.path.join(SRC, f), os.path.join(MUT, f))

only = sys.argv[1:]
results = []
for mid, what, edits, project, facts in MUTANTS:
    if only and mid not in only:
        continue
    files = sorted({e[0] for e in edits})
    restore(files)
    applied = True
    for f, old, new in edits:
        path = os.path.join(MUT, f)
        text = open(path, encoding="utf-8", newline="").read()
        if text.count(old) != 1:
            applied = False
            print(f"{mid}: does not apply to {f} ({text.count(old)} matches)", flush=True)
            break
        open(path, "w", encoding="utf-8", newline="").write(text.replace(old, new))
    if not applied:
        results.append((mid, what, "DOES NOT APPLY", ""))
        restore(files)
        continue
    code, out = run(["dotnet", "build", project, "-c", "Debug", "-f", "net10.0"], MUT)
    if code != 0:
        errors = sorted(set(l.strip() for l in out.splitlines() if " error " in l))[:3]
        results.append((mid, what, "DOES NOT BUILD", " | ".join(errors)))
        print(f"{mid}: does not build", errors, flush=True)
        restore(files)
        continue
    flt = "|".join("FullyQualifiedName~" + f for f in facts)
    code, out = run(["dotnet", "test", project, "-c", "Debug", "-f", "net10.0", "--no-build", "--filter", flt], MUT)
    totals = [l.strip() for l in out.splitlines() if l.strip().startswith(("Passed!", "Failed!"))]
    failed = sorted(set(re.sub(r"^\s*Failed (\S+?)(\(.*)?\s\[.*$", r"\1", l).split(".")[-1] for l in out.splitlines() if re.match(r"^  Failed \S", l)))
    status = "CAUGHT" if failed else ("SURVIVED" if totals else "NO TESTS RAN")
    results.append((mid, what, status, "; ".join(failed) + (" [" + " / ".join(totals) + "]" if totals else "")))
    print(f"{mid}: {status} {failed} {totals}", flush=True)
    restore(files)

with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "mutations-" + time.strftime("%H%M%S") + ".txt"), "w", encoding="utf-8", newline="\n") as out:
    for mid, what, status, detail in results:
        out.write(f"{mid} ({what}): {status}" + (f": {detail}" if detail else "") + "\n")
print("done", flush=True)
