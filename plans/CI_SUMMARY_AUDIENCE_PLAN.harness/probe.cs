// What a published Kronikol puts in the CI job summary, for #169. Run from this folder:
//   dotnet run --file probe.cs            (the .NET 10 SDK; restores Kronikol from nuget.org)
// Every case calls the public builders the run and `kronikol merge` call, with the inputs a run hands them.
#:package Kronikol@4.18.1
#:property PublishAot=false
using Kronikol;
using Kronikol.Reports;

const string Dir = "/home/runner/work/shop/shop/tests/Acceptance/bin/Release/net10.0/Reports";

RunSummaryFile Json = new("TestRunReport.json", 10_700_000), Html = new("TestRunReport.html", 2_100_000), Digest = new("Failures.md", 4_096);
var failure = new RunSummaryFailure("0a1b2c3d4e5f6a7b", "Orders", "A refund is shown against its order");

void Case(string title, string text)
{
    Console.WriteLine($"=== {title}");
    Console.Write(text);
    Console.WriteLine("=== end");
    Console.WriteLine();
}

// A: the issue's case. A failing run on GitHub Actions, WriteCiDebugSection on (the default).
var failing = new RunSummary(Dir, [Json, Html, Digest], 42, [failure], AgentInstructionsWritten: true) { QueryScriptWritten = true };
Case("A. failing run: the Debug this run block (job summary and log)", RunSummaryConsoleWriter.BuildCiSummarySection(failing));

// B: the annotation the same run writes. Shown on the run page and on a pull request's checks.
Case("B. failing run: the ::notice annotation", RunSummaryConsoleWriter.BuildGitHubNotice(failing) + "\n");

// C: WriteCiSummary on, green run. ReportGenerator appends the block to CiSummary.md whatever the result.
var green = new RunSummary(Dir, [Json, Html, Digest], 42, [], AgentInstructionsWritten: true) { QueryScriptWritten = true };
Case("C. green run with WriteCiSummary: the block CiSummary.md ends with", RunSummaryConsoleWriter.BuildCiSummarySection(green));

// D: WriteCiSummary on, 11 failures, MaxCiSummaryDiagrams 10 (the default): the summary's own pointer and the block's rule, in one page.
var scenarios = Enumerable.Range(1, 11).Select(i => new Scenario { Id = $"t{i}", DisplayName = $"Refund {i}", Result = ExecutionResult.Failed, ErrorMessage = "Assert.Equal() Failure" }).ToArray();
var features = new[] { new Feature { DisplayName = "Orders", Scenarios = scenarios } };
var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, 10)
               + RunSummaryConsoleWriter.BuildCiSummarySection(new RunSummary(Dir, [Json, Html, Digest], 11,
                   scenarios.Select(s => new RunSummaryFailure(s.Id, "Orders", s.DisplayName)).ToArray(), true));
Case("D. failing run with WriteCiSummary, 11 failures: the lines that point at the report",
    string.Join("\n", markdown.Split('\n').Where(l => l.Contains("full report") || l.Contains("Do not open") || l.StartsWith('#'))) + "\n");

// E: a run that wrote no HTML (GenerateTestRunReport = false): the rule still names TestRunReport.html.
var noHtml = new RunSummary(Dir, [Json, Digest], 42, [failure], AgentInstructionsWritten: true);
Case("E. failing run that wrote no HTML: the first sentence", RunSummaryConsoleWriter.BuildCiSummarySection(noHtml).Split('\n')[2] + "\n");
