"""Adds a probe of #222's dependency: scenarios that share a Given whose sub-steps differ in number."""
import sys

FEATURE = """Feature: Shapes

@small
Scenario: The first scenario's shared step has one sub-step
    Given a shared step whose sub-steps depend on the scenario
    When a later step runs
    Then the shape is small

@big
Scenario: The second scenario's shared step has three sub-steps
    Given a shared step whose sub-steps depend on the scenario
    When a later step runs
    Then the shape is big
"""

BINDINGS = """    [Given("a shared step whose sub-steps depend on the scenario")]
    public async Task GivenASharedStepWhoseSubStepsDependOnTheScenario(ScenarioContext scenario)
    {
        if (scenario.ScenarioInfo.Tags.Contains("big"))
        {
            await probe.The_same_sub_step("big-one");
            await probe.The_same_sub_step("big-two");
            await probe.The_same_sub_step("big-three");
        }
        else
            await probe.The_same_sub_step("small-one");
    }

    [Then("the shape is small")]
    [Then("the shape is big")]
    public void ThenTheShapeIs() { }

    [When("a sub-step is still running at an assertion")]"""

for root in sys.argv[1:]:
    with open(f"{root}/Features/Shapes.feature", "w", encoding="utf-8") as f:
        f.write(FEATURE)
    path = f"{root}/Steps/ProbeSteps.cs"
    with open(path, encoding="utf-8") as f:
        s = f.read()
    old = '    [When("a sub-step is still running at an assertion")]'
    assert s.count(old) == 1
    with open(path, "w", encoding="utf-8") as f:
        f.write(s.replace(old, BINDINGS))
    print("added", root[-12:])
