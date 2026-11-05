Feature: S4 a file in a background step, a hook step on the first scenario only

  Background:
    Given a customer with a screenshot

  @seeded
  Scenario: S4a
    And an order of 1 is placed
    Then the order is confirmed

  Scenario: S4b
    And an order of 2 is placed
    Then the order is confirmed
