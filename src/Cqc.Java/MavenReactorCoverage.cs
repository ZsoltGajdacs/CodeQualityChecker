using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Cqc.Core;

namespace Cqc.Java;

/// <summary>Collects downstream coverage inside a single evaluated Maven reactor.</summary>
internal sealed class MavenReactorCoverage(string root)
{
    private const string EntryPoint = "cqc.CoverageMain";
    private const string DownstreamReport = "downstream.xml";

    private sealed record Owner(string Module, string Classes, bool LocalReport, string Report);
    private sealed record Input(string Module, string Data, string? TestClasses, string[] VisibleModules)
    {
        public string? AgentVersion { get; init; }
        public string[] CompetingPaths { get; init; } = [];
        public Dictionary<string, string[]> ProductionPaths { get; init; } = new(StringComparer.Ordinal);
    }
    private sealed record MainOutputs(ILookup<string, string> Directories, Dictionary<string, string> Contexts);
    private sealed record Request(Owner[] Owners, Input[] Inputs);
    private sealed record Problem(string Code, string Message, string Module);
    private sealed record Response(string[] Reports, Problem[] Diagnostics);

    internal async Task<Diagnostic[]> MergeAsync(IEnumerable<CollectedMavenModule[]> reactors,
        Dictionary<string, CollectedMavenModule> contexts, SortedDictionary<string, string> reports, CancellationToken token)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var reactor in reactors)
        {
            var modules = contexts.Where(item => reactor.Contains(item.Value))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var request = BuildRequest(modules);
            if (!request.Inputs.Any(input => input.VisibleModules.Length != 0)) continue;
            try
            {
                var response = await RunAsync(request, token);
                diagnostics.AddRange(response.Diagnostics.Select(problem =>
                    new Diagnostic(problem.Code, problem.Message, problem.Module, "java")));
                foreach (var module in response.Reports.Distinct(StringComparer.Ordinal))
                    diagnostics.AddRange(ApplyReport(module, modules[module], reports));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or
                                              System.ComponentModel.Win32Exception)
            {
                diagnostics.Add(new("java.coverage_aggregation_failed", exception.Message, Language: "java"));
            }
        }
        return diagnostics.ToArray();
    }

    private Request BuildRequest(Dictionary<string, CollectedMavenModule> modules)
    {
        var ownership = new MavenOutputOwnership(root, modules.Values.Select(value => value.Metadata));
        var owners = modules.Where(item => Directory.Exists(item.Value.Metadata.MainOutput))
            .Select(item => new Owner(item.Key, MavenPaths.Canonical(item.Value.Metadata.MainOutput),
                item.Value.LocalCoverage is not null, Path.Combine(item.Value.Output, DownstreamReport))).ToArray();
        var mainOutputs = new MainOutputs(
            owners.ToLookup(owner => owner.Classes, owner => owner.Module, StringComparer.Ordinal),
            owners.ToDictionary(owner => owner.Module + "#main", owner => owner.Module, StringComparer.Ordinal));
        var inputs = modules.Select(item => SourceInput(item, ownership, mainOutputs)).OfType<Input>().ToArray();
        return new(owners, inputs);
    }

    private static Input? SourceInput(KeyValuePair<string, CollectedMavenModule> item,
        MavenOutputOwnership ownership, MainOutputs mainOutputs)
    {
        var module = item.Value;
        if (module.ExecutionData is not { } data) return null;
        var testSet = Array.Find(module.Metadata.SourceSets, source => source.Name == "test");
        var references = testSet is null ? [] : ownership.References(testSet);
        var testClasses = Directory.Exists(module.Metadata.TestOutput)
            ? MavenPaths.Canonical(module.Metadata.TestOutput) : null;
        var production = references.SelectMany(reference => ProductionCandidates(reference, mainOutputs))
            .GroupBy(entry => entry.Module, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Path).ToArray(), StringComparer.Ordinal);
        var visible = production.Keys.Where(owner => owner != item.Key).ToArray();
        return new(item.Key, data, testClasses, visible)
        {
            AgentVersion = module.Metadata.Coverage?.Version,
            CompetingPaths = references.Where(reference => IsCompeting(reference, mainOutputs.Directories, testClasses))
                .Select(reference => reference.Path).ToArray(),
            ProductionPaths = production
        };
    }

    private static IEnumerable<(string Module, string Path)> ProductionCandidates(
        MavenOutputOwnership.Reference reference, MainOutputs mainOutputs)
    {
        foreach (var owner in mainOutputs.Directories[reference.Path]) yield return (owner, reference.Path);
        if (reference.Contexts.Length != 1 || !CollectedMavenModule.IsRegularOutput(reference.Path)) yield break;
        if (mainOutputs.Contexts.TryGetValue(reference.Contexts[0], out var archiveOwner))
            yield return (archiveOwner, reference.Path);
    }

    private static bool IsCompeting(MavenOutputOwnership.Reference reference, ILookup<string, string> mainOutputs, string? testClasses)
    {
        if (reference.Path == testClasses) return false;
        if (!Directory.Exists(reference.Path) && !CollectedMavenModule.IsRegularOutput(reference.Path)) return false;
        if (!mainOutputs.Contains(reference.Path)) return true;
        return reference.Contexts.Any(context => context.EndsWith("#test", StringComparison.Ordinal));
    }

    private async Task<Response> RunAsync(Request request, CancellationToken token)
    {
        var command = JavaWorkerCommand.Create(root, JsonSerializer.Serialize(request, Wire.Json), EntryPoint)
            ?? throw new InvalidOperationException("Java coverage worker is unavailable.");
        var result = await ProcessRunner.RunAsync(command, token);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Java coverage worker failed: " + result.Error.Trim());
        var response = JsonSerializer.Deserialize<Response>(result.Output, Wire.Json)
            ?? throw new InvalidOperationException("Java coverage worker returned no response.");
        var known = request.Owners.Select(owner => owner.Module).ToHashSet(StringComparer.Ordinal);
        if (response.Reports.Any(module => !known.Contains(module)))
            throw new InvalidOperationException("Java coverage worker returned an unknown module.");
        return response;
    }

    private static Diagnostic[] ApplyReport(string module, CollectedMavenModule source,
        SortedDictionary<string, string> reports)
    {
        var overlay = Path.Combine(source.Output, DownstreamReport);
        if (!CollectedMavenModule.IsRegularOutput(overlay))
            return [new("java.coverage_aggregation_failed", "Expected downstream coverage report is missing.", module, "java")];
        try
        {
            var local = source.LocalCoverage;
            var combined = local is null ? overlay : MergeReports(local, overlay);
            if (local is not null) reports.Remove(local);
            reports.Add(combined, module);
            return [];
        }
        catch (Exception exception) when (exception is IOException or XmlException or InvalidOperationException)
        {
            return [new("java.coverage_aggregation_failed", exception.Message, module, "java")];
        }
    }

    // Preserve the local report's class selection and replace only entries measured downstream.
    private static string MergeReports(string local, string overlay)
    {
        var downstream = XDocument.Load(overlay).Descendants("class")
            .ToLookup(type => (string?)type.Attribute("name"), StringComparer.Ordinal);
        var report = XDocument.Load(local);
        foreach (var type in report.Descendants("class").ToArray())
        {
            var matches = downstream[(string?)type.Attribute("name")].Take(2).ToArray();
            if (matches.Length == 1) type.ReplaceWith(matches[0]);
        }
        var path = Path.Combine(Path.GetDirectoryName(overlay)!, "combined.xml");
        using var output = File.Open(path, FileMode.CreateNew, FileAccess.Write);
        report.Save(output);
        return path;
    }
}
