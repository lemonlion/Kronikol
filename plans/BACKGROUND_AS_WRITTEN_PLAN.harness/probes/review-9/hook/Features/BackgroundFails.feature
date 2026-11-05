Feature: Background fails

Background:
    Given a background step that fails on row two

Scenario Outline: B1 outline <row>
    When own passes for <row>

    Examples:
        | row |
        | one |
        | two |
        | three |

Scenario: B2 plain after it
    When own passes
