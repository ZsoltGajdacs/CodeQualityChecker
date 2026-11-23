using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

/// <summary>Tracks syntax separately from each project's semantic context.</summary>
internal static class SourceEvidence
{
    internal static EvidenceState Type(BaseTypeDeclarationSyntax declaration, SourceAnalysisContext context)
    {
        var syntax = context.Options.Complete && !Unmodeled(declaration)
            && !declaration.GetDiagnostics().Any(x => x.Severity == DiagnosticSeverity.Error);
        var semantic = !context.SourceOnly && !context.Model.GetDiagnostics(declaration.Span, context.Token).Any(x => x.Severity == DiagnosticSeverity.Error);
        return new(syntax, true, semantic, semantic, context.VerifiedContext, []);
    }

    internal static IEnumerable<SyntaxGap> Gaps(ParsedProject project, CancellationToken token)
    {
        return project.Trees.SelectMany(tree => Gaps(tree, project, token));
    }

    private static IEnumerable<SyntaxGap> Gaps(SyntaxTree tree, ParsedProject project, CancellationToken token)
    {
        if (!project.Options.Complete) yield return Gap(tree.FilePath, "csharp.compile_configuration", project.ContextId);
        if (tree.GetDiagnostics(token).Any(x => x.Severity == DiagnosticSeverity.Error))
            yield return Gap(tree.FilePath, "csharp.parse", project.ContextId);
        var root = tree.GetRoot(token);
        if (root.DescendantNodes().OfType<GlobalStatementSyntax>().Any())
            yield return Gap(tree.FilePath, "csharp.unmodeled_executable", project.ContextId);
        if (root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Any(Unmodeled))
            yield return Gap(tree.FilePath, "csharp.unmodeled_executable", project.ContextId, false);
    }

    private static SyntaxGap Gap(string file, string code, string context, bool unknownEntities = true) =>
        new(file, "csharp", context, code) { UnknownEntities = unknownEntities };

    private static bool Unmodeled(BaseTypeDeclarationSyntax declaration)
    {
        if (declaration is TypeDeclarationSyntax { ParameterList: not null }) return true;
        return declaration.ChildNodes().Any(member => member switch
        {
            FieldDeclarationSyntax field => !field.Modifiers.Any(SyntaxKind.ConstKeyword)
                && field.Declaration.Variables.Any(variable => variable.Initializer is not null),
            EventFieldDeclarationSyntax field => field.Declaration.Variables.Any(variable => variable.Initializer is not null),
            PropertyDeclarationSyntax { Initializer: not null } => true,
            _ => false
        });
    }
}
