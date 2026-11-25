#!/usr/bin/env python3
"""Build and install CQC for Claude Code, Codex, or both."""
import argparse
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tempfile


REPOSITORY = Path(__file__).resolve().parent.parent


def run(arguments, capture=False, cwd=None):
    result = subprocess.run([str(argument) for argument in arguments], check=True,
                            text=True, stdout=subprocess.PIPE if capture else None, cwd=cwd)
    return result.stdout


def read_json(path):
    with path.open(encoding='utf-8') as source:
        return json.load(source)


def generate(mcp_directory, packaging):
    executable = packaging['executable_path'](str(mcp_directory / 'cqc'))
    distribution = REPOSITORY / 'dist'
    packaging['reject_symlinks'](distribution)
    distribution.mkdir(exist_ok=True)
    output = Path(tempfile.mkdtemp(prefix='cqc-plugin-install.', dir=distribution))
    validated = packaging['output_path'](str(output), executable)
    packaging['package'](executable, validated)
    print(f'Generated plugin packages: {output}', flush=True)
    return output


def prepare(arguments, packaging):
    if arguments.packages_root is not None:
        return arguments.packages_root.resolve(strict=True)
    if arguments.mcp_dir is None:
        run(['bash', REPOSITORY / 'scripts/package-macos.sh'])
        mcp_directory = REPOSITORY / 'dist/cqc-osx-arm64'
    else:
        mcp_directory = arguments.mcp_dir.expanduser().absolute()
    return generate(mcp_directory, packaging)


def validate_catalog(marketplace, client):
    marker = '.claude-plugin' if client == 'claude' else '.agents/plugins'
    catalog = read_json(marketplace / marker / 'marketplace.json')
    expected = f'cqc-local-{client}'
    if catalog['name'] != expected:
        raise ValueError(f'Expected marketplace {expected}')
    entry = next(item for item in catalog['plugins'] if item['name'] == 'cqc')
    validate_catalog_source(entry['source'], client)


def validate_mcp(plugin, packaging):
    configuration = read_json(plugin / '.mcp.json')['mcpServers']['cqc']
    command = configuration['command']
    if not isinstance(command, str) or not Path(command).is_absolute():
        raise ValueError('CQC MCP command must be an absolute executable path')
    packaging['executable_path'](command)


def validate_package(root, client, packaging):
    marketplace = root / client
    validate_catalog(marketplace, client)
    plugin = marketplace / 'plugins/cqc'
    manifest = read_json(plugin / f'.{client}-plugin/plugin.json')
    if manifest['name'] != 'cqc':
        raise ValueError('Expected plugin identity cqc')
    validate_mcp(plugin, packaging)
    for relative in ('check-project/SKILL.md', 'check-uncommitted/SKILL.md', 'review-report.md'):
        skill = plugin / 'skills' / relative
        if not skill.is_file():
            raise ValueError(f'Missing generated skill file: {skill}')
    return marketplace.resolve(strict=True)


def validate_catalog_source(source, client):
    expected = './plugins/cqc'
    if client == 'codex':
        source = source['path'] if source['source'] == 'local' else None
    if source != expected:
        raise ValueError('Expected marketplace source ./plugins/cqc')


def records(client, command, cwd, container=None):
    payload = json.loads(run([client, 'plugin', *command, '--json'], capture=True, cwd=cwd))
    if container is not None:
        payload = payload[container]
    if not isinstance(payload, list) or not all(isinstance(item, dict) for item in payload):
        raise ValueError(f'Unexpected JSON inventory from {client}')
    return payload


def existing_registration(client, cwd):
    name = f'cqc-local-{client}'
    plugin_id = f'cqc@{name}'
    if client == 'codex':
        plugins = records(client, ['list'], cwd, 'installed')
        marketplaces = records(client, ['marketplace', 'list'], cwd, 'marketplaces')
        installed = any(item['pluginId'] == plugin_id for item in plugins)
    else:
        plugins = records(client, ['list'], cwd)
        marketplaces = records(client, ['marketplace', 'list'], cwd)
        installed = any(item['id'] == plugin_id and item['scope'] == 'user' for item in plugins)
    registered = marketplace_registered(client, name, marketplaces)
    return installed, registered


def marketplace_registered(client, name, marketplaces):
    if client != 'claude':
        return any(item['name'] == name for item in marketplaces)
    directory = Path(os.environ.get('CLAUDE_CONFIG_DIR', str(Path.home() / '.claude')))
    settings = directory / 'settings.json'
    if not settings.exists():
        return False
    return name in user_marketplaces(read_json(settings))


def user_marketplaces(settings):
    if not isinstance(settings, dict):
        raise ValueError('Claude user settings must be a JSON object')
    declarations = settings.get('extraKnownMarketplaces', {})
    if not isinstance(declarations, dict):
        raise ValueError('Claude extraKnownMarketplaces must be a JSON object')
    return declarations


def installation_commands(client, marketplace):
    name = f'cqc-local-{client}'
    plugin_id = f'cqc@{name}'
    if client == 'claude':
        return (
            ['uninstall', '--scope', 'user', '--keep-data', plugin_id],
            ['marketplace', 'remove', '--scope', 'user', name],
            ['marketplace', 'add', '--scope', 'user', marketplace],
            ['install', '--scope', 'user', plugin_id]
        )
    return (
        ['remove', plugin_id],
        ['marketplace', 'remove', name],
        ['marketplace', 'add', marketplace],
        ['add', plugin_id]
    )


def install(client, marketplace):
    with tempfile.TemporaryDirectory(prefix='cqc-plugin-client-') as cwd:
        refresh_installation(client, marketplace, cwd)


def refresh_installation(client, marketplace, cwd):
    installed, registered = existing_registration(client, cwd)
    remove_plugin, remove_marketplace, add_marketplace, add_plugin = installation_commands(client, marketplace)
    for needed, command in ((installed, remove_plugin), (registered, remove_marketplace),
                            (True, add_marketplace), (True, add_plugin)):
        if needed:
            run([client, 'plugin', *command], cwd=cwd)


def parse_arguments():
    parser = argparse.ArgumentParser(
        description=__doc__,
        epilog='Run install-plugin.sh without arguments to build once and refresh both clients. '
               'The default executable is <repository>/dist/cqc-osx-arm64/cqc.')
    parser.add_argument('client', choices=('claude', 'codex', 'both'))
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--mcp-dir', type=Path,
                      help='Use an existing directory containing cqc instead of building')
    mode.add_argument('packages_root', nargs='?', type=Path,
                      help='Install existing generated packages without building or generating')
    return parser.parse_args()


def main():
    arguments = parse_arguments()
    clients = ('claude', 'codex') if arguments.client == 'both' else (arguments.client,)
    try:
        for client in clients:
            if shutil.which(client) is None:
                raise ValueError(f'{client} CLI was not found in PATH')
        packaging = runpy.run_path(str(REPOSITORY / 'scripts/package-plugins.py'))
        root = prepare(arguments, packaging)
        marketplaces = {client: validate_package(root, client, packaging) for client in clients}
        for client, marketplace in marketplaces.items():
            install(client, marketplace)
            print(f'CQC installed. Restart {client} to use it.', flush=True)
    except (OSError, ValueError, KeyError, TypeError, StopIteration, subprocess.CalledProcessError) as error:
        print(f'Cannot install CQC plugin: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
