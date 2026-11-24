using System.Text.Json;

namespace Cqc.Core;

internal sealed class LanguageFactCollection(ILanguageAnalyzer[] adapters, LanguageDefinition[] definitions)
{
    internal async Task<AnalysisFacts> CollectAsync(ProjectSnapshot snapshot, List<Diagnostic> diagnostics, CancellationToken token)
    {
        var languages = definitions.Where(definition => snapshot.Files.Any(definition.IsSource))
            .Select(definition => definition.Language).ToHashSet(StringComparer.Ordinal);
        var results = new List<AnalysisFacts>();
        foreach (var adapter in adapters.Where(adapter => languages.Contains(adapter.Language)))
            results.Add(await AnalyzeAsync(adapter, snapshot, diagnostics, token));
        return Merge(results);
    }

    private static async Task<AnalysisFacts> AnalyzeAsync(ILanguageAnalyzer adapter, ProjectSnapshot snapshot,
        List<Diagnostic> diagnostics, CancellationToken token)
    {
        try
        {
            var facts = await adapter.AnalyzeAsync(snapshot, token);
            if (!snapshot.SourceOnly && MissingSemanticFacts(facts))
                facts = facts with { Diagnostics = facts.Diagnostics.Append(new Diagnostic("analyzer_failed",
                    "No analyzable types were produced. Check language diagnostics and installed analyzer runtimes.",
                    Language: adapter.Language)).ToArray() };
            return LanguageIdentity.Qualify(facts, adapter.Language);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
            or System.ComponentModel.Win32Exception or JsonException)
        {
            var failure = new Diagnostic("analyzer_failed", exception.Message, Language: adapter.Language);
            if (!snapshot.SourceOnly) diagnostics.Add(failure);
            return new([], [], [], [failure], false);
        }
    }

    private static bool MissingSemanticFacts(AnalysisFacts facts) => !facts.SemanticComplete && facts.Types.Length == 0;

    private static AnalysisFacts Merge(List<AnalysisFacts> results) => new(
        results.SelectMany(result => result.Types).ToArray(),
        results.SelectMany(result => result.Methods).ToArray(),
        results.SelectMany(result => result.Dependencies).ToArray(),
        results.SelectMany(result => result.Diagnostics).ToArray(),
        results.All(result => result.SemanticComplete))
    {
        Contexts = results.SelectMany(result => result.Contexts).ToArray(),
        SyntaxGaps = results.SelectMany(result => result.SyntaxGaps).ToArray(),
        Executables = results.SelectMany(result => result.Executables).ToArray()
    };
}
