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


FIXED_REVIEW_TOOLS = {'check_code_quality', 'check_uncommitted_changes_quality'}


class McpSession:
    def __init__(self, package, server=None):
        environment = {key: value for key, value in os.environ.items()
                       if not key.startswith(('JAVA_', 'JDK_', 'DOTNET_', 'CQC_'))}
        self.command_path = tempfile.TemporaryDirectory(prefix='cqc-smoke-path-')
        (Path(self.command_path.name) / 'git').symlink_to('/usr/bin/git')
        environment['PATH'] = self.command_path.name
        environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
        command = [server['command'], *server.get('args', [])] if server else [str(package / 'cqc')]
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE,
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
                line = self.responses.get(timeout=60)
                assert len(line.encode('utf-8')) < 1024 * 1024, 'MCP message exceeded 1 MiB'
                result = json.loads(line)
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
    expected = FIXED_REVIEW_TOOLS | {'inspect_project', 'check_test_quality',
                                    'check_duplication', 'check_dependencies', 'check_all', 'get_report_page'}
    assert {tool['name'] for tool in tools} == expected
    return [tool for tool in tools if tool['name'] != 'get_report_page']


def structured(response):
    assert not response.get('isError'), response
    size = len(json.dumps(response, separators=(',', ':')).encode('utf-8'))
    assert size <= 512 * 1024, 'Tool result exceeded 512 KiB'
    return response['structuredContent']


def retrieve_full(session, summary):
    assert summary['schemaVersion'] == '3', summary
    chunks = []
    cursor = None
    while True:
        request = {'reportId': summary['reportId'], 'section': 'raw'}
        if cursor is not None:
            request['cursor'] = cursor
        page = structured(session.request('tools/call', {
            'name': 'get_report_page', 'arguments': {'request': request}}))
        chunks.append(page['rawChunk'])
        cursor = page.get('nextCursor')
        if cursor is None:
            return json.loads(''.join(chunks))


def invoke(session, tool, root):
    assert '10 is the desired score.' in tool['description']
    response = session.request('tools/call', {'name': tool['name'], 'arguments': {'request': {
        'projectRoot': str(root), 'execute': False}}})
    summary = structured(response)
    verify_summary_schema(tool, summary)
    result = retrieve_full(session, summary)
    assert result['message'] == '10 is the desired score.'
    if tool['name'] in FIXED_REVIEW_TOOLS:
        verify_source_review(tool['name'], summary, result)
    if tool['name'] == 'check_all':
        code = next(family for family in summary['families'] if family['family'] == 'code')
        assert any(bucket['score'] is None for bucket in code['scores']), code
    return result


def verify_summary_schema(tool, summary):
    properties = tool['outputSchema']['properties']
    optional_fields = {
        'families': {'scores', 'scoredFileCount', 'unscoredFileCount'},
        'diagnostics': {'selectedCount', 'contextCount', 'projectWideCount', 'extensions'},
    }
    for section, optional in optional_fields.items():
        verify_summary_items(f"{tool['name']} {section}", properties[section]['items'], summary[section], optional)


def verify_summary_items(label, schema, items, optional):
    required = set(schema.get('required', []))
    assert optional <= schema['properties'].keys() - required, schema
    for index, item in enumerate(items):
        missing = required.difference(item)
        assert not missing, f"{label}/{index} missing required fields {sorted(missing)}"


def verify_source_review(tool, summary, result):
    expected_scope = 'repository' if tool == 'check_code_quality' else 'changed'
    assert summary['scope'] == result['scope'] == expected_scope
    assert summary['fileCount'] > 0, summary
    assert summary['analysisNote'] == 'Running with `execute: true` may reveal additional findings.'
    assert {family['family'] for family in result['results']} <= {'code', 'duplication'}
    families = {family['family']: family for family in summary['families']}
    duplication = families['duplication']
    assert duplication['scores'], duplication
    verify_static_assessments(result)
    production = next(family for family in result['results'] if family['family'] == 'code')
    verify_source_production(families['code'], production)


def verify_source_production(code, production):
    assert 'scores' not in code, code
    assert code['staticComplexityScores'], code
    assert code['scoredFileCount'] > 0, code
    assert code['unscoredFileCount'] == 0, code
    assert production['scoredFileCount'] == code['scoredFileCount'], production
    assert all('score' not in entity and 'blockers' not in entity for entity in production['entities']), production


def initialize_git(root):
    subprocess.run(['/usr/bin/git', 'init', '--quiet', str(root)], check=True, capture_output=True)


def verify_languages(result):
    entities = [entity for family in result['results'] for entity in family['entities']]
    assert any(entity['entityId'].startswith('java:') for entity in entities), result
    assert any(entity['entityId'].startswith('csharp:') for entity in entities), result
    verify_standard_modules(result)
    verify_diagnostics(result)


def verify_standard_modules(result):
    dependencies = next(family for family in result['results'] if family['family'] == 'dependencies')
    classes = [entity for entity in dependencies['entities'] if entity['kind'] == 'class']
    assert classes, dependencies
    verify_unverified_java_contexts(classes)
    verify_platform_symbols(result)
    verify_static_assessments(result)


def verify_unverified_java_contexts(classes):
    java = [entity for entity in classes if entity['entityId'].startswith('java:')]
    assert java and all('context.unverified' in entity['blockers'] for entity in java), classes


def verify_static_assessments(result):
    production = next(family for family in result['results'] if family['family'] == 'code')
    assert production['entities'], production
    assert all(e['staticComplexity']['status'] == 'complete' for e in production['entities']), production


def verify_platform_symbols(result):
    unresolved = ('java.unresolved_type', 'java.unresolved_call')
    assert not any(d['code'] in unresolved for d in result['diagnostics']), result


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
        initialize_git(root)
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
    print('PASS: eight packaged MCP tools, bounded summaries/raw pages, Java/C# without host runtimes.')


if __name__ == '__main__':
    run(Path(sys.argv[1]).resolve())
