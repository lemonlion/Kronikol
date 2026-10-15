# The plan's audit (2026-10-15): each fix's mutants, applied in a worktree of their own (MUT), the projects named rebuilt
# for net10.0, the facts named run, and the files restored from the audit's tree (SRC). A mutant is caught when at
# least one of its facts fails; one that does not apply or does not build is reported, never counted as caught.
# A core mutant builds the library it changes, then the test project without its references (each would rebuild).
import os, re, shutil, subprocess, sys, time

SRC = r"C:\Code\Kronikol-165-audit"
MUT = r"C:\Code\Kronikol-165-audit-mut"
NL = chr(10)

WEAVER = r"src\Kronikol.AssertionTracking"
SD = WEAVER + r"\SourceDocuments.cs"
AW = WEAVER + r"\AssertionWeaver.cs"
TARGETS = WEAVER + r"\build\Kronikol.AssertionTracking.targets"
STEPW = r"src\Kronikol.StepTracking\StepWeaver.cs"
STEPT = r"src\Kronikol.StepTracking\build\Kronikol.StepTracking.targets"
EDP = r"src\Kronikol\Reports\ErrorDiffParser.cs"
FT = r"src\Kronikol\Reports\FailureText.cs"
HCM = r"src\Kronikol.Tool\HistoryCommand.Maintenance.cs"

WT = r"tests\Kronikol.Tests.AssertionTracking\Kronikol.Tests.AssertionTracking.csproj"
ST = r"tests\Kronikol.Tests.StepTracking\Kronikol.Tests.StepTracking.csproj"
CORE = r"tests\Kronikol.Tests\Kronikol.Tests.csproj"
LIB = r"src\Kronikol\Kronikol.csproj"
TOOL = r"src\Kronikol.Tool\Kronikol.Tool.csproj"

PARSE = "An_unquoted_failure_has_an_expected_and_actual"
KEY = "Shouldly_failures_without_the_source_cluster_by_the_check_and_its_expected_value"

