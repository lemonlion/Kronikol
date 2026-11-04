Feature: 15 Background and a scenario with no steps

Background:
    Given a logged in customer

Scenario: 15 empty

Scenario: 15 full
    When the customer acts "full"
    Then the outcome is "full"
