Feature: A rules

  Background:
    Given the shop is ready

  Rule: a rule with a background

    Background:
      Given the rule background runs
      And the rule background fails when asked

    Scenario: A1 passes under the rule
      When the customer places the order
      Then the order is confirmed

    @failbg
    Scenario: A2 the rule background fails
      When the customer places the order
      Then the order is confirmed

    @failgiven
    Scenario: A3 the feature background fails under the rule
      When the customer places the order
      Then the order is confirmed

    Scenario: A4 an own step is undefined under the rule
      When nobody defined this rule step
      Then the order is confirmed

    Scenario: A5 an own step fails under the rule
      When the payment fails
      Then the order is confirmed

  Rule: a rule with no background

    Scenario: A6 passes under the plain rule
      When the customer places the order
      Then the order is confirmed

    Scenario: A7 fails under the plain rule
      When the payment fails
      Then the order is confirmed

    Scenario Outline: A8 an outline under a rule
      When the payment <fate>
      Then order <n> is read back

      Examples:
        | fate   | n |
        | passes | 1 |
        | breaks | 2 |
