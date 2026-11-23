using Cqc.Core;
using Microsoft.CodeAnalysis.CSharp;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

/// <summary>Conservative static Debug configuration. This does not evaluate MSBuild.</summary>
internal sealed record SourceOptions(CSharpParseOptions ParseOptions, bool ImplicitUsings, bool TestProject,
    Diagnostic[] Diagnostics)
{
    public string? TargetFramework { get; init; }
    public required string AssemblyName { get; init; }
    public string[] ProjectReferences { get; init; } = [];
    public string[] ReferenceProblems { get; init; } = [];
    public bool Complete => Diagnostics.Length == 0;

    public static SourceOptions Read(ProjectSnapshot snapshot, ProjectModule? project, ProjectOwnership ownership)
    {
        var settings = StaticProjectSettings.Read(snapshot, project);
        if (ownership.IsAmbiguous(project))
            settings.Problems.Add("Multiple projects share this source directory; analyze an unambiguous module configuration for verified scores.");
        if (project is { Supported: false })
            settings.Problems.Add("Project compilation settings are unavailable for " + project.Integration + ".");
        return Build(settings, project?.Path);
    }

    private static SourceOptions Build(StaticProjectSettings settings, string? file)
    {
        var diagnostics = settings.Problems.Select(message => new Diagnostic("csharp.compile_configuration", message, file, "csharp")).ToList();
        var symbols = PreprocessorSymbols.Read(settings.Properties, diagnostics, file);
        var language = settings.Properties.GetValueOrDefault("LangVersion", PreprocessorSymbols.DefaultLanguage(settings.Properties));
        if (!LanguageVersionFacts.TryParse(language, out var version))
        {
            diagnostics.Add(new("csharp.compile_configuration", "Cannot statically determine LangVersion: " + language, file, "csharp"));
            version = LanguageVersion.Latest;
        }
        var imports = settings.Properties.GetValueOrDefault("ImplicitUsings", "disable") is "true" or "enable";
        var assemblyName = settings.Properties.GetValueOrDefault("AssemblyName");
        if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName.IndexOfAny(['$', '@', '/', '\\', ':', '\0']) >= 0)
            assemblyName = file is null ? "CqcLooseSources" : Path.GetFileNameWithoutExtension(file);
        return new(new CSharpParseOptions(version, preprocessorSymbols: symbols), imports, settings.IsTestProject, diagnostics.ToArray())
        {
            TargetFramework = settings.Properties.GetValueOrDefault("TargetFramework"),
            AssemblyName = assemblyName,
            ProjectReferences = settings.ProjectReferences.Includes.ToArray(),
            ReferenceProblems = settings.ProjectReferences.Problems.ToArray()
        };
    }
}
