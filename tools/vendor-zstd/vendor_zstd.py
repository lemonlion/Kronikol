"""Imports ZstdSharp's source at a tagged release into src/Kronikol/Compression/Zstd (plans/REPORT_FORMAT_PLAN.md 4.10).

    python tools/vendor-zstd/vendor_zstd.py --tag 0.8.8 [--tarball FILE] [--commit SHA]
    python tools/vendor-zstd/vendor_zstd.py --check

Only a tagged release is imported, never a branch. The copy is ZstdSharp's src/ZstdSharp/**/*.cs and its LICENSE, changed
by this script alone and in these ways only, so a run on the same tag changes nothing:

1. Every type becomes internal. A public ZstdSharp type inside Kronikol.dll would clash with the real package in an app
   that also has it (MongoDB.Driver and MySql.Data bring it in), with ambiguous-type errors.
2. The namespaces ZstdSharp and ZstdSharp.Unsafe become Kronikol.Compression.Zstd and Kronikol.Compression.Zstd.Unsafe.
3. [InlineMethod.Inline] is removed. ZstdSharp's build runs InlineMethod.Fody, which inlines those methods into their
   callers; Kronikol's build does not, and the methods are ordinary calls without it.
4. [module: SkipLocalsInit] is removed. Compiled into Kronikol.dll it would stop the runtime zeroing the locals of
   every method in the module, Kronikol's own included, not only the copy's.

Line ends are written as LF. --check re-runs the import into a temporary folder and exits 1 if the result differs
from the copy, so CI can tell an edit by hand from an import.

The tarball is untrusted input: it is read as data, nothing in it is executed, and only .cs files and LICENSE under
src/ZstdSharp/ are taken from it.
"""
import argparse
import io
import json
import os
import re
import shutil
import sys
import tarfile
import tempfile
import urllib.request

REPO = 'oleg-st/ZstdSharp'
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
DEST = os.path.join(ROOT, 'src', 'Kronikol', 'Compression', 'Zstd')

TYPE_DECLARATION = re.compile(
    r'^(?P<indent>[ \t]*)public(?P<modifiers>(?:[ \t]+(?:static|unsafe|partial|sealed|abstract|readonly|ref))*)'
    r'(?P<kind>[ \t]+(?:class|struct|enum|interface|delegate|record)\b)', re.MULTILINE)


def api(path):
    request = urllib.request.Request(f'https://api.github.com/repos/{REPO}/{path}',
                                     headers={'Accept': 'application/vnd.github+json', 'User-Agent': 'kronikol-vendor-zstd'})
    token = os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')
    if token:
        request.add_header('Authorization', f'Bearer {token}')
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def transform(text):
    """The four changes, and nothing else."""
    text = text.replace('\r\n', '\n')
    text = TYPE_DECLARATION.sub(lambda m: f"{m.group('indent')}internal{m.group('modifiers')}{m.group('kind')}", text)
    text = re.sub(r'\bnamespace ZstdSharp\b', 'namespace Kronikol.Compression.Zstd', text)
    text = re.sub(r'\busing ZstdSharp\b', 'using Kronikol.Compression.Zstd', text)
    text = re.sub(r'\busing static ZstdSharp\b', 'using static Kronikol.Compression.Zstd', text)
    text = re.sub(r'\bglobal::ZstdSharp\.', 'global::Kronikol.Compression.Zstd.', text)
    text = re.sub(r'^[ \t]*\[InlineMethod\.Inline\][ \t]*\n', '', text, flags=re.MULTILINE)
    text = re.sub(r'\[InlineMethod\.Inline\]\s*', '', text)
    text = re.sub(r'^[ \t]*\[module:[ \t]*SkipLocalsInit\][ \t]*\n', '', text, flags=re.MULTILINE)
    return text


