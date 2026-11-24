import path from 'node:path';
import ts from 'typescript';
import { compileProjects } from './compilation.mjs';
import { dependencies } from './dependencies.mjs';
import { canonical, loadProjects, projectOwner, toContext } from './projects.mjs';
import { referenceOutputs } from './reference-outputs.mjs';
import { ResolutionHost } from './resolution-host.mjs';
import { executables } from './structure.mjs';
import { language, location } from './syntax.mjs';

export function analyze(snapshot) {
    const schemaVersion = '2';
    if (snapshot.schemaVersion !== schemaVersion) throw new Error(`Expected snapshot schemaVersion ${schemaVersion}.`);
    const { files, sourceOnly } = snapshot;
    const diagnostics = [];
    const sorted = [...files].sort();
    const workspace = prepareWorkspace(snapshot, sorted, diagnostics);
    const facts = sorted.map(file => analyzeFile(file, workspace.absolute(file), workspace));
    const stable = workspace.host.isStable();
    if (!stable) diagnostics.push({ code: 'typescript.resolution_changed',
        message: 'TypeScript analysis inputs changed during collection. Run CQC again against stable inputs.', language });
    const contexts = workspace.projects.map(project => toContext(project, stable)).sort((left, right) => ordinal(left.id, right.id));
    const byContext = new Map(contexts.map(context => [context.id, context]));
    const types = facts.map(fact => typeFact(fact, byContext));
    return {
        schemaVersion, types, methods: [],
        dependencies: facts.flatMap(fact => fact.edges),
        diagnostics: diagnostics.concat(facts.flatMap(fact => fact.diagnostics)),
        contexts, syntaxGaps: facts.flatMap(fact => fact.gaps),
        executables: facts.flatMap(fact => fact.executables),
        semanticComplete: !sourceOnly && contexts.every(context => context.verified)
            && types.every(type => type.evidence.dependencies)
    };
}

function prepareWorkspace(snapshot, files, diagnostics) {
    const { sourceOnly } = snapshot;
    const host = new ResolutionHost(snapshot.root);
    const absolute = file => path.resolve(host.root, file);
    const authored = new Map(files.map(file => [canonical(absolute(file)), file]));
    const configs = sourceOnly ? [] : snapshot.modules.map(module => module.path).filter(file => file !== '.');
    const projects = loadProjects(host.root, configs, host, diagnostics);
    const outputsFor = referenceOutputs(projects, authored, host);
    compileProjects(projects, { host, authored, outputsFor });
    const owner = projectOwner(projects, authored);
    const ownerOf = sourceOnly ? () => ({}) : file => owner(absolute(file));
    return { host, absolute, projects, authored, ownerOf, outputsFor };
}

function analyzeFile(file, absolute, workspace) {
    const text = workspace.host.readFile(absolute);
    const { project, blocker } = workspace.ownerOf(file);
    const source = project?.compilation?.sources.get(canonical(absolute)) ?? parseSource(absolute, text);
    const contextId = project?.id;
    const gaps = syntaxGaps(file, source, contextId, text);
    const resolved = gaps.length ? { edges: [], diagnostics: [] } :
        dependencies(file, source, project, workspace);
    const diagnostics = [...resolved.diagnostics];
    if (blocker) diagnostics.push(ownershipDiagnostic(file, blocker));
    return {
        file, contextId, gaps, diagnostics, edges: resolved.edges,
        location: location(file, source, 0, source.end),
        executables: gaps.length ? [] : executables(file, source)
    };
}

function parseSource(file, text) {
    const kind = file.toLowerCase().endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
    return ts.createSourceFile(file, text ?? '', ts.ScriptTarget.Latest, true, kind);
}

function syntaxGaps(file, source, contextId, text) {
    const gap = (code, start, end, construct) => ({
        file, language, contextId, code, unknownEntities: true, typeId: file,
        location: location(file, source, start, end), construct
    });
    if (text === undefined) return [gap('typescript.source_unavailable', 0, 0, 'source file')];
    return source.parseDiagnostics.map(error => gap('typescript.parse', error.start ?? 0,
        (error.start ?? 0) + (error.length ?? 0), `TS${error.code}: ${ts.flattenDiagnosticMessageText(error.messageText, '\n')}`));
}

function ownershipDiagnostic(file, code) {
    const message = code === 'typescript.configuration_missing' ? 'No TypeScript project configuration owns this source file.' :
        'Multiple TypeScript project configurations own this source file.';
    return { code, message, file, language };
}

function typeFact(fact, contexts) {
    const syntax = fact.gaps.length === 0;
    const verifiedContext = contexts.get(fact.contextId)?.verified ?? false;
    const dependencies = verifiedContext && syntax && fact.diagnostics.length === 0;
    return {
        id: fact.file, language, location: fact.location, isAbstract: false, complete: syntax,
        contextId: fact.contextId, qualifiedName: fact.file,
        evidence: { syntax, identity: syntax, calls: false, dependencies, verifiedContext,
            blockers: [...new Set(fact.gaps.concat(fact.diagnostics).map(item => item.code))].sort() }
    };
}

function ordinal(left, right) {
    if (left < right) return -1;
    return left === right ? 0 : 1;
}
