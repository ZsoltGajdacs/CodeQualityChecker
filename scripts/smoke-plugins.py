#!/usr/bin/env python3
"""Exercise both generated plugin MCP connections against real scoring fixtures."""
import argparse
import json
from pathlib import Path
import runpy
import shutil
import tempfile


ROOT = Path(__file__).resolve().parents[1]
SMOKE = runpy.run_path(str(ROOT / 'scripts/smoke-package.py'))


def verify_analysis(result):
    SMOKE['verify_diagnostics'](result)
    entities = [entity for family in result['results'] for entity in family['entities']]
    assert any(e['entityId'].startswith('java:') for e in entities), result
    assert any(e['entityId'].startswith('csharp:') for e in entities), result
    code = next(f for f in result['results'] if f['family'] == 'code')
    assert any(e['score'] is None for e in code['entities']), 'Missing coverage must not invent a score'


def verify_unsupported(session, root):
    inspection = session.request('tools/call', {'name': 'inspect_project', 'arguments': {
        'request': {'projectRoot': str(root / 'unsupported'), 'execute': False}}})
    data = SMOKE['retrieve_full'](session, SMOKE['structured'](inspection))
    assert not data['project']['selectedFiles'], data


def verify_client(marketplace, root):
    plugin = marketplace / 'plugins/cqc'
    canonical = ROOT / 'plugins/skills/cqc-review/SKILL.md'
    assert (plugin / 'skills/cqc-review/SKILL.md').read_bytes() == canonical.read_bytes()
    server = json.loads((plugin / '.mcp.json').read_text())['mcpServers']['cqc']
    session = SMOKE['McpSession'](Path(server['command']).parent, server=server)
    try:
        for tool in SMOKE['initialize'](session):
            result = SMOKE['invoke'](session, tool, root)
            if tool['name'] == 'check_all':
                verify_analysis(result)
        verify_unsupported(session, root)
    finally:
        session.close()


def run(output):
    with tempfile.TemporaryDirectory(prefix='cqc-plugin-smoke-') as directory:
        root = Path(directory)
        shutil.copytree(ROOT / 'fixtures/scoring', root / 'scoring',
                        ignore=shutil.ignore_patterns('bin', 'obj', 'target', '.cqc'))
        (root / 'unsupported').mkdir()
        (root / 'unsupported/example.py').write_text('print("unsupported")\n')
        SMOKE['initialize_git'](root)
        original = SMOKE['digest_sources'](root)
        for client in ('claude', 'codex'):
            verify_client(output / client, root)
            assert SMOKE['digest_sources'](root) == original, 'Source-only analysis changed fixtures'
            print(f'PASS: {client}: eight real tools, bounded pages, Java/C#, missing coverage, unchanged inputs')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path, help='Generated local marketplaces directory')
    run(parser.parse_args().output.resolve())
