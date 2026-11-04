Feature: 17 Background fails, then Then

Background:
    Given a logged in customer

Scenario: 17 holds
    Then the outcome is "holds"

@fail-bg2
Scenario: 17 breaks
    Then the outcome is "breaks"
