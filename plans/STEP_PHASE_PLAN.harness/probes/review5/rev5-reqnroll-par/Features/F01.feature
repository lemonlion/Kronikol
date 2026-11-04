Feature: F01

Scenario: F01-A a When runs a woven Given
    Given a call named f01-a-seed
    And a call named f01-a-seed2
    When a binding calls a woven Given named f01-a-act
    And a call named f01-a-act2
    Then a call named f01-a-then

Scenario: F01-B a Given runs a woven When
    Given a binding calls a woven When named f01-b-seed
    When a call named f01-b-act
    Then a call named f01-b-then

Scenario: F01-C plain
    Given a call named f01-c-seed
    When a call named f01-c-act
    Then a call named f01-c-then
