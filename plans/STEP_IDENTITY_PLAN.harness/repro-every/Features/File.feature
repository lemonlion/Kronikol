@everyfile
Feature: A hook step that attaches a file in every scenario, with a background

Background:
    Given a registered customer

Scenario: F0 with a coupon
    And the customer has a coupon
    When the customer places the order
    Then the order is confirmed

Scenario: F1 with a voucher
    And the customer has a voucher
    When the customer places the order
    Then the order is confirmed
