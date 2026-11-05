Feature: Hook steps

Scenario: T00 no hook is the control
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t01
Scenario: T01 a step method in BeforeScenario
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t02
Scenario: T02 a step method in BeforeScenarioBlock
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t03
Scenario: T03 a step method in AfterScenarioBlock
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t04
Scenario: T04 a step method in AfterScenario
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t05
Scenario: T05 a step method that fails in BeforeScenario
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t06
Scenario: T06 an AfterStep hook that throws
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t07
Scenario: T07 a BeforeStep hook that throws
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

Scenario: T08 a failing step with steps after it
    Given a registered customer
    When the payment fails
    Then the order is confirmed

@t09
Scenario: T09 a plain call in BeforeScenario with no step method
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t10
Scenario: T10 a step method in BeforeStep
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t11
Scenario: T11 a Given step method in BeforeScenario
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t12
Scenario: T12 a step method in AfterStep
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t13
Scenario: T13 a step method in BeforeScenario and a failing step
    Given a registered customer
    When the payment fails
    Then the order is confirmed

@t14
Scenario: T14 an attachment and a plain call in BeforeScenario
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t15
Scenario: T15 an AfterScenario hook that throws
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t16
Scenario: T16 a BeforeScenario hook at the same order as Kronikol's
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

Scenario: T17 a step method left running when its step ends
    Given a step method is left running
    When the customer places the order
    Then the order is confirmed

Scenario: T18 an undefined step
    Given a registered customer
    When nobody has written this step
    Then the order is confirmed

@t19
Scenario: T19 two step methods in parallel
    Given two step methods run in parallel
    When the customer places the order
    Then the order is confirmed

@t20
Scenario: T20 a plain call in AfterScenario with no step method
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

Scenario: T21 a step that runs another step
    Given a step that runs another step
    When the customer places the order
    Then the order is confirmed

@t22
Scenario: T22 a BeforeScenarioBlock hook that throws on the second block
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

Scenario: T23 two step methods left running, one ending before its step
    Given two step methods are left running
    When the order takes a while
    Then the order is confirmed

@t24
Scenario: T24 a step method left running by a hook
    Given a registered customer
    When the order takes a while
    Then the order is confirmed

Scenario: T25 a step that runs steps with arguments
    Given a step that runs steps with arguments
    When the customer places the order
    Then the order is confirmed

Scenario: T26 a step that runs two steps, the first failing
    Given a step that runs two steps, the first failing
    When the customer places the order
    Then the order is confirmed

@t27
Scenario: T27 a hook that runs a step
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

@t28
Scenario: T28 a suite reads the step stopwatch on the scenario
    Given a step that runs another step
    When the customer places the order

@t29
Scenario: T29 a hook ahead of Kronikol's throws at the first step, after a hook step
    Given a registered customer
    When the customer places the order

@t30
Scenario: T30 two step methods at once in a hook
    Given a registered customer
    When the customer places the order

@t31
Scenario: T31 a step opened by hand in an async hook, closed by its test id with its failure
    Given a registered customer
    When the customer places the order
