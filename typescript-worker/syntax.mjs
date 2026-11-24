import ts from 'typescript';

export const language = 'typescript';

export function location(file, source, start, end = start + 1) {
    const line = offset => source.getLineAndCharacterOfPosition(Math.min(offset, source.end)).line + 1;
    return { file, startLine: line(start), endLine: line(Math.max(start, end - 1)) };
}

export function nodeLocation(file, source, node) {
    return location(file, source, node.getStart(source), node.getEnd());
}

export const hasBody = node => ts.isFunctionLike(node) && node.body !== undefined;
export const hasInitializer = node => (ts.isPropertyDeclaration(node) || ts.isParameter(node))
    && node.initializer !== undefined;
export const isIndependentInitializer = node => hasInitializer(node) && !hasBody(node.initializer);

export function isClassHeritage(node) {
    if (!ts.isExpressionWithTypeArguments(node)) return false;
    return node.parent.token === ts.SyntaxKind.ExtendsKeyword && ts.isClassLike(node.parent.parent);
}

function isTypeExpression(node) {
    if (!ts.isExpressionWithTypeArguments(node)) return ts.isTypeNode(node);
    return ts.isHeritageClause(node.parent) && !isClassHeritage(node);
}

export function isTypeOnly(node) {
    return isTypeExpression(node) || ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node)
        || ts.isJSDoc(node) || (ts.canHaveModifiers(node)
            && ts.getModifiers(node)?.some(modifier => modifier.kind === ts.SyntaxKind.DeclareKeyword));
}
