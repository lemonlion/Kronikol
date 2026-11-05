Feature: What did not run

  @plainhookthrows
  Scenario: N3 a plain BeforeScenario hook throws
    Given a registered customer
    When the customer places the order

  Scenario: N4 an ambiguous step after a failure
    When the payment fails
    When this step is ambiguous
    Then the order is confirmed

  Scenario: N5 a step is ambiguous
    Given a registered customer
    When this step is ambiguous
    Then the order is confirmed

  @stopsinner
  Scenario: N6 a hook ahead of Kronikol's stops an inner step and a step method runs after it
    Given a step that runs an inner step a hook stops, then a step method
    Then the order is confirmed
