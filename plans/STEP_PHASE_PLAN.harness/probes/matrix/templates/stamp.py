#!/usr/bin/env python
"""Stamps the plain xUnit v3 shapes (xunit3-plain/Shapes.cs), the thread shapes (T01..) and the lifecycle shapes into each adapter's suite,
so every adapter runs the same test bodies as written, with its own test attribute. Usage: python -I templates/stamp.py (from probes/)."""
import re, pathlib

HERE = pathlib.Path(__file__).resolve().parent
PROBES = HERE.parent

ADAPTERS = {
    'nunit4': dict(ns='Kronikol.NUnit4', using='using NUnit.Framework;', test='[Test]', cls='[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]\n'),
    'mstest': dict(ns='Kronikol.MSTest', using='using Microsoft.VisualStudio.TestTools.UnitTesting;', test='[TestMethod]', cls='[TestClass]\n'),
    'tunit': dict(ns='Kronikol.TUnit', using='using TUnit.Core;', test='[Test]', cls=''),
    'xunit2': dict(ns='Kronikol.xUnit2', using='using Xunit;', test='[Fact]', cls=''),
    'xunit3': dict(ns='Kronikol.xUnit3', using='using Xunit;', test='[Fact]', cls=''),
}

source = (PROBES / 'xunit3-plain' / 'Shapes.cs').read_text(encoding='utf-8').replace('\r\n', '\n')
start = source.index('public class Shapes : DiagrammedComponentTest')
body = source[start:]
body = re.sub(r'private readonly Calls _calls = new\(.*?\)\);\n', 'private readonly Calls _calls = ProbeEnv.NewCalls();\n', body, count=1, flags=re.S)
threads = (HERE / 'Threads.fragment.cs').read_text(encoding='utf-8').replace('\r\n', '\n')
# The class's closing brace: the thread shapes go inside it.
body = body.rstrip()
assert body.endswith('}')
body = body[:-1].rstrip() + '\n\n' + threads + '\n}\n'

for name, a in ADAPTERS.items():
    folder = PROBES / name
    folder.mkdir(exist_ok=True)
    text = body.replace('[Fact]', a['test'])
    header = (f"// Stamped by templates/stamp.py from xunit3-plain/Shapes.cs and templates/Threads.fragment.cs: edit those.\n"
              f"using Kronikol;\nusing Kronikol.Tracking;\nusing {a['ns']};\nusing PhaseProbe;\n{a['using']}\n\n"
              f"namespace PhaseProbe.Suite;\n\n{a['cls']}")
    (folder / 'Shapes.g.cs').write_text(header + text, encoding='utf-8', newline='\n')
    print('stamped', folder.name)
