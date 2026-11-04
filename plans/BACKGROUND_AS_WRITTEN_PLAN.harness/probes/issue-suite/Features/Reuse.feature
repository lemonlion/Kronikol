Feature: Reuse

Scenario: A step definition that is also a woven step
    Given a registered customer
    When the customer places the order
    Then the order is confirmed
