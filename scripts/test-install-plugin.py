#!/usr/bin/env python3
"""Exercise installer entrypoints with real generation and disposable build/client CLIs."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
CLIENTS = ('claude', 'codex')
FAKE_CLIENT = '''import json, os, sys
from pathlib import Path
args = sys.argv[1:]
root = Path(os.environ['CQC_TEST_ROOT'])
configs = sorted(str(p) for p in (root / 'dist').glob('**/.mcp.json'))
with Path(os.environ['CQC_TEST_LOG']).open('a') as output:
    output.write(json.dumps({'client': Path(sys.argv[0]).name, 'args': args,
                             'configs': configs, 'cwd': str(Path.cwd())}) + '\\n')
if 'list' in args and os.environ.get('CQC_TEST_BROKEN_JSON'):
    print('{broken JSON')
elif args == ['plugin', 'list', '--json']:
    plugins = json.loads(os.environ.get('CQC_TEST_PLUGINS', '[]'))
    print(json.dumps({'installed': plugins} if Path(sys.argv[0]).name == 'codex' else plugins))
elif args == ['plugin', 'marketplace', 'list', '--json']:
    entries = json.loads(os.environ.get('CQC_TEST_MARKETPLACES', '[]'))
    if os.environ.get('CQC_TEST_PROJECT_CATALOG'):
        entries = [{'name': 'cqc-local-claude'}]
    print(json.dumps({'marketplaces': entries} if Path(sys.argv[0]).name == 'codex' else entries))
elif os.environ.get('CQC_TEST_PROJECT_CATALOG') and args[:3] == ['plugin', 'marketplace', 'remove']:
    sys.exit(29)
elif ' '.join(args).startswith(os.environ.get('CQC_TEST_CLIENT_FAIL', '\\0')):
    sys.exit(23)
'''
FAKE_BUILD = '''#!/bin/bash
set -eu
cd "$(dirname "$0")/.."
echo '{"build":true}' >> "$CQC_TEST_LOG"
if [[ "${CQC_TEST_BUILD_FAIL:-}" == 1 ]]; then exit 19; fi
mkdir -p dist/cqc-osx-arm64
printf '#!/bin/sh\\nexit 0\\n' > dist/cqc-osx-arm64/cqc
chmod +x dist/cqc-osx-arm64/cqc
'''


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix='cqc-installer-tests-')
        self.addCleanup(self.scratch.cleanup)
        self.directory = Path(self.scratch.name).resolve()
        self.repository = self.directory / 'checkout with spaces'
        self.repository.mkdir()
        self.scripts = self.repository / 'scripts'
        self.scripts.mkdir()
        for name in ('install-plugin.py', 'install-plugin.sh', 'install-claude-plugin.sh',
                     'install-codex-plugin.sh', 'package-plugins.py'):
            shutil.copy2(ROOT / 'scripts' / name, self.scripts / name)
        shutil.copytree(ROOT / 'plugins', self.repository / 'plugins')
        self.log = self.directory / 'operations.jsonl'
        self.environment = {**os.environ, 'CQC_TEST_ROOT': str(self.repository),
                            'CQC_TEST_LOG': str(self.log)}
        self.claude_config = self.directory / 'claude-config'
        self.claude_config.mkdir()
        self.environment['CLAUDE_CONFIG_DIR'] = str(self.claude_config)
        self.bin = self.directory / 'bin'
        self.bin.mkdir()
        self.environment['PATH'] = str(self.bin) + os.pathsep + os.environ['PATH']
        for client in CLIENTS:
            self.write_executable(self.bin / client, '#!' + sys.executable + '\n' + FAKE_CLIENT)
        self.write_executable(self.scripts / 'package-macos.sh', FAKE_BUILD)

    @staticmethod
    def write_executable(path, content):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content)
        path.chmod(0o755)

    def install(self, client, *arguments):
        return subprocess.run([str(self.scripts / f'install-{client}-plugin.sh'), *map(str, arguments)],
                              cwd=self.directory, env=self.environment, capture_output=True,
                              text=True, timeout=30)

    def operations(self):
        if not self.log.exists():
            return []
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def configs(self, client):
        return sorted((self.repository / 'dist').glob(f'**/{client}/plugins/cqc/.mcp.json'))

    def assert_success(self, result):
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def clear_log(self):
        self.log.unlink(missing_ok=True)

    def failed_install_calls(self, client):
        self.clear_log()
        result = self.install(client)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn('CQC installed.', result.stdout)
        return [entry['args'] for entry in self.operations() if 'args' in entry]

    def test_default_builds_and_generates_before_client_commands(self):
        for client in CLIENTS:
            with self.subTest(client=client):
                self.clear_log()
                previous = set(self.configs(client))
                self.assert_success(self.install(client))
                generated = set(self.configs(client)) - previous
                self.assertEqual(len(generated), 1)
                configuration = generated.pop()
                operations = self.operations()
                self.assertEqual(operations[0], {'build': True})
                self.assertTrue(operations[1]['configs'], operations)
                commands = [entry['args'] for entry in operations[1:]]
                additions = [call for call in commands if call[:3] == ['plugin', 'marketplace', 'add']]
                self.assertEqual(additions[-1][-1], str(configuration.parents[2]))
                expected = self.repository / 'dist/cqc-osx-arm64/cqc'
                server = json.loads(configuration.read_text())['mcpServers']['cqc']
                self.assertEqual(server['command'], str(expected))

    def test_shared_wrapper_refreshes_both_clients_with_one_build(self):
        executable = self.repository / 'dist/cqc-osx-arm64/cqc'
        self.write_executable(executable, '#!/bin/sh\nexit 42\n')
        result = subprocess.run([self.scripts / 'install-plugin.sh'], cwd=self.directory,
                                env=self.environment, capture_output=True, text=True, timeout=30)
        self.assert_success(result)
        self.assertEqual(executable.read_text(), '#!/bin/sh\nexit 0\n')
        operations = self.operations()
        self.assertEqual([entry for entry in operations if 'build' in entry], [{'build': True}])
        self.assertEqual(operations[0], {'build': True})
        commands = {'claude': ['plugin', 'install'], 'codex': ['plugin', 'add']}
        for client, command in commands.items():
            config, = self.configs(client)
            server = json.loads(config.read_text())['mcpServers']['cqc']
            self.assertEqual(server['command'], str(executable))
            self.assertTrue(any(entry['client'] == client and entry['args'][:2] == command
                                for entry in operations[1:]))

    def test_build_failure_stops_before_generation_and_client(self):
        self.environment['CQC_TEST_BUILD_FAIL'] = '1'
        for client in CLIENTS:
            with self.subTest(client=client):
                self.clear_log()
                self.assertNotEqual(self.install(client).returncode, 0)
                self.assertEqual(self.operations(), [{'build': True}])
                self.assertFalse(self.configs(client))

    def test_generation_failure_leaves_client_settings_untouched(self):
        shutil.rmtree(self.repository / 'plugins/skills')
        for client in CLIENTS:
            with self.subTest(client=client):
                self.clear_log()
                self.assertNotEqual(self.install(client).returncode, 0)
                self.assertEqual(self.operations(), [{'build': True}])

    def test_prebuilt_mcp_with_spaces_skips_build_and_reconfigures(self):
        for client in CLIENTS:
            for name in ('first MCP', 'second MCP'):
                with self.subTest(client=client, mcp=name):
                    self.clear_log()
                    prebuilt = self.directory / name
                    self.write_executable(prebuilt / 'cqc', '#!/bin/sh\nexit 0\n')
                    previous = set(self.configs(client))
                    self.assert_success(self.install(client, '--mcp-dir', prebuilt))
                    self.assertTrue(all('build' not in item for item in self.operations()))
                    generated = set(self.configs(client)) - previous
                    self.assertEqual(len(generated), 1)
                    server = json.loads(generated.pop().read_text())['mcpServers']['cqc']
                    self.assertEqual(server['command'], str(prebuilt / 'cqc'))

    def test_invalid_arguments_do_not_build_or_invoke_client(self):
        for client in CLIENTS:
            for arguments in (('--mcp-dir', self.directory / 'missing'),
                              ('packages', '--mcp-dir', 'mcp'), ('one', 'two')):
                with self.subTest(client=client, arguments=arguments):
                    self.clear_log()
                    self.assertNotEqual(self.install(client, *arguments).returncode, 0)
                    self.assertEqual(self.operations(), [])

    def test_existing_packages_skip_build_and_generation(self):
        for client in CLIENTS:
            with self.subTest(client=client):
                previous = set(self.configs(client))
                self.assert_success(self.install(client))
                generated = set(self.configs(client)) - previous
                package = generated.pop().parents[3]
                before = set((self.repository / 'dist').glob('**/.mcp.json'))
                self.clear_log()
                self.assert_success(self.install(client, package))
                self.assertTrue(all('build' not in item for item in self.operations()))
                self.assertEqual(set((self.repository / 'dist').glob('**/.mcp.json')), before)

    def test_registration_failure_does_not_install_or_claim_success(self):
        self.environment['CQC_TEST_CLIENT_FAIL'] = 'plugin marketplace add'
        for client in CLIENTS:
            with self.subTest(client=client):
                calls = self.failed_install_calls(client)
                self.assertTrue(calls and calls[-1][:3] == ['plugin', 'marketplace', 'add'], calls)

    def test_installation_failure_is_reported(self):
        for client in CLIENTS:
            with self.subTest(client=client):
                self.environment['CQC_TEST_CLIENT_FAIL'] = (
                    f'plugin {"install" if client == "claude" else "add"}')
                self.failed_install_calls(client)

    def test_refresh_removes_only_matching_plugin_and_catalog(self):
        (self.claude_config / 'settings.json').write_text(json.dumps({
            'extraKnownMarketplaces': {'cqc-local-claude': {'source': {'source': 'directory', 'path': 'old'}}}}))
        for client in CLIENTS:
            with self.subTest(client=client):
                identity = 'id' if client == 'claude' else 'pluginId'
                target = f'cqc@cqc-local-{client}'
                self.environment['CQC_TEST_PLUGINS'] = json.dumps([
                    {identity: target, 'scope': 'user'},
                    {identity: 'unrelated@other-marketplace', 'scope': 'user'}])
                self.environment['CQC_TEST_MARKETPLACES'] = json.dumps([
                    {'name': f'cqc-local-{client}'}, {'name': 'other-marketplace'}])
                self.clear_log()
                self.assert_success(self.install(client))
                calls = [entry['args'] for entry in self.operations() if 'args' in entry]
                removals = [call for call in calls if 'remove' in call or 'uninstall' in call]
                self.assertEqual(len(removals), 2, calls)
                self.assertEqual({call[-1] for call in removals}, {target, f'cqc-local-{client}'})
                self.assertTrue(all('other-marketplace' not in ' '.join(call) for call in calls))

    def test_claude_project_plugin_is_not_uninstalled(self):
        self.environment['CQC_TEST_PLUGINS'] = json.dumps([
            {'id': 'cqc@cqc-local-claude', 'scope': 'project'}])
        self.assert_success(self.install('claude'))
        calls = [entry['args'] for entry in self.operations() if 'args' in entry]
        self.assertFalse(any('uninstall' in call for call in calls), calls)

    def test_broken_client_inventory_stops_before_settings_changes(self):
        self.environment['CQC_TEST_BROKEN_JSON'] = '1'
        for client in CLIENTS:
            with self.subTest(client=client):
                calls = self.failed_install_calls(client)
                self.assertTrue(calls and all('list' in call for call in calls), calls)

    def test_project_only_claude_catalog_does_not_affect_user_install(self):
        project_settings = self.directory / '.claude/settings.json'
        project_settings.parent.mkdir()
        original = '{"extraKnownMarketplaces":{"cqc-local-claude":{"source":{"source":"directory","path":"project-only"}}}}'
        project_settings.write_text(original)
        self.environment['CQC_TEST_PROJECT_CATALOG'] = '1'
        self.assert_success(self.install('claude'))
        self.assertEqual(project_settings.read_text(), original)
        calls = [entry for entry in self.operations() if 'args' in entry]
        self.assertTrue(calls and all(entry['cwd'] != str(self.directory) for entry in calls), calls)

    def test_malformed_claude_user_settings_prevent_mutation(self):
        (self.claude_config / 'settings.json').write_text('{broken settings')
        calls = self.failed_install_calls('claude')
        self.assertTrue(all('list' in call for call in calls), calls)


if __name__ == '__main__':
    unittest.main()
