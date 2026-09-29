namespace Kronikol.Tests.Templates;

/// <summary>
/// The runner the history action's facts run on (plans/HISTORY_ACTION_PLAN.md §5.2): each expression form, the
/// expressions it refuses, a run step's files and workflow commands, an environment that is built and never
/// inherited, and the artifact service's rules. A runner that got any of these wrong would make every fact built on it
/// prove something GitHub does not do.
/// </summary>
public class CompositeActionRunnerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-runner").FullName;

    public void Dispose()
    {
        try { BareOrigin.DeleteTree(_dir); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static object? Values(string path) => path switch
    {
        "inputs.name" => "octo",
        "inputs.empty" => "",
        "inputs.flag" => "true",
        "inputs.a" => "x",
        "inputs.b" => "z",
        "steps.stage.outputs.files" => "3",
        "github.event_name" => "push",
        _ => null
    };

    [Theory]
    [InlineData("inputs.name", "octo")]
    [InlineData("inputs.missing", "")]
    [InlineData("'OCTO' == inputs.name", "true")]
    [InlineData("inputs.name != 'Octo'", "false")]
    [InlineData("inputs.empty || 'fallback'", "fallback")]
    [InlineData("inputs.name || 'fallback'", "octo")]
    [InlineData("inputs.name && 'second'", "second")]
    [InlineData("inputs.empty && 'second'", "")]
    [InlineData("!inputs.empty", "true")]
    [InlineData("!inputs.flag == false", "true")]
    [InlineData("(inputs.a == 'x' || inputs.b == 'y') && inputs.name != ''", "true")]
    [InlineData("inputs.a == 'x' || inputs.b == 'y' && inputs.empty != ''", "true")]
    [InlineData("steps.stage.outputs.files != '0'", "true")]
    [InlineData("github.event_name == 'pull_request' || github.event_name == 'pull_request_target'", "false")]
    [InlineData("1 == '1'", "true")]
    [InlineData("'' == 0", "true")]
    [InlineData("null == inputs.missing", "true")]
    [InlineData("'it''s'", "it's")]
    public void Each_expression_form_evaluates_as_the_runner_evaluates_it(string expression, string expected) =>
        Assert.Equal(expected, WorkflowExpression.Interpolate("${{ " + expression + " }}", Values));

    [Fact]
    public void Templates_inside_a_value_are_each_replaced()
    {
        Assert.Equal("kronikol-history-octo-x", WorkflowExpression.Interpolate("kronikol-history-${{ inputs.name }}-${{inputs.a}}", Values));
        Assert.Equal("no templates", WorkflowExpression.Interpolate("no templates", Values));
    }

    [Theory]
    [InlineData("", "success", true)]
    [InlineData("", "failure", false)]
    [InlineData("inputs.flag == 'true'", "success", true)]
    [InlineData("inputs.flag == 'true'", "failure", false)]
    [InlineData("${{ inputs.flag == 'true' }}", "failure", false)]
    [InlineData("!cancelled()", "failure", true)]
    [InlineData("${{ !cancelled() && inputs.flag == 'true' }}", "failure", true)]
    [InlineData("!cancelled()", "cancelled", false)]
    [InlineData("always()", "cancelled", true)]
    [InlineData("failure()", "failure", true)]
    [InlineData("success()", "failure", false)]
    public void A_condition_without_a_status_function_reads_as_success_and_one_with_it_as_written(string condition, string status, bool expected) =>
        Assert.Equal(expected, WorkflowExpression.Condition(condition, Values, status));

    [Theory]
    [InlineData("contains(inputs.name, 'o')")]
    [InlineData("format('{0}', inputs.name)")]
    [InlineData("fromJSON(inputs.name)")]
    [InlineData("hashFiles('**/x')")]
    [InlineData("secrets.TOKEN")]
    [InlineData("vars.BRANCH")]
    [InlineData("needs.test.result")]
    [InlineData("inputs.a > 1")]
    [InlineData("github['token']")]
    [InlineData("inputs.*")]
    [InlineData("inputs")]
    [InlineData("always(1)")]
    [InlineData("'unclosed")]
    public void An_expression_outside_the_subset_is_refused(string expression) =>
        Assert.Throws<NotSupportedException>(() => WorkflowExpression.Validate(expression));

    // ─── Run steps ──────────────────────────────────────────────

    private string WriteAction(string yaml)
    {
        var directory = Path.Combine(_dir, "action-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "action.yml"), yaml.ReplaceLineEndings("\n"));
        return directory;
    }

    private RunnerJob Job() => new(Path.Combine(_dir, "job-" + Guid.NewGuid().ToString("N")[..8]), new ArtifactStore(Path.Combine(_dir, "artifacts-" + Guid.NewGuid().ToString("N")[..8])), "lane");

    [Fact]
    public void A_run_step_reads_its_inputs_through_env_and_its_files_reach_the_next_step_and_the_job()
    {
        Assert.SkipWhen(!BashProbe.IsAvailable, "bash not available");
        var action = WriteAction("""
            name: probe
            inputs:
              name:
                default: ${{ github.job }}
            outputs:
              greeting:
                value: ${{ steps.second.outputs.greeting }}
              lines:
                value: ${{ steps.first.outputs.lines }}
            runs:
              using: composite
              steps:
                - id: first
                  shell: bash
                  env:
                    WHO: ${{ inputs.name }}
                  run: |
                    echo "who=$WHO" >> "$GITHUB_OUTPUT"
                    { echo "lines<<EOF_1"; echo one; echo two; echo "EOF_1"; } >> "$GITHUB_OUTPUT"
                    echo "FROM_FIRST=set by the first step" >> "$GITHUB_ENV"
                    echo "/opt/probe/bin" >> "$GITHUB_PATH"
                    echo "### from the probe" >> "$GITHUB_STEP_SUMMARY"
                - id: second
                  shell: bash
                  env:
                    WHO: ${{ steps.first.outputs.who }}
                  run: echo "greeting=hello $WHO, $FROM_FIRST" >> "$GITHUB_OUTPUT"
            """);
        var job = Job();

        var run = CompositeActionRunner.Run(job, action);

        Assert.True(run.Succeeded, run.ToString());
        Assert.Equal("hello lane, set by the first step", run.Outputs["greeting"]);
        Assert.Equal("one\ntwo", run.Outputs["lines"]);
        Assert.Equal("set by the first step", job.Env["FROM_FIRST"]);
        Assert.Equal(["/opt/probe/bin"], job.AddedPath);
        Assert.Contains("### from the probe", job.Summary.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_environment_is_built_and_never_inherited_from_the_test_process()
    {
        Assert.SkipWhen(!BashProbe.IsAvailable, "bash not available");
        var action = WriteAction("""
            name: probe
            outputs:
              seen:
                value: ${{ steps.look.outputs.seen }}
            runs:
              using: composite
              steps:
                - id: look
                  shell: bash
                  run: |
                    echo "seen=${KRONIKOL_HISTORY-unset} ${KRONIKOL_KEEP_RUNS-unset} ${GIT_CONFIG_NOSYSTEM-} $(git config --global --list 2>/dev/null | wc -l | tr -d ' ') ${GITHUB_ACTIONS-}" >> "$GITHUB_OUTPUT"
            """);
        var job = Job();

        var run = CompositeActionRunner.Run(job, action);

        Assert.True(run.Succeeded, run.ToString());
        // The test process carries KRONIKOL_HISTORY=off from test.runsettings; a step that inherited it would test history switched off.
        Assert.Equal("unset unset 1 0 true", run.Outputs["seen"]);
    }

    [Fact]
    public void A_failed_step_fails_the_action_skips_a_default_step_and_runs_a_not_cancelled_one()
    {
        Assert.SkipWhen(!BashProbe.IsAvailable, "bash not available");
        var action = WriteAction("""
            name: probe
            runs:
              using: composite
              steps:
                - id: fails
                  shell: bash
                  run: exit 3
                - id: default
                  shell: bash
                  run: echo never
                - id: still
                  if: ${{ !cancelled() }}
                  shell: bash
                  run: echo "::notice::ran after a failure"
            """);

        var run = CompositeActionRunner.Run(Job(), action);

        Assert.False(run.Succeeded);
        Assert.Equal("failure", run.StepOutcomes["fails"]);
        Assert.Equal("skipped", run.StepOutcomes["default"]);
        Assert.Equal("success", run.StepOutcomes["still"]);
        Assert.Equal(["ran after a failure"], run.Notices);
    }

    [Fact]
    public void A_job_that_failed_before_the_action_skips_its_default_steps()
    {
        Assert.SkipWhen(!BashProbe.IsAvailable, "bash not available");
        var action = WriteAction("""
            name: probe
            runs:
              using: composite
              steps:
                - id: default
                  shell: bash
                  run: echo never
                - id: guarded
                  if: ${{ !cancelled() }}
                  shell: bash
                  run: echo ran
            """);
        var job = Job();
        job.Failed = true;

        var run = CompositeActionRunner.Run(job, action);

        Assert.Equal("skipped", run.StepOutcomes["default"]);
        Assert.Equal("success", run.StepOutcomes["guarded"]);
    }

    [Fact]
    public void Workflow_commands_become_annotations_and_masks_and_stop_commands_suspends_them()
    {
        Assert.SkipWhen(!BashProbe.IsAvailable, "bash not available");
        var action = WriteAction("""
            name: probe
            runs:
              using: composite
              steps:
                - shell: bash
                  run: |
                    echo "::add-mask::s3cret"
                    echo "::warning::50%25 done%0Aand more"
                    echo "::stop-commands::tok123"
                    echo "::error::not a command while stopped"
                    echo "::tok123::"
                    echo "::error file=x.sh,line=2::an error"
            """);

        var run = CompositeActionRunner.Run(Job(), action);

        Assert.Equal(["s3cret"], run.Masks);
        Assert.Equal(["50% done\nand more"], run.Warnings);
        Assert.Equal(["an error"], run.Errors);
    }

    [Theory]
    [InlineData("""
        name: probe
        runs:
          using: composite
          steps:
            - run: echo no shell
        """)]
    [InlineData("""
        name: probe
        runs:
          using: composite
          steps:
            - shell: pwsh
              run: echo another shell
        """)]
    [InlineData("""
        name: probe
        inputs:
          name:
            default: x
        runs:
          using: composite
          steps:
            - shell: bash
              run: echo "${{ inputs.name }}"
        """)]
    [InlineData("""
        name: probe
        runs:
          using: composite
          steps:
            - uses: actions/cache@v4
        """)]
    public void A_step_the_template_actions_may_not_have_is_refused(string yaml) =>
        Assert.Throws<NotSupportedException>(() => CompositeActionRunner.Run(Job(), WriteAction(yaml)));

    [Fact]
    public void A_call_with_an_undeclared_input_or_without_a_required_one_is_refused()
    {
        var action = WriteAction("""
            name: probe
            inputs:
              needed:
                required: true
            runs:
              using: composite
              steps:
                - uses: actions/setup-dotnet@v5
            """);

        Assert.Throws<ArgumentException>(() => CompositeActionRunner.Run(Job(), action));
        Assert.Throws<ArgumentException>(() => CompositeActionRunner.Run(Job(), action, new Dictionary<string, string> { ["needed"] = "1", ["typo"] = "2" }));
        Assert.True(CompositeActionRunner.Run(Job(), action, new Dictionary<string, string> { ["needed"] = "1" }).Succeeded);
    }

    [Fact]
    public void File_commands_take_both_forms_and_refuse_a_heredoc_left_open()
    {
        var file = Path.Combine(_dir, "output");
        File.WriteAllText(file, "a=1\nb<<END\nx\ny=z\nEND\nc=\n");
        Assert.Equal(new Dictionary<string, string> { ["a"] = "1", ["b"] = "x\ny=z", ["c"] = "" }, CompositeActionRunner.ReadKeyValues(file));

        File.WriteAllText(file, "b<<END\nx\n");
        Assert.Throws<FormatException>(() => CompositeActionRunner.ReadKeyValues(file));
    }

    // ─── The artifact service ───────────────────────────────────

    private string Tree(string name, params string[] files)
    {
        var root = Path.Combine(_dir, name);
        foreach (var file in files)
        {
            var path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }
        return root;
    }

    [Fact]
    public void A_second_upload_of_a_name_fails_as_the_service_answers_409_unless_overwrite()
    {
        var store = new ArtifactStore(Path.Combine(_dir, "store"));
        var tree = Tree("tree", "a.txt");

        Assert.Null(store.Upload("reports", [tree], false, "error", false, out _));
        Assert.Contains("(409) Conflict", store.Upload("reports", [tree], false, "error", false, out _), StringComparison.Ordinal);
        Assert.Null(store.Upload("reports", [tree], false, "error", true, out _));
        Assert.Equal(["reports"], store.Names);
    }

    [Fact]
    public void An_upload_keeps_the_tree_below_its_directory_and_leaves_out_hidden_files_unless_asked()
    {
        var store = new ArtifactStore(Path.Combine(_dir, "store"));
        var tree = Tree("tree", "Reports/History.run.json", ".logs/kronikol/History.run.json", "Reports/.hidden");

        Assert.Null(store.Upload("plain", [tree], includeHiddenFiles: false, "error", false, out _));
        Assert.Null(store.Upload("hidden", [tree], includeHiddenFiles: true, "error", false, out _));

        Assert.Equal(["Reports/History.run.json"], store.Files("plain"));
        Assert.Equal([".logs/kronikol/History.run.json", "Reports/.hidden", "Reports/History.run.json"], store.Files("hidden"));
    }

    [Theory]
    [InlineData("error", true, false)]
    [InlineData("warn", false, true)]
    [InlineData("ignore", false, false)]
    public void An_upload_with_no_files_follows_if_no_files_found(string ifNone, bool fails, bool warns)
    {
        var store = new ArtifactStore(Path.Combine(_dir, "store"));
        var failure = store.Upload("empty", [Tree("nothing")], false, ifNone, false, out var warning);

        Assert.Equal(fails, failure is not null);
        Assert.Equal(warns, warning is not null);
        Assert.Empty(store.Names);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("")]
    public void An_artifact_name_the_service_refuses_is_refused(string name) =>
        Assert.NotNull(new ArtifactStore(Path.Combine(_dir, "store")).Upload(name, [Tree("tree", "a.txt")], false, "error", false, out _));

    [Fact]
    public void A_download_puts_one_match_flat_and_several_under_their_names_and_none_nowhere()
    {
        var store = new ArtifactStore(Path.Combine(_dir, "store"));
        Assert.Null(store.Upload("kronikol-history-a", [Tree("a", "Reports/History.run.json")], false, "error", false, out _));
        Assert.Null(store.Upload("kronikol-history-b", [Tree("b", "Reports/History.run.json")], false, "error", false, out _));
        Assert.Null(store.Upload("other", [Tree("c", "x.txt")], false, "error", false, out _));

        var one = Path.Combine(_dir, "one");
        Assert.Equal(["kronikol-history-a"], store.Download("kronikol-history-a*", one, mergeMultiple: false));
        Assert.True(File.Exists(Path.Combine(one, "Reports", "History.run.json")));

        var several = Path.Combine(_dir, "several");
        Assert.Equal(["kronikol-history-a", "kronikol-history-b"], store.Download("kronikol-history-*", several, mergeMultiple: false));
        Assert.True(File.Exists(Path.Combine(several, "kronikol-history-a", "Reports", "History.run.json")));
        Assert.True(File.Exists(Path.Combine(several, "kronikol-history-b", "Reports", "History.run.json")));

        var none = Path.Combine(_dir, "none");
        Assert.Empty(store.Download("nothing-*", none, mergeMultiple: false));
        Assert.False(Directory.Exists(none));
    }
}
