Feature: F07

Scenario: F07-A a When runs a woven Given
    Given a call named f07-a-seed
    And a call named f07-a-seed2
    When a binding calls a woven Given named f07-a-act
    And a call named f07-a-act2
    Then a call named f07-a-then

Scenario: F07-B a Given runs a woven When
    Given a binding calls a woven When named f07-b-seed
    When a call named f07-b-act
    Then a call named f07-b-then

Scenario: F07-C plain
    Given a call named f07-c-seed
    When a call named f07-c-act
    Then a call named f07-c-then
