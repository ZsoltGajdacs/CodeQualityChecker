import ts from 'typescript';
import { assetSpecifier, verifiesAsset } from './asset-resolution.mjs';
import { imports } from './imports.mjs';
import { resolveModule } from './module-resolution.mjs';
import { canonical } from './projects.mjs';
import { verifiesRuntimeImport } from './runtime-resolution.mjs';
import { language, nodeLocation } from './syntax.mjs';

export function dependencies(file, source, project, workspace) {
    if (!project?.options) return { edges: [], diagnostics: [] };
    const { host, outputsFor } = workspace;
    source.impliedNodeFormat = ts.getImpliedNodeFormatForFile(source.fileName, undefined, host, project.options);
    const input = { file, source, project, ...outputsFor(project) };
    const results = imports(source, project.options, project.compilation?.checker)
        .map(request => resolveImport(request, input, workspace));
    for (const contextId of results.map(result => result.contextId).filter(Boolean))
        if (contextId !== project.id) project.reachable.add(contextId);
    return {
        edges: results.map(result => result.edge).filter(Boolean),
        diagnostics: results.map(result => result.diagnostic).filter(Boolean)
    };
}

function resolveImport(request, input, workspace) {
    const { file, source, project, resolutionHost, outputs } = input;
    const location = nodeLocation(file, source, request.node);
    const target = resolveModule(request, source, project, resolutionHost);
    const asset = assetSpecifier(request, project.options);
    const to = authoredTarget(target, workspace, outputs, asset);
    if (to !== undefined) return dependencyFact({ from: file, to, location }, workspace);
    if (asset !== undefined)
        return verifiesAsset(asset, request, input, workspace.host) ? {} : { diagnostic: unresolved(file, location, request.specifier) };
    if (target || verifiesRuntimeImport(request, input, workspace.host)) return {};
    return unresolvedImport(request, project, file, location);
}

function authoredTarget(target, workspace, outputs, asset) {
    if (!target) return;
    const path = canonical(workspace.host.realpath(target));
    if (asset !== undefined && ts.isDeclarationFileName(target)) return outputs.get(path);
    return workspace.authored.get(path) ?? outputs.get(path);
}

function dependencyFact(edge, workspace) {
    const { from: file, to, location } = edge;
    if (to === null) return { diagnostic: { code: 'typescript.reference_output_ambiguous', language, file, location,
        message: 'Multiple referenced source files own the resolved declaration output.' } };
    if (!to || to === file) return {};
    const contextId = workspace.ownerOf(to).project?.id;
    if (contextId) return { edge, contextId };
    return { edge, diagnostic: { code: 'typescript.target_context_unverified', language, file, location,
        message: `Resolved module "${to}" has no unambiguous project configuration.` } };
}

function unresolvedImport({ specifier }, project, file, location) {
    if (specifier !== undefined && project.compilation?.hasAmbientModule(specifier)) return {};
    return { diagnostic: unresolved(file, location, specifier) };
}

function unresolved(file, location, specifier) {
    return specifier === undefined ?
        { code: 'typescript.computed_import', message: 'A computed import has no statically resolvable module target.', file, location, language } :
        { code: 'typescript.module_unresolved', message: `Cannot resolve module "${specifier}". Check the project configuration and installed dependencies.`, file, location, language };
}
