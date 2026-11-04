Feature: 01 Background then When

Background:
    Given a logged in customer

Scenario: 01 one
    When the customer acts "one"
    Then the outcome is "one"

Scenario: 01 two
    When the customer acts "two"
    Then the outcome is "two"
