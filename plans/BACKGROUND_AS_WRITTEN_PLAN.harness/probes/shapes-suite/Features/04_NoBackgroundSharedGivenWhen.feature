Feature: 04 No background, shared Given and When

Scenario: 04 one
    Given the basket holds
        | sku   |
        | APPLE |
    When the customer acts "the same"
    Then the outcome is "one"

Scenario: 04 two
    Given the basket holds
        | sku    |
        | BANANA |
    When the customer acts "the same"
    Then the outcome is "two"
