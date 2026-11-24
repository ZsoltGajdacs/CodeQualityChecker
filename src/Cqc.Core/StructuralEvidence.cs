namespace Cqc.Core;

/// <summary>Request-local structural gaps and their relevance to selected files.</summary>
internal sealed class StructuralEvidence
{
    private readonly HashSet<string> failedLanguages;
    private readonly HashSet<string> unknownFiles;
    private readonly HashSet<string> blockedTypes;

    internal StructuralEvidence(ProjectSnapshot snapshot, AnalysisFacts facts)
    {
        failedLanguages = facts.Diagnostics.Where(IsAnalyzerFailure).Select(d => d.Language).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        unknownFiles = facts.SyntaxGaps.Where(g => g.UnknownEntities).Select(g => g.File).ToHashSet(StringComparer.Ordinal);
        blockedTypes = facts.SyntaxGaps.Select(g => g.TypeId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        IncompleteLanguages = facts.SyntaxGaps.Select(g => g.Language).Concat(failedLanguages)
            .Concat(facts.Methods.Where(m => !AssessmentEvidence.Of(m).Syntax).Select(m => m.Language))
            .Concat(facts.Types.Where(t => !AssessmentEvidence.Of(t).Syntax).Select(t => t.Language))
            .ToHashSet(StringComparer.Ordinal);
        HasIncompleteSelectedLanguage = snapshot.SelectedFiles.Any(file => IncompleteLanguages.Contains(Language(file)));
        UnavailableSelectedFiles = snapshot.SelectedFiles.Where(file => unknownFiles.Contains(file)
            || failedLanguages.Contains(Language(file))).ToHashSet(StringComparer.Ordinal);
    }

    internal HashSet<string> IncompleteLanguages { get; }
    internal bool HasIncompleteSelectedLanguage { get; }
    internal HashSet<string> UnavailableSelectedFiles { get; }

    internal string[] Blockers(TypeFact type, IEnumerable<StructuralExecutable> executables) => AssessmentEvidence.Reasons(
        (!unknownFiles.Contains(type.Location.File) && !blockedTypes.Contains(type.Id) && AssessmentEvidence.Of(type).Syntax
            && executables.All(executable => executable.Syntax), "syntax.incomplete"));

    internal static string Language(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".java" => "java",
        ".cs" => "csharp",
        _ => ""
    };

    internal static bool IsAnalyzerFailure(Diagnostic diagnostic) => diagnostic.Code is
        "analyzer_failed" or "java.worker_missing" or "java.worker_failed" or "java.worker_protocol" or "java.worker_empty";
}
