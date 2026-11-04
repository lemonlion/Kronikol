Feature: E50 External data under a Background

Background:
    Given the basket holds "from the background"

@DataSource:E50.csv
Scenario Outline: E50 from a file alone
    When the customer acts "<row>"
    Then the outcome is "<row>"

@DataSource:E50.csv
Scenario Outline: E50 from a file and written
    When the customer acts "<row>"
    Then the outcome is "<row>"

Examples:
    | row     |
    | written |

Scenario: E50 no file
    When the customer acts "no file"
    Then the outcome is "no file"
