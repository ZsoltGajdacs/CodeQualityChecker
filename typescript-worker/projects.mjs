import path from 'node:path';
import ts from 'typescript';
import { language } from './syntax.mjs';

export const relative = (root, file) => path.relative(root, file).split(path.sep).join('/');
export const inside = (root, file) => {
    const name = path.relative(root, file);
    return name !== '..' && !name.startsWith('..' + path.sep) && !path.isAbsolute(name);
};

function configId(root, file) {
    return language + ':' + relative(root, file);
}

const fileCase = ts.sys.useCaseSensitiveFileNames ? file => file : file => file.toLowerCase();
export const canonical = file => fileCase(path.resolve(file));

function configDiagnostic(root, file, error) {
    const message = ts.flattenDiagnosticMessageText(error.messageText, '\n').replaceAll(root + path.sep, '');
    return { code: 'typescript.configuration', message: `TS${error.code}: ${message}`, file: relative(root, file), language };
}

function configErrors(parsed, errors) {
    // A solution config can legitimately own zero sources and only name references.
    const harmless = new Set([18002, 18003]);
    const found = [...errors, ...parsed?.errors ?? [], ...optionErrors(parsed?.options)];
    return found.filter(error =>
        error.category === ts.DiagnosticCategory.Error && !(harmless.has(error.code) && parsed?.projectReferences?.length));
}

function optionErrors(options) {
    if (!options) return [];
    // Validate option combinations without adding program roots or loading the project's types.
    const flags = { ...options, noLib: true, types: [], lib: options.noLib ? options.lib : undefined };
    return ts.createProgram({ rootNames: [], options: flags }).getOptionsDiagnostics();
}

function project(root, file, host) {
    const errors = [];
    const parsed = ts.getParsedCommandLineOfConfigFile(file, {}, {
        ...host, onUnRecoverableConfigFileDiagnostic: error => errors.push(error)
    });
    const failures = configErrors(parsed, errors);
    const { options, fileNames = [], projectReferences = [], raw } = parsed ?? {};
    const modulePath = relative(root, file);
    return {
        id: configId(root, file),
        file,
        modulePath,
        options,
        sourceFiles: fileNames,
        parsed,
        references: projectReferences.map(ts.resolveProjectReferencePath),
        inheritedConfigs: options?.configFile?.extendedSourceFiles ?? [],
        explicitProject: isExplicitProject(file, raw),
        blockers: parsed && failures.length === 0 ? [] : ['typescript.configuration'],
        diagnostics: failures.map(error => configDiagnostic(root, file, error)),
        cache: ts.createModuleResolutionCache(path.dirname(file), fileCase, options),
        reachable: new Set()
    };
}

function isExplicitProject(file, raw) {
    if (path.basename(file) === 'tsconfig.json') return true;
    return raw?.files !== undefined || raw?.include !== undefined;
}

export function loadProjects(root, configFiles, host, diagnostics) {
    const projects = new Map();
    const pending = configFiles.map(file => path.resolve(root, file)).reverse();
    while (pending.length) {
        const file = pending.pop();
        if (projects.has(canonical(file)) || !inside(root, file)) continue;
        const current = project(root, file, host);
        projects.set(canonical(file), current);
        pending.push(...current.references);
    }
    const selected = excludeInheritedConfigs([...projects.values()]);
    for (const current of selected) {
        diagnostics.push(...current.diagnostics);
        connectReferences(current, projects, diagnostics);
    }
    flagReferenceCycles(selected, diagnostics);
    return selected;
}

function excludeInheritedConfigs(projects) {
    const inherited = new Set(projects.flatMap(project => project.inheritedConfigs).map(canonical));
    const referenced = new Set(projects.flatMap(project => project.references).map(canonical));
    return projects.filter(project => {
        const key = canonical(project.file);
        return project.explicitProject || !inherited.has(key) || referenced.has(key);
    });
}

function flagReferenceCycles(projects, diagnostics) {
    const remaining = countPendingReferences(projects);
    const code = 'typescript.project_reference_cycle';
    for (const project of projects) {
        if (remaining.get(project.id) === 0) continue;
        project.blockers.push(code);
        diagnostics.push({ code, language, file: project.modulePath,
            message: 'This project contains or reaches a circular project reference.' });
    }
}

function indexDependents(projects) {
    const dependents = new Map(projects.map(project => [project.id, []]));
    for (const project of projects)
        for (const reference of project.reachable) dependents.get(reference).push(project.id);
    return dependents;
}

function countPendingReferences(projects) {
    const dependents = indexDependents(projects);
    const remaining = new Map(projects.map(project => [project.id, project.reachable.size]));
    const ready = projects.filter(project => project.reachable.size === 0).map(project => project.id);
    for (let index = 0; index < ready.length; index++) {
        const id = ready[index];
        for (const dependent of dependents.get(id)) {
            const count = remaining.get(dependent) - 1;
            remaining.set(dependent, count);
            if (count === 0) ready.push(dependent);
        }
    }
    return remaining;
}

function connectReferences(current, projects, diagnostics) {
    for (const file of current.references) {
        const referenced = projects.get(canonical(file));
        if (referenced) {
            current.reachable.add(referenced.id);
            continue;
        }
        current.blockers.push('typescript.project_reference');
        diagnostics.push({ code: 'typescript.project_reference', language, file: current.modulePath,
            message: 'Project reference is outside the analysis root.' });
    }
}

function ownersFor(projects, keysFor, authoredFiles) {
    const owners = new Map();
    for (const current of projects) {
        for (const key of keysFor(current)) {
            if (!authoredFiles.has(key)) continue;
            if (!owners.has(key)) owners.set(key, []);
            owners.get(key).push(current);
        }
    }
    return owners;
}

function nearestProjects(file, candidates) {
    if (candidates.length <= 1) return candidates;
    const distance = current => {
        const directory = path.dirname(current.file);
        return inside(directory, file) ? directory.length : -1;
    };
    const nearest = Math.max(...candidates.map(distance));
    return candidates.filter(current => distance(current) === nearest);
}

export function projectOwner(projects, authored) {
    const configured = ownersFor(projects, project => project.sourceFiles.map(canonical), authored);
    const imported = ownersFor(projects, project => project.compilation?.sources.keys() ?? [], authored);
    return file => {
        const key = canonical(file);
        const candidates = nearestProjects(file, configured.get(key) ?? imported.get(key) ?? []);
        return candidates.length === 1 ? { project: candidates[0] } :
            { blocker: candidates.length === 0 ? 'typescript.configuration_missing' : 'typescript.configuration_conflict' };
    };
}

export function toContext(project, stable) {
    const blockers = stable ? project.blockers : [...project.blockers, 'typescript.resolution_changed'];
    return {
        id: project.id, modulePath: project.modulePath, sourceSet: 'compilation',
        reachableContexts: [...project.reachable].sort(), verified: blockers.length === 0,
        blockers: [...new Set(blockers)].sort()
    };
}
