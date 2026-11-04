Feature: 02 Background then Given

Background:
    Given a logged in customer

Scenario: 02 one
    Given the basket holds "apple"
    When the customer acts "one"
    Then the outcome is "one"

Scenario: 02 two
    Given the basket holds "pear"
    When the customer acts "two"
    Then the outcome is "two"
