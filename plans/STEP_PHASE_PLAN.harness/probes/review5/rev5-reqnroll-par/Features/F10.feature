Feature: F10

Scenario: F10-A a When runs a woven Given
    Given a call named f10-a-seed
    And a call named f10-a-seed2
    When a binding calls a woven Given named f10-a-act
    And a call named f10-a-act2
    Then a call named f10-a-then

Scenario: F10-B a Given runs a woven When
    Given a binding calls a woven When named f10-b-seed
    When a call named f10-b-act
    Then a call named f10-b-then

Scenario: F10-C plain
    Given a call named f10-c-seed
    When a call named f10-c-act
    Then a call named f10-c-then
