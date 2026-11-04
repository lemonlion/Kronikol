Feature: 07 Feature and rule backgrounds

Background:
    Given a logged in customer

Rule: 07 rule with its own background

    Background:
        Given the address is checked

    Example: 07 a one
        When the customer acts "a one"
        Then the outcome is "a one"

    Example: 07 a two
        When the customer acts "a two"
        Then the outcome is "a two"

Rule: 07 rule without one

    Example: 07 b one
        When the customer acts "b one"
        Then the outcome is "b one"

    Example: 07 b two
        When the customer acts "b two"
        Then the outcome is "b two"
