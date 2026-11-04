Feature: 19 A background with a table

Background:
    Given the basket holds
        | sku   |
        | APPLE |

Scenario: 19 one
    When the customer acts "one"
    Then the outcome is "one"

Scenario: 19 two
    When the customer acts "two"
    Then the outcome is "two"
