using System.Text.Json;
using Cqc.Core;

namespace Cqc.CSharp;

public sealed class CSharpExecution : ILanguageExecution
{
    public const string CoverletVersion = "6.0.4";
    internal const string MethodIdentityVersion = "2";
    public string Language => "csharp";
    public string? CoverageIdentityVersion => MethodIdentityVersion;

    public async Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string runDirectory, CancellationToken cancellationToken)
    {
        var projectRoot = ProjectPaths.ResolveDirectory(new DirectoryInfo(snapshot.Root));
        var projects = new List<BuiltProject>();
        var versions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var results = Path.Combine(runDirectory, "dotnet-results");
        Directory.CreateDirectory(results);
        var environment = new ExecutionEnvironment(projectRoot, runDirectory, results, versions);
        foreach (var module in snapshot.Modules.Where(module => module.Language == Language))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!module.Supported) throw new InvalidOperationException("C# execution requires SDK-style .NET projects.");
            projects.Add(await BuildModuleAsync(environment, module, cancellationToken));
        }
        await CSharpReferenceMetadata.WriteAsync(snapshot, runDirectory, projects.Select(project => project.References), cancellationToken);
        var reports = ReportOwners(projects);
        return new(Language, reports.Keys.ToArray(), versions)
        {
            SourceRoot = projectRoot,
            ReferenceManifestName = CSharpReferenceMetadata.FileName,
            ReportModules = reports,
            ProjectOutputs = projects.ToDictionary(project => project.References.ProjectPath, project => project.TargetPath, StringComparer.Ordinal)
        };
    }

    public MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods) =>
        CSharpCoverageReader.ReadReports(build, methods.Where(method => method.Language == Language));

    private sealed record BuiltProject(CSharpProjectReferences References, string TargetPath, string[] Reports);

    private static SortedDictionary<string, string> ReportOwners(IEnumerable<BuiltProject> projects) => new(
        projects.SelectMany(project => project.Reports.Select(report => (Report: report, Owner: project.References.ProjectPath)))
            .ToDictionary(item => item.Report, item => item.Owner, StringComparer.Ordinal), StringComparer.Ordinal);

    private static async Task<BuiltProject> BuildModuleAsync(ExecutionEnvironment environment, ProjectModule module, CancellationToken token)
    {
        var root = environment.ProjectRoot;
        var project = Path.Combine(root, module.Path);
        if (environment.Collector is null)
        {
            environment.Versions["dotnet"] = (await Required("dotnet", ["--version"], root, token)).Output.Trim();
            environment.Versions["coverlet"] = CoverletVersion;
            environment.Collector = await CollectorPath(root, environment.RunDirectory, token);
        }
        var collector = environment.Collector;
        var results = Path.Combine(environment.Results, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(results);
        await Required("dotnet", ["test", project, "--test-adapter-path", collector, "--collect",
            "XPlat Code Coverage;Format=opencover", "--results-directory", results], root, token);
        var references = await Required("dotnet", ["msbuild", project, "-target:ResolveReferences", "-getItem:ReferencePath",
            "-getProperty:TargetPath", "-nologo"], root, token);
        using var json = JsonDocument.Parse(references.Output);
        var paths = json.RootElement.GetProperty("Items").GetProperty("ReferencePath").EnumerateArray()
            .Where(item => !ProjectOutput(item)).Select(item => item.GetProperty("FullPath").GetString()!)
            .Where(File.Exists).ToArray();
        var target = json.RootElement.GetProperty("Properties").GetProperty("TargetPath").GetString() ?? "";
        return new(new(module.Path, paths), target, Directory.GetFiles(results, "coverage.opencover.xml", SearchOption.AllDirectories));
    }

    private static bool ProjectOutput(JsonElement item) =>
        item.TryGetProperty("ReferenceSourceTarget", out var source) && source.GetString() == "ProjectReference";

    private sealed record ExecutionEnvironment(string ProjectRoot, string RunDirectory, string Results, SortedDictionary<string, string> Versions)
    {
        public string? Collector { get; set; }
    }

    private static async Task<string> CollectorPath(string projectRoot, string runDirectory, CancellationToken token)
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var collector = Path.Combine(packageRoot, "coverlet.collector", CoverletVersion, "build", "netstandard2.0");
        if (Directory.Exists(collector)) return collector;
        var tools = Path.Combine(runDirectory, "cqc-tools.csproj");
        await File.WriteAllTextAsync(tools, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"coverlet.collector\" Version=\"{CoverletVersion}\" /></ItemGroup></Project>", token);
        await Required("dotnet", ["restore", tools], projectRoot, token);
        return collector;
    }

    private static async Task<CommandResult> Required(string executable, string[] arguments, string directory, CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync(executable, arguments, directory, token);
        if (result.ExitCode != 0) throw new InvalidOperationException($"{executable} exited {result.ExitCode}: {result.Error}\n{result.Output}");
        return result;
    }
}
