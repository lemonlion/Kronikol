Feature: B3 a pending background step

  Background:
    Given a background step is pending
    And the shop is stocked

  Scenario: B31 first under a pending background
    When the customer places the order

  Scenario: B32 second under a pending background
    When the customer places the order
