# The node probe fix's mutation: the probe waits five seconds again, whatever it is given. Expects the six-second fact to FAIL.
import subprocess, sys
P = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests/NodeProbe.cs'
old = '            if (!p.WaitForExit(wait))\n'
new = '            if (!p.WaitForExit(TimeSpan.FromSeconds(5)))\n'
s = open(P, encoding='utf-8', newline='').read()
assert s.count(old) == 1
open(P, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
try:
    r = subprocess.run(['dotnet', 'test', 'C:/Code/Kronikol-v5/tests/Kronikol.Tests/Kronikol.Tests.csproj', '--filter',
                        'FullyQualifiedName~Kronikol.Tests.NodeProbeTests', '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                       capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
    killed = r.returncode != 0 and 'Failed!' in r.stdout and 'error CS' not in r.stdout
    print(('KILLED  ' if killed else 'SURVIVED') + ' the probe waits five seconds again')
finally:
    open(P, 'w', encoding='utf-8', newline='').write(s)
sys.exit(0 if killed else 1)
