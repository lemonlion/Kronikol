@blockhook
Feature: C block hooks inside the background

  Background:
    Given a registered customer
    And a second fixture

  Scenario: C1 block hook
    And an order of 1 is placed
    Then the order is confirmed

  Scenario: C2 block hook
    And an order of 2 is placed
    Then the order fails
