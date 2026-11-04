Feature: F08

Scenario: F08-A a When runs a woven Given
    Given a call named f08-a-seed
    And a call named f08-a-seed2
    When a binding calls a woven Given named f08-a-act
    And a call named f08-a-act2
    Then a call named f08-a-then

Scenario: F08-B a Given runs a woven When
    Given a binding calls a woven When named f08-b-seed
    When a call named f08-b-act
    Then a call named f08-b-then

Scenario: F08-C plain
    Given a call named f08-c-seed
    When a call named f08-c-act
    Then a call named f08-c-then