# (id, what, edits [(file, old, new, all?)], test project, prebuilt libraries, facts)
MUTANTS = [
    ("A1", "KRONIKOL002 counts statements when found", [
        (AW, "            _sources.CountStatement(assertion.SequencePoint.Document);" + NL, "", False),
        (AW, "            results.Add(new AssertionStatement", "            _sources.CountStatement(sp.Document);" + NL + "            results.Add(new AssertionStatement", False)],
     WT, [], ["The_warning_counts_only_the_statements_the_weave_wrapped"]),
    ("A2", "a map entry that does not begin the name gives a candidate", [
        (SD, "            if (!name.StartsWith(entry.Value, StringComparison.Ordinal))" + NL + "                continue;",
             "            if (!name.StartsWith(entry.Value, StringComparison.Ordinal))" + NL + "            { candidates.Add(entry.Key + name.TrimStart('/')); continue; }", False)],
     WT, [], ["A_document_no_map_entry_matches_is_tried_under_its_own_name_alone"]),
    ("S1", "no NUnit type prefix", [(EDP, "(?:Shouldly\\.\\w+ : )?", "", False)], CORE, [LIB], [PARSE]),
    ("S3a", "a Shouldly value crosses marker lines", [(EDP, "internal const string ShouldlyValue = @\"(?:(?!\\r?\\n {4}(?:should |but was)).)*?\";", "internal const string ShouldlyValue = @\".*?\";", False)],
     CORE, [LIB], [PARSE, KEY]),
    ("S3b", "the actual value may start on a marker's line", [(EDP, "new(@\"^(?! {4}(?:should |but was))(?:Shouldly", "new(@\"^(?:Shouldly", False)], CORE, [LIB], [PARSE]),
    ("S4a", "with source: no comparison after but was", [(EDP, "{4}but was(?: \\([^)\\r\\n]*\\))?\\r?\\n(?<actual>", "{4}but was\\r?\\n(?<actual>", False)], CORE, [LIB], [PARSE]),
    ("S4b", "without source: no comparison after but was not", [(EDP, "{4}but was not(?: \\([^)\\r\\n]*\\))?(?=", "{4}but was not(?=", False)], CORE, [LIB], [PARSE]),
    ("S4c", "the key: no comparison after but was not", [(FT, "{4}but was not(?: \\([^)\\r\\n]*\\))?(?=", "{4}but was not(?=", False)], CORE, [LIB], [KEY]),
    ("S4d", "with source: no options on the check", [(EDP, "new(@\"^ {4}should be(?: with options: [^\\r\\n]*)?", "new(@\"^ {4}should be", False)], CORE, [LIB], [PARSE]),
    ("S4e", "without source: no options on the check", [(EDP, "+ @\")\\r?\\n {4}should be(?: with options: [^\\r\\n]*)?", "+ @\")\\r?\\n {4}should be", False)], CORE, [LIB], [PARSE]),
    ("S5", "the key reads the form only with its check on the second line", [(FT, "if (ShouldlyWithoutSource.Match(message) is { Success: true } sourceless)",
        "if (ShouldlyWithoutSource.Match(message) is { Success: true, Index: 0 } sourceless && sourceless.Groups[\"actual\"].Value.IndexOf('\\n') < 0)", False)], CORE, [LIB], [KEY]),
    ("S8a", "without source: no Singleline", [(EDP, "RegexOptions.Multiline | RegexOptions.Singleline)," + NL + NL + "        // FluentAssertions, a string",
        "RegexOptions.Multiline)," + NL + NL + "        // FluentAssertions, a string", False)], CORE, [LIB], [PARSE]),
    ("S8b", "without source: CRLF only", [(EDP, "+ @\")\\r?\\n {4}should be(", "+ @\")\\r\\n {4}should be(", False)], CORE, [LIB], [PARSE]),
    ("S8c", "the key: CRLF only", [(FT, "+ @\")\\r?\\n(?<check>", "+ @\")\\r\\n(?<check>", False)], CORE, [LIB], [KEY]),
    ("S2a", "the diff's bound far lower", [(EDP, "internal const long MaxComparedPairs = 4_000_000;", "internal const long MaxComparedPairs = 1_000;", False)],
     CORE, [LIB], ["GenerateDiffHtml_compares_each_character_up_to_its_bound"]),
    ("S2b", "past the bound, no common start kept", [(EDP, "        while (start < shorter && primary[start] == other[start])", "        while (start < shorter && primary[start] == other[start] && start < 0)", False)],
     CORE, [LIB], ["GenerateDiffHtml_bounds_its_work_on_long_values"]),
    ("S2c", "the diff by UTF-16 unit", [(EDP, "        foreach (var rune in text.EnumerateRunes())" + NL + "            points.Add(rune.Value);",
        "        foreach (var unit in text)" + NL + "            points.Add(unit);", False)], CORE, [LIB], ["GenerateDiffHtml_keeps_a_character_outside_the_basic_plane_whole"]),
    ("S7a", "CTRF import keys by the first line", [(HCM, "test.TryGetProperty(\"message\", out var m) && m.ValueKind == JsonValueKind.String ? FailureText.Truncate(FailureText.ClusterKey(",
        "test.TryGetProperty(\"message\", out var m) && m.ValueKind == JsonValueKind.String ? FailureText.Truncate(FailureText.FirstLine(", False)],
     CORE, [LIB, TOOL], ["Import_keys_a_failure_as_the_run_itself_would"]),
    ("S7b", "Allure import keys by the first line", [(HCM, "                    ? FailureText.Truncate(FailureText.ClusterKey(m.GetString())",
        "                    ? FailureText.Truncate(FailureText.FirstLine(m.GetString())", False)], CORE, [LIB, TOOL], ["Import_keys_a_failure_as_the_run_itself_would"]),
    ("C1", "Windows-1252 as the fallback everywhere", [(SD, "return onWindows ? Windows1252(bytes) : ReplacingUtf8.GetString(bytes);", "return Windows1252(bytes);", False)],
     WT, [], ["A_file_that_is_not_utf8_is_read_with_the_platforms_fallback"]),
    ("C4", "the file shared for reading only", [(SD, "FileShare.ReadWrite | FileShare.Delete", "FileShare.Read", False)], WT, [], ["A_file_another_process_holds_open_for_writing_is_read"]),
    ("C5", "no sign in a CodePage", [(SD, "NumberStyles.AllowLeadingSign", "NumberStyles.None", False)], WT, [], ["The_builds_code_page_is_read_as_the_compiler_reads_it"]),
    ("C6a", "no UTF-32 for a large file", [(SD, ">= LargeTextChars && bytes.Length >= 4)", ">= LargeTextChars && bytes.Length < 0)", False)], WT, [], ["A_utf32_file_is_read_as_the_compiler_reads_it_by_its_size"]),
    ("C6b", "UTF-32 for a file of any size", [(SD, ">= LargeTextChars && bytes.Length >= 4)", ">= 0 && bytes.Length >= 4)", False)], WT, [], ["A_utf32_file_is_read_as_the_compiler_reads_it_by_its_size"]),
    ("W5", "a changed file not named so", [(SD, "tried.Add(candidate + \" (changed since the compile)\");", "tried.Add(candidate);", False)],
     WT, [], ["A_mapped_file_that_changed_since_the_compile_is_named_so_among_the_paths_tried"]),
    ("W1", "the attributes written on every build", [(TARGETS, NL + "      WriteOnlyWhenDifferent=\"true\"", "", True)], WT, [], ["A_second_build_with_nothing_changed_compiles_nothing"]),
    ("W2", "the attributes for every language", [(TARGETS, NL + "          Condition=\"'$(Language)' == 'C#'\">", ">", False)], WT, [], ["A_visual_basic_project_that_imports_the_targets_builds"]),
    ("W6", "the step weaver keeps the type records", [(STEPW, "            DropTypeDefinitionDocuments(assembly.MainModule);" + NL, "", False)], ST, [], ["Weave_leaves_no_type_document_record_in_the_pdb"]),
    ("W1s", "the step attributes written on every build", [(STEPT, NL + "      WriteOnlyWhenDifferent=\"true\"", "", True)], ST, [], ["TargetsFile_writes_each_attribute_only_when_it_changes_and_for_CSharp_only"]),
    ("W2s", "the step attributes for every language", [(STEPT, " and '$(Language)' == 'C#'", "", False)], ST, [], ["TargetsFile_writes_each_attribute_only_when_it_changes_and_for_CSharp_only"]),
]


