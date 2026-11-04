#!/usr/bin/env python
"""Section 6.3's real run, as a first cut (apply_examples.py <worktree>): the only place the hooks themselves are exercised.

The same three features, one step definitions file, in the three Reqnroll example projects. A step that fails does so only when KRONIKOL_EXAMPLE_STEP_FAILURES is set,
which only ReqNRollDuplicateStepsTests does through TestProjectRunner, so each project's own run, which CI asserts, stays green.
"""
import os
import sys

root = sys.argv[1]
TESTS = "examples/Example.Api/tests"
PROJECTS = {
    "Example.Api.Tests.Component.ReqNRoll.xUnit2": "Example.Api.Tests.Component.ReqNRoll.xUnit2.StepDefinitions",
    "Example.Api.Tests.Component.ReqNRoll.xUnit3": "Example.Api.Tests.Component.ReqNRoll.xUnit3.StepDefinitions",
    "Example.Api.Tests.Component.ReqNRoll.TUnit": "Example.Api.Tests.Component.ReqNRollTUnit.StepDefinitions",
}

AS_WRITTEN = """@reads-the-public-list
Feature: Steps As Written
    A Background is what this file writes, and a step a step definition runs is that step's sub-step.

Background:
    Given a step that passes
    And a step runs a step with a table and a doc string

Scenario: A scenario's own steps come after its Background
    When another step that passes
    Then a third step that passes

Rule: A rule's Background follows its feature's

    Background:
        Given a rule's step that passes

    Scenario: A scenario in a rule runs both Backgrounds
        When another step that passes
"""

THAT_FAIL = """Feature: Steps That Fail
    Each fails only when the Integration project asks, with KRONIKOL_EXAMPLE_STEP_FAILURES, so this project's own run stays green.

Scenario: An inner step fails
    Given a step runs a step that fails when asked
    Then a third step that passes

Scenario: An inner argument does not convert
    Given a step runs a step whose argument does not convert when asked

@first-before-step-fails
Scenario: A BeforeStep hook ahead of the adapter's fails
    Given a step that passes
    And a step a hook stops when asked

@first-before-step-fails
Scenario: A BeforeStep hook ahead of the adapter's fails on an inner step
    Given a step runs a step a hook stops when asked

@after-step-fails
Scenario: An AfterStep hook fails on an inner step
    Given a step runs a step an AfterStep hook fails when asked

@after-step-fails
Scenario: An AfterStep hook fails on a scenario's own step
    Given a step that passes
    When a step an AfterStep hook fails when asked
"""

BACKGROUND_THAT_FAILS = """Feature: A Background That Fails
    Its step fails only when the Integration project asks, with KRONIKOL_EXAMPLE_STEP_FAILURES.

Background:
    Given a Background step that fails when asked

Scenario: The Background step is the one that failed
    When another step that passes
"""

DEFINITIONS = """using Kronikol.ReqNRoll;
using Reqnroll;

namespace @NAMESPACE@;

/// <summary>
/// What runs the adapter's own hooks for real: a Background as written, a step that runs steps, and a failure in either. A step
/// that fails does so only when the Integration project's ReqNRollDuplicateStepsTests sets KRONIKOL_EXAMPLE_STEP_FAILURES.
/// </summary>
[Binding]
public class StepsAsWrittenStepDefinitions(ScenarioContext scenario, ITestRunner runner)
{
    private static bool AskedToFail => Environment.GetEnvironmentVariable("KRONIKOL_EXAMPLE_STEP_FAILURES") == "true";

    private static void FailIfAsked(string what)
    {
        if (AskedToFail)
            throw new InvalidOperationException(what);
    }

    [StepDefinition("a step that passes")]
    [StepDefinition("another step that passes")]
    [StepDefinition("a third step that passes")]
    [StepDefinition("a rule's step that passes")]
    public void AStepThatPasses()
    {
    }

    [StepDefinition("a step runs a step with a table and a doc string")]
    public async Task AStepRunsAStepWithATableAndADocString()
    {
        var table = new Table("sku");
        table.AddRow("APPLE");
        await runner.GivenAsync("an inner step takes a table", null, table);
        await runner.GivenAsync("an inner step takes a doc string", "a doc string", null);
    }

    [StepDefinition("an inner step takes a table")]
    public void AnInnerStepTakesATable(Table table)
    {
    }

    [StepDefinition("an inner step takes a doc string")]
    public void AnInnerStepTakesADocString(string docString)
    {
    }

    [StepDefinition("a Background step that fails when asked")]
    public void ABackgroundStepThatFails() => FailIfAsked("a Background step failed");

    [StepDefinition("a step runs a step that fails when asked")]
    public Task AStepRunsAStepThatFails() => runner.GivenAsync("an inner step that fails when asked");

    [StepDefinition("an inner step that fails when asked")]
    public void AnInnerStepThatFails() => FailIfAsked("an inner step failed");

    [StepDefinition("a step runs a step whose argument does not convert when asked")]
    public Task AStepRunsAStepWhoseArgumentDoesNotConvert() =>
        runner.GivenAsync($"a step takes the number \\"{(AskedToFail ? "not a number" : "1")}\\"");

    [StepDefinition("a step takes the number \\"(.*)\\"")]
    public void AStepTakesTheNumber(int number)
    {
    }

    [StepDefinition("a step a hook stops when asked")]
    [StepDefinition("a step an AfterStep hook fails when asked")]
    public void AStepAHookActsOn()
    {
    }

    [StepDefinition("a step runs a step a hook stops when asked")]
    public Task AStepRunsAStepAHookStops() => runner.GivenAsync("a step a hook stops when asked");

    [StepDefinition("a step runs a step an AfterStep hook fails when asked")]
    public Task AStepRunsAStepAnAfterStepHookFails() => runner.GivenAsync("a step an AfterStep hook fails when asked");

    private string StepText => scenario.StepContext.StepInfo.Text;

    // The one order that can run ahead of the adapter's own BeforeStep, whose Order is int.MinValue.
    [BeforeStep("first-before-step-fails", Order = int.MinValue)]
    public void FirstBeforeStepFails()
    {
        if (StepText == "a step a hook stops when asked")
            FailIfAsked("a BeforeStep hook ahead of the adapter's failed");
    }

    [AfterStep("after-step-fails")]
    public void AfterStepFails()
    {
        if (StepText == "a step an AfterStep hook fails when asked")
            FailIfAsked("an AfterStep hook failed");
    }

    // The list a suite can read holds every step that ran, a step a step ran among them.
    [AfterScenario("reads-the-public-list")]
    public void ReadsThePublicList()
    {
        var steps = (List<ReqNRollStepInfo>)scenario[ReqNRollConstants.StepsCollectionKey];
        if (!steps.Any(step => step.Text == "an inner step takes a table"))
            throw new InvalidOperationException("The steps list lost a step a step ran.");
    }
}
"""


