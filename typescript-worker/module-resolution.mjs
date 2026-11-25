import ts from 'typescript';

export function usesBundlerResolution(options) {
    return ts.getEmitModuleResolutionKind(options) === ts.ModuleResolutionKind.Bundler;
}

export function resolveModule(request, source, project, host) {
    return resolveUsingHost(request, { source, options: project.options, host, cache: project.cache })?.resolvedFileName;
}

export function resolveUsingHost({ specifier, node, mode }, { source, options, host, cache }) {
    if (specifier === undefined) return;
    const resolutionMode = mode ?? (ts.isStringLiteralLike(node) ?
        ts.getModeForUsageLocation(source, node, options) : source.impliedNodeFormat);
    return ts.resolveModuleName(specifier, source.fileName, options, host, cache, undefined, resolutionMode).resolvedModule;
}
