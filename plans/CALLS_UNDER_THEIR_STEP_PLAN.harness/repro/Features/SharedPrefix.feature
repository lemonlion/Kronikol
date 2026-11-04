Feature: Shared prefix

@happy-path
Scenario: The first scenario's order goes through
    Given the basket holds
        | sku   | qty |
        | APPLE | 1   |
    When the customer places the order
    Then the order is confirmed

@declined
Scenario: The second scenario's payment is declined
    Given the basket holds
        | sku    | qty |
        | BANANA | 2   |
    When the customer places the order
    Then the order is rejected
