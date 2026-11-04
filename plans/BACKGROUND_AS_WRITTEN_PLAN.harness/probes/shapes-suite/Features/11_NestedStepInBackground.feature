Feature: 11 A background step that calls a step

Background:
    Given a composite step calls another

Scenario: 11 one
    When the customer acts "one"
    Then the outcome is "one"

Scenario: 11 two
    When the customer acts "two"
    Then the outcome is "two"
