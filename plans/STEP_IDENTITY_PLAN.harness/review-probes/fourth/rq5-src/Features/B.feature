Feature: B a failing hook

  Background:
    Given a registered customer

  @seedfails
  Scenario: B1 the hook fails
    And an order of 1 is placed
    Then the order is confirmed

  Scenario: B2 sibling
    And an order of 2 is placed
    Then the order is confirmed

  Scenario: B3 sibling
    And an order of 3 is placed
    Then the order is confirmed
