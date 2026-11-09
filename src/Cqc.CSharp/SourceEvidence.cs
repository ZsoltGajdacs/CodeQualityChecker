using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

/// <summary>The current C# adapter has one compilation context; it does not evaluate MSBuild.</summary>
internal static class SourceEvidence
{
    internal const string ContextId = "csharp:compilation";

    internal static EvidenceState Type(BaseTypeDeclarationSyntax declaration, SourceAnalysisContext context)
    {
        var syntax = context.Options.Complete && !Unmodeled(declaration)
            && !declaration.GetDiagnostics().Any(x => x.Severity == DiagnosticSeverity.Error);
        var semantic = !context.Model.GetDiagnostics(declaration.Span, context.Token).Any(x => x.Severity == DiagnosticSeverity.Error);
        return new(syntax, true, semantic, semantic, context.VerifiedContext, []);
    }

    internal static SyntaxGap[] Gaps(ParsedSources sources, CancellationToken token)
    {
        return sources.Trees.SelectMany((tree, index) => Gaps(tree, sources.Consistent && sources.Options[index].Complete, token))
            .Distinct().ToArray();
    }

    private static IEnumerable<SyntaxGap> Gaps(SyntaxTree tree, bool configuration, CancellationToken token)
    {
        if (!configuration) yield return Gap(tree.FilePath, "csharp.compile_configuration");
        if (tree.GetDiagnostics(token).Any(x => x.Severity == DiagnosticSeverity.Error))
            yield return Gap(tree.FilePath, "csharp.parse");
        var root = tree.GetRoot(token);
        if (root.DescendantNodes().OfType<GlobalStatementSyntax>().Any())
            yield return Gap(tree.FilePath, "csharp.unmodeled_executable");
        if (root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Any(Unmodeled))
            yield return Gap(tree.FilePath, "csharp.unmodeled_executable", false);
    }

    private static SyntaxGap Gap(string file, string code, bool unknownEntities = true) => new(file, "csharp", ContextId, code) { UnknownEntities = unknownEntities };

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
