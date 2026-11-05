Feature: S1 a file in a background step, a hook step in every scenario, all green

  Background:
    Given a customer with a screenshot

  @seeded
  Scenario: S1a
    And an order of 1 is placed
    Then the order is confirmed

  @seeded
  Scenario: S1b
    And an order of 2 is placed
    Then the order is confirmed
