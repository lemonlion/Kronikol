Feature: Step method shapes

Scenario: The issue's value task
    When a value task step runs
    Then the next step is the Gherkin step's own

Scenario: The issue's number
    Given a number is chosen
    Then the next step is the Gherkin step's own

Scenario: A value task with a number
    When a value task step hands back a number
    Then the next step is the Gherkin step's own

Scenario: A task step and a void step, which work today
    When a task step and a void step run
    Then the next step is the Gherkin step's own

Scenario: A guard clause in a task step
    When a task step throws before it has a task

Scenario: A guard clause in a value task step
    When a value task step throws before it has a task

Scenario: A bypassed sub-step inside another step
    When a step calls a sub-step that is bypassed and then a sub-step that runs
    Then the next step is the Gherkin step's own

@byref
Scenario: A parameter passed by reference
    When a step takes a quantity by reference
    Then the next step is the Gherkin step's own
