Feature: Payments

Scenario: A declined card rejects the order
    Given a registered customer with a full basket
    When the customer pays with a declined card
    Then the order is rejected
