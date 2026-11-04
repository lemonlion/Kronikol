Feature: Probes

Scenario: Two sub-steps run at once
    When two sub-steps run at once

Scenario: A call outlives its step
    Given a call is started and left running
    When a later step runs

Scenario: A step whose label is too long to draw on one line and whose calls therefore lose their step, as reported in issue one hundred and twenty eight
    Given a step with a long label and a call
    When a later step runs

Scenario: An assertion ends before a sub-step that began first
    When a sub-step is still running at an assertion

Scenario: One sub-step twice
    Given one sub-step runs twice

Scenario: A call in the step and calls in its sub-steps
    Given a step makes its own calls around a sub-step

Scenario: A step whose own label is too long to draw on one line
    Given a basket that held two apples and a pear last week but nothing at all this week, while the shop's prices rose
    When a later step runs

Scenario: A step whose label holds a backslash
    Given the shopper uploads C:\baskets\monday.csv
    When a later step runs

Scenario: One of two sub-steps at once fails
    When two sub-steps run at once and the first fails

Scenario: A sub-step is left running past its step
    Given a sub-step is started and left running
    When a later step runs

Scenario: A tracked call and an assertion from another thread
    When a sub-step hands its call to another thread
