Feature: F05

Scenario: F05-A a When runs a woven Given
    Given a call named f05-a-seed
    And a call named f05-a-seed2
    When a binding calls a woven Given named f05-a-act
    And a call named f05-a-act2
    Then a call named f05-a-then

Scenario: F05-B a Given runs a woven When
    Given a binding calls a woven When named f05-b-seed
    When a call named f05-b-act
    Then a call named f05-b-then

Scenario: F05-C plain
    Given a call named f05-c-seed
    When a call named f05-c-act
    Then a call named f05-c-then
