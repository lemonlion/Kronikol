Feature: Steps That Fail
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

@after-scenario-runs-a-step
Scenario: An AfterScenario hook runs a step an AfterStep hook fails
    Given a step that passes
