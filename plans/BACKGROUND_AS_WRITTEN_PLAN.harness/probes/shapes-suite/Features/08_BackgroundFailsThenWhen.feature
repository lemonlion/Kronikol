Feature: 08 Background fails, then When

Background:
    Given a logged in customer

Scenario: 08 holds
    When the customer acts "holds"
    Then the outcome is "holds"

@fail-bg
Scenario: 08 breaks
    When the customer acts "breaks"
    Then the outcome is "breaks"
