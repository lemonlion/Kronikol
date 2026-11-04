Feature: F02

Scenario: F02-A a When runs a woven Given
    Given a call named f02-a-seed
    And a call named f02-a-seed2
    When a binding calls a woven Given named f02-a-act
    And a call named f02-a-act2
    Then a call named f02-a-then

Scenario: F02-B a Given runs a woven When
    Given a binding calls a woven When named f02-b-seed
    When a call named f02-b-act
    Then a call named f02-b-then

Scenario: F02-C plain
    Given a call named f02-c-seed
    When a call named f02-c-act
    Then a call named f02-c-then
