Feature: A hook then background

  Background:
    Given a registered customer
    And a second fixture

  @seeded
  Scenario: A1 hook before background own step fails
    And an order of 1 is placed
    Then the order fails

  @seeded
  Scenario: A2 hook before background passes
    And an order of 2 is placed
    Then the order is confirmed

  Scenario: A3 no hook
    And an order of 3 is placed
    Then the order is confirmed
