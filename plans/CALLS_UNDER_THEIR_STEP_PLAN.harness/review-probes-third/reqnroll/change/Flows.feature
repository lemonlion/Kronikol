Feature: FlowsF

Scenario: L1 Parallel sub steps in a step
  Given a step runs two sub steps at once
  Then I check

Scenario: L2 A task started in one Gherkin step calls in the next
  Given a step starts a task
  When the task is released
  Then I check

Scenario: L3 A sub step left running into the next Gherkin step
  Given a step leaves a sub step running
  When the left sub step calls
  Then I check

Scenario: L4 A sync SkipIf inside a step
  Given a step with a sync bypass
  Then I check

Scenario: L5 An async SkipIf inside a step
  Given a step with an async bypass
  Then I check

Scenario: L6 Attachments everywhere
  Given a step attaches
  Then I check

Scenario: L7 Assertion in a step
  Given a step asserts
  Then I check

Scenario: L8 Async SkipIf then a failure in the same step
  Given a step with an async bypass then a failure
  Then I check

Scenario Outline: L9 Outline rows
  Given a plain given
  When I act "<row>"
  Then I check

Examples:
  | row |
  | r1  |
  | r2  |
  | r3  |
