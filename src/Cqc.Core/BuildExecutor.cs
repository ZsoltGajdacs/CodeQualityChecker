namespace Cqc.Core;

public sealed class BuildArtifacts : IDisposable
{
    internal BuildArtifacts(string runDirectory) => RunDirectory = runDirectory;

    public string RunDirectory { get; }
    public LanguageBuildResult[] Results { get; internal set; } = [];

    public SortedDictionary<string, string> ToolVersions => new(Results.SelectMany(result => result.ToolVersions
        .Select(pair => new KeyValuePair<string, string>(result.Language + ":" + pair.Key, pair.Value)))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
    public void Dispose()
    {
        try { Directory.Delete(RunDirectory, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}

/// <summary>Dispatches builds and owns the lifetime of temporary CQC files.</summary>
public sealed class BuildExecutor(IEnumerable<ILanguageExecution> languages)
{
    public async Task<BuildArtifacts> CollectAsync(ProjectSnapshot snapshot, CancellationToken token)
    {
        if (snapshot.Modules.Any(module => !module.Supported))
            throw new InvalidOperationException("Execution requires supported project integrations; inspect_project lists unsupported modules.");
        var artifacts = new BuildArtifacts(Directory.CreateTempSubdirectory("cqc-").FullName);
        try
        {
            var results = new List<LanguageBuildResult>();
            foreach (var language in snapshot.Modules.Select(m => m.Language).Distinct().Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var driver = languages.SingleOrDefault(x => x.Language == language)
                    ?? throw new InvalidOperationException("No execution integration registered for " + language + ".");
                results.Add(await driver.ExecuteAsync(snapshot, artifacts.RunDirectory, token));
            }
            artifacts.Results = results.ToArray();
            return artifacts;
        }
        catch { artifacts.Dispose(); throw; }
    }
}
