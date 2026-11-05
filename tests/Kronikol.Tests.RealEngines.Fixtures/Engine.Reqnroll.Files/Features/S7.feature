@attachthenthrow
Feature: S7 an AfterStep hook attaches a file then throws in the first scenario, its opening step shared

  @armed
  Scenario: S7a the hook throws after the opening step
    Given a registered customer
    And an order of 1 is placed
    Then the order is confirmed

  Scenario: S7b passes
    Given a registered customer
    And an order of 2 is placed
    Then the order is confirmed
