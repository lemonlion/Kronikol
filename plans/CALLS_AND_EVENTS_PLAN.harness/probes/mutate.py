"""Runs a release's mutations in a committed tree: one source change at a time, the named facts built and run, and the change undone.
args: <tree> <results file> <mutation list python file>. A mutation that does not compile is reported as BUILD FAILED, which is not 'caught'."""
import runpy
import subprocess
import sys

tree, out_path, spec = sys.argv[1], sys.argv[2], sys.argv[3]
mutations = runpy.run_path(spec)['MUTATIONS']


def run(args, **kw):
    return subprocess.run(args, cwd=tree, capture_output=True, text=True, encoding='utf8', errors='replace', **kw)


out = open(out_path, 'a', encoding='utf8', newline='\n')


def say(line):
    print(line, flush=True)
    out.write(line + '\n')
    out.flush()


clean = run(['git', 'status', '--short', '--', 'src', 'tests']).stdout.strip()
assert not clean, 'commit first: ' + clean[:400]

for name, path, old, new, checks in mutations:
    full = f'{tree}/{path}'
    source = open(full, encoding='utf8', newline='').read()
    assert source.count(old) == 1, (name, source.count(old), old)
    open(full, 'w', encoding='utf8', newline='').write(source.replace(old, new))
    say(f'== {name}: {path}')
    try:
        caught = False
        for project, framework, filt in checks:
            build_args = ['dotnet', 'build', f'tests/{project}', '--nologo', '-v', 'q', '-clp:ErrorsOnly']
            if framework:
                build_args += ['-f', framework]
            built = run(build_args)
            if built.returncode != 0:
                errors = [l for l in built.stdout.splitlines() if ' error ' in l][:2]
                say(f'   BUILD FAILED {project}: ' + ' | '.join(e[:200] for e in errors))
                continue
            test_args = ['dotnet', 'test', f'tests/{project}', '--no-build', '--nologo', '-v', 'q', '--logger', 'console;verbosity=normal']
            if framework:
                test_args += ['-f', framework]
            if filt:
                test_args += ['--filter', filt]
            result = run(test_args)
            failed = sorted({l.strip().split(' [')[0].replace('Failed ', '') for l in result.stdout.splitlines() if l.startswith('  Failed ')})
            total = [l.strip() for l in result.stdout.splitlines() if l.strip().startswith('Total tests:')]
            say(f'   {project}: {len(failed)} failed of {total[0] if total else "?"}')
            for f in failed[:6]:
                say('     ' + f[:150])
            if len(failed) > 6:
                say(f'     ... and {len(failed) - 6} more')
            caught = caught or len(failed) > 0
        say('   CAUGHT' if caught else '   SURVIVED')
    finally:
        open(full, 'w', encoding='utf8', newline='').write(source)
say('DONE')
