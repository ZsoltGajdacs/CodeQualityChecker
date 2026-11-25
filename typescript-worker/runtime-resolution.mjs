import path from 'node:path';
import ts from 'typescript';
import { resolveUsingHost, usesBundlerResolution } from './module-resolution.mjs';

const javascript = /\.(?:[cm]?js|jsx)$/i;
const invalidSegments = new Set(['', '.', '..']);

export function verifiesRuntimeImport(request, { source, project, resolutionHost }, filesystem) {
    if (request.typeOnly || !usesBundlerResolution(project.options)) return false;
    const packageName = externalSubpathPackage(request.specifier);
    if (!packageName) return false;

    const host = {
        ...resolutionHost,
        readFile: file => runtimeManifest(file, packageName, resolutionHost.readFile(file))
    };
    const resolved = resolveUsingHost(request, { source, options: project.options, host });
    return !!resolved?.isExternalLibraryImport && javascript.test(resolved.resolvedFileName)
        && filesystem.fileExists(resolved.resolvedFileName);
}

function externalSubpathPackage(specifier) {
    if (!specifier) return;
    if (ts.isExternalModuleNameRelative(specifier) || /[#?:\\]/.test(specifier)) return;
    if (specifier.split('/').some(segment => invalidSegments.has(segment))) return;
    const { packageName, rest } = ts.parsePackageName(specifier);
    return rest ? packageName : undefined;
}

function runtimeManifest(file, packageName, text) {
    const suffix = path.sep + path.join('node_modules', packageName, 'package.json');
    if (text === undefined || !path.normalize(file).endsWith(suffix)) return text;
    try {
        const manifest = JSON.parse(text);
        if (manifest?.typesVersions === undefined) return text;
        delete manifest.typesVersions;
        return JSON.stringify(manifest);
    } catch {
        return text;
    }
}
