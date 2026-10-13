using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Who reads the CI text a run writes (plans/CI_SUMMARY_AUDIENCE_PLAN.md, #169). A GitHub job summary is read by people
/// only, in a browser: no API, <c>gh</c> command or MCP tool returns one. The annotation is read by people and by agents
/// through <c>gh run view</c>, and <c>CiSummary.md</c> by whoever downloads the artifact. None of them is on the runner,
/// whose folder is gone once the job ends. So the section speaks to people first, labels its rule for AI agents, and
/// names what exists where its readers are: the run's artifacts, a download of them, and the folder relative to the
/// workspace.
///
/// <para>Every fact here hands the builders a <see cref="CiReader"/> made from its own variables, so none sets a
/// process-wide one: on this repository's own CI the process is a GitHub Actions run with a real run id.</para>
/// </summary>
public class CiSummaryAudienceTests
{
    // The issue's own probe, on a Linux runner.
    private const string Workspace = "/home/runner/work/shop/shop";
    private const string Folder = "tests/Acceptance/bin/Release/net10.0/Reports";
    private const string Dir = Workspace + "/" + Folder;
    private const string RunUrl = "https://github.com/acme/shop/actions/runs/123456789";
    private const string BuildUrl = "https://dev.azure.com/acme/Shop/_build/results?buildId=77";

    private static readonly string Version = ReportGenerator.KronikolVersion.Split('+')[0];

    private static RunSummary Summary(int failures = 1, string? html = "TestRunReport.html", string? data = "TestRunReport.json",
        bool digest = true, string directory = Dir)
    {
        var files = new List<RunSummaryFile>();
        if (html is not null)
            files.Add(new RunSummaryFile(html, 1_200_000));
        if (data is not null)
            files.Add(new RunSummaryFile(data, 50_540_000));
        if (digest)
            files.Add(new RunSummaryFile("Failures.md", 12_000));

        return new RunSummary(directory, files, ScenarioCount: 42,
            Failures: Enumerable.Range(0, failures).Select(i => new RunSummaryFailure(i.ToString("x16"), "Checkout", $"Pay {i}")).ToArray(),
            AgentInstructionsWritten: true);
    }

    private static Func<string, string?> Env(params (string Name, string? Value)[] variables) =>
        name => variables.LastOrDefault(v => v.Name == name).Value;

    /// <summary>A GitHub Actions run of acme/shop, with <paramref name="overrides"/> replacing or removing a variable.</summary>
    private static Func<string, string?> GitHubEnv(params (string Name, string? Value)[] overrides) =>
        Env([
            ("GITHUB_ACTIONS", "true"), ("GITHUB_SERVER_URL", "https://github.com"), ("GITHUB_REPOSITORY", "acme/shop"),
            ("GITHUB_RUN_ID", "123456789"), ("GITHUB_RUN_ATTEMPT", "1"), ("GITHUB_WORKSPACE", Workspace), .. overrides
        ]);

    private static CiReader GitHub(params (string Name, string? Value)[] overrides) =>
        CiReader.Detect(GitHubEnv(overrides), new ReportConfigurationOptions());

    /// <summary>GitHub with <c>PublishCiArtifacts</c>, which hands the folder to the job's upload step through <c>$GITHUB_OUTPUT</c>.</summary>
    private static CiReader GitHubPublishing(string? githubOutput = "/home/runner/work/_temp/output") =>
        CiReader.Detect(GitHubEnv(("GITHUB_OUTPUT", githubOutput)), new ReportConfigurationOptions { PublishCiArtifacts = true });

    private static CiReader Azure(bool publish, string project = "Shop") =>
        CiReader.Detect(
            Env(("TF_BUILD", "True"), ("BUILD_BUILDID", "77"), ("SYSTEM_TEAMFOUNDATIONSERVERURI", "https://dev.azure.com/acme/"),
                ("SYSTEM_TEAMPROJECT", project), ("BUILD_SOURCESDIRECTORY", Workspace)),
            new ReportConfigurationOptions { PublishCiArtifacts = publish });

    private static string Section(RunSummary summary, CiReader reader) => RunSummaryConsoleWriter.BuildCiSummarySection(summary, reader);

    private static string Paragraph(string section, string label)
    {
        var start = section.IndexOf(label, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no {label} in:\n{section}");
        var end = section.IndexOf("\n\n", start, StringComparison.Ordinal);
        return end < 0 ? section[start..] : section[start..end];
    }

    private static string People(string section) => Paragraph(section, "**People:**");

    private static string BashBlock(string section)
    {
        var match = Regex.Match(section, "```bash\n(.*?)\n```", RegexOptions.Singleline);
        Assert.True(match.Success, $"no bash block in:\n{section}");
        return match.Groups[1].Value;
    }

    // ─── R1: who each part is for ──────────────────────────────

    [Fact]
    public void People_come_first_and_the_rule_is_for_AI_agents()
    {
        foreach (var reader in new[] { GitHub(), GitHub(("GITHUB_RUN_ID", null)), Azure(publish: true), CiReader.None })
        {
            var section = Section(Summary(), reader);

            Assert.StartsWith("## Debug this run\n\n**People:** open `TestRunReport.html`", section);
            var agents = section.IndexOf("\n\n**AI agents:** do not open `TestRunReport.json` or `TestRunReport.html` — ", StringComparison.Ordinal);
            Assert.True(agents > section.IndexOf("**People:**", StringComparison.Ordinal), section);
            // The rule is said once, and only under the agents' label.
            Assert.Single(Regex.Matches(section, "do not open", RegexOptions.IgnoreCase));
        }
    }

    [Fact]
    public void The_people_part_names_the_report_and_the_digest_the_run_wrote()
    {
        Assert.StartsWith(
            "**People:** open `TestRunReport.html` in a browser to read the whole run, diagrams included, or `Failures.md` for every failure in context. "
            + $"This job wrote them to `{Folder}`;",
            People(Section(Summary(), GitHub())));
        Assert.StartsWith(
            $"**People:** open `TestRunReport.html` in a browser to read the whole run, diagrams included. This job wrote it to `{Folder}`;",
            People(Section(Summary(digest: false), GitHub())));
    }

    /// <summary>
    /// A run with <c>GenerateTestRunReport = false</c> was told not to open a <c>TestRunReport.html</c> it never wrote
    /// (the plan's probe E), and the people part would have sent people to it.
    /// </summary>
    [Fact]
    public void No_part_names_an_html_file_the_run_did_not_write()
    {
        var section = Section(Summary(html: null), GitHub());

        Assert.DoesNotContain(".html", section);
        Assert.StartsWith($"**People:** open `Failures.md` for every failure in context. This job wrote it to `{Folder}`; it is in this run's", People(section));
        Assert.Contains("**AI agents:** do not open `TestRunReport.json` — a real report", section);
    }

    [Fact]
    public void A_merge_is_named_by_its_own_files()
    {
        var merged = Summary(html: "Combined.html", data: "Combined.json") with { QueryTarget = Dir + "/Combined.json" };

        foreach (var reader in new[] { GitHub(), CiReader.None })
        {
            var section = Section(merged, reader);
            Assert.Contains("**People:** open `Combined.html` in a browser", section);
            Assert.Contains("do not open `Combined.json` or `Combined.html` — ", section);
            Assert.DoesNotContain("TestRunReport", section);
        }
    }

    [Fact]
    public void A_run_with_no_report_and_no_digest_still_says_where_its_folder_is()
    {
        var section = Section(Summary(html: null, data: null, digest: false), GitHub());

        Assert.Equal($"**People:** this job wrote its reports to `{Folder}`; they are in this run's [artifacts]({RunUrl}#artifacts) when the job uploads that folder.", People(section));
        Assert.DoesNotContain("AI agents", section);
    }

    [Fact]
    public void The_people_part_links_the_run_s_artifacts()
    {
        Assert.Contains($"[artifacts]({RunUrl}#artifacts)", People(Section(Summary(), GitHub())));
        Assert.Contains($"([build]({BuildUrl}))", People(Section(Summary(), Azure(publish: false))));

        // Without a server URL there is no run page to link, and the sentence stands without one.
        var unlinked = People(Section(Summary(), GitHub(("GITHUB_SERVER_URL", null))));
        Assert.DoesNotContain("](", unlinked);
        Assert.EndsWith("; they are in this run's artifacts when the job uploads that folder.", unlinked);
    }

    /// <summary>
    /// The agent puts <c>SYSTEM_TEAMPROJECT</c> into the build URL as it is (<see cref="CiMetadataDetector"/>), and a
    /// space ends a markdown link's target, so a project called "My Project" printed a broken link.
    /// </summary>
    [Fact]
    public void An_azure_devops_project_with_a_space_still_links_its_build()
    {
        var people = People(Section(Summary(), Azure(publish: false, project: "My Project (EU)")));

        var target = Regex.Match(people, @"\[build\]\(([^)]*)\)").Groups[1].Value;
        Assert.Equal("https://dev.azure.com/acme/My%20Project%20%28EU%29/_build/results?buildId=77", target);
    }

    [Fact]
    public void The_folder_is_named_relative_to_the_workspace_with_forward_slashes()
    {
        // Native paths, so a Windows host checks the separator it uses and Linux the one it uses.
        var workspace = Path.Combine(Path.GetTempPath(), "ws", "shop");
        var directory = Path.Combine(workspace, "tests", "Acceptance", "Reports");

        var people = People(Section(Summary(directory: directory), GitHub(("GITHUB_WORKSPACE", workspace))));

        Assert.Contains("This job wrote them to `tests/Acceptance/Reports`;", people);
    }

    [Fact]
    public void A_folder_outside_the_workspace_is_named_in_full()
    {
        Assert.Contains("This job wrote them to `/tmp/reports`;", People(Section(Summary(directory: "/tmp/reports"), GitHub())));
        Assert.Contains($"This run wrote them to `{Dir}`.", People(Section(Summary(), CiReader.None)));
    }

    [Fact]
    public void The_upload_is_stated_only_where_Kronikol_made_it()
    {
        Assert.EndsWith($"; they are in this run's [artifacts]({RunUrl}#artifacts) when the job uploads that folder.", People(Section(Summary(), GitHub())));
        Assert.EndsWith($"; the job's upload step puts them in this run's [artifacts]({RunUrl}#artifacts).", People(Section(Summary(), GitHubPublishing())));
        // With no $GITHUB_OUTPUT, PublishCiArtifacts hands nothing over on GitHub.
        Assert.EndsWith("when the job uploads that folder.", People(Section(Summary(), GitHubPublishing(githubOutput: null))));
        Assert.EndsWith($"; they are in this build's `TestReports` artifact ([build]({BuildUrl})).", People(Section(Summary(), Azure(publish: true))));
        Assert.EndsWith($"; they are in this build's artifacts ([build]({BuildUrl})) when the pipeline publishes that folder.", People(Section(Summary(), Azure(publish: false))));
        Assert.EndsWith($"This run wrote them to `{Dir}`.", People(Section(Summary(), CiReader.None)));

        foreach (var reader in new[] { GitHub(), GitHubPublishing(), Azure(publish: true), Azure(publish: false), CiReader.None })
            Assert.DoesNotContain("artifact also carries", Section(Summary(), reader));
    }

    [Theory]
    [InlineData("GITHUB_REPOSITORY", "acme/shop\n::error::forged")]
    [InlineData("GITHUB_SERVER_URL", "https://ghe.example.com\n::error::forged")]
    [InlineData("GITHUB_WORKSPACE", "/home/runner\n::error::forged")]
    [InlineData("GITHUB_RUN_ID", "123\r\n::error::forged")]
    [InlineData("GITHUB_RUN_ATTEMPT", "2\u2028::error::forged")]
    public void A_ci_variable_holding_a_line_break_stays_on_its_line(string name, string value)
    {
        var reader = GitHub((name, value));

        // The section is also printed to the log, line by line, where a line starting "::" is a workflow command.
        var section = Section(Summary(), reader);
        Assert.DoesNotContain(section.Split('\n'), l => l.StartsWith("::", StringComparison.Ordinal));
        Assert.DoesNotContain(section, c => c is '\r' or '\u2028' or '\u2029' or '\u0085');

        var notice = RunSummaryConsoleWriter.BuildGitHubNotice(Summary(), reader);
        Assert.NotNull(notice);
        Assert.DoesNotContain(notice, c => c is '\n' or '\r' or '\u2028');
    }

    [Fact]
    public void A_folder_with_spaces_quotes_and_line_breaks_is_flattened_and_its_commands_quoted()
    {
        var directory = Workspace + "/My \"Reports\"\n::error::forged";
        var reader = GitHub();

        var section = Section(Summary(directory: directory), reader);
        Assert.DoesNotContain(section.Split('\n'), l => l.StartsWith("::", StringComparison.Ordinal));
        Assert.Contains("This job wrote them to `My \"Reports\" ::error::forged`;", People(section));

        var local = Section(Summary(directory: directory), CiReader.None);
        Assert.Contains("kronikol query failures \"/home/runner/work/shop/shop/My \\\"Reports\\\" ::error::forged\"", local);
    }

    // ─── R2: commands that work where the reader is ────────────

    [Fact]
    public void The_bash_block_downloads_the_run_behind_a_guard_and_queries_the_download()
    {
        var block = BashBlock(Section(Summary(), GitHub()));

        Assert.Equal(
            "test -d kronikol-123456789 || gh run download 123456789 --repo acme/shop --dir kronikol-123456789\n"
            + "dotnet tool install -g Kronikol.Tool\n"
            + "kronikol query summary kronikol-123456789\n"
            + "kronikol query failures kronikol-123456789",
            block);
        Assert.DoesNotContain("/home/runner", block);
    }

    [Fact]
    public void The_paragraph_after_the_block_says_what_a_download_holds()
    {
        var section = Section(Summary(), GitHub());

        Assert.Contains(
            "\n```\n\nRead `Failures.md` in the download first. If the run uploaded several reports (a matrix, or shards beside their merge), "
            + "the query lists them with their counts: pass the one you mean. "
            + $"A later step of this job finds the folder at `{Dir}`.\n\n",
            section);
    }

    [Fact]
    public void The_runner_s_folder_appears_once_for_a_later_step_of_the_job()
    {
        var summary = Summary() with
        {
            QueryScriptWritten = true,
            PreviousRun = new RunSummaryPreviousRun(Dir + "/runs/gh_123456789_1", 3),
            History = "history: 1 broke (against 4 earlier runs on main)"
        };

        var section = Section(summary, GitHub());

        Assert.Single(Regex.Matches(section, Regex.Escape(Dir)));
        Assert.Contains($"A later step of this job finds the folder at `{Dir}`.", section);
    }

    [Fact]
    public void A_data_file_the_folder_lookup_would_miss_is_found_by_name_in_the_download()
    {
        var summary = Summary(html: "Checkout.html", data: "Checkout.json") with { QueryTarget = Dir + "/Checkout.json" };

        var section = Section(summary, GitHub());

        const string find = "\"$(find kronikol-123456789 -name Checkout.json -not -path '*/runs/*' -not -path '*/baseline/*' -print -quit)\"";
        Assert.Equal(
            "test -d kronikol-123456789 || gh run download 123456789 --repo acme/shop --dir kronikol-123456789\n"
            + "dotnet tool install -g Kronikol.Tool\n"
            + $"kronikol query summary {find}\n"
            + $"kronikol query failures {find}",
            BashBlock(section));
        Assert.Contains("The `find` takes the first `Checkout.json` in the download: if the run uploaded several (a matrix, or shards), "
                        + "`find kronikol-123456789 -name Checkout.json` lists them, and a query takes the path of the one you mean.", section);
    }

    [Fact]
    public void On_GitHub_Enterprise_the_download_names_the_host()
    {
        Assert.Contains("--repo ghe.example.com/acme/shop --dir", BashBlock(Section(Summary(), GitHub(("GITHUB_SERVER_URL", "https://ghe.example.com")))));
        Assert.Contains("--repo acme.ghe.com/acme/shop --dir", BashBlock(Section(Summary(), GitHub(("GITHUB_SERVER_URL", "https://acme.ghe.com/")))));
        Assert.Contains("gh run download 123456789 --repo acme/shop --dir", BashBlock(Section(Summary(), GitHub(("GITHUB_SERVER_URL", "https://github.com")))));
        Assert.Contains("--repo acme/shop --dir", RunSummaryConsoleWriter.BuildGitHubNotice(Summary(), GitHub())!);
        Assert.Contains("--repo ghe.example.com/acme/shop --dir", RunSummaryConsoleWriter.BuildGitHubNotice(Summary(), GitHub(("GITHUB_SERVER_URL", "https://ghe.example.com")))!);
    }

    /// <summary>What R2 keeps: without a run id or a repository there is nothing to download, and a reader on the machine has the folder.</summary>
    [Theory]
    [InlineData("GITHUB_RUN_ID")]
    [InlineData("GITHUB_REPOSITORY")]
    public void Without_a_run_id_or_a_repository_the_agent_part_queries_the_folder(string missing)
    {
        var section = Section(Summary(), GitHub((missing, null)));

        Assert.Equal($"dotnet tool install -g Kronikol.Tool\nkronikol query summary {Dir}\nkronikol query failures {Dir}", BashBlock(section));
        Assert.Contains("**AI agents:** do not open `TestRunReport.json` or `TestRunReport.html` — a real report reaches megabytes, and a single embedded diagram can be larger than a context window. Query it instead:\n\n", section);
        Assert.DoesNotContain("gh run download", section);
    }

    [Fact]
    public void Off_ci_the_agent_part_queries_the_folder()
    {
        var section = Section(Summary() with { QueryScriptWritten = true }, CiReader.None);

        Assert.Equal($"dotnet tool install -g Kronikol.Tool\nkronikol query summary {Dir}\nkronikol query failures {Dir}", BashBlock(section));
        Assert.Contains($"Read `Failures.md` beside the report first. Nothing to install and no network needed: `dotnet run --file {Path.Combine(Dir, "query.cs")} -- summary {Dir}`", section);
        Assert.DoesNotContain("later step", section);
    }

    [Fact]
    public void A_re_run_says_its_attempt_and_that_a_download_may_hold_another()
    {
        Assert.Contains(
            "This is attempt 2. After a re-run, `gh run download` keeps one upload of each artifact name, not always this attempt's: "
            + "`kronikol query summary` names the report's attempt.",
            Section(Summary(), GitHub(("GITHUB_RUN_ATTEMPT", "2"))));

        foreach (var attempt in new[] { "1", null, "not a number" })
            Assert.DoesNotContain("attempt", Section(Summary(), GitHub(("GITHUB_RUN_ATTEMPT", attempt))));
    }

    [Fact]
    public void The_previous_run_is_named_beside_the_report()
    {
        var summary = Summary() with { PreviousRun = new RunSummaryPreviousRun(Dir + "/runs/gh_123456789_1", 3) };

        Assert.Contains("The previous run (3 failed) is kept beside the report in `runs/gh_123456789_1`: add `--run last-failed` to a query.\n\n", Section(summary, GitHub()));
        Assert.Contains($"The previous run (3 failed) is kept in `{Dir}/runs/gh_123456789_1` — `kronikol query failures {Dir} --run last-failed`.", Section(summary, CiReader.None));
    }

    [Fact]
    public void The_query_cs_line_says_what_it_needs_off_the_runner()
    {
        var section = Section(Summary() with { QueryScriptWritten = true }, GitHub());

        Assert.Contains(
            "With nothing installed, `dotnet run --file query.cs -- failures .` in the folder that holds the report runs the same engine, "
            + $"given the .NET 10 SDK and Kronikol {Version} in the NuGet cache.",
            section);
        Assert.DoesNotContain("no network", section);

        // Named, for a data file the folder lookup would miss.
        var named = Section(Summary(data: "Checkout.json") with { QueryScriptWritten = true, QueryTarget = Dir + "/Checkout.json" }, GitHub());
        Assert.Contains("`dotnet run --file query.cs -- failures Checkout.json` in the folder that holds the report", named);
    }

    [Fact]
    public void The_annotation_names_the_folder_the_download_and_the_query_and_never_the_runner()
    {
        var notice = RunSummaryConsoleWriter.BuildGitHubNotice(Summary(), GitHub());

        Assert.Equal(
            "::notice title=Kronikol::1 failed of 42 scenarios. Reports in tests/Acceptance/bin/Release/net10.0/Reports "
            + "(people: TestRunReport.html or Failures.md; agents: gh run download 123456789 --repo acme/shop --dir kronikol-123456789, "
            + "then kronikol query failures kronikol-123456789)",
            notice);
    }

    [Fact]
    public void Without_a_run_to_download_the_annotation_queries_the_folder()
    {
        Assert.Equal(
            $"::notice title=Kronikol::1 failed of 42 scenarios — kronikol query failures {Dir} · Failures.md",
            RunSummaryConsoleWriter.BuildGitHubNotice(Summary(), GitHub(("GITHUB_RUN_ID", null))));
        Assert.Null(RunSummaryConsoleWriter.BuildGitHubNotice(Summary(failures: 0), GitHub()));
    }

    [Fact]
    public void The_annotation_names_only_files_the_run_wrote_and_a_query_only_for_json()
    {
        Assert.EndsWith("(people: Failures.md; agents: gh run download 123456789 --repo acme/shop --dir kronikol-123456789, then kronikol query failures kronikol-123456789)",
            RunSummaryConsoleWriter.BuildGitHubNotice(Summary(html: null), GitHub()));
        Assert.EndsWith("(people: TestRunReport.html or Failures.md; agents: gh run download 123456789 --repo acme/shop --dir kronikol-123456789, then read Failures.md in it)",
            RunSummaryConsoleWriter.BuildGitHubNotice(Summary(data: "TestRunReport.xml"), GitHub()));
        Assert.Equal("::notice title=Kronikol::1 failed of 42 scenarios — Failures.md",
            RunSummaryConsoleWriter.BuildGitHubNotice(Summary(data: null), CiReader.None));
    }

    [Fact]
    public void On_azure_devops_the_agent_part_queries_the_artifact_Kronikol_uploaded()
    {
        var section = Section(Summary() with { QueryScriptWritten = true }, Azure(publish: true));

        Assert.Contains("Download this build's `TestReports` artifact and query it instead:", section);
        Assert.Equal("dotnet tool install -g Kronikol.Tool\nkronikol query summary TestReports\nkronikol query failures TestReports", BashBlock(section));
        Assert.Contains("Read `Failures.md` in the artifact first. With nothing installed, `dotnet run --file query.cs -- failures .`", section);
        Assert.Contains($"A later step of this job finds the folder at `{Dir}`.", section);
        Assert.Single(Regex.Matches(section, Regex.Escape(Dir)));
        // No az line: `az pipelines runs artifact download` fails on the container artifacts ##vso[artifact.upload] makes.
        Assert.DoesNotContain("az ", section);
    }

    [Fact]
    public void On_azure_devops_without_an_upload_the_agent_part_queries_the_folder()
    {
        Assert.Equal($"dotnet tool install -g Kronikol.Tool\nkronikol query summary {Dir}\nkronikol query failures {Dir}", BashBlock(Section(Summary(), Azure(publish: false))));
    }

    /// <summary>
    /// <c>kronikol query</c> reads a JSON data file and nothing else (F11): an XML or YAML data file, or none, made
    /// every printed command answer "No TestRunReport.json under".
    /// </summary>
    [Theory]
    [InlineData("TestRunReport.xml")]
    [InlineData("TestRunReport.yml")]
    [InlineData(null)]
    public void Without_a_json_data_file_no_query_command_is_printed(string? data)
    {
        foreach (var reader in new[] { GitHub(), GitHub(("GITHUB_RUN_ID", null)), Azure(publish: true), CiReader.None })
        {
            var summary = Summary(data: data) with { QueryScriptWritten = false, PreviousRun = new RunSummaryPreviousRun(Dir + "/runs/x", 1) };
            var section = Section(summary, reader);
            Assert.DoesNotContain("kronikol query", section);
            Assert.DoesNotContain("--run last-failed", section);
            Assert.DoesNotContain("kronikol query", RunSummaryConsoleWriter.Build(summary));
            Assert.Contains("Failures.md", Paragraph(section, "**AI agents:**"));
        }

        // The rule names the file the run wrote, whatever its format, and never one it did not.
        var xml = Section(Summary(data: "TestRunReport.xml"), CiReader.None);
        Assert.Contains("do not open `TestRunReport.xml` or `TestRunReport.html` — ", xml);
        Assert.Contains("Read `Failures.md` beside the report instead.", xml);
        Assert.Contains("do not open `TestRunReport.html` — ", Section(Summary(data: null), CiReader.None));
    }

    [Fact]
    public void The_pointer_without_a_json_data_file_says_what_failed_and_where_to_read_it()
    {
        var text = RunSummaryConsoleWriter.Build(Summary(data: "TestRunReport.xml") with { QueryScriptWritten = true });

        Assert.Contains("  1 failed — see Failures.md\n", text);
        Assert.Contains($"  agents: read {Path.Combine(Dir, "CLAUDE.md")} first; never open TestRunReport.xml\n", text);
        Assert.DoesNotContain("no tool:", text);

        var none = RunSummaryConsoleWriter.Build(Summary(data: null, digest: false) with { AgentInstructionsWritten = false });
        Assert.Contains("  1 failed\n", none);
        Assert.Contains("  agents: never open TestRunReport.html\n", none);
    }

    [Fact]
    public void The_public_builders_read_the_process_s_ci()
    {
        // Whatever the process is, the public builder writes what the internal one writes for the process's CI.
        Assert.Equal(Section(Summary(), CiReader.FromProcess()), RunSummaryConsoleWriter.BuildCiSummarySection(Summary()));
        Assert.Equal(RunSummaryConsoleWriter.BuildGitHubNotice(Summary(), CiReader.FromProcess()), RunSummaryConsoleWriter.BuildGitHubNotice(Summary()));
    }

    [Fact]
    public void Detect_reads_the_run_off_the_ci_s_variables()
    {
        var reader = CiReader.Detect(GitHubEnv(("GITHUB_RUN_ATTEMPT", "3"), ("GITHUB_SERVER_URL", "https://ghe.example.com:8443")), new ReportConfigurationOptions());

        Assert.Equal(CiEnvironment.GitHubActions, reader.Provider);
        Assert.Equal("https://ghe.example.com:8443/acme/shop/actions/runs/123456789", reader.RunUrl);
        Assert.Equal("ghe.example.com:8443", reader.Host);
        Assert.Equal(3, reader.Attempt);
        Assert.Equal(Workspace, reader.Workspace);
        Assert.True(reader.CanDownload);
        Assert.Null(reader.ArtifactName);
        Assert.Equal(Version, reader.KronikolVersion);

        Assert.Equal(CiEnvironment.None, CiReader.Detect(Env(), new ReportConfigurationOptions()).Provider);
        Assert.False(CiReader.Detect(Env(), new ReportConfigurationOptions()).CanDownload);
        Assert.Equal("Drop", CiReader.Detect(Env(("TF_BUILD", "True")), new ReportConfigurationOptions { PublishCiArtifacts = true, CiArtifactName = "Drop" }).ArtifactName);
    }

    // ─── R3: the full summary on a green run ───────────────────

    [Fact]
    public void A_green_run_s_section_is_the_report_for_people()
    {
        foreach (var reader in new[] { GitHub(), CiReader.None })
        {
            var section = Section(Summary(failures: 0), reader);

            Assert.StartsWith("## The report\n\n**People:** open `TestRunReport.html` in a browser to read the whole run, diagrams included. This ", section);
            Assert.DoesNotContain("Debug this run", section);
            Assert.DoesNotContain("AI agents", section);
            // Failures.md holds "No failures" on a green run: nothing to send a person to.
            Assert.DoesNotContain("Failures.md", section);
        }
    }

    /// <summary>
    /// The history line and the previous run are facts about the run for every reader, and a green run after a failing
    /// one is the case #80 is about: the failure is no longer on top, and the summary is where that has to be said.
    /// </summary>
    [Fact]
    public void A_green_run_s_section_keeps_the_history_and_the_previous_run()
    {
        var summary = Summary(failures: 0) with
        {
            History = "history: 1 fixed (against 4 earlier runs on main)",
            PreviousRun = new RunSummaryPreviousRun(Dir + "/runs/gh_123456789_1", 3)
        };

        var section = Section(summary, GitHub());

        Assert.EndsWith(
            "when the job uploads that folder.\n\nhistory: 1 fixed (against 4 earlier runs on main)\n\n"
            + "The previous run (3 failed) is kept beside the report in `runs/gh_123456789_1`: add `--run last-failed` to a query.\n\n",
            section);
        Assert.DoesNotContain("AI agents", section);
    }
}
