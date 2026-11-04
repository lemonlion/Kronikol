Feature: F09

Scenario: F09-A a When runs a woven Given
    Given a call named f09-a-seed
    And a call named f09-a-seed2
    When a binding calls a woven Given named f09-a-act
    And a call named f09-a-act2
    Then a call named f09-a-then

Scenario: F09-B a Given runs a woven When
    Given a binding calls a woven When named f09-b-seed
    When a call named f09-b-act
    Then a call named f09-b-then

Scenario: F09-C plain
    Given a call named f09-c-seed
    When a call named f09-c-act
    Then a call named f09-c-then
