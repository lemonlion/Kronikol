Feature: 23 A hook that runs a step, beside a background

Background:
    Given a logged in customer

@hook-runs-gherkin
Scenario: 23 gherkin from a hook
    When the customer acts "gherkin"
    Then the outcome is "gherkin"

@hook-runs-woven
Scenario: 23 woven from a hook
    When the customer acts "woven"
    Then the outcome is "woven"
