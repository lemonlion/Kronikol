Feature: K files on a failed scenario

  @shotafter
  Scenario: K1 a failing step, a file attached in AfterScenario
    Given a registered customer
    When the payment fails
    Then the order is confirmed

  @shotafterstep
  Scenario: K2 a failing step, a file attached in AfterStep
    Given a registered customer
    When the payment fails
    Then the order is confirmed

  @shotbefore
  Scenario: K3 a file attached in BeforeScenario, a failing first step
    When the payment fails
    Then the order is confirmed

  @shotafter @hookthrows
  Scenario: K4 a plain hook throws, a file attached in AfterScenario
    Given a registered customer
    When the customer places the order

  @shotafter
  Scenario: K5 an undefined step, a file attached in AfterScenario
    Given a registered customer
    When nobody defined this step in K5
    Then the order is confirmed
