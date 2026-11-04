Feature: F11

Scenario: F11-A a When runs a woven Given
    Given a call named f11-a-seed
    And a call named f11-a-seed2
    When a binding calls a woven Given named f11-a-act
    And a call named f11-a-act2
    Then a call named f11-a-then

Scenario: F11-B a Given runs a woven When
    Given a binding calls a woven When named f11-b-seed
    When a call named f11-b-act
    Then a call named f11-b-then

Scenario: F11-C plain
    Given a call named f11-c-seed
    When a call named f11-c-act
    Then a call named f11-c-then
