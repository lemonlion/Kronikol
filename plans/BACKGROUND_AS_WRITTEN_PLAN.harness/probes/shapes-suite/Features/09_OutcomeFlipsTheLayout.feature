Feature: 09 Outcome decides the layout

Scenario: 09 one
    Given a logged in customer
    And the address is checked
    When the customer acts "the same"
    Then the outcome is "one"

@fail-and @fail-when
Scenario: 09 two
    Given a logged in customer
    And the address is checked
    When the customer acts "the same"
    Then the outcome is "two"
