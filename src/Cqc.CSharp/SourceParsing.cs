using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

internal sealed record ParsedSources(SyntaxTree[] Trees, SourceFileOptions[] Options, bool Consistent, SyntaxTree? Imports, Diagnostic[] Diagnostics);

internal static class SourceParsing
{
    public static async Task<ParsedSources> ReadAsync(ProjectSnapshot snapshot, CancellationToken token)
    {
        var options = new SourceOptions(snapshot);
        var files = snapshot.Files.Where(x => x.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToArray();
        var settings = files.Select(options.ForFile).ToArray();
        var diagnostics = settings.SelectMany(x => x.Diagnostics).Distinct().ToList();
        var consistent = Consistent(settings, diagnostics);
        var trees = new List<SyntaxTree>();
        for (var index = 0; index < files.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(Path.Combine(snapshot.Root, files[index]), token);
            var parse = consistent ? settings[index].ParseOptions : settings[index].ParseOptions.WithLanguageVersion(LanguageVersion.Latest);
            trees.Add(CSharpSyntaxTree.ParseText(text, parse, files[index], cancellationToken: token));
        }
        var version = trees.FirstOrDefault()?.Options as CSharpParseOptions ?? CSharpParseOptions.Default;
        var imports = settings.Any(x => x.ImplicitUsings) ? ImplicitImports.Create(version) : null;
        return new(trees.ToArray(), settings, consistent, imports, diagnostics.ToArray());
    }

    private static bool Consistent(SourceFileOptions[] settings, List<Diagnostic> diagnostics)
    {
        var sameImports = settings.Select(x => x.ImplicitUsings).Distinct().Count() <= 1;
        var sameVersion = settings.Select(x => x.ParseOptions.LanguageVersion).Distinct().Count() <= 1;
        if (sameImports && sameVersion) return true;
        diagnostics.Add(new("csharp.compile_configuration", "Projects have differing language versions or implicit imports. Analyze module roots separately for verified scores.", null, "csharp"));
        return false;
    }
}
