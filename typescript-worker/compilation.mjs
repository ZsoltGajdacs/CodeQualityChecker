import path from 'node:path';
import ts from 'typescript';
import { resolveModule } from './dependencies.mjs';
import { imports } from './imports.mjs';
import { canonical } from './projects.mjs';

export function compileProjects(projects, workspace) {
    const parsed = new Map(projects.map(project => [canonical(project.file), project.parsed]));
    const getParsedCommandLine = file => parsed.get(canonical(file));
    for (const project of projects) project.compilation = compileProject(project, { ...workspace, getParsedCommandLine });
}

function compileProject(project, workspace) {
    if (project.blockers.length || !project.sourceFiles.length) return;
    const { host, authored, outputsFor, getParsedCommandLine } = workspace;
    const { resolutionHost } = outputsFor(project);
    const options = project.options;
    const compilerHost = Object.assign(ts.createCompilerHost(options, true), host, {
        trace: undefined,
        writeFile: () => {},
        useCaseSensitiveFileNames: () => host.useCaseSensitiveFileNames,
        getParsedCommandLine
    });
    const rootNames = [...project.sourceFiles];
    const visited = new Set();
    let program;
    while (true) {
        program = ts.createProgram({ rootNames: [...rootNames], options, host: compilerHost,
            projectReferences: project.parsed.projectReferences, oldProgram: program });
        const sources = authoredSources(program, host, authored);
        const checker = program.getTypeChecker();
        const supplemental = options.noResolve ? [] :
            requireRoots(sources, visited, { checker, project, host, resolutionHost, authored });
        if (!supplemental.length) return { sources, checker, hasAmbientModule: ambientModuleMatcher(checker) };
        rootNames.push(...supplemental);
    }
}

function authoredSources(program, host, authored) {
    const sources = new Map();
    for (const source of program.getSourceFiles()) {
        const key = canonical(host.realpath(source.fileName));
        if (!authored.has(key)) continue;
        if (!sources.has(key) || canonical(source.fileName) === key) sources.set(key, source);
    }
    return sources;
}

function requireRoots(sources, visited, context) {
    const supplemental = new Set();
    for (const [key, source] of sources) {
        if (visited.has(key)) continue;
        visited.add(key);
        for (const target of requireTargets(source, context))
            if (!sources.has(target) && context.authored.has(target)) supplemental.add(target);
    }
    return [...supplemental].map(key => path.resolve(context.host.root, context.authored.get(key)));
}

function requireTargets(source, { checker, project, host, resolutionHost }) {
    // The compiler does not follow bare CommonJS require calls in TypeScript source.
    return imports(source, project.options, checker)
        .filter(request => request.mode === ts.ModuleKind.CommonJS)
        .map(request => resolveModule(request, source, project, resolutionHost))
        .filter(Boolean)
        .map(target => canonical(host.realpath(target)));
}

function ambientModuleMatcher(checker) {
    const names = new Set();
    const patterns = [];
    for (const symbol of checker.getAmbientModules()) {
        const name = ts.tryParsePattern(symbol.getName().slice(1, -1));
        if (!name) continue;
        if (typeof name !== 'string') patterns.push(name);
        else if (!ts.isExternalModuleNameRelative(name)) names.add(name);
    }
    return specifier => names.has(specifier) || patterns.some(pattern => ts.isPatternMatch(pattern, specifier));
}
