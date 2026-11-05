Feature: B2 an undefined background step

  Background:
    Given the shop is ready
    And nobody defined this background step
    And the shop is stocked

  Scenario: B21 first under an undefined background
    When the customer places the order

  Scenario: B22 second under an undefined background
    When the customer places the order
