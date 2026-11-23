using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cqc.CSharp;

internal sealed record ParsedProject(string Path, SourceOptions Options, SyntaxTree[] Trees, SyntaxTree? Imports)
{
    public string ContextId => "csharp:" + Path;
}

internal static class SourceParsing
{
    public static async Task<ParsedProject[]> ReadAsync(ProjectSnapshot snapshot, CancellationToken token)
    {
        var modules = snapshot.Modules.Where(module => CSharpDiscovery.Definition.IsManifest(module.Path) && module.Language == "csharp")
            .ToDictionary(module => module.Path, StringComparer.Ordinal);
        var ownership = new ProjectOwnership(modules.Values);
        var files = snapshot.Files.Where(CSharpDiscovery.Definition.IsSource)
            .ToLookup(file => ownership.ForFile(file)?.Path ?? ".", StringComparer.Ordinal);
        var paths = modules.Keys.Concat(files.Select(group => group.Key)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var projects = new List<ParsedProject>();
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            var options = SourceOptions.Read(snapshot, modules.GetValueOrDefault(path), ownership);
            var trees = await ReadTrees(snapshot.Root, files[path], options.ParseOptions, token);
            var imports = options.ImplicitUsings ? ImplicitImports.Create(options.ParseOptions) : null;
            projects.Add(new(path, options, trees, imports));
        }
        return projects.ToArray();
    }

    private static async Task<SyntaxTree[]> ReadTrees(string root, IEnumerable<string> files, CSharpParseOptions options, CancellationToken token)
    {
        var trees = new List<SyntaxTree>();
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(Path.Combine(root, file), token);
            trees.Add(CSharpSyntaxTree.ParseText(text, options, file, cancellationToken: token));
        }
        return trees.ToArray();
    }
}
