Feature: Placeholder in a Background

Background:
    Given own passes for <row>

Scenario Outline: Z1 outline <row>
    When own passes

    Examples:
        | row |
        | one |
        | two |
