Feature: N1 a hook swallows a step method failure

@swallow
Scenario: A swallowed hook failure and a real failure
    Given the shop is ready
    When the payment fails
    Then the order is listed

@swallow
Scenario: B swallowed hook failure and a pass
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
