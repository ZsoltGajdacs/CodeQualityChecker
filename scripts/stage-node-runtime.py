"""Verify the locked Node release archive and stage its runtime and license."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import tarfile
import urllib.request


def stage(output, scratch):
    lock = json.loads(Path('upstream.lock.json').read_text())['nodeRuntime']
    release = f"node-v{lock['version']}-darwin-arm64"
    name = f'{release}.tar.xz'
    provided = os.environ.get('CQC_NODE_ARCHIVE')
    archive = Path(provided or scratch / name)
    if not provided:
        url = f"https://nodejs.org/dist/v{lock['version']}/{name}"
        with urllib.request.urlopen(url, timeout=60) as response, archive.open('wb') as destination:
            shutil.copyfileobj(response, destination)
    with archive.open('rb') as source:
        if sha256(source) != lock['sha256']:
            raise SystemExit(f'{name} does not match upstream.lock.json')
        source.seek(0)
        with tarfile.open(fileobj=source, mode='r:xz') as distribution:
            install(distribution, f'{release}/bin/node', output / 'node-runtime/bin/node', 0o755)
            install(distribution, f'{release}/LICENSE', output / 'legal/node/LICENSE', 0o644)


def sha256(source):
    digest = hashlib.sha256()
    for block in iter(lambda: source.read(1024 * 1024), b''):
        digest.update(block)
    return digest.hexdigest()


def install(archive, name, destination, mode):
    member = archive.getmember(name)
    if not member.isfile():
        raise SystemExit(f'Expected a regular archive entry for {name}')
    destination.parent.mkdir(parents=True, exist_ok=True)
    with archive.extractfile(member) as source, destination.open('wb') as target:
        shutil.copyfileobj(source, target)
    destination.chmod(mode)


if __name__ == '__main__':
    stage(*map(Path, sys.argv[1:]))
