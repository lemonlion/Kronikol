Feature: A background that runs steps

  Background:
    Given a step that runs another
    And a registered customer

  @seeded
  Scenario: E16 a hook step and a background step that runs another
    When an order of 40 is placed
    Then the order is confirmed

  Scenario: E16b its sibling
    When an order of 41 is placed
    Then the order is confirmed
