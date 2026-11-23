using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

internal sealed record ProjectCompilation(
    ParsedProject Source, CSharpCompilation Compilation, ProjectSymbolIdentity Identity, AnalysisContext Context,
    KeyValuePair<MetadataReference, string>[] ProjectReferences, Diagnostic[] Diagnostics);

/// <summary>Builds only the project references that static configuration can establish.</summary>
internal sealed class ProjectCompilations(ProjectSnapshot snapshot, ParsedProject[] sources, CancellationToken token)
{
    private readonly CachedReferences references = new(snapshot);
    private readonly Dictionary<string, ParsedProject> projects = sources.ToDictionary(source => source.Path, StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectCompilation> compilations = new(StringComparer.Ordinal);
    private readonly HashSet<string> active = new(StringComparer.Ordinal);

    public ProjectCompilation[] Build() => sources.Select(BuildProject).ToArray();

    private ProjectCompilation BuildProject(ParsedProject source)
    {
        token.ThrowIfCancellationRequested();
        if (compilations.TryGetValue(source.Path, out var built)) return built;

        active.Add(source.Path);
        var diagnostics = source.Options.Diagnostics.ToList();
        diagnostics.AddRange(source.Options.ReferenceProblems.Select(message => ProjectDiagnostic(source, message)));
        var directReferences = source.Options.ProjectReferences.Select(path => FindProject(source, path, diagnostics))
            .OfType<ParsedProject>().DistinctBy(project => project.Path).Select(BuildProject).ToArray();
        var projectReferences = ReferenceClosure(directReferences);
        var metadata = references.Read(source, diagnostics);
        var compilation = CreateCompilation(source, metadata.Concat(projectReferences.Select(reference => reference.Key)));
        if (directReferences.Any(project => !project.Context.Verified))
            diagnostics.Add(ProjectDiagnostic(source, "A referenced project's compilation is incomplete."));
        diagnostics.AddRange(CompilationDiagnostics(source, compilation));
        var verified = diagnostics.Count == 0;
        var context = new AnalysisContext(source.ContextId, source.Path, "compilation",
            directReferences.Select(reference => reference.Context.Id).ToArray(), verified, verified ? [] : ["context.unverified"]);
        var identity = new ProjectSymbolIdentity(source.Path, compilation, projectReferences);
        var result = new ProjectCompilation(source, compilation, identity, context, projectReferences, diagnostics.ToArray());
        compilations.Add(source.Path, result);
        active.Remove(source.Path);
        return result;
    }

    private ParsedProject? FindProject(ParsedProject source, string include, List<Diagnostic> diagnostics)
    {
        var directory = Path.GetDirectoryName(Path.Combine(snapshot.Root, source.Path))!;
        var normalized = include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetRelativePath(snapshot.Root, Path.GetFullPath(normalized, directory)).Replace('\\', '/');
        if (active.Contains(path))
            diagnostics.Add(ProjectDiagnostic(source, "Cyclic ProjectReference to " + path + "."));
        else if (projects.TryGetValue(path, out var project) && path != ".")
            return project;
        else
            diagnostics.Add(ProjectDiagnostic(source, "ProjectReference " + include + " does not name a discovered project."));
        return null;
    }

    private static KeyValuePair<MetadataReference, string>[] ReferenceClosure(ProjectCompilation[] direct) =>
        direct.Select(project => new KeyValuePair<MetadataReference, string>(project.Compilation.ToMetadataReference(), project.Source.Path))
            .Concat(direct.SelectMany(project => project.ProjectReferences))
            .DistinctBy(reference => reference.Value, StringComparer.Ordinal).ToArray();

    private CSharpCompilation CreateCompilation(ParsedProject source, IEnumerable<MetadataReference> metadata)
    {
        var trees = source.Imports is null ? source.Trees : source.Trees.Append(source.Imports);
        var topLevel = source.Trees.Any(tree => tree.GetRoot(token).ChildNodes().OfType<GlobalStatementSyntax>().Any());
        return CSharpCompilation.Create(source.Options.AssemblyName, trees, metadata,
            new CSharpCompilationOptions(topLevel ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    private IEnumerable<Diagnostic> CompilationDiagnostics(ParsedProject project, CSharpCompilation compilation) =>
        compilation.GetDiagnostics(token).Where(error => error.Severity == DiagnosticSeverity.Error).Select(error =>
            new Diagnostic(error.Id, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                error.Location.SourceTree?.FilePath ?? DiagnosticFile(project), "csharp"));

    private static Diagnostic ProjectDiagnostic(ParsedProject project, string message) =>
        new("csharp.project_reference", message, DiagnosticFile(project), "csharp");

    private static string? DiagnosticFile(ParsedProject project) => project.Path == "." ? null : project.Path;
}
