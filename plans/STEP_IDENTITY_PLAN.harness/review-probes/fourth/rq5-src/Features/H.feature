Feature: H keyword hooks and undefined

  Background:
    Given a registered customer

  @tenanted
  Scenario: H1 a Given keyword hook
    And an order of 1 is placed
    Then the order is confirmed

  Scenario: H2 sibling
    And an order of 2 is placed
    Then the order is confirmed

  Scenario: H3 undefined step
    And nothing binds this step
    Then the order is confirmed