def read_release(tarball_bytes):
    """The .cs files under src/ZstdSharp and the LICENSE, by their path below src/ZstdSharp (LICENSE at the top)."""
    files = {}
    with tarfile.open(fileobj=io.BytesIO(tarball_bytes), mode='r:gz') as archive:
        for member in archive.getmembers():
            if not member.isfile():
                continue
            parts = member.name.split('/')
            if len(parts) == 2 and parts[1] == 'LICENSE':
                files['LICENSE'] = archive.extractfile(member).read()
            elif len(parts) > 3 and parts[1:3] == ['src', 'ZstdSharp'] and member.name.endswith('.cs'):
                relative = '/'.join(parts[3:])
                if '..' in parts or relative.startswith('/'):
                    raise SystemExit(f'refusing a path outside the folder: {member.name}')
                files[relative] = archive.extractfile(member).read()
    if 'LICENSE' not in files or not any(name.endswith('.cs') for name in files):
        raise SystemExit('the tarball holds no LICENSE or no src/ZstdSharp/*.cs')
    return files


def write_copy(files, tag, commit, destination):
    if os.path.isdir(destination):
        shutil.rmtree(destination)
    os.makedirs(destination)
    for relative, data in sorted(files.items()):
        path = os.path.join(destination, *relative.split('/'))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        if relative.endswith('.cs'):
            text = data.decode('utf-8-sig')
            data = transform(text).encode('utf-8')
        else:
            data = data.replace(b'\r\n', b'\n')
        with open(path, 'wb') as handle:
            handle.write(data)
    with open(os.path.join(destination, 'VERSION'), 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(f'ZstdSharp {tag}\nhttps://github.com/{REPO}/releases/tag/{tag}\ncommit {commit}\n')


def current_version(destination):
    path = os.path.join(destination, 'VERSION')
    if not os.path.exists(path):
        return None, None
    lines = open(path, encoding='utf-8').read().splitlines()
    return lines[0].split(' ', 1)[1], lines[2].split(' ', 1)[1]


def fetch(tag, tarball, commit):
    data = open(tarball, 'rb').read() if tarball else api(f'tarball/{tag}')
    if commit is None:
        commit = json.loads(api(f'git/ref/tags/{tag}'))['object']['sha']
    return data, commit


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('--tag', help='the ZstdSharp release to import; the latest when omitted')
    parser.add_argument('--tarball', help="a local copy of the release's source tarball, instead of downloading it")
    parser.add_argument('--commit', help="the tag's commit, when the tarball is local and there is no network")
    parser.add_argument('--check', action='store_true', help='re-import the tag the copy names and fail on any difference')
    parser.add_argument('--dest', default=DEST, help=argparse.SUPPRESS)
    args = parser.parse_args()

    if args.check:
        tag, commit = current_version(args.dest)
        if tag is None:
            raise SystemExit('no copy to check')
        data, commit = fetch(tag, args.tarball, args.commit or commit)
        with tempfile.TemporaryDirectory() as scratch:
            fresh = os.path.join(scratch, 'Zstd')
            write_copy(read_release(data), tag, commit, fresh)
            differences = compare(fresh, args.dest)
        if differences:
            print(f'the copy differs from ZstdSharp {tag} as imported:', *differences[:20], sep='\n  ')
            sys.exit(1)
        print(f'the copy is ZstdSharp {tag} as imported')
        return

    tag = args.tag or json.loads(api('releases/latest'))['tag_name']
    data, commit = fetch(tag, args.tarball, args.commit)
    write_copy(read_release(data), tag, commit, args.dest)
    print(f'imported ZstdSharp {tag} ({commit}) into {os.path.relpath(args.dest, ROOT)}')


def compare(left, right):
    def listing(root):
        return {os.path.relpath(os.path.join(d, f), root).replace(os.sep, '/')
                for d, _, names in os.walk(root) for f in names}
    a, b = listing(left), listing(right)
    differences = [f'only in the import: {p}' for p in sorted(a - b)] + [f'only in the copy: {p}' for p in sorted(b - a)]
    for name in sorted(a & b):
        if open(os.path.join(left, name), 'rb').read() != open(os.path.join(right, name), 'rb').read():
            differences.append(f'differs: {name}')
    return differences


if __name__ == '__main__':
    main()
