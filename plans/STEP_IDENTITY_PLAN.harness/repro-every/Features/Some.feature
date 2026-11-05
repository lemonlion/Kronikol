@some
Feature: A hook step in one scenario, with a background

Background:
    Given a registered customer

@onehook
Scenario: S0 with a coupon
    And the customer has a coupon
    When the customer places the order
    Then the order is confirmed

Scenario: S1 with a voucher
    And the customer has a voucher
    When the customer places the order
    Then the order is confirmed
