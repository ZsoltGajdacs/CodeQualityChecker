#!/usr/bin/env python3
"""Generate local Claude Code and Codex marketplaces for an existing CQC install."""
import argparse
import json
import os
from pathlib import Path
import shutil
import tempfile


REPOSITORY = Path(__file__).resolve().parent.parent
SOURCES = REPOSITORY / 'plugins'


def reject_symlinks(path):
    for ancestor in (path, *path.parents):
        if ancestor.is_symlink():
            raise ValueError(f'Symlinked paths are not supported: {ancestor}')


def executable_path(value):
    path = Path(os.path.abspath(Path(value).expanduser()))
    reject_symlinks(path)
    if not path.is_file() or not os.access(path, os.X_OK):
        raise ValueError(f'CQC executable must be an executable file: {path}')
    return path.resolve()


def output_path(value, executable):
    path = Path(os.path.abspath(Path(value).expanduser()))
    reject_symlinks(path)
    path = path.resolve()
    distribution = REPOSITORY / 'dist'
    if path == distribution or not path.is_relative_to(distribution):
        raise ValueError(f'Output must be strictly beneath {distribution}')
    installation = executable.parent
    if path.is_relative_to(installation) or installation.is_relative_to(path):
        raise ValueError('Output must not overlap the CQC installation')
    validate_empty_output(path)
    return path


def validate_empty_output(path):
    if not path.exists():
        return
    if path.is_dir() and not any(path.iterdir()):
        return
    raise ValueError('Output must be a new or empty directory; choose a new --output to regenerate')


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def marketplace(client):
    entry = {'name': 'cqc', 'source': './plugins/cqc'}
    catalog = {
        'name': f'cqc-local-{client}',
        'description': 'Local CodeQualityChecker MCP connection for Java, C#, TypeScript, and React.',
        'plugins': [entry]
    }
    if client == 'claude':
        catalog['owner'] = {'name': 'CodeQualityChecker contributors'}
        return '.claude-plugin/marketplace.json', catalog
    entry['source'] = {'source': 'local', 'path': './plugins/cqc'}
    entry['policy'] = {'installation': 'AVAILABLE', 'authentication': 'ON_INSTALL'}
    entry['category'] = 'Productivity'
    catalog['interface'] = {'displayName': 'CodeQualityChecker local'}
    return '.agents/plugins/marketplace.json', catalog


def populate(root, executable):
    for client in ('claude', 'codex'):
        destination = root / client / 'plugins' / 'cqc'
        shutil.copytree(SOURCES / client, destination)
        shutil.copytree(SOURCES / 'skills', destination / 'skills')
        write_json(destination / '.mcp.json', {
            'mcpServers': {'cqc': {'command': str(executable), 'args': []}}
        })
        relative, catalog = marketplace(client)
        write_json(root / client / relative, catalog)


def package(executable, output):
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.cqc-plugins-', dir=output.parent) as temporary:
        staging = Path(temporary) / 'package'
        staging.mkdir()
        populate(staging, executable)
        staging.rename(output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cqc-executable', required=True, help='Path to the existing packaged cqc executable')
    parser.add_argument('--output', default=str(REPOSITORY / 'dist' / 'cqc-plugins'),
                        help='New or empty directory strictly beneath this repository\'s dist directory')
    arguments = parser.parse_args()
    try:
        executable = executable_path(arguments.cqc_executable)
        output = output_path(arguments.output, executable)
        package(executable, output)
    except (OSError, ValueError) as error:
        parser.exit(1, f'Cannot package CQC plugins: {error}\n')
    print(f'Generated Claude Code marketplace: {output / "claude"}')
    print(f'Generated Codex marketplace: {output / "codex"}')


if __name__ == '__main__':
    main()
