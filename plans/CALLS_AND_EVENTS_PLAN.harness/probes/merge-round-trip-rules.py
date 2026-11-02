# A merge across a change of InteractionShape.Version: false alarm, silence, or correct? Published packages write the runs;
# the ledger is folded by each sequence's tool; `kronikol merge --history` and merge-round-trip-fixture's read mode give the
# merged reading beside the run's own. args: <work dir> <published 5.27.13 tool dll> <prototype tool dll>
# Each sequence: packages per run (the last is the one read), and the tools that read it.
import os, re, shutil, subprocess, sys

work, tool_513, tool_proto, *only = sys.argv[1:]
probes = os.path.dirname(os.path.abspath(__file__))
fixture_src = os.path.join(probes, 'merge-round-trip-rules.cs')
read_src = os.path.join(probes, 'merge-round-trip-fixture.cs')
build_root = os.path.join(work, 'rules-bin')
os.makedirs(build_root, exist_ok=True)


def sh(cmd, env=None, cwd=None):
    r = subprocess.run(cmd, env=env, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding='utf-8', errors='replace')
    return r.returncode, r.stdout


def build(source, package, label):
    out = os.path.join(build_root, f'{label}-{package}')
    if os.path.exists(os.path.join(out, os.path.basename(source)[:-3] + '.dll')):
        return out
    text = open(source, 'rb').read().decode('utf-8').replace('#:package Kronikol@5.27.13', f'#:package Kronikol@{package}')
    copy_dir = os.path.join(work, 'rules-src', f'{label}-{package}')
    os.makedirs(copy_dir, exist_ok=True)
    copy = os.path.join(copy_dir, os.path.basename(source))
    open(copy, 'wb').write(text.encode('utf-8'))
    code, log = sh(['dotnet', 'build', copy, '-o', out, '-p:WarningLevel=0'])
    if code != 0:
        raise SystemExit(f'build of {label} on {package} failed:\n{log[-1500:]}')
    return out


def env_for(number):
    env = dict(os.environ)
    env.update({'GITHUB_ACTIONS': 'true', 'GITHUB_RUN_ID': str(2000 + number), 'GITHUB_RUN_ATTEMPT': '1', 'GITHUB_REF_NAME': 'main', 'GITHUB_SHA': 'abc%d' % number, 'GITHUB_REPOSITORY': 'x/y', 'GITHUB_SERVER_URL': 'https://github.example'})
    env.pop('KRONIKOL_HISTORY', None)
    return env


def tool_for(package):
    if package == '5.27.13':
        return tool_513
    path = os.path.join(work, 'tools', package)
    if not os.path.isdir(path):
        code, log = sh(['dotnet', 'tool', 'install', 'Kronikol.Tool', '--version', package, '--tool-path', path])
        if code != 0:
            raise SystemExit(log[-1000:])
    for base, _, files in os.walk(path):
        if 'Kronikol.Tool.dll' in files:
            return os.path.join(base, 'Kronikol.Tool.dll')
    raise SystemExit('no tool for ' + package)


def sequence(name, packages, recorder, readers):
    if only and name not in only:
        return
    print(f'\n=== {name}: runs on {", ".join(packages)}; recorded by {recorder}', flush=True)
    root = os.path.join(work, 'rules', name)
    shutil.rmtree(root, ignore_errors=True)
    os.makedirs(root)
    ledger = os.path.join(root, 'ledger.jsonl')
    last = len(packages)
    for number, package in enumerate(packages, 1):
        folder = os.path.join(root, f'run{number}')
        code, out = sh(['dotnet', os.path.join(build(fixture_src, package, 'run'), 'merge-round-trip-rules.dll'), folder, '-', str(number), str(last)], env_for(number), cwd=root)
        if code != 0:
            raise SystemExit(out[-1500:])
        if number < last:
            code, out = sh(['dotnet', tool_for(recorder), 'history', 'record', folder, '--history', ledger], env_for(number), cwd=root)
            if code != 0:
                raise SystemExit(out[-1000:])
    run = os.path.join(root, f'run{last}')
    for label, tool, read_package, swap in readers:
        out_dir = os.path.join(root, 'm-' + label)
        os.makedirs(out_dir, exist_ok=True)
        code, out = sh(['dotnet', tool, 'merge', run, '-o', os.path.join(out_dir, 'merged.html'), '--history', ledger], env_for(last), cwd=root)
        history = [l.strip() for l in out.splitlines() if l.strip().startswith('history:')]
        print(f'-- {label}: kronikol merge exit {code}: {history[0] if history else "(no history line: nothing to say)"}')
        reader = build(read_src, read_package, 'read')
        if swap:
            swapped = reader + '-proto'
            if not os.path.isdir(swapped):
                shutil.copytree(reader, swapped)
                shutil.copy(os.path.join(os.path.dirname(tool), 'Kronikol.dll'), os.path.join(swapped, 'Kronikol.dll'))
            reader = swapped
        code, out = sh(['dotnet', os.path.join(reader, 'merge-round-trip-fixture.dll'), 'read', ledger, tool, run], env_for(last), cwd=root)
        sys.stdout.write(out)
        sys.stdout.flush()


proto = ('prototype', tool_proto, '5.27.13', True)
published = ('5.27.13', tool_513, '5.27.13', False)
old_tool = ('5.27.8 tool', tool_for('5.27.8'), '5.27.8', False)

sequence('upgrade-10-to-11', ['5.27.8'] * 3 + ['5.27.13'], '5.27.13', [published, proto])
sequence('upgrade-9-to-11', ['5.27.6'] * 3 + ['5.27.13'], '5.27.13', [proto])
sequence('upgrade-8-to-11', ['5.27.2'] * 3 + ['5.27.13'], '5.27.13', [proto])
sequence('upgrade-7-to-11', ['5.16.0'] * 3 + ['5.27.13'], '5.27.13', [proto])
sequence('upgrade-6-to-11', ['5.12.1'] * 3 + ['5.27.13'], '5.27.13', [proto])
sequence('after-upgrade-window-mixed', ['5.27.8'] * 3 + ['5.27.13'] * 2, '5.27.13', [proto])
sequence('package-behind-tool', ['5.27.8'] * 4, '5.27.13', [proto])
sequence('tool-behind-package-window-mixed', ['5.27.8'] * 2 + ['5.27.13'] * 2, '5.27.8', [old_tool])
sequence('tool-behind-package-window-all-new', ['5.27.13'] * 4, '5.27.8', [old_tool])
