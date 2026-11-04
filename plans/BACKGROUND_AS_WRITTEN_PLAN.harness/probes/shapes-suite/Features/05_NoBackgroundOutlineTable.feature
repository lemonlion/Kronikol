Feature: 05 No background, outline with placeholders in a table

Scenario Outline: 05 row <sku>
    Given the basket holds
        | sku   |
        | <sku> |
    When the customer acts "the same"
    Then the outcome is "the same"

Examples:
    | sku    |
    | APPLE  |
    | BANANA |
