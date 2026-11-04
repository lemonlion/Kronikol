Feature: 21 A background step that runs steps with arguments

Background:
    Given a composite step passes arguments

Scenario: 21 one
    When the customer acts "one"
    Then the outcome is "one"

Scenario: 21 two
    When the customer acts "two"
    Then the outcome is "two"
