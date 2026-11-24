import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { analyze } from '../analysis.mjs';

function fixture(t, sources, manifests = {}) {
    const root = mkdtempSync(path.join(tmpdir(), 'cqc-ts-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    for (const [file, contents] of Object.entries({ ...sources, ...manifests })) {
        const absolute = path.join(root, file);
        mkdirSync(path.dirname(absolute), { recursive: true });
        writeFileSync(absolute, typeof contents === 'string' ? contents : JSON.stringify(contents));
    }
    return {
        root,
        files: Object.keys(sources).sort(),
        modules: Object.keys(manifests).filter(file => path.basename(file).startsWith('tsconfig')).map(path => ({ path })),
        schemaVersion: '2'
    };
}

test('TSX ranges and nested callables have independent complexity', t => {
    const input = fixture(t, { 'View.tsx': [
        'export function View({ visible }: { visible: boolean }) {',
        '    const render = (name: string) => name ? <em>{name}</em> : null;',
        '    return visible && <div>{render("test")}</div>;',
        '}'
    ].join('\n') });
    const facts = analyze({ ...input, sourceOnly: true });
    assert.deepEqual(facts.executables.map(({ complexity, location }) => [complexity, location.startLine, location.endLine]),
        [[2, 1, 4], [2, 2, 2]]);
    assert.deepEqual(facts.executables.map(({ id }) => id), ['View.tsx#View', 'View.tsx#View/render']);
    assert.equal(facts.executables[0].duplicationLines, 2);
});

test('read-only project references and aliases resolve to authored modules', t => {
    const input = fixture(t, {
        'app/View.tsx': 'import { shared } from "@repo/shared"; export const View = () => <p>{shared}</p>;',
        'shared/index.ts': 'export { View } from "../app/View"; export const shared = 1;'
    }, {
        'tsconfig.json': { files: [], references: [{ path: './app/config.json' }, { path: './shared' }] },
        'tsconfig.base.json': { compilerOptions: { target: 'es2022' } },
        'app/config.json': {
            extends: '../tsconfig.base.json',
            compilerOptions: { module: 'esnext', moduleResolution: 'bundler', jsx: 'preserve', paths: { '@repo/shared': ['../shared/index.ts'] } },
            include: ['View.tsx']
        },
        'shared/tsconfig.json': { include: ['index.ts'] }
    });
    const facts = analyze(input);
    assert.equal(facts.semanticComplete, true);
    assert.deepEqual(facts.dependencies.map(({ from, to }) => [from, to]), [
        ['app/View.tsx', 'shared/index.ts'],
        ['shared/index.ts', 'app/View.tsx']
    ]);
    assert.deepEqual(facts, analyze(input));
    assert.ok(facts.contexts.some(context => context.id === 'typescript:app/config.json'));
    assert.ok(facts.contexts.every(context => context.modulePath !== 'tsconfig.base.json'));
});

test('parse failures and unresolved imports keep independent source evidence', t => {
    const input = fixture(t, {
        'Gap.ts': 'export function broken( {',
        'View.tsx': 'import missing from "missing"; export function View(ok: boolean) { return ok ? <p>{missing}</p> : null; }'
    }, { 'tsconfig.json': { compilerOptions: { jsx: 'preserve' } } });
    const facts = analyze(input);
    assert.equal(facts.semanticComplete, false);
    assert.equal(facts.syntaxGaps[0].file, 'Gap.ts');
    assert.equal(facts.types.find(type => type.id === 'View.tsx').evidence.syntax, true);
    assert.equal(facts.types.find(type => type.id === 'View.tsx').evidence.dependencies, false);
    assert.ok(facts.executables.some(body => body.id === 'View.tsx#View'));
    assert.ok(facts.diagnostics.some(diagnostic => diagnostic.code === 'typescript.module_unresolved'));
});

test('synthetic executable identities cannot collide with named declarations', t => {
    const input = fixture(t, {
        'module.ts': 'export function module() { return 2; } export const answer = module();'
    });
    const facts = analyze({ ...input, sourceOnly: true });
    assert.deepEqual(facts.executables.map(({ id }) => id), ['module.ts#!module', 'module.ts#module']);
});

test('evaluated class declaration expressions are independently measured', t => {
    const input = fixture(t, { 'classes.ts': [
        '@decorate(ready && token)',
        'class Derived extends (ready ? Left : Right) {',
        '    [ready ? "left" : "right"] = token ?? "none";',
        '    static { if (ready) start(); }',
        '}'
    ].join('\n') });
    const facts = analyze({ ...input, sourceOnly: true });
    assert.deepEqual(facts.executables.map(({ complexity }) => complexity), [2, 2, 2, 2, 2]);
    assert.deepEqual(facts.executables.map(({ id }) => id), [
        'classes.ts#Derived/decorator', 'classes.ts#Derived/heritage',
        'classes.ts#Derived/field-computed', 'classes.ts#Derived/field-computed/computed-name',
        'classes.ts#Derived/static-block'
    ]);
});

test('clone measurements normalize renamed values and exclude declaration lines and braces', t => {
    function body(fn, name, literal) {
        return [
            `export function ${fn}(${name}: number) {`,
            '    // Annotation does not contribute a code line.',
            `    const changed = ${name} + ${literal};`,
            '    if (changed > 0) {',
            '        return changed * 2;',
            '    }',
            '    return 0;',
            '}'
        ].join('\n');
    }
    const input = fixture(t, {
        'first.ts': body('a', 'x', 1),
        'second.ts': body('b', 'y', 10),
        'third.ts': body('c', 'z', 100).replace('return changed * 2;', 'throw new Error("unexpected");')
    });
    const facts = analyze({ ...input, sourceOnly: true });
    const [first, second, third] = facts.executables;
    assert.equal(first.duplicationLines, 4);
    assert.equal(second.duplicationLines, 4);
    assert.equal(first.complexity, 2);
    assert.ok(first.nodeCount >= 20);
    assert.ok(first.fingerprints.length > 0);
    assert.deepEqual(first.fingerprints, second.fingerprints);
    assert.notDeepEqual(first.fingerprints, third.fingerprints);
});

test('project reference output paths resolve before any declarations have been built', t => {
    const input = fixture(t, {
        'app/view.ts': 'import type { Data } from "@domain"; export type Reading = Data;',
        'domain/src/index.ts': 'export interface Data { value: string }'
    }, {
        'app/tsconfig.json': {
            compilerOptions: { module: 'esnext', moduleResolution: 'bundler', paths: { '@domain': ['../domain/dist/index.d.ts'] } },
            references: [{ path: '../domain' }], include: ['view.ts']
        },
        'domain/tsconfig.json': {
            compilerOptions: { composite: true, rootDir: './src', outDir: './dist' }, include: ['src']
        }
    });
    const facts = analyze(input);
    assert.equal(facts.semanticComplete, true);
    assert.deepEqual(facts.dependencies.map(({ from, to }) => [from, to]), [['app/view.ts', 'domain/src/index.ts']]);
});

test('known edges survive computed imports and context ambiguity', t => {
    const input = fixture(t, {
        'app.ts': 'export const load = (specifier: string) => import(specifier); export const later = () => import("./other");',
        'other.ts': 'export const value = true;'
    }, {
        'tsconfig.json': { files: ['app.ts', 'other.ts'] },
        'tsconfig.other.json': { files: ['other.ts'] }
    });
    const facts = analyze(input);
    assert.equal(facts.semanticComplete, false);
    assert.equal(facts.dependencies[0].to, 'other.ts');
    assert.deepEqual(facts.diagnostics.map(({ code }) => code), [
        'typescript.computed_import', 'typescript.target_context_unverified', 'typescript.configuration_conflict'
    ]);
});

test('circular project references cannot verify dependency context', t => {
    const input = fixture(t, {
        'a/index.ts': 'export const a = 1;',
        'b/index.ts': 'export const b = 2;'
    }, {
        'a/tsconfig.json': { files: ['index.ts'], references: [{ path: '../b' }] },
        'b/tsconfig.json': { files: ['index.ts'], references: [{ path: '../a' }] }
    });
    const facts = analyze(input);
    assert.equal(facts.semanticComplete, false);
    assert.ok(facts.contexts.every(context => context.blockers.includes('typescript.project_reference_cycle')));
    assert.ok(facts.types.every(type => type.evidence.syntax));
});

test('invalid compiler option combinations prevent dependency verification', t => {
    const input = fixture(t, { 'a.ts': 'export const a = true;' }, {
        'tsconfig.json': { compilerOptions: { module: 'esnext', moduleResolution: 'node16' } }
    });
    const facts = analyze(input);
    assert.equal(facts.semanticComplete, false);
    assert.ok(facts.types[0].evidence.syntax);
    assert.match(facts.diagnostics[0].message, /TS5110/);
});

test('workspace package source matches when the checkout root itself is a symlink', t => {
    const input = fixture(t, {
        'src/a.ts': 'import { shared } from "shared"; export const value = shared;',
        'packages/shared/index.ts': 'export const shared = 42;'
    }, {
        'packages/shared/package.json': { name: 'shared', types: 'index.ts' },
        'tsconfig.json': {
            compilerOptions: { module: 'esnext', moduleResolution: 'bundler', preserveSymlinks: true },
            include: ['src', 'packages']
        }
    });
    const aliasParent = mkdtempSync(path.join(tmpdir(), 'cqc-ts-alias-'));
    t.after(() => rmSync(aliasParent, { recursive: true, force: true }));
    const alias = path.join(aliasParent, 'checkout');
    symlinkSync(input.root, alias, 'junction');
    mkdirSync(path.join(input.root, 'node_modules'));
    symlinkSync(path.join(input.root, 'packages/shared'), path.join(input.root, 'node_modules/shared'), 'junction');
    const facts = analyze({ ...input, root: alias });
    assert.equal(facts.semanticComplete, true);
    assert.equal(facts.dependencies[0].to, 'packages/shared/index.ts');
});

test('entry-point configs own transitively imported authored files', t => {
    const input = fixture(t, {
        'a.ts': 'export { b } from "./b"; export const a = 1;',
        'b.ts': 'export { a } from "./a"; export const b = 2;'
    }, { 'tsconfig.json': { files: ['a.ts'] } });
    const facts = analyze(input);
    assert.equal(facts.semanticComplete, true);
    assert.deepEqual(facts.dependencies.map(({ from, to }) => [from, to]), [['a.ts', 'b.ts'], ['b.ts', 'a.ts']]);
});

test('ambient imports leave dependency evidence complete only for matching modules', t => {
    const input = fixture(t, {
        'view.tsx': 'import { join } from "node:path"; import "./View.css"; export const view = join("a", "b");',
        'missing.ts': 'import "./unexpected.js";'
    }, {
        'node_modules/@types/node/index.d.ts': 'declare module "node:path" { export function join(...parts: string[]): string; }',
        'assets.d.ts': 'declare module "*.css"; declare module "./unexpected.js";',
        'tsconfig.json': { compilerOptions: { types: ['node'] } }
    });
    const facts = analyze(input);
    assert.equal(facts.types.find(type => type.id === 'view.tsx').evidence.dependencies, true);
    assert.deepEqual(facts.dependencies, []);
    assert.deepEqual(facts.diagnostics.map(({ code, file }) => [code, file]),
        [['typescript.module_unresolved', 'missing.ts']]);
});

test('CommonJS dependencies prefer require exports, follow their sources and ignore shadowed calls', t => {
    const input = fixture(t, {
        'a.ts': 'export const b = require("peer"); const load = (require: (path: string) => string) => require("./missing");',
        'packages/peer/cjs.ts': 'export const a = require("../../a.js");'
    }, {
        'package.json': { type: 'module' },
        'tsconfig.json': { files: ['a.ts'], compilerOptions: { module: 'nodenext' } },
        'packages/peer/package.json': { name: 'peer', exports: { import: './esm.js', require: './cjs.js' } },
        'packages/peer/esm.ts': 'export const wrong = true;'
    });
    mkdirSync(path.join(input.root, 'node_modules'));
    symlinkSync(path.join(input.root, 'packages/peer'), path.join(input.root, 'node_modules/peer'), 'junction');
    const facts = analyze(input);
    assert.equal(facts.semanticComplete, true);
    assert.deepEqual(facts.dependencies.map(({ from, to }) => [from, to]),
        [['a.ts', 'packages/peer/cjs.ts'], ['packages/peer/cjs.ts', 'a.ts']]);
});

test('computed CommonJS imports preserve known edges and only block their source', t => {
    const input = fixture(t, {
        'a.ts': 'export const known = require("./b"); export const load = (name: string) => require(name);',
        'b.ts': 'export const b = true;'
    }, { 'tsconfig.json': { files: ['a.ts', 'b.ts'] } });
    const facts = analyze(input);
    assert.deepEqual(facts.dependencies.map(({ from, to }) => [from, to]), [['a.ts', 'b.ts']]);
    assert.deepEqual(facts.diagnostics.map(({ code, file }) => [code, file]),
        [['typescript.computed_import', 'a.ts']]);
    assert.equal(facts.types.find(type => type.id === 'b.ts').evidence.dependencies, true);
});
