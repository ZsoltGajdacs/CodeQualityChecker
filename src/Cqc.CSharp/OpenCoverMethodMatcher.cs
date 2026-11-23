using Cqc.Core;

namespace Cqc.CSharp;

internal sealed class OpenCoverMethodMatcher(IEnumerable<MethodFact> methods, string? sourceRoot)
{
    private readonly ILookup<string, MethodFact> byFile = methods.ToLookup(method => method.Location.File, StringComparer.Ordinal);
    private readonly string? realRoot = sourceRoot is null ? null : CSharpPaths.ResolveDirectory(new DirectoryInfo(sourceRoot));

    internal MethodFact? Find(OpenCoverMethodIdentity identity, int[] lines, bool allowSourceFallback = true)
    {
        var candidates = Candidates(identity.File).Where(identity.Owns)
            .Where(method => lines.Length == 0 || lines.Any(line => Contains(method, line))).ToArray();
        var matching = candidates.Where(identity.Matches).Take(2).ToArray();
        if (matching.Length > 0)
            return matching.Length == 1 && lines.All(line => Contains(matching[0], line)) ? matching[0] : null;
        return allowSourceFallback && identity.File is not null ? SourceMatch(candidates, lines) : null;
    }

    private IEnumerable<MethodFact> Candidates(string? file)
    {
        if (sourceRoot is not null && realRoot is not null)
        {
            if (file is null || !Path.IsPathFullyQualified(file)) return [];
            var relative = Relative(sourceRoot, file);
            return byFile.Contains(relative) ? byFile[relative] : byFile[Relative(realRoot, file)];
        }
        if (file is null) return byFile.SelectMany(group => group);
        var path = file.Replace('\\', '/');
        return byFile.Where(group => path == group.Key || path.EndsWith("/" + group.Key, StringComparison.Ordinal))
            .SelectMany(group => group);
    }

    // PDB source points identify compiler-transformed methods. Equal spans are ambiguous.
    private static MethodFact? SourceMatch(IEnumerable<MethodFact> methods, int[] lines)
    {
        if (lines.Length == 0) return null;
        var candidates = methods.Where(method => lines.All(line => Contains(method, line)))
            .OrderBy(Span).Take(2).ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            2 when Span(candidates[0]) < Span(candidates[1]) => candidates[0],
            _ => null
        };
    }

    private static bool Contains(MethodFact method, int line) => method.Location.StartLine <= line && line <= method.Location.EndLine;
    private static int Span(MethodFact method) => method.Location.EndLine - method.Location.StartLine;
    private static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');
}
