using System.Text.Json;
using Cqc.Core;

namespace Cqc.CSharp;

public sealed class CSharpExecution : ILanguageExecution
{
    public const string CoverletVersion = "6.0.4";
    public string Language => "csharp";

    public async Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string scratchDirectory, CancellationToken cancellationToken)
    {
        var references = new List<string>();
        var versions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var results = Path.Combine(scratchDirectory, ".cqc-dotnet-results-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(results);
        var environment = new ExecutionEnvironment(scratchDirectory, results, versions);
        foreach (var module in snapshot.Modules.Where(module => module.Language == Language))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!module.Supported) throw new InvalidOperationException("C# execution requires SDK-style .NET projects.");
            references.AddRange(await BuildModuleAsync(environment, module, cancellationToken));
        }
        await CSharpReferenceMetadata.WriteAsync(snapshot, scratchDirectory, references, cancellationToken);
        return new(Language, Directory.GetFiles(results, "coverage.opencover.xml", SearchOption.AllDirectories), versions);
    }

    public MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods) =>
        CSharpCoverageReader.ReadReports(build.Reports, methods.Where(method => method.Language == Language));

    private static async Task<string[]> BuildModuleAsync(ExecutionEnvironment environment, ProjectModule module, CancellationToken token)
    {
        var scratch = environment.Scratch;
        var project = Path.Combine(scratch, module.Path);
        environment.Versions["dotnet"] = (await Required("dotnet", ["--version"], scratch, token)).Output.Trim();
        environment.Versions["coverlet"] = CoverletVersion;
        var collector = await CollectorPath(scratch, token);
        await Required("dotnet", ["test", project, "--test-adapter-path", collector, "--collect",
            "XPlat Code Coverage;Format=opencover", "--results-directory", environment.Results], scratch, token);
        var references = await Required("dotnet", ["msbuild", project, "-target:ResolveReferences", "-getItem:ReferencePath", "-nologo"], scratch, token);
        using var json = JsonDocument.Parse(references.Output);
        return json.RootElement.GetProperty("Items").GetProperty("ReferencePath").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString()!).Where(File.Exists).ToArray();
    }

    private sealed record ExecutionEnvironment(string Scratch, string Results, SortedDictionary<string, string> Versions);

    private static async Task<string> CollectorPath(string scratch, CancellationToken token)
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var collector = Path.Combine(packageRoot, "coverlet.collector", CoverletVersion, "build", "netstandard2.0");
        if (Directory.Exists(collector)) return collector;
        var tools = Path.Combine(scratch, "cqc-tools.csproj");
        await File.WriteAllTextAsync(tools, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"coverlet.collector\" Version=\"{CoverletVersion}\" /></ItemGroup></Project>", token);
        await Required("dotnet", ["restore", tools], scratch, token);
        return collector;
    }

    private static async Task<CommandResult> Required(string executable, string[] arguments, string directory, CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync(executable, arguments, directory, token);
        if (result.ExitCode != 0) throw new InvalidOperationException($"{executable} exited {result.ExitCode}: {result.Error}\n{result.Output}");
        return result;
    }
}
