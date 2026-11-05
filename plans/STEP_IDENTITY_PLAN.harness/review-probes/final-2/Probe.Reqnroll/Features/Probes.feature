Feature: Probes

  Scenario: Q0 control
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

  @caught
  Scenario: Q1 a hook's step method fails and the hook catches it, in a green scenario
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

  @caught
  Scenario: Q2 a hook's step method fails and is caught, and a later step fails
    Given a registered customer
    When the payment fails
    Then the order is confirmed

  Scenario: Q3 a row a consumer rebuilt in a scenario with a nested step
    Given a step that runs another step
    When the customer places the order
    Then the order is confirmed

  Scenario: Q3b a row a consumer rebuilt in a scenario with a nested step with a table
    Given a step that runs a step with a table
    When the customer places the order
    Then the order is confirmed

  @q4
  Scenario: Q4 a hook ahead of Kronikol's throws at When, after a block hook's step method
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

  @q7
  Scenario: Q7 an AfterScenario hook runs a Gherkin step
    Given a registered customer
    When the customer places the order

  @q8
  Scenario: Q8 a hook's step method left running fails later and is caught
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

  @seeded
  Scenario: Q10 a hook's step method in a scenario with no steps

  Scenario: Q15 a nested step attaches files
    Given a step that runs a step with a file
    When the customer places the order

  Scenario: Q27 a nested step is pending
    Given a step that runs a pending step
    When the customer places the order

  Scenario: Q28 a nested step nobody wrote
    Given a step that runs a step nobody wrote
    When the customer places the order

  @q18
  Scenario: Q18 a nested step's AfterStep hook throws
    Given a step that runs a step whose after step hook throws
    When the customer places the order

  @seeded @needsgpu
  Scenario: Q19 a hook's step method and a scenario that skips itself
    Given a registered customer
    When the customer places the order

  Scenario: Q20 a binding that is a step method
    Given a binding that is a step method
    When the customer places the order

  Scenario: Q21 a step method skips the test in a step
    Given a registered customer
    When a step method skips the test
    Then the order is confirmed

  @q43
  Scenario: Q43 a step method fails in AfterScenario in a green scenario
    Given a registered customer
    When the customer places the order

  @q23
  Scenario: Q23b a block hook's step method with no Background
    Given a registered customer
    When the customer places the order
