namespace Cqc.Core;

public sealed record LanguageBuildResult(string Language, string[] Reports, SortedDictionary<string, string> ToolVersions)
{
    public SortedDictionary<string, string> ReportModules { get; init; } = new(StringComparer.Ordinal);
    public Diagnostic[] Diagnostics { get; init; } = [];
}

/// <summary>Optional build/report integration supplied by each language module.</summary>
public interface ILanguageExecution
{
    string Language { get; }
    Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string scratchDirectory, CancellationToken cancellationToken);
    MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods);
}
