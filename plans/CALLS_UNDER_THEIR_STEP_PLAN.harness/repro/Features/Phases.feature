Feature: Phases

Scenario: A Given sub-step inside a When step
    Given a registered customer with a full basket
    When the customer applies a discount through a Given sub-step
    Then the order is confirmed
