#!/usr/bin/env python3
"""Verify packaged MCP tools with host Java/.NET runtimes removed from PATH."""
import hashlib
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading


class McpSession:
    def __init__(self, package):
        environment = {key: value for key, value in os.environ.items()
                       if not key.startswith(('JAVA_', 'JDK_', 'DOTNET_', 'CQC_'))}
        self.command_path = tempfile.TemporaryDirectory(prefix='cqc-smoke-path-')
        (Path(self.command_path.name) / 'git').symlink_to('/usr/bin/git')
        environment['PATH'] = self.command_path.name
        environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
        self.process = subprocess.Popen([str(package / 'cqc')], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        text=True, env=environment, cwd=package)
        self.responses = queue.Queue()
        self.errors = []
        threading.Thread(target=self._read, args=(self.process.stdout, self.responses.put), daemon=True).start()
        threading.Thread(target=self._read, args=(self.process.stderr, self.errors.append), daemon=True).start()
        self.sequence = 0

    @staticmethod
    def _read(stream, consume):
        for line in stream:
            consume(line)

    def request(self, method, params):
        self.sequence += 1
        self.send({'jsonrpc': '2.0', 'id': self.sequence, 'method': method, 'params': params})
        while True:
            try:
                result = json.loads(self.responses.get(timeout=60))
            except queue.Empty as error:
                raise AssertionError('MCP response timed out: ' + ''.join(self.errors)) from error
            if result.get('id') != self.sequence:
                continue
            if 'error' in result:
                raise AssertionError(result['error'])
            return result['result']

    def send(self, payload):
        self.process.stdin.write(json.dumps(payload) + '\n')
        self.process.stdin.flush()

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()
        self.command_path.cleanup()


def digest_sources(root):
    return {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in root.rglob('*') if path.is_file()}


def initialize(session):
    session.request('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                  'clientInfo': {'name': 'cqc-package-smoke', 'version': '1'}})
    session.send({'jsonrpc': '2.0', 'method': 'notifications/initialized'})
    tools = session.request('tools/list', {})['tools']
    expected = {'inspect_project', 'check_code_quality', 'check_test_quality',
                'check_duplication', 'check_dependencies', 'check_all'}
    assert {tool['name'] for tool in tools} == expected
    return tools


def invoke(session, tool, root):
    assert '10 is the desired score.' in tool['description']
    response = session.request('tools/call', {'name': tool['name'], 'arguments': {'request': {'projectRoot': str(root)}}})
    assert not response.get('isError'), response
    text = ''.join(block['text'] for block in response['content'] if block['type'] == 'text')
    result = json.loads(text)
    assert result['message'] == '10 is the desired score.'
    return result


def verify_languages(result):
    entities = [entity for family in result['results'] for entity in family['entities']]
    assert any(entity['entityId'].startswith('java:') for entity in entities), result
    assert any(entity['entityId'].startswith('csharp:') for entity in entities), result
    verify_standard_modules(result)
    verify_diagnostics(result)


def verify_standard_modules(result):
    dependencies = next(family for family in result['results'] if family['family'] == 'dependencies')
    classes = [entity for entity in dependencies['entities'] if entity['kind'] == 'class']
    assert classes and all(entity['status'] == 'complete' for entity in classes), dependencies


def verify_diagnostics(result):
    diagnostics = result['diagnostics'] + [d for family in result['results'] for d in family['diagnostics']]
    assert not any('worker_' in d['code'] or d['code'] == 'analyzer_failed' for d in diagnostics), result


def run(package):
    with tempfile.TemporaryDirectory(prefix='cqc-package-smoke-') as directory:
        root = Path(directory)
        (root / 'Example.java').write_text('package sample; import java.sql.Connection; import java.sql.SQLException; import java.net.http.HttpClient; '
                                          'public class Example { public int add(int a, int b) { return a + b; } '
                                          'public boolean ready(Connection connection) throws SQLException { return !connection.isClosed(); } '
                                          'public HttpClient client() { return HttpClient.newHttpClient(); } }\n')
        (root / 'Example.cs').write_text('using System.Data.Common; namespace Sample; public class Example { '
                                        'public DbConnection? Connection { get; set; } '
                                        'public int Add(int a, int b) { return a + b; } }\n')
        original = digest_sources(root)
        session = McpSession(package)
        try:
            for tool in initialize(session):
                result = invoke(session, tool, root)
                if tool['name'] == 'check_all':
                    verify_languages(result)
            assert digest_sources(root) == original, 'Analysis-only changed fixture files'
        finally:
            session.close()
    print('PASS: packaged MCP initialized and all six tools analyzed Java/C# without host runtimes.')


if __name__ == '__main__':
    run(Path(sys.argv[1]).resolve())
