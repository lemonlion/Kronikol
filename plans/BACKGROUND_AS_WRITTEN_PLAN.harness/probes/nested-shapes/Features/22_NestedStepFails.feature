Feature: 22 A background step whose inner step fails

Background:
    Given a composite step runs two steps

Scenario: 22 breaks
    When the customer acts "breaks"
    Then the outcome is "breaks"
