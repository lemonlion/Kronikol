@seeded
Feature: Seeding

Scenario: Reference data is seeded by a hook
    Given a registered customer with a full basket
    When the customer places the order
    Then the order is confirmed
