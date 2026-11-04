Feature: Server

# The issue's own shape, on a real server: the When binding calls the service, then a nested [GivenStep], then the service again.
Scenario: S01 a When binding with a nested GivenStep, on the default TestServer
    Given a server call named s01-given
    When a When binding calls the server around a nested GivenStep, as s01
    Then a server call named s01-then

Scenario: S02 a When binding with a nested Gherkin Given, on the default TestServer
    Given a server call named s02-given
    When a When binding calls the server around a nested Gherkin Given, as s02
    Then a server call named s02-then

Scenario: S03 a Given binding with a nested WhenStep, on the default TestServer
    Given a Given binding calls the server around a nested WhenStep, as s03
    When a server call named s03-when
    Then a server call named s03-then

Scenario: S04 And and But after a When, on the default TestServer
    Given a server call named s04-given
    And a server call named s04-given-and
    When a server call named s04-when
    And a server call named s04-when-and
    But a server call named s04-when-but
    Then a server call named s04-then

@preserve-context
Scenario: S11 a When binding with a nested GivenStep, with the test's execution context preserved into the server
    Given a server call named s11-given
    When a When binding calls the server around a nested GivenStep, as s11
    Then a server call named s11-then

@kestrel
Scenario: S21 a When binding with a nested GivenStep, on Kestrel
    Given a server call named s21-given
    When a When binding calls the server around a nested GivenStep, as s21
    Then a server call named s21-then

# A handler woven because it lives in the test assembly runs on the server's flow, during a Given.
@woven-events
Scenario: S31 a woven When on the server's flow, during a Given
    Given a server call named s31-woven-given
    And a server call named s31-given-and
    When a server call named s31-when
    Then a server call named s31-then
