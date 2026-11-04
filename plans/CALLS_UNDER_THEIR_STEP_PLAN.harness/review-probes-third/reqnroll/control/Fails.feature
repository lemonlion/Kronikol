Feature: Fails

Scenario: X1 A step throws
  Given a plain given
  When a step throws
  Then I check
  And I check again

Scenario: X2 A step method inside a step throws
  Given a plain given
  When a step's sub step throws
  Then I check

Scenario: X3 A step is undefined
  Given a plain given
  When this step has no binding
  Then I check

Scenario: X4 A step is pending
  Given a plain given
  When a step is pending
  Then I check

Scenario: X5 The first step is undefined
  Given this first step has no binding
  Then I check

Scenario: X6 A sub step fails and the step catches it
  Given a plain given
  When a step catches its sub step's failure
  Then I check

Scenario: X7 A step throws after a sub step passed
  Given a plain given
  When a step throws after a sub step
  Then I check

Scenario: X8 An ambiguous step
  Given a plain given
  When an ambiguous step
  Then I check
