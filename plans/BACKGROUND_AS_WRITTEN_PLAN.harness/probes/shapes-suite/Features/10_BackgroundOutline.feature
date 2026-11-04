Feature: 10 Background and an outline

Background:
    Given a logged in customer

Scenario Outline: 10 row <what>
    When the customer acts "<what>"
    Then the outcome is "<what>"

Examples:
    | what |
    | x    |
    | y    |
