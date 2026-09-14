using Kronikol.Reports;

namespace Kronikol.History;

/// <summary>
/// The constants of the cross-run history ledger: where it lives, what a line's result characters mean,
/// and the one format version every reader and writer agrees on.
///
/// <para><b>Why a ledger and not a database, a dashboard or a server.</b> Everything a hosted test-history
/// product needs a database for — unbounded retention, accounts, push — is out of scope by contract
/// (plans/CROSS_RUN_HISTORY_PLAN.md §14). Everything else is arithmetic over the last N runs, and N runs
/// of verdicts is small: measured at 228&#160;KB for fifty runs of a two-hundred-scenario suite. A file in
/// the repository answers "is this failure new, or has main been red since Tuesday?" inside a pull-request
/// build with no infrastructure, no credentials and no network.</para>
///
/// <para><b>Why append-only JSONL.</b> A test run is exactly the kind of process CI kills, and a history
/// file one <c>SIGKILL</c> can destroy will be destroyed. An append loses at most its own last line; a
/// rewritten document loses everything. Under git's built-in <c>merge=union</c> driver two branches that
/// both appended merge without conflict wherever there is a checkout.</para>
/// </summary>
public static class HistoryFormat
{
    /// <summary>
    /// The ledger's shape version, written as the first line of every file. A reader understands every
    /// version at or below its own; a writer refuses to append to a version above its own, because an
    /// older line in a newer file is a file neither version reads correctly (§3.5).
    /// </summary>
    public const int Version = 1;

    /// <summary>The directory beside a repository root that holds the ledger and its companions.</summary>
    public const string DirectoryName = ".kronikol";

    /// <summary>The ledger file, under <see cref="DirectoryName"/>.</summary>
    public const string FileName = "history.jsonl";

    /// <summary>The one-run fragment a run writes into its reports directory — the shard unit <c>kronikol history record</c> folds.</summary>
    public const string FragmentFileName = "History.run.json";

    /// <summary>The hand-edited quarantine list, under <see cref="DirectoryName"/>.</summary>
    public const string QuarantineFileName = "quarantine.json";

    /// <summary>The rename aliases, under <see cref="DirectoryName"/>.</summary>
    public const string AliasesFileName = "aliases.json";

    /// <summary>The environment variable that names the ledger when no option does (the <c>KRONIKOL_BASELINE</c> idiom).</summary>
    public const string EnvironmentVariable = "KRONIKOL_HISTORY";

    /// <summary>The value of <see cref="EnvironmentVariable"/> that switches history off for a run entirely.</summary>
    public const string EnvironmentOff = "off";

    /// <summary>
    /// The <c>.gitattributes</c> line that lets two branches append without a conflict. A bare file name
    /// matches at any depth, so it covers <c>.kronikol/history.jsonl</c> in a working tree and a
    /// <c>history.jsonl</c> at the root of an orphan data branch alike. The attribute is read from the
    /// working tree, so it applies to every merge with a checkout and not to a server-side one (§5.10).
    /// </summary>
    public const string GitAttributesLine = "history.jsonl merge=union text eol=lf";

    /// <summary>A scenario that passed.</summary>
    public const char Passed = 'P';

    /// <summary>A scenario that failed.</summary>
    public const char Failed = 'F';

    /// <summary>A scenario that was skipped.</summary>
    public const char Skipped = 'S';

    /// <summary>A scenario the framework bypassed.</summary>
    public const char Bypassed = 'B';

    /// <summary>A scenario skipped because an earlier one in its group failed.</summary>
    public const char SkippedAfterFailure = 'A';

    /// <summary>
    /// A result that was defaulted or is otherwise unknown — a scenario that never reported an end and
    /// took <c>ResultWhenUnknown</c>. Recorded as this and never as a pass, because a crashed worker's
    /// scenarios written as passes would poison the trend permanently (§5.11).
    /// </summary>
    public const char Unknown = '?';

    /// <summary>A roster position this run had no result for at all.</summary>
    public const char Absent = '.';

    /// <summary>The attempt digit written when the runner reported nothing about attempts.</summary>
    public const char AttemptUnknown = '-';

    /// <summary>Characters of an error cluster key kept in the ledger; the rest is cut (§9.1).</summary>
    public const int ErrorKeyLimit = 200;

    /// <summary>The character a result is written as.</summary>
    public static char ResultChar(ExecutionResult result) => result switch
    {
        ExecutionResult.Passed => Passed,
        ExecutionResult.Failed => Failed,
        ExecutionResult.Skipped => Skipped,
        ExecutionResult.Bypassed => Bypassed,
        ExecutionResult.SkippedAfterFailure => SkippedAfterFailure,
        _ => Unknown
    };

    /// <summary>
    /// Whether a result character is a verdict the flip and fail rates are counted over. Only a pass or a
    /// fail is: skipping a test for a week neither creates nor hides flakiness, and a defaulted result is
    /// not a verdict at all (§7.2).
    /// </summary>
    public static bool IsRealVerdict(char result) => result is Passed or Failed;

    /// <summary>The attempt character for a 1-based attempt number, or <see cref="AttemptUnknown"/>.</summary>
    public static char AttemptChar(int? attempt) => attempt switch
    {
        null or < 1 => AttemptUnknown,
        > 9 => '+',
        var n => (char)('0' + n.Value)
    };

    /// <summary>The 1-based attempt a character stands for, or null.</summary>
    public static int? AttemptOf(char attempt) => attempt switch
    {
        >= '1' and <= '9' => attempt - '0',
        '+' => 10,
        _ => null
    };
}
