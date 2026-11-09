#!/usr/bin/env python3
"""Behavioral checks for local plugin generation (stdlib only)."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


class PackagingTests(unittest.TestCase):
    def setUp(self):
        (ROOT / 'dist').mkdir(exist_ok=True)
        self.scratch = tempfile.TemporaryDirectory(prefix='plugin-tests-', dir=ROOT / 'dist')
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.executable = self.root / 'server with spaces' / 'cqc'
        self.executable.parent.mkdir()
        self.executable.write_text('#!/bin/sh\nprintf "server started"\n')
        self.executable.chmod(0o755)
        self.output = self.root / 'packages'

    def generate(self, executable=None, output=None):
        return subprocess.run([sys.executable, str(ROOT / 'scripts/package-plugins.py'),
                               '--cqc-executable', str(executable or self.executable),
                               '--output', str(output or self.output)],
                              capture_output=True, text=True)

    def test_space_path_is_directly_launchable_and_skill_is_shared(self):
        result = self.generate()
        self.assertEqual(result.returncode, 0, result.stderr)
        canonical = ROOT / 'plugins/skills/cqc-review/SKILL.md'
        for client in ('claude', 'codex'):
            plugin = self.output / client / 'plugins/cqc'
            server = json.loads((plugin / '.mcp.json').read_text())['mcpServers']['cqc']
            process = subprocess.run([server['command'], *server.get('args', [])],
                                     capture_output=True, text=True, check=True)
            self.assertEqual(process.stdout, 'server started')
            self.assertEqual((plugin / 'skills/cqc-review/SKILL.md').read_bytes(),
                             canonical.read_bytes())
        self.assertEqual(sorted(self.executable.parent.iterdir()), [self.executable])

    def test_invalid_executable_leaves_no_output(self):
        for candidate in (self.root / 'missing', self.executable.parent):
            with self.subTest(candidate=candidate):
                self.assertNotEqual(self.generate(executable=candidate).returncode, 0)
                self.assertFalse(self.output.exists())
        self.executable.chmod(0o644)
        self.assertNotEqual(self.generate().returncode, 0)
        self.assertFalse(self.output.exists())

    def test_existing_content_is_not_overwritten(self):
        self.output.mkdir()
        marker = self.output / 'keep.txt'
        marker.write_text('keep')
        self.assertNotEqual(self.generate().returncode, 0)
        self.assertEqual(marker.read_text(), 'keep')
        self.assertEqual(list(self.output.iterdir()), [marker])

    def test_empty_output_is_accepted(self):
        self.output.mkdir()
        result = self.generate()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((self.output / 'codex/plugins/cqc/.mcp.json').is_file())

    def test_executable_symlink_is_rejected(self):
        link = self.root / 'cqc-link'
        link.symlink_to(self.executable)
        self.assertNotEqual(self.generate(executable=link).returncode, 0)
        self.assertFalse(self.output.exists())

    def test_unsafe_output_is_rejected_without_touching_target(self):
        for target in (ROOT, self.executable.parent, ROOT / 'plugins'):
            with self.subTest(target=target):
                self.assertNotEqual(self.generate(output=target).returncode, 0)
        self.output.symlink_to(self.executable.parent, target_is_directory=True)
        self.assertNotEqual(self.generate().returncode, 0)
        self.assertTrue(self.executable.is_file())


if __name__ == '__main__':
    unittest.main()
