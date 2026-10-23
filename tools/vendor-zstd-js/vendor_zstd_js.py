"""Imports the compressed report's fallback zstd decoders from their npm releases into src/Kronikol/Reports/ZstdDecoder
(plans/HTML_COMPRESSION_PLAN.md 4.8).

    python tools/vendor-zstd-js/vendor_zstd_js.py --zstddec 0.3.4 --fzstd 0.1.1 [--tarball-dir DIR]
    python tools/vendor-zstd-js/vendor_zstd_js.py --check

Only a published release is imported, never a branch. The copy is, from each package's tarball and nothing else:

- zstddec: the WebAssembly module its dist/zstddec.mjs carries in base64, written as zstddec.wasm (libzstd's decoder,
  built from zstd's single-file decoder by the package's author), and its LICENSE (MIT, then zstd's BSD-3-Clause).
  The package's own JavaScript is not taken: the report's glue (report-zstd-decoder.js) instantiates the module from
  its bytes and reads ZSTD_decompress's return value, so that a frame whose checksum does not match is an error.
- fzstd: umd/index.js, written as fzstd.js, and its LICENSE (MIT).

VERSION names each release with the SHA-512 integrity npm publishes for its tarball, which the import checks before
reading it, and the SHA-256 of each file written. --check re-runs the import into a temporary folder and exits 1 if the
result differs from the copy, so CI can tell an edit by hand from an import.

The tarballs are untrusted input: they are read as data, nothing in them is executed, and only the files above are
taken from them.
"""
import argparse
import base64
import hashlib
import io
import json
import os
import re
import shutil
import sys
import tarfile
import tempfile
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
DEST = os.path.join(ROOT, 'src', 'Kronikol', 'Reports', 'ZstdDecoder')
REGISTRY = 'https://registry.npmjs.org'

WASM_LITERAL = re.compile(r'const wasm = "([A-Za-z0-9+/=]+)"')


def fetch(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'kronikol-vendor-zstd-js'})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def release(package, version, tarball_dir):
    """The tarball of package@version, checked against the integrity npm publishes for it."""
    meta = json.loads(fetch(f'{REGISTRY}/{package}/{version}'))
    integrity = meta['dist']['integrity']
    if tarball_dir:
        with open(os.path.join(tarball_dir, f'{package}-{version}.tgz'), 'rb') as f:
            data = f.read()
    else:
        data = fetch(meta['dist']['tarball'])
    algorithm, digest = integrity.split('-', 1)
    if algorithm != 'sha512' or base64.b64encode(hashlib.sha512(data).digest()).decode() != digest:
        sys.exit(f'{package}@{version}: the tarball does not match the integrity npm publishes ({integrity}).')
    return data, integrity


def member(data, name):
    with tarfile.open(fileobj=io.BytesIO(data), mode='r:gz') as archive:
        for m in archive.getmembers():
            if m.isfile() and m.name == name:
                return archive.extractfile(m).read()
    sys.exit(f'{name} is not in the tarball.')


def build(dest, zstddec_version, fzstd_version, tarball_dir):
    os.makedirs(dest, exist_ok=True)
    files = {}
    zdata, zintegrity = release('zstddec', zstddec_version, tarball_dir)
    module = member(zdata, 'package/dist/zstddec.mjs').decode('utf-8')
    found = WASM_LITERAL.findall(module)
    if len(found) != 1:
        sys.exit('zstddec: dist/zstddec.mjs does not hold exactly one wasm literal.')
    wasm = base64.b64decode(found[0])
    if wasm[:4] != b'\0asm':
        sys.exit('zstddec: the literal is not a WebAssembly module.')
    files['zstddec.wasm'] = wasm
    files['LICENSE-zstddec'] = member(zdata, 'package/LICENSE').replace(b'\r\n', b'\n')
    fdata, fintegrity = release('fzstd', fzstd_version, tarball_dir)
    files['fzstd.js'] = member(fdata, 'package/umd/index.js').replace(b'\r\n', b'\n')
    files['LICENSE-fzstd'] = member(fdata, 'package/LICENSE').replace(b'\r\n', b'\n')
    lines = [
        f'zstddec {zstddec_version} {zintegrity}',
        f'fzstd {fzstd_version} {fintegrity}',
    ]
    for name in sorted(files):
        with open(os.path.join(dest, name), 'wb') as f:
            f.write(files[name])
        lines.append(f'{name} sha256:{hashlib.sha256(files[name]).hexdigest()}')
    with open(os.path.join(dest, 'VERSION'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')


def pinned():
    with open(os.path.join(DEST, 'VERSION'), encoding='utf-8') as f:
        first, second = f.read().splitlines()[:2]
    return first.split()[1], second.split()[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--zstddec')
    parser.add_argument('--fzstd')
    parser.add_argument('--tarball-dir')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    if args.check:
        zstddec_version, fzstd_version = pinned()
        with tempfile.TemporaryDirectory() as tmp:
            build(tmp, zstddec_version, fzstd_version, args.tarball_dir)
            differs = []
            for name in sorted(set(os.listdir(tmp)) | set(os.listdir(DEST))):
                a, b = os.path.join(tmp, name), os.path.join(DEST, name)
                if not (os.path.isfile(a) and os.path.isfile(b) and open(a, 'rb').read() == open(b, 'rb').read()):
                    differs.append(name)
        if differs:
            sys.exit('The copy is not its releases: ' + ', '.join(differs) + ' differ from what the import makes.')
        print(f'The copy is zstddec {zstddec_version} and fzstd {fzstd_version}, as imported.')
        return
    if not args.zstddec or not args.fzstd:
        parser.error('name both releases: --zstddec X --fzstd Y (or --check)')
    if os.path.isdir(DEST):
        for name in os.listdir(DEST):
            os.remove(os.path.join(DEST, name))
    build(DEST, args.zstddec, args.fzstd, args.tarball_dir)
    print(f'Imported zstddec {args.zstddec} and fzstd {args.fzstd} into {os.path.relpath(DEST, ROOT)}.')


if __name__ == '__main__':
    main()
