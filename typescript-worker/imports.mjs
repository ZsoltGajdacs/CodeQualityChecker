import ts from 'typescript';

const jsxKinds = new Set([ts.SyntaxKind.JsxElement, ts.SyntaxKind.JsxSelfClosingElement, ts.SyntaxKind.JsxFragment]);

const syntaxImports = new Map([
    [ts.SyntaxKind.ImportDeclaration, [node => node.moduleSpecifier, importTypesOnly]],
    [ts.SyntaxKind.ExportDeclaration, [node => node.moduleSpecifier, node => node.isTypeOnly || namedTypesOnly(node.exportClause)]],
    [ts.SyntaxKind.ExternalModuleReference, [node => node.expression, node => node.parent.isTypeOnly]],
    [ts.SyntaxKind.ImportType, [node => ts.isLiteralTypeNode(node.argument) ? node.argument.literal : undefined, () => true]]
]);

function implicitImport(node, source, options) {
    if (!node) return;
    // The pinned compiler observes per-file JSX runtime and import-source directives.
    const specifier = ts.getJSXRuntimeImport(ts.getJSXImplicitImportBase(options, source), options);
    if (specifier) return { node, specifier };
}

export function imports(source, options, checker) {
    const requests = [];
    let jsx;
    function visit(node) {
        const request = ts.isCallExpression(node) ? callRequest(node, checker) : syntaxRequest(node);
        if (request) requests.push(request);
        jsx ??= jsxKinds.has(node.kind) ? node : undefined;
        ts.forEachChild(node, visit);
    }
    visit(source);
    const runtime = implicitImport(jsx, source, options);
    if (runtime) requests.push(runtime);
    return requests;
}

function syntaxRequest(node) {
    const syntax = syntaxImports.get(node.kind);
    if (!syntax) return;
    const [operandOf, typesOnly] = syntax;
    const operand = operandOf(node);
    if (!operand) return;
    return { ...request(operand), typeOnly: typesOnly(node) };
}

function importTypesOnly({ importClause }) {
    if (!importClause) return false;
    return importClause.isTypeOnly || !importClause.name && namedTypesOnly(importClause.namedBindings);
}

function namedTypesOnly(bindings) {
    return !!bindings?.elements?.length && bindings.elements.every(element => element.isTypeOnly);
}

function callRequest(node, checker) {
    if (node.expression.kind === ts.SyntaxKind.ImportKeyword) return request(node.arguments[0] ?? node);
    if (!checker || !ts.isRequireCall(node, false)) return;
    if (isCommonJsRequire(node.expression, checker)) return request(node.arguments[0], ts.ModuleKind.CommonJS);
}

function request(node, mode) {
    return { node, specifier: ts.isStringLiteralLike(node) ? node.text : undefined, mode };
}

function isCommonJsRequire(identifier, checker) {
    const symbol = checker.getSymbolAtLocation(identifier);
    if (!symbol) return true;
    const declarations = symbol.declarations;
    const isAmbient = declarations?.length && declarations.every(node => (node.flags & ts.NodeFlags.Ambient) !== 0);
    return isAmbient && !checker.resolveName('require', identifier, ts.SymbolFlags.Value | ts.SymbolFlags.Alias, true);
}
