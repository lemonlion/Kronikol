Feature: D background step fails in one

  Background:
    Given a customer that fails when tagged
    And a second fixture

  Scenario: D1 passes first by title
    And an order of 1 is placed
    Then the order is confirmed

  @bgfails
  Scenario: D2 background fails
    And an order of 2 is placed
    Then the order is confirmed

  @bgfails @seeded
  Scenario: D3 hook then background fails
    And an order of 3 is placed
    Then the order is confirmed

  Scenario: D4 passes last by title
    And an order of 4 is placed
    Then the order is confirmed
