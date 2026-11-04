Feature: Orders

@happy-path
Scenario: A customer's order is confirmed
    Given a registered customer with a full basket
    When the customer places the order
    Then the order is confirmed

Scenario: A customer's order sends a confirmation email
    Given a registered customer with a full basket
    When the customer places the order
    Then a confirmation email is sent
