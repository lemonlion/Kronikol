# S2: ViewportSweepTests' header checks (5 and 6) rewritten for the run band.
p = 'tests/Kronikol.Tests.EndToEnd/ViewportSweepTests.cs'
s = open(p, 'rb').read().decode()
a = s.index('            // 5 and 6. The header')
b = s.index('            // 7. Nothing wider than the feature')
new = '''            // 5 and 6. The run band on a run report (5.0.0, plans/OPTION_C_PLAN.md S2; 4.x checked the header row):
            //    the filtering box on a row of its own under the band and as wide; no CI label wraps; and the CI zone
            //    beside the summary unless the band marks it below, then under it.
            if (runReport) {
                const band = document.querySelector('.run-band');
                const b = box.getBoundingClientRect(), r = band.getBoundingClientRect();
                if (b.top < r.bottom - 1) problems.push('the filtering box is not on a row of its own under the run band');
                if (Math.abs(b.width - r.width) > 1)
                    problems.push(`the filtering box is ${Math.round(b.width)} px wide, not the run band's ${Math.round(r.width)}`);
                const ci = band.querySelector('.run-ci');
                if (ci) {
                    for (const dt of ci.querySelectorAll('dt'))
                        if (lines(dt) > 1) problems.push(`the CI label "${label(dt)}" wraps`);
                    const s = band.querySelector('.run-summary').getBoundingClientRect(), c = ci.getBoundingClientRect();
                    if (band.classList.contains('ci-below')) {
                        if (c.top < s.bottom - 1) problems.push('the CI zone is marked below the summary but sits beside it');
                    } else if (Math.abs(c.top - s.top) > 1) problems.push('the CI zone is neither beside the summary nor marked below it');
                }
            }

'''
s = s[:a] + new + s[b:]
open(p, 'wb').write(s.encode())
print('sweep ok')
