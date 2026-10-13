#:project ../../../src/Kronikol/Kronikol.csproj
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property TreatWarningsAsErrors=false
#:property NoWarn=CS1591

using Kronikol.Reports;

// Prints the CI text the public builders write for the issue's probe, on the CI this process pretends to be.
var dir = "/home/runner/work/shop/shop/tests/Acceptance/bin/Release/net10.0/Reports";
RunSummary Summary(int failures) => new(dir,
    [new RunSummaryFile("TestRunReport.html", 1_200_000), new RunSummaryFile("TestRunReport.json", 50_540_000), new RunSummaryFile("Failures.md", 12_000)],
    42, Enumerable.Range(0, failures).Select(i => new RunSummaryFailure(i.ToString("x16"), "Checkout", $"Pay {i}")).ToArray(), true)
{
    QueryScriptWritten = true,
    PreviousRun = failures > 0 ? new RunSummaryPreviousRun(dir + "/runs/gh_123456789_1", 3) : null
};

void Set(params (string Name, string? Value)[] variables)
{
    foreach (var name in new[] { "GITHUB_ACTIONS", "GITHUB_SERVER_URL", "GITHUB_REPOSITORY", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_WORKSPACE",
                 "GITHUB_OUTPUT", "TF_BUILD", "BUILD_BUILDID", "SYSTEM_TEAMFOUNDATIONSERVERURI", "SYSTEM_TEAMPROJECT", "BUILD_SOURCESDIRECTORY" })
        Environment.SetEnvironmentVariable(name, null);
    foreach (var (name, value) in variables)
        Environment.SetEnvironmentVariable(name, value);
}

void Show(string title, int failures = 1)
{
    Console.WriteLine($"==================== {title}");
    Console.Write(RunSummaryConsoleWriter.BuildCiSummarySection(Summary(failures)));
    Console.WriteLine("-- annotation:");
    Console.WriteLine(RunSummaryConsoleWriter.BuildGitHubNotice(Summary(failures)) ?? "(none)");
}

Set(("GITHUB_ACTIONS", "true"), ("GITHUB_SERVER_URL", "https://github.com"), ("GITHUB_REPOSITORY", "acme/shop"),
    ("GITHUB_RUN_ID", "123456789"), ("GITHUB_RUN_ATTEMPT", "2"), ("GITHUB_WORKSPACE", "/home/runner/work/shop/shop"));
Show("GitHub, failing, attempt 2");
Show("GitHub, green", failures: 0);

Set(("GITHUB_ACTIONS", "true"), ("GITHUB_WORKSPACE", "/home/runner/work/shop/shop"));
Show("GitHub without a run id");

Set(("TF_BUILD", "True"), ("BUILD_BUILDID", "77"), ("SYSTEM_TEAMFOUNDATIONSERVERURI", "https://dev.azure.com/acme/"),
    ("SYSTEM_TEAMPROJECT", "My Project"), ("BUILD_SOURCESDIRECTORY", "/home/runner/work/shop/shop"));
Show("Azure DevOps, PublishCiArtifacts off (the public builder's default options)");

Set();
Show("Off CI");
