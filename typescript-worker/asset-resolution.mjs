import path from 'node:path';
import ts from 'typescript';
import { resolveUsingHost, usesBundlerResolution } from './module-resolution.mjs';

const assetPath = /^[^?#]+\.(css|png|svg)$/i;
const urlImport = /^([^?#]+)\?url$/;
const assetDeclaration = /\.d\.(css|png|svg)\.ts$/i;

export function assetSpecifier({ specifier, typeOnly }, options) {
    if (!specifier) return;
    if (typeOnly || !usesBundlerResolution(options)) return;
    return assetPath.test(specifier) ? specifier : urlImport.exec(specifier)?.[1];
}

export function verifiesAsset(specifier, request, { source, project, resolutionHost }, filesystem) {
    if (ts.isExternalModuleNameRelative(specifier))
        return filesystem.fileExists(path.resolve(path.dirname(source.fileName), specifier));

    const context = { source, options: project.options, host: resolutionHost };
    const asset = { ...request, specifier };
    if (resolvesToFile(asset, context, filesystem)) return true;
    if (request.specifier.endsWith('?url') && verifiesUrl(asset, context, filesystem)) return true;
    return verifiesMappedAsset(asset, context, filesystem);
}

function resolvesToFile(request, context, filesystem) {
    const target = resolveUsingHost(request, context)?.resolvedFileName;
    return target !== undefined && assetFileExists(target, filesystem);
}

function assetFileExists(file, filesystem) {
    if (assetDeclaration.test(file)) return filesystem.fileExists(file.replace(assetDeclaration, '.$1'));
    return !ts.isDeclarationFileName(file) && filesystem.fileExists(file);
}

function verifiesUrl(request, context, filesystem) {
    // A package exports entry can expose a declaration to TypeScript and a different file to the bundler.
    const options = { ...context.options, noDtsResolution: true };
    return resolvesToFile(request, { ...context, options }, filesystem);
}

function verifiesMappedAsset(request, context, filesystem) {
    if (!assetPath.test(request.specifier)) return false;
    // Supply the compiler's arbitrary-extension declaration candidate only for an existing asset.
    const host = {
        ...context.host,
        fileExists: file => assetDeclaration.test(file) ? assetFileExists(file, filesystem) : context.host.fileExists(file)
    };
    const options = { ...context.options, allowArbitraryExtensions: true };
    const match = resolveUsingHost(request, { ...context, host, options })?.resolvedFileName;
    return !!match && assetDeclaration.test(match) && assetFileExists(match, filesystem);
}
