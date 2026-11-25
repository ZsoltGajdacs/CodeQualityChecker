#!/usr/bin/env python3
"""Install and discover local CQC plugins using disposable client settings, without inference."""
import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time


TOOLS = {'inspect_project', 'check_code_quality', 'check_test_quality',
         'check_duplication', 'check_dependencies', 'check_hotspots', 'check_all', 'get_report_page',
         'check_uncommitted_changes_quality'}
SKILLS = {'check-project', 'check-uncommitted'}


def command(arguments, environment, cwd):
    result = subprocess.run(arguments, env=environment, cwd=cwd, text=True,
                            capture_output=True, timeout=60)
    if result.returncode:
        raise RuntimeError(f'{arguments}: {result.stdout}\n{result.stderr}')
    return result.stdout


class AppServer:
    def __init__(self, environment, cwd):
        self.errors = tempfile.TemporaryFile(mode='w+')
        self.process = subprocess.Popen(['codex', 'app-server', '--stdio'],
                                        env=environment, cwd=cwd, text=True,
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=self.errors)
        self.responses = queue.Queue()
        self.sequence = 0
        threading.Thread(target=self.read, daemon=True).start()

    def read(self):
        for line in self.process.stdout:
            self.responses.put(line)
        self.responses.put(None)

    def send(self, message):
        self.process.stdin.write(json.dumps(message) + '\n')
        self.process.stdin.flush()

    def request(self, method, params):
        self.sequence += 1
        self.send({'id': self.sequence, 'method': method, 'params': params})
        deadline = time.monotonic() + 60
        while True:
            line = self.responses.get(timeout=max(0, deadline - time.monotonic()))
            if line is None:
                raise RuntimeError('Codex app-server exited before responding')
            response = json.loads(line)
            if response.get('id') != self.sequence:
                continue
            if 'error' in response:
                raise RuntimeError(response['error'])
            return response['result']

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()
        self.process.stdout.close()
        self.errors.close()


def isolated_environment(variable, root):
    environment = os.environ.copy()
    directory = root / variable.lower()
    directory.mkdir()
    environment[variable] = str(directory)
    return environment


def packaged_version(packages, client):
    manifest = packages / client / 'plugins' / 'cqc' / f'.{client}-plugin' / 'plugin.json'
    return json.loads(manifest.read_text())['version']


def verify_codex_inventory(environment, cwd):
    server = AppServer(environment, cwd)
    try:
        server.request('initialize', {
            'clientInfo': {'name': 'cqc-plugin-validation', 'version': '1'},
            'capabilities': {'experimentalApi': True}})
        server.send({'method': 'initialized'})
        groups = server.request('skills/list', {'cwds': [str(cwd)], 'forceReload': True})
        skills = [skill for group in groups['data'] for skill in group['skills']
                  if skill.get('pluginId') == 'cqc@cqc-local-codex']
        names = {skill['name'] for skill in skills}
        assert names == {f'cqc:{name}' for name in SKILLS}, skills
        assert all(skill['enabled'] for skill in skills), skills
        inventory = server.request('mcpServerStatus/list', {'detail': 'full'})
        cqc = next(item for item in inventory['data'] if item['name'] == 'cqc')
        assert not cqc.get('toolsError'), cqc
        assert set(cqc['tools']) == TOOLS, cqc
        return {'skills': sorted(names), 'tools': sorted(cqc['tools'])}
    finally:
        server.close()


def verify_codex(packages, root, cwd):
    environment = isolated_environment('CODEX_HOME', root)
    version = command(['codex', '--version'], environment, cwd).strip()
    command(['codex', 'plugin', 'marketplace', 'add', str(packages / 'codex')], environment, cwd)
    command(['codex', 'plugin', 'add', 'cqc@cqc-local-codex'], environment, cwd)
    listing = json.loads(command(['codex', 'plugin', 'list', '--json'], environment, cwd))
    installed = next(item for item in listing['installed'] if item['name'] == 'cqc')
    assert installed['enabled'] and installed['version'] == packaged_version(packages, 'codex'), installed
    return {'client': version, 'pluginVersion': installed['version'],
            **verify_codex_inventory(environment, cwd)}


def verify_claude(packages, root, cwd):
    environment = isolated_environment('CLAUDE_CONFIG_DIR', root)
    version = command(['claude', '--version'], environment, cwd).strip()
    command(['claude', 'plugin', 'marketplace', 'add', str(packages / 'claude')], environment, cwd)
    command(['claude', 'plugin', 'install', 'cqc@cqc-local-claude'], environment, cwd)
    listing = json.loads(command(['claude', 'plugin', 'list', '--json'], environment, cwd))
    installed = next(item for item in listing if item['id'] == 'cqc@cqc-local-claude')
    assert installed['enabled'] and installed['version'] == packaged_version(packages, 'claude'), installed
    assert 'cqc' in installed['mcpServers'], installed
    details = command(['claude', 'plugin', 'details', 'cqc@cqc-local-claude'], environment, cwd)
    assert all(name in details for name in SKILLS) and 'cqc-review' not in details, details
    health = command(['claude', 'mcp', 'list'], environment, cwd)
    assert 'plugin:cqc:cqc:' in health and 'Connected' in health, health
    return {'client': version, 'pluginVersion': installed['version'],
            'skills': sorted(f'cqc:{name}' for name in SKILLS), 'mcpHealth': health.strip()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('packages', type=Path, nargs='?',
                        default=Path(__file__).resolve().parents[1] / 'dist/cqc-plugins')
    packages = parser.parse_args().packages.resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix='cqc-client-validation-') as directory:
        root = Path(directory)
        cwd = root / 'workspace'
        cwd.mkdir()
        results = {'codex': verify_codex(packages, root, cwd),
                   'claude': verify_claude(packages, root, cwd)}
        print(json.dumps(results, indent=2))


if __name__ == '__main__':
    main()
