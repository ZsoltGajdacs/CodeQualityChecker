namespace Cqc.Core;

internal static class DiagnosticScope
{
    internal static Diagnostic[] Classify(ProjectSnapshot snapshot, IEnumerable<Diagnostic> diagnostics)
    {
        var files = snapshot.Files.ToHashSet(StringComparer.Ordinal);
        var selected = snapshot.SelectedFiles.ToHashSet(StringComparer.Ordinal);
        return diagnostics.Select(diagnostic => diagnostic with { Scope = Scope(diagnostic.File, files, selected) })
            .OrderBy(diagnostic => diagnostic.Scope != "selected")
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.File, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location?.StartLine)
            .ToArray();
    }

    private static string Scope(string? file, HashSet<string> files, HashSet<string> selected)
    {
        if (file is null) return "project";
        if (selected.Contains(file)) return "selected";
        return files.Contains(file) ? "context" : "project";
    }
}
