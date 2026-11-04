Feature: Retried

@retry(2)
Scenario: R30 a retried scenario, the first attempt fails in its Then
    Given a call named r30-given
    When a call named r30-when
    Then the first attempt of r30 fails
    And a call named r30-then

@retry(2)
Scenario: R31 a retried scenario, the first attempt fails in a nested Given under a When
    Given a call named r31-given
    When the first attempt of r31 fails under a nested Given
    Then a call named r31-then

@retry(2)
Scenario: R32 a retried scenario, the first attempt fails in its Given
    Given the first attempt of r32 fails in a Given
    When a call named r32-when
    Then a call named r32-then
