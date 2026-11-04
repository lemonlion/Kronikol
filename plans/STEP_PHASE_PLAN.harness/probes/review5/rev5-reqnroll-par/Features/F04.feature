Feature: F04

Scenario: F04-A a When runs a woven Given
    Given a call named f04-a-seed
    And a call named f04-a-seed2
    When a binding calls a woven Given named f04-a-act
    And a call named f04-a-act2
    Then a call named f04-a-then

Scenario: F04-B a Given runs a woven When
    Given a binding calls a woven When named f04-b-seed
    When a call named f04-b-act
    Then a call named f04-b-then

Scenario: F04-C plain
    Given a call named f04-c-seed
    When a call named f04-c-act
    Then a call named f04-c-then
