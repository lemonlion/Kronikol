Feature: D ignored

  @ignore
  Scenario: D1 an ignored scenario
    Given a registered customer
    When the customer places the order

  Scenario: D2 passes beside it
    Given a registered customer
    When the customer places the order

  Scenario Outline: D3 an outline with an ignored Examples block
    When the payment <fate>

    Examples: run
      | fate   |
      | passes |

    @ignore
    Examples: ignored
      | fate   |
      | breaks |
