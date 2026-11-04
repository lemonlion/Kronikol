Feature: Shapes

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
