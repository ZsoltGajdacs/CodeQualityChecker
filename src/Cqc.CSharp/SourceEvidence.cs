using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

/// <summary>Tracks syntax separately from each project's semantic context.</summary>
internal static class SourceEvidence
{
    private sealed record UnmodeledExecutable(string Construct, SyntaxNode Node);

    internal static EvidenceState Type(BaseTypeDeclarationSyntax declaration, SourceAnalysisContext context)
    {
        var syntax = context.Options.Complete && !Unmodeled(declaration).Any()
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
        foreach (var diagnostic in project.Options.Diagnostics)
            yield return new(tree.FilePath, diagnostic.Language ?? "csharp", project.ContextId, diagnostic.Code) { Message = diagnostic.Message };
        foreach (var diagnostic in tree.GetDiagnostics(token).Where(item => item.Severity == DiagnosticSeverity.Error))
            yield return new(tree.FilePath, "csharp", project.ContextId, "csharp.parse")
            { Message = $"{diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}", Location = SourceLocation.Of(diagnostic.Location) };
        var root = tree.GetRoot(token);
        foreach (var statement in root.ChildNodes().OfType<GlobalStatementSyntax>())
            yield return Gap(project.ContextId, new("top-level statement", statement), unknownEntities: true);
        foreach (var executable in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().SelectMany(Unmodeled))
            yield return Gap(project.ContextId, executable, unknownEntities: false);
    }

    private static SyntaxGap Gap(string context, UnmodeledExecutable executable, bool unknownEntities)
    {
        var location = SourceLocation.Of(executable.Node);
        return new(location.File, "csharp", context, "csharp.unmodeled_executable")
        {
            UnknownEntities = unknownEntities, Location = location, Construct = executable.Construct,
            Message = $"CQC cannot measure the {executable.Construct}."
        };
    }

    private static IEnumerable<UnmodeledExecutable> Unmodeled(BaseTypeDeclarationSyntax declaration)
    {
        if (declaration is TypeDeclarationSyntax { ParameterList: { } parameters })
            yield return new("primary constructor", parameters);
        foreach (var initializer in declaration.ChildNodes().SelectMany(Initializers))
            yield return initializer;
    }

    private static IEnumerable<UnmodeledExecutable> Initializers(SyntaxNode member) => member switch
    {
        FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.ConstKeyword) =>
            Initializers(field.Declaration, "field initializer"),
        EventFieldDeclarationSyntax field => Initializers(field.Declaration, "event initializer"),
        PropertyDeclarationSyntax { Initializer: { } initializer } => [new("property initializer", initializer)],
        _ => []
    };

    private static IEnumerable<UnmodeledExecutable> Initializers(VariableDeclarationSyntax declaration, string construct) =>
        declaration.Variables.Select(variable => variable.Initializer).OfType<EqualsValueClauseSyntax>()
            .Select(initializer => new UnmodeledExecutable(construct, initializer));
}
