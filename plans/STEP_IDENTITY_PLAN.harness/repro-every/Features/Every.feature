@every
Feature: A hook step in every scenario, with a background

Background:
    Given a registered customer

Scenario: E0 with a coupon
    And the customer has a coupon
    When the customer places the order
    Then the order is confirmed

Scenario: E1 with a voucher
    And the customer has a voucher
    When the customer places the order
    Then the order is confirmed