def run(cmd, cwd, timeout=3600):
    p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    return p.returncode, p.stdout + p.stderr


def restore(files):
    for f in files:
        shutil.copyfile(os.path.join(SRC, f), os.path.join(MUT, f))


only = sys.argv[1:]
results = []
for mid, what, edits, project, libraries, facts in MUTANTS:
    if only and mid not in only:
        continue
    files = sorted({e[0] for e in edits})
    restore(files)
    applied = True
    for f, old, new, every in edits:
        path = os.path.join(MUT, f)
        text = open(path, encoding="utf-8", newline="").read()
        n = text.count(old)
        if n == 0 or (n != 1 and not every):
            applied = False
            print(f"{mid}: does not apply to {f} ({n} matches)", flush=True)
            break
        open(path, "w", encoding="utf-8", newline="").write(text.replace(old, new))
    if not applied:
        results.append((mid, what, "DOES NOT APPLY", ""))
        restore(files)
        continue
    built = True
    for library in libraries:
        code, out = run(["dotnet", "build", library, "-c", "Debug", "-f", "net10.0"], MUT)
        if code != 0:
            built = False
            break
    if built:
        extra = ["-p:BuildProjectReferences=false"] if libraries else []
        code, out = run(["dotnet", "build", project, "-c", "Debug", "-f", "net10.0", *extra], MUT)
        built = code == 0
    if not built:
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

# Leave the libraries built as the audit's tree has them.
for library in (LIB, TOOL):
    run(["dotnet", "build", library, "-c", "Debug", "-f", "net10.0"], MUT)
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "mutations-" + time.strftime("%H%M%S") + ".txt"), "w", encoding="utf-8", newline="\n") as out:
    for mid, what, status, detail in results:
        out.write(f"{mid} ({what}): {status}" + (f": {detail}" if detail else "") + "\n")
print("done", flush=True)
