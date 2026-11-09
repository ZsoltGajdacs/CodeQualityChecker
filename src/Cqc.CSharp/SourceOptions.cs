using Cqc.Core;
using Microsoft.CodeAnalysis.CSharp;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

internal sealed record SourceFileOptions(CSharpParseOptions ParseOptions, bool Complete, bool ImplicitUsings, bool TestProject, Diagnostic[] Diagnostics);

/// <summary>Conservative static Debug configuration. This does not evaluate MSBuild.</summary>
internal sealed class SourceOptions(ProjectSnapshot snapshot)
{
    private readonly ProjectOwnership ownership = new(snapshot.Modules);
    private readonly Dictionary<string, SourceFileOptions> projects = new(StringComparer.Ordinal);

    public SourceFileOptions ForFile(string file)
    {
        var project = ownership.ForFile(file);
        var key = project?.Path ?? ".";
        if (!projects.TryGetValue(key, out var options))
        {
            var settings = StaticProjectSettings.Read(snapshot, project);
            if (ownership.IsAmbiguous(project))
                settings.Problems.Add("Multiple projects share this source directory; analyze an unambiguous module configuration for verified scores.");
            options = Build(settings, project?.Path ?? file);
            projects.Add(key, options);
        }
        return options;
    }

    private static SourceFileOptions Build(StaticProjectSettings settings, string file)
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
        return new(new CSharpParseOptions(version, preprocessorSymbols: symbols), diagnostics.Count == 0, imports, settings.IsTestProject, diagnostics.ToArray());
    }
}
