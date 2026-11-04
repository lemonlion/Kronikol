Feature: F12

Scenario: F12-A a When runs a woven Given
    Given a call named f12-a-seed
    And a call named f12-a-seed2
    When a binding calls a woven Given named f12-a-act
    And a call named f12-a-act2
    Then a call named f12-a-then

Scenario: F12-B a Given runs a woven When
    Given a binding calls a woven When named f12-b-seed
    When a call named f12-b-act
    Then a call named f12-b-then

Scenario: F12-C plain
    Given a call named f12-c-seed
    When a call named f12-c-act
    Then a call named f12-c-then
