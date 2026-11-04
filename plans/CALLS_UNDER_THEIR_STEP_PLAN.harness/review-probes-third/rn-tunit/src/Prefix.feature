Feature: Prefix

Scenario: P1 Common prefix one
  Given a shared given
  And a second shared given
  When I act "p1"
  Then I check

Scenario: P2 Common prefix two
  Given a shared given
  And a second shared given
  When I act "p2"
  Then I check
