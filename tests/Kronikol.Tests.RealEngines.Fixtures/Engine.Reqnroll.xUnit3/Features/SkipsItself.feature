Feature: Skips itself

  @skipsitself
  Scenario: X2 a hook's step method skips the test
    Given a registered customer

  @catchesfailedstep
  Scenario: N1 a hook catches a failed step method and a step skips itself
    Given a registered customer
    When a step skips itself
    Then the order is confirmed

  @stepfailsafter
  Scenario: N2 a step skips itself and an AfterScenario step method fails
    Given a registered customer
    When a step skips itself
    Then the order is confirmed
