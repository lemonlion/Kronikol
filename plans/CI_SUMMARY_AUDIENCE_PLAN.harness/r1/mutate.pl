use strict;
use warnings;

# Applies one mutation of plans/CI_SUMMARY_AUDIENCE_PLAN.md's release to a worktree, by exact text: the Nth occurrence
# of FROM in FILE becomes TO. Exits 1 when FROM does not occur that often, so a mutation that no longer matches the
# source is reported rather than silently skipped. Byte-level: the strings below are UTF-8 as the sources are.
# No backslash is written below: the tools that write files here mangle escape text, so BS and NL are built.
my $BS = chr(92);
my $NL = chr(10);

my $rscw = 'src/Kronikol/Reports/RunSummaryConsoleWriter.cs';
my $rg = 'src/Kronikol/Reports/ReportGenerator.cs';
my $mro = 'src/Kronikol/Reports/Merge/MergedRunOutputs.cs';
my $qc = 'src/Kronikol/Query/QueryCommand.cs';
my $ci = 'src/Kronikol/Reports/CiReader.cs';
my $nn = $BS . 'n' . $BS . 'n';

my %mutations = (
    M01 => [$rscw, 1, q~new StringBuilder("**People:** ")~, q~new StringBuilder("")~, 'drop the People label'],
    M02 => [$rscw, 1, q~markdown.Append("**AI agents:** ").Append(opening)~, q~markdown.Append(opening)~, 'drop the AI agents label'],
    M03 => [$rscw, 1,
        q~markdown.Append("## Debug this run~ . $nn . q~").Append(PeoplePart(summary, reader)).Append("~ . $nn . q~");~ . $NL . q~        AppendAgentPart(markdown, summary, reader);~,
        q~markdown.Append("## Debug this run~ . $nn . q~");~ . $NL . q~        AppendAgentPart(markdown, summary, reader);~ . $NL . q~        markdown.Append(PeoplePart(summary, reader)).Append("~ . $nn . q~");~,
        'swap the parts'],
    M04 => [$rscw, 1, q~var target = DownloadTarget(summary, folder);~, q~var target = QueryTarget(summary);~, "put the runner's folder back in the bash block"],
    M05 => [$rscw, 1, q~var guard = $"test -d {folder} || gh run download~, q~var guard = $"gh run download~, 'drop the guard'],
    M06 => [$rscw, 1, q~reader.Host is { } host ? $"{host}/{reader.Repository}" : reader.Repository ?? "";~, q~reader.Repository ?? "";~, 'drop the host'],
    M07 => [$rscw, 1, q~if (reader.Attempt > 1)~, q~if (false)~, 'drop the attempt sentence'],
    M08 => [$rscw, 1, q~summary.QueryTarget is { } file~ . $NL . q~            ? $~, q~summary.QueryTarget is { } file && false~ . $NL . q~            ? $~, 'drop the file-name check in the download target'],
    M09 => [$rg, 1, q~queryTarget: options.TestRunReportDataFormat == DataFormat.Json && written.Contains(~, q~queryTarget: false && written.Contains(~, "drop the run's data-file target"],
    M10 => [$rscw, 1, q~return reader.HandsFolderToUpload~, q~return true~, 'state an upload on GitHub that Kronikol cannot see'],
    M11 => [$rscw, 2, q~if (summary.Failures.Count == 0)~, q~if (false)~, 'drop the green-run branch'],
    M12 => [$qc, 1, q~return $"{line}{suite} · {manifest.Failed} of {manifest.Scenarios} failed";~, q~return line;~, "drop the list's counts"],
    M13 => [$rscw, 1, q~if (!reader.CanDownload)~, q~if (true)~, "print the annotation's old target"],
    M14 => [$rscw, 1, q~StringComparison.OrdinalIgnoreCase))?.Name;~, q~StringComparison.OrdinalIgnoreCase))?.Name ?? "TestRunReport.html";~, 'name an HTML file the run did not write'],
    M15 => [$rscw, 1, q~DataFile(summary) is { } name && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : null;~, q~DataFile(summary) ?? "TestRunReport.json";~, 'query a data file the tool cannot read'],
    M16 => [$rscw, 1, q~OneLine(url).Replace(" ", "%20", StringComparison.Ordinal).Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);~, q~OneLine(url);~, "drop the link target's encoding"],
    M17 => [$rscw, 1, q~workspace is null ? directory : Relative(workspace, directory) ?? directory;~, q~directory;~, 'name the folder in full, never relative to the workspace'],
    M18 => [$rscw, 1, q~ -not -path '*/runs/*' -not -path '*/baseline/*'~, q~~, "drop find's runs/ and baseline/ exclusions"],
    M19 => [$rscw, 1, q~var digest = summary.Failures.Count > 0 && HasDigest(summary);~, q~var digest = HasDigest(summary);~, "name Failures.md for people on a green run"],
    M20 => [$rscw, 1, q~Relative(summary.Directory, previousRun.Directory) is { } kept~, q~Relative(summary.Directory, previousRun.Directory) is { } kept && false~, 'name the previous run by its runner folder'],
    M21 => [$rscw, 1, q~given the .NET 10 SDK and {engine} in the NuGet cache.~, q~given the .NET 10 SDK.~, "drop what query.cs needs off the runner"],
    M22 => [$ci, 1, q~HandsFolderToUpload = gitHub && options.PublishCiArtifacts && !string.IsNullOrEmpty(getEnv("GITHUB_OUTPUT")),~, q~HandsFolderToUpload = gitHub && options.PublishCiArtifacts,~, 'hand the folder over without $GITHUB_OUTPUT'],
    M23 => [$qc, 1, q~|| !manifest.Files.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))~, q~)~, "take another run's Run.json for the report"],
    M24 => [$rscw, 1, q~=> DataFile(summary) ?? HtmlFileName(summary);~, q~=> DataFile(summary) ?? "TestRunReport.json";~, 'the pointer forbids a data file the run did not write'],
    M25 => [$ci, 1, q~ArtifactName = !gitHub && options.PublishCiArtifacts ? Value(options.CiArtifactName) : null,~, q~ArtifactName = !gitHub ? Value(options.CiArtifactName) : null,~, 'name an Azure DevOps artifact Kronikol did not upload'],
    M26 => [$ci, 1, q~ && !string.IsNullOrWhiteSpace(Repository);~, q~;~, 'download without a repository'],
    M27 => [$rscw, 1, q~: "If the run uploaded several reports (a matrix, or shards beside their merge), the query lists them with their counts: pass the one you mean.");~, q~: "");~, 'drop the several-reports sentence'],
    M28 => [$rscw, 1, q~after.Add($"A later step of this job finds the folder at `{OneLine(summary.Directory)}`.");~, q~;~, 'drop the later-step sentence'],
    M29 => [$rscw, 1, q~var folder = OneLine(FolderForPeople(summary.Directory, reader.Workspace));~, q~var folder = FolderForPeople(summary.Directory, reader.Workspace);~, "drop OneLine on the people part's folder"],
    M30 => [$rscw, 1, q~--repo {Quote(RepositoryArgument(reader))} --dir {folder}";~, q~--repo {RepositoryArgument(reader)} --dir {folder}";~, "drop Quote on the download's repository"],
    M31 => [$rg, 1, q~var ciReader = CiReader.Detect(getEnv, options);~, q~var ciReader = CiReader.None;~, "a run ignores its CI"],
    M32 => [$mro, 1, q~var reader = CiReader.Detect(getEnv, options);~, q~var reader = CiReader.None;~, 'a merge ignores its CI'],
    M33 => [$rscw, 1, q~AppendRunLines(markdown, summary, reader);~ . $NL . q~            return markdown.ToString();~, q~return markdown.ToString();~, "drop a green run's history and previous-run lines"],
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
