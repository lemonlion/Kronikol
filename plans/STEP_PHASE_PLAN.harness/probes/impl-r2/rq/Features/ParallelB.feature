Feature: Parallel B

@shared-handler
Scenario: PB1
    Given a call named pb1-g0
    And a call named pb1-g1
    When a call named pb1-w0
    And a call named pb1-w1
    Then a call named pb1-t

@shared-handler
Scenario: PB2
    Given a call named pb2-g0
    And a call named pb2-g1
    When a call named pb2-w0
    And a call named pb2-w1
    Then a call named pb2-t
