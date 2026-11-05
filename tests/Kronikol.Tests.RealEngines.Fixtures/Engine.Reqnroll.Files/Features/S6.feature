@afterstepfile
Feature: S6 no Background written, two scenarios open with the same Given, a file after every step, a hook step

  @seeded
  Scenario: S6a
    Given a registered customer
    And an order of 1 is placed
    Then the order is confirmed

  @seeded
  Scenario: S6b
    Given a registered customer
    Then the order is confirmed
