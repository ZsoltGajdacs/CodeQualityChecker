import ts from 'typescript';
import { hasBody, isTypeOnly } from './syntax.mjs';

const kind = ts.SyntaxKind;
const decisions = new Set([
    kind.IfStatement, kind.ForStatement, kind.ForInStatement, kind.ForOfStatement,
    kind.WhileStatement, kind.DoStatement, kind.CaseClause, kind.CatchClause, kind.ConditionalExpression
]);
const logicalOperators = new Set([
    kind.AmpersandAmpersandToken, kind.BarBarToken, kind.QuestionQuestionToken,
    kind.AmpersandAmpersandEqualsToken, kind.BarBarEqualsToken, kind.QuestionQuestionEqualsToken
]);
const nonCodeTokens = new Set([kind.OpenBraceToken, kind.CloseBraceToken, kind.SemicolonToken, kind.EndOfFileToken]);

function beginsNestedScope(node) {
    return hasBody(node) || ts.isClassLike(node) || ts.isClassStaticBlockDeclaration(node);
}

function visitBody(node, visit, children) {
    if (beginsNestedScope(node) || isTypeOnly(node)) return;
    visit(node);
    children(node, child => visitBody(child, visit, children));
}

function addsDecision(node) {
    return decisions.has(node.kind) || node.questionDotToken !== undefined
        || (ts.isBinaryExpression(node) && logicalOperators.has(node.operatorToken.kind));
}

function isCallName(node) {
    const parent = node.parent;
    if (ts.isPropertyAccessExpression(parent) && parent.name === node)
        return isCalled(parent);
    return isCalled(node);
}

function isCalled(node) {
    const parent = node.parent;
    return (ts.isCallExpression(parent) || ts.isNewExpression(parent)) && parent.expression === node;
}

function fingerprint(node) {
    return ts.isIdentifier(node) && isCallName(node) ? `call:${node.text}` : String(node.kind);
}

function codeLines(roots, source) {
    const lines = new Set();
    function countToken(node) {
        if (nonCodeTokens.has(node.kind) || node.getChildCount(source) !== 0) return;
        const start = node.getStart(source);
        const end = node.getEnd();
        if (end <= start) return;
        const first = source.getLineAndCharacterOfPosition(start).line;
        const last = source.getLineAndCharacterOfPosition(end - 1).line;
        const tokenLines = source.text.slice(start, end).split(/\r\n|[\r\n\u2028\u2029]/u);
        for (let line = first; line <= last; line++)
            if (tokenLines[line - first].trim()) lines.add(line);
    }
    const children = (node, visit) => node.getChildren(source).forEach(visit);
    roots.forEach(root => visitBody(root, countToken, children));
    return lines.size;
}

export function measure(roots, source) {
    let complexity = 1;
    let nodeCount = 0;
    const fingerprints = new Set();
    const prior = [];
    function count(node) {
        const token = fingerprint(node);
        if (addsDecision(node)) complexity++;
        nodeCount++;
        prior.push(token);
        if (prior.length === 3) {
            fingerprints.add(prior.join('/'));
            prior.shift();
        }
    }
    roots.forEach(root => visitBody(root, count, ts.forEachChild));
    return { complexity, nodeCount, fingerprints: [...fingerprints].sort(), duplicationLines: codeLines(roots, source) };
}
