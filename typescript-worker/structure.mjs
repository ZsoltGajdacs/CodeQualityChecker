import ts from 'typescript';
import { measure } from './measurement.mjs';
import { hasBody, isClassHeritage, isIndependentInitializer, isTypeOnly, language, location, nodeLocation } from './syntax.mjs';

const namedParents = new Set([
    ts.SyntaxKind.VariableDeclaration, ts.SyntaxKind.PropertyAssignment, ts.SyntaxKind.PropertyDeclaration
]);
const excludedStatements = new Set([
    ts.SyntaxKind.FunctionDeclaration, ts.SyntaxKind.ClassDeclaration, ts.SyntaxKind.ImportDeclaration,
    ts.SyntaxKind.ImportEqualsDeclaration, ts.SyntaxKind.ExportDeclaration, ts.SyntaxKind.EmptyStatement
]);

function moduleStatement(node) {
    return !isTypeOnly(node) && !excludedStatements.has(node.kind);
}

function label(name) {
    if (name?.text === undefined) return;
    return encodeURIComponent(name.text.toWellFormed()).replaceAll('!', '%21').replaceAll('~', '%7E');
}

function callableLabel(node) {
    if (ts.isConstructorDeclaration(node)) return 'constructor';
    if (ts.isGetAccessorDeclaration(node)) return 'get-' + (label(node.name) ?? 'computed');
    if (ts.isSetAccessorDeclaration(node)) return 'set-' + (label(node.name) ?? 'computed');
    if (node.name) return label(node.name) ?? 'computed';
    const parent = node.parent;
    if (namedParents.has(parent.kind)) return label(parent.name) ?? 'callback';
    return ts.isExportAssignment(parent) ? 'default' : 'callback';
}

function ownedBody(node) {
    if (hasBody(node)) return { name: callableLabel(node), kind: 'callable', span: node, body: node.body };
    if (ts.isClassStaticBlockDeclaration(node)) return { name: 'static-block', kind: 'staticBlock', span: node, body: node.body };
    if (isIndependentInitializer(node)) return initializerBody(node);
    return declarationExpression(node);
}

function declarationExpression(node) {
    if (ts.isDecorator(node)) return expressionBody('decorator', node.expression);
    if (isClassHeritage(node)) return expressionBody('heritage', node.expression);
    if (isIndependentName(node)) return expressionBody('computed-name', node.expression);
}

function isIndependentName(node) {
    if (!ts.isComputedPropertyName(node)) return false;
    return hasBody(node.parent) || ts.isClassLike(node.parent.parent);
}

function initializerBody(node) {
    const name = label(node.name) ?? 'computed';
    const prefix = ts.isParameter(node) ? 'parameter-' : 'field-';
    return expressionBody(prefix + name, node.initializer);
}

function expressionBody(name, expression) {
    return { name, kind: 'initializer', span: expression, body: expression };
}

export function executables(file, source) {
    const bodies = [];
    const labels = new Map();
    function unique(owner, name) {
        const key = owner ? owner + '/' + name : name;
        const occurrence = (labels.get(key) ?? 0) + 1;
        labels.set(key, occurrence);
        return occurrence === 1 ? key : key + '~' + occurrence;
    }
    function add(id, kind, span, roots) {
        bodies.push({ id: file + '#' + id, typeId: file, kind, language, location: span, ...measure(roots, source) });
    }
    function walk(node, owner) {
        if (isTypeOnly(node)) return;
        if (ts.isClassLike(node) || ts.isModuleDeclaration(node)) {
            owner = unique(owner, label(node.name) ?? 'anonymous');
        }
        const body = ownedBody(node);
        if (body) {
            owner = unique(owner, body.name);
            add(owner, body.kind, nodeLocation(file, source, body.span), [body.body]);
        }
        ts.forEachChild(node, child => walk(child, owner));
    }
    const statements = source.statements.filter(moduleStatement);
    if (statements.length > 0) {
        const span = location(file, source, statements[0].getStart(source), statements.at(-1).getEnd());
        add('!module', 'moduleBody', span, statements);
    }
    walk(source, '');
    return bodies;
}
