import path from 'node:path';
import ts from 'typescript';
import { canonical } from './projects.mjs';

function addOutput(outputs, output, file) {
    const key = canonical(output);
    outputs.set(key, outputs.has(key) ? null : file);
}

function hasDeclarationOutputs(project) {
    const options = project.options;
    if (!options || project.blockers.length) return false;
    return !options.noEmit && !options.outFile;
}

export function referenceOutputs(projects, authoredFiles, host) {
    const byFile = new Map(projects.map(project => [canonical(project.file), project]));
    const cache = new Map();

    return function forProject(project) {
        if (!cache.has(project.id)) cache.set(project.id, build(project));
        return cache.get(project.id);
    };

    function build(project) {
        const outputs = new Map();
        const visited = new Set([project.id]);
        const pending = [...project.references];
        while (pending.length) {
            const referenced = byFile.get(canonical(pending.pop()));
            if (!referenced || visited.has(referenced.id)) continue;
            visited.add(referenced.id);
            pending.push(...referenced.references);
            collect(referenced, outputs);
        }
        const directories = outputDirectories(outputs);
        return {
            outputs,
            resolutionHost: {
                ...host,
                fileExists: file => outputs.get(canonical(file)) != null || host.fileExists(file),
                directoryExists: directory => directories.has(canonical(directory)) || host.directoryExists(directory)
            }
        };
    }

    function collect(project, outputs) {
        if (!hasDeclarationOutputs(project)) return;
        const config = { options: project.options, fileNames: project.sourceFiles, errors: [] };
        for (const input of project.sourceFiles) {
            const file = authoredFiles.get(canonical(input));
            if (!file) continue;
            for (const output of ts.getOutputFileNames(config, input, !host.useCaseSensitiveFileNames))
                if (/\.d\.[cm]?ts$/i.test(output)) addOutput(outputs, output, file);
        }
    }
}

function outputDirectories(outputs) {
    const directories = new Set();
    for (const output of outputs.keys()) {
        let directory = path.dirname(output);
        while (!directories.has(directory)) {
            directories.add(directory);
            const parent = path.dirname(directory);
            if (directory === parent) break;
            directory = parent;
        }
    }
    return directories;
}
