@whenfirst
Feature: A hook step in every scenario, each starting with a When

Scenario: W0 confirmed
    When the customer places the order
    Then the order is confirmed

Scenario: W1 read back
    When the customer places the order
    Then the order is read back
