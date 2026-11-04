Feature: Outline table

Scenario Outline: An order line for <sku>
    Given the basket holds
        | sku   | qty   |
        | <sku> | <qty> |
    When the customer places the order
    Then the order is confirmed

Examples:
    | sku    | qty |
    | APPLE  | 1   |
    | BANANA | 2   |
