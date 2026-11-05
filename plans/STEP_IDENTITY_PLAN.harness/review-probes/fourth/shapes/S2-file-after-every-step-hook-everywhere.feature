@afterstepfile
Feature: S2 a file after every step, a hook step in every scenario, all green

  Background:
    Given a registered customer

  @seeded
  Scenario: S2a
    And an order of 1 is placed
    Then the order is confirmed

  @seeded
  Scenario: S2b
    And an order of 2 is placed
    Then the order is confirmed
