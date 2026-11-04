Feature: 13 Star keyword

Background:
    * a logged in customer

Scenario: 13 one
    * the basket holds "apple"
    Then the outcome is "one"

Scenario: 13 two
    * the basket holds "pear"
    Then the outcome is "two"
