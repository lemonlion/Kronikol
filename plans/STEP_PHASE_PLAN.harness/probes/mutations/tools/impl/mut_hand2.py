"""Mutants of what R1's review changed, for mut_facts.py."""
SC = 'src/Kronikol/Tracking/StepCollector.cs'
BD = 'src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs'
LB = 'src/Kronikol.LightBDD.Core/StepTrackingStepDecorator.cs'
PU = 'src/Kronikol/PlantUml/PlantUmlCreator.cs'

MUTANTS = {
    'rv01': {'desc': 'Without by recursion again',
             'edits': [(SC, r'''        var above = new Stack<FlowStep>();
        for (var at = head; !ReferenceEquals(at, node); at = at.Parent!)
            above.Push(at);

        var rebuilt = node.Parent;
        while (above.TryPop(out var step))
            rebuilt = step with { Parent = rebuilt };
        return rebuilt;
''', r'''        return ReferenceEquals(head, node) ? node.Parent : head with { Parent = Without(head.Parent!, node) };
''')]},
    'rv02': {'desc': "BypassStep leaves its node on this flow",
             'edits': [(SC, r'''            if (CurrentFlowStep.Value is { } head && ReferenceEquals(head.Step, bypassed))
                CurrentFlowStep.Value = head.Parent;
''', '')]},
    'rv03': {'desc': 'ClearSteps does not close what it clears',
             'edits': [(SC, r'''                foreach (var open in state.StepStack)
                    open.EndTime = Stopwatch.GetTimestamp();
''', r'''                _ = state.StepStack;
''')]},
    'rv04': {'desc': "ClearSteps leaves this flow's nodes",
             'edits': [(SC, r'''            if (node.TestId == testId)
                CurrentFlowStep.Value = Without(CurrentFlowStep.Value!, node);
''', r'''            if (node.TestId == testId + "-not")
                CurrentFlowStep.Value = Without(CurrentFlowStep.Value!, node);
''')]},
    'rv05': {'desc': "LightBDD's catch closes the stack's top alone",
             'edits': [(LB, 'StepCollector.CompleteDownTo(testId, depth, passed: false);', 'StepCollector.CompleteStep(testId, passed: false);')]},
    'rv06': {'desc': "BDDfy's catch closes the stack's top alone",
             'edits': [(BD, 'StepCollector.CompleteDownTo(testId, depth, passed: false);', 'StepCollector.CompleteStep(testId, passed: false);')]},
    'rv07': {'desc': "TakeOffFlow drops what is above the step",
             'edits': [(SC, r'''                CurrentFlowStep.Value = Without(CurrentFlowStep.Value!, node);
                return;
''', r'''                CurrentFlowStep.Value = node.Parent;
                return;
''')]},
    'rv08': {'desc': 'TakeOffFlow does nothing',
             'edits': [(SC, r'''                CurrentFlowStep.Value = Without(CurrentFlowStep.Value!, node);
                return;
''', r'''                return;
''')]},
    'rv09': {'desc': 'CompleteDownTo closes one step too many',
             'edits': [(SC, 'while (state.StepStack.Count > depth)', 'while (state.StepStack.Count > depth - 1 && state.StepStack.Count > 0)')]},
    'rw01': {'desc': "a woven Then draws the boundary",
             'edits': [(SC, '''
                        && (frameworkStep || keyword != "Then");''', ''';''')]},
    'rw02': {'desc': 'the last marker always the boundary',
             'edits': [(PU, 'var replacing = traces.FindLastIndex(t => t.IsActionStart && t.PlantUml == DefaultTrackingDiagramOverride.ReplacesBoundary);', 'var replacing = traces.FindLastIndex(t => t.IsActionStart);')]},
    'rw03': {'desc': 'the first marker always the boundary',
             'edits': [(PU, 'return replacing >= 0 ? replacing : traces.FindIndex(t => t.IsActionStart);', 'return traces.FindIndex(t => t.IsActionStart);')]},
    'rw04': {'desc': "a store drops a marker's flag",
             'edits': [('src/Kronikol/Ingestion/InteractionRecord.cs', '                half.PlantUml = PlantUml;\n', '')]},
    'rw05': {'desc': "a step's marker never says it replaces",
             'edits': [(SC, '                        replacesBoundary = state.BoundaryLogged;\n                        state.BoundaryLogged = true;\n                    }\n                }\n            }\n', '                        state.BoundaryLogged = true;\n                    }\n                }\n            }\n')]},
    'rw06': {'desc': "an explicit marker never says it replaces",
             'edits': [(SC, '                replacesBoundary = state.BoundaryLogged;\n                state.BoundaryLogged = true;\n            }\n\n            return first;', '                state.BoundaryLogged = true;\n            }\n\n            return first;')]},
    'rw07': {'desc': 'the client draws the boundary after the lock',
             'edits': [(SC, '            DefaultTrackingDiagramOverride.DrawBoundary(testId);\n        }\n    }\n', '        }\n\n        DefaultTrackingDiagramOverride.DrawBoundary(testId);\n    }\n')]},
    'rw08': {'desc': "the handler's null id guard dropped",
             'edits': [(SC, '        if (testId is null)\n            return false;\n\n        if (!States.TryGetValue(testId, out var state))\n            return true;\n', '        if (!States.TryGetValue(testId!, out var state))\n            return true;\n')]},
    'rw09': {'desc': "StartAction's null id guard dropped",
             'edits': [(SC, '        replacesBoundary = false;\n        if (testId is null)\n            return false;\n', '        replacesBoundary = false;\n')]},
    'rw10': {'desc': "Expire's null id check dropped",
             'edits': [('src/Kronikol/Reports/BackgroundAttribution.cs', 'if (log?.TestId is null || !Inherited(log)', 'if (log is null || !Inherited(log)')]},
    'rw11': {'desc': 'the diagnostic page counts markers as requests',
             'edits': [('src/Kronikol/Reports/DiagnosticReportGenerator.cs', 'l.Type == RequestResponseType.Request && !l.IsDiagramMarker', 'l.Type == RequestResponseType.Request')]},
    'rw12': {'desc': 'the diagnostic page names a test by a marker',
             'edits': [('src/Kronikol/Reports/DiagnosticReportGenerator.cs', '(g.FirstOrDefault(l => !l.IsDiagramMarker) ?? g.First()).TestName', 'g.First().TestName')]},
    'rw13': {'desc': 'a scenario with only markers has log entries',
             'edits': [('src/Kronikol/Reports/DiagnosticReportGenerator.cs', 'l.TestId == s.Id && !l.IsDiagramMarker', 'l.TestId == s.Id')]},
    'rw14': {'desc': 'a boundary alone makes a scenario',
             'edits': [('src/Kronikol/Ingestion/FeatureSynthesizer.cs', 'string.IsNullOrWhiteSpace(log.TestId) || log.IsActionStart', 'string.IsNullOrWhiteSpace(log.TestId)')]},
    'rw05b': {'desc': "a step's marker never says it replaces",
              'edits': [(SC, '                    if (startsAction)\n                    {\n                        replacesBoundary = state.BoundaryLogged;\n', '                    if (startsAction)\n                    {\n')]},
}
