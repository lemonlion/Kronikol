Feature: Runner

Scenario: R1 A step runs another through ITestRunner
  Given a plain given
  When a step runs another
  Then I check

Scenario: R2 The inner step fails
  Given a plain given
  When a step runs another that fails
  Then I check

Scenario: R3 The inner step is undefined
  Given a plain given
  When a step runs an undefined one
  Then I check

Scenario: R4 A step method runs another step through ITestRunner
  Given a plain given
  When a step method runs a gherkin step
  Then I check

Scenario: R5 A step runs two, and calls after each
  When a step runs two others
  Then I check
