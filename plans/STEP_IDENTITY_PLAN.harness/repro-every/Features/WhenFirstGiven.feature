@whenfirstgiven
Feature: A Given step method in a hook, each scenario starting with a When

Scenario: G0 confirmed
    When the customer places the order
    Then the order is confirmed

Scenario: G1 read back
    When the customer places the order
    Then the order is read back
