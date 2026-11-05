@afterstepfile
Feature: BG12 a file after every step, one scenario skips itself in a hook

  Scenario: BG12a passes
    Given a registered customer
    And an order of 121 is placed
    Then the order is confirmed

  @needsgpu
  Scenario: BG12b skips itself
    Given a registered customer
    And an order of 122 is placed
    Then the order is confirmed

  Scenario: BG12c passes
    Given a registered customer
    And an order of 123 is placed
    Then the order is confirmed
