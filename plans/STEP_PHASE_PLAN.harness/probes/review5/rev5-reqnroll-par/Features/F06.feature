Feature: F06

Scenario: F06-A a When runs a woven Given
    Given a call named f06-a-seed
    And a call named f06-a-seed2
    When a binding calls a woven Given named f06-a-act
    And a call named f06-a-act2
    Then a call named f06-a-then

Scenario: F06-B a Given runs a woven When
    Given a binding calls a woven When named f06-b-seed
    When a call named f06-b-act
    Then a call named f06-b-then

Scenario: F06-C plain
    Given a call named f06-c-seed
    When a call named f06-c-act
    Then a call named f06-c-then
