Feature: F03

Scenario: F03-A a When runs a woven Given
    Given a call named f03-a-seed
    And a call named f03-a-seed2
    When a binding calls a woven Given named f03-a-act
    And a call named f03-a-act2
    Then a call named f03-a-then

Scenario: F03-B a Given runs a woven When
    Given a binding calls a woven When named f03-b-seed
    When a call named f03-b-act
    Then a call named f03-b-then

Scenario: F03-C plain
    Given a call named f03-c-seed
    When a call named f03-c-act
    Then a call named f03-c-then
