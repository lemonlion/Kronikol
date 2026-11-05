Feature: E after hooks

  Background:
    Given a registered customer

  @cleaned
  Scenario: E1 after hook step passes
    And an order of 1 is placed
    Then the order is confirmed

  @cleanfails
  Scenario: E2 after hook step fails
    And an order of 2 is placed
    Then the order is confirmed

  Scenario: E3 no hook
    And an order of 3 is placed
    Then the order is confirmed
