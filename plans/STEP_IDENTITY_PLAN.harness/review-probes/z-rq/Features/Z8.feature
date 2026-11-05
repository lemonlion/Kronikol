Feature: N2 outline rows sharing text, one failing at its Given

@fail-given-row
Scenario Outline: Row <row>
    Given the shop is ready for <row>
    When the customer orders
    Then the order is listed

Examples:
    | row |
    | one |
    | two |