def write(path, text):
    full = root + "/" + path
    os.makedirs(os.path.dirname(full), exist_ok=True)
    open(full, "w", encoding="utf-8", newline="\n").write(text)


for project, namespace in PROJECTS.items():
    write(f"{TESTS}/{project}/Features/StepsAsWritten.feature", AS_WRITTEN)
    write(f"{TESTS}/{project}/Features/StepsThatFail.feature", THAT_FAIL)
    write(f"{TESTS}/{project}/Features/BackgroundThatFails.feature", BACKGROUND_THAT_FAILS)
    write(f"{TESTS}/{project}/StepDefinitions/StepsAsWrittenStepDefinitions.cs", DEFINITIONS.replace("@NAMESPACE@", namespace))

facts = root + "/" + TESTS + "/Example.Api.Tests.Integration/Tests/ReqNRollDuplicateStepsTests.cs"
text = open(facts, encoding="utf-8", newline="").read()
old = """    public static TheoryData<string> XUnit3Only() """
assert text.count(old) == 1
new = """    [Theory]
    [MemberData(nameof(ReqNRollProjects))]
    public async Task A_Background_is_what_the_feature_file_writes_and_a_step_a_step_ran_is_no_row(string projectName)
    {
        var scenarios = await RunAndParseScenariosAsync(projectName);

        var own = Assert.Single(scenarios, s => s.Name == "A scenario's own steps come after its Background");
        Assert.Equal(["Given a step that passes", "And a step runs a step with a table and a doc string"], own.BackgroundSteps);
        Assert.Equal(["When another step that passes", "Then a third step that passes"], own.Steps);

        var inARule = Assert.Single(scenarios, s => s.Name == "A scenario in a rule runs both Backgrounds");
        Assert.Equal(["Given a step that passes", "And a step runs a step with a table and a doc string", "Given a rule's step that passes"], inARule.BackgroundSteps);
        Assert.Equal(["When another step that passes"], inARule.Steps);
    }

    [Theory]
    [MemberData(nameof(ReqNRollProjects))]
    public async Task A_failure_is_read_on_the_step_that_failed(string projectName)
    {
        var result = await TestProjectRunner.RunAsync(projectName, new Dictionary<string, string> { ["KRONIKOL_EXAMPLE_STEP_FAILURES"] = "true", ["GITHUB_STEP_SUMMARY"] = "" });
        Assert.False(result.Success, $"{projectName} was asked to fail and did not:\\n{result.StandardOutput}");

        var failing = (await File.ReadAllLinesAsync(Path.Combine(result.ReportsFolderPath, "Failures.jsonl")))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Where(row => row.GetProperty("kind").GetString() == "failure")
            .ToDictionary(
                row => row.GetProperty("scenario").GetString()!,
                row => row.GetProperty("failingSteps").EnumerateArray().Select(step => step.GetProperty("text").GetString()!).ToArray());

        Assert.Equal(7, failing.Count);
        Assert.Equal(["Given a Background step that fails when asked"], failing["The Background step is the one that failed"]);
        Assert.Equal(["Given a step runs a step that fails when asked", "Given an inner step that fails when asked"], failing["An inner step fails"]);
        Assert.Contains("Given a step runs a step whose argument does not convert when asked", failing["An inner argument does not convert"]);
        Assert.Equal(["And a step a hook stops when asked"], failing["A BeforeStep hook ahead of the adapter's fails"]);
        Assert.Equal(["Given a step runs a step a hook stops when asked", "Given a step a hook stops when asked"], failing["A BeforeStep hook ahead of the adapter's fails on an inner step"]);
        Assert.Equal(["Given a step runs a step an AfterStep hook fails when asked", "Given a step an AfterStep hook fails when asked"], failing["An AfterStep hook fails on an inner step"]);
        Assert.Equal(["When a step an AfterStep hook fails when asked"], failing["An AfterStep hook fails on a scenario's own step"]);
    }

    public static TheoryData<string> XUnit3Only() """
text = text.replace(old, new)
if "using System.Text.Json;" not in text:
    text = text.replace("using Example.Api.Tests.Integration.Helpers;\n", "using System.Text.Json;\nusing Example.Api.Tests.Integration.Helpers;\n", 1)
open(facts, "w", encoding="utf-8", newline="").write(text)
print("ok")
