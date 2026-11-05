Feature: Rules

  Rule: A rule with a background

    Background:
      Given a registered customer

    @seeded
    Scenario: E03 a hook step in a rule with a background
      When an order of 30 is placed
      Then the order is confirmed

    Scenario: E03b its sibling with no hook
      When an order of 31 is placed
      Then the order is confirmed
