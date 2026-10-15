use strict;
use warnings;

# The audit's mutations (plans/CI_SUMMARY_AUDIENCE_PLAN.md section 12), in r1/mutate.pl's form: the Nth occurrence of
# FROM in FILE becomes TO, and a mutation whose text is no longer in the source exits 1 rather than being skipped.
# No backslash is written below: the tools that write files here mangle escape text, so BS and NL are built.
my $BS = chr(92);
my $NL = chr(10);

my $rscw = 'src/Kronikol/Reports/RunSummaryConsoleWriter.cs';
my $rg = 'src/Kronikol/Reports/ReportGenerator.cs';
my $mro = 'src/Kronikol/Reports/Merge/MergedRunOutputs.cs';
my $qc = 'src/Kronikol/Query/QueryCommand.cs';
my $rrr = 'src/Kronikol/Query/RetainedRunResolver.cs';
my $fdg = 'src/Kronikol/Reports/FailuresDigestGenerator.cs';

my %mutations = (
    A01 => [$rscw, 1, q~=> BreakLoggingCommands((text ?? "").ReplaceLineEndings(" "));~, q~=> (text ?? "").ReplaceLineEndings(" ");~, 'stop breaking ##[ and ##vso[ in a name'],
    A02 => [$rscw, 1, q~return flat.Length > 0 && flat.All(c => char.IsAsciiLetterOrDigit(c) || "_./:=@%+,-".Contains(c, StringComparison.Ordinal))~,
        q~return !flat.Contains(' ', StringComparison.Ordinal) && !flat.Contains('"', StringComparison.Ordinal)~, 'quote a bash word only for a space or a quote, as before'],
    A03 => [$rscw, 1, q~if (c is '*' or '?' or '[' or ']' or '~ . $BS . $BS . q~')~, q~if (false)~, "stop escaping find's pattern characters"],
    A04 => [$rscw, 1, q~then read {DigestFileName} in it"~ . $NL . q~            : null;~, q~then read {DigestFileName} in it"~ . $NL . q~            : $"agents: {download}";~,
        'name a download for agents with nothing in it'],
    A05 => [$rscw, 1, q~: Relative(workspace, directory) is { } relative ? (relative == "." ? null : relative)~, q~: Relative(workspace, directory) is { } relative ? relative~,
        'name the workspace itself as .'],
    A06 => [$rscw, 1, q~? $"; this run named the folder in `reports-path` for the job's upload step, which uploads the last folder named there to this run's {artifacts}"~,
        q~? $"; the job's upload step puts them in this run's {artifacts}"~, 'claim the upload for every project of a step'],
    A07 => [$rscw, 1, q~reader.Provider == CiEnvironment.AzureDevOps && reader.ArtifactName is null && !string.IsNullOrWhiteSpace(reader.RunId)~,
        q~false && reader.ArtifactName is null && !string.IsNullOrWhiteSpace(reader.RunId)~, "query the agent's folder on Azure DevOps without an upload"],
    A08 => [$rscw, 1, q~ShellWord(reader.Attempt > 1 ? $"kronikol-{reader.RunId}-{reader.Attempt.ToString(CultureInfo.InvariantCulture)}" : $"kronikol-{reader.RunId}");~,
        q~ShellWord($"kronikol-{reader.RunId}");~, 'one download folder for every attempt'],
    A09 => [$rscw, 1, q~+ (queryable ? ": `kronikol query summary` names the report's attempt." : "."));~, q~+ ": `kronikol query summary` names the report's attempt.");~,
        'name a query in the attempt sentence with no JSON data file'],
    A10 => [$rscw, 1, q~Quote(Path.GetFileName(previousRun.Directory.TrimEnd('/', '~ . $BS . $BS . q~')));~, q~"last-failed";~, 'open the kept run by last-failed'],
    A11 => [$rrr, 1, q~File.Exists(named))~, q~File.Exists(named) && false)~, 'look a kept run up by the folder lookup alone'],
    A12 => [$qc, 1, q~if (found.Length > MaxReportsListed)~, q~if (false)~, 'stop saying how many reports the list leaves out'],
    A13 => [$fdg, 1, q~--run {RunSummaryConsoleWriter.ShellWord(kept)}~, q~--run last-failed~, "open the digest's earlier run by last-failed"],
    A14 => [$fdg, 1, q~var here = dataFileName is null ? "." : RunSummaryConsoleWriter.ShellWord(dataFileName);~, q~var here = ".";~, 'every digest command on the folder'],
    A15 => [$rg, 1, q~dataFileName: options.GenerateTestRunReportData && options.TestRunReportDataFormat == DataFormat.Json~,
        q~dataFileName: false && options.TestRunReportDataFormat == DataFormat.Json~, "drop the run's data file name from the digest"],
    A16 => [$mro, 1, q~dataFileName: RunSummaryConsoleWriter.QueryTargetFor(dataFilePath) is { } named ? Path.GetFileName(named) : null));~, q~dataFileName: null));~,
        "drop the merge's data file name from the digest"],
    A17 => [$rscw, 1, q~public static string BuildCiSummarySection(RunSummary summary) => BuildCiSummarySection(summary, CiReader.FromProcess());~,
        q~public static string BuildCiSummarySection(RunSummary summary) => BuildCiSummarySection(summary, CiReader.None);~, "the public section ignores the process's CI"],
    A18 => [$rscw, 1, q~public static string? BuildGitHubNotice(RunSummary summary) => BuildGitHubNotice(summary, CiReader.FromProcess());~,
        q~public static string? BuildGitHubNotice(RunSummary summary) => BuildGitHubNotice(summary, CiReader.None);~, "the public annotation ignores the process's CI"],
    A19 => [$rscw, 1, q~environment == CiEnvironment.GitHubActions ? CiReader.FromProcess() : CiReader.None);~, q~CiReader.None);~, "the public Write ignores the process's CI"],
    A20 => [$rscw, 1, q~OneLine(url).Replace(" ", "%20"~, q~url.Replace(" ", "%20"~, "drop OneLine on the link target"],
    A21 => [$rscw, 1, q~gh run download {ShellWord(reader.RunId)}~, q~gh run download {reader.RunId}~, "the annotation's run id unflattened"],
    A22 => [$rscw, 2, q~gh run download {ShellWord(reader.RunId)}~, q~gh run download {reader.RunId}~, "the guard's run id unflattened"],
    A23 => [$rscw, 1, q~var word = ShellWord(summary.QueryTarget ?? summary.Directory);~, q~var word = QueryTarget(summary);~, "the folder block's words in double quotes"],
    A24 => [$rscw, 1, q~if (ReadsADownload(reader))~, q~if (reader.CanDownload || (reader.Provider == CiEnvironment.AzureDevOps && reader.ArtifactName is not null))~,
        "the previous run on the agent's folder on Azure DevOps without an upload"],
    A25 => [$rscw, 2, q~after.Add(SeveralInTheDownload(summary, folder));~, q~~, 'drop the several-reports sentence on Azure DevOps'],
    A26 => [$fdg, 1, q~Commented($"kronikol query failures {here}", 35,~, q~Commented($"kronikol query failures {here}", 36,~, "move the digest's first comment"],
);

my ($id, $root) = @ARGV;
die "usage: perl mutate.pl <id|list> <worktree>" unless $id;
if ($id eq 'list') {
    print join($NL, map { "$_ " . $mutations{$_}[4] } sort keys %mutations), $NL;
    exit 0;
}
my $m = $mutations{$id} or die "no mutation $id";
my ($file, $nth, $from, $to) = @$m;
my $path = "$root/$file";

open(my $in, '<:raw', $path) or die "cannot read $path";
my $text = do { local $/; <$in> };
close $in;

my $at = -1;
for (1 .. $nth) {
    $at = index($text, $from, $at + 1);
    if ($at < 0) {
        print "$id: occurrence $nth of the text not found in $file", $NL;
        exit 1;
    }
}
substr($text, $at, length($from)) = $to;

open(my $out, '>:raw', $path) or die "cannot write $path";
print $out $text;
close $out;
print "$id applied to $file: ", $m->[4], $NL;
