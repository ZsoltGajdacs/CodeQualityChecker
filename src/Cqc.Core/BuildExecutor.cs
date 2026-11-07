namespace Cqc.Core;

public sealed record BuildArtifacts(string Directory, LanguageBuildResult[] Results) : IDisposable
{
    public SortedDictionary<string, string> ToolVersions => new(Results.SelectMany(result => result.ToolVersions
        .Select(pair => new KeyValuePair<string, string>(result.Language + ":" + pair.Key, pair.Value)))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
    public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
}

/// <summary>Owns isolation and lifetime; language modules own build commands and report formats.</summary>
public sealed class BuildExecutor(IEnumerable<ILanguageExecution> languages)
{
    public async Task<BuildArtifacts> CollectAsync(ProjectSnapshot snapshot, CancellationToken token)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "cqc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            SnapshotCopy.Copy(snapshot.Root, scratch);
            if (snapshot.Modules.Any(m => !m.Supported))
                throw new InvalidOperationException("Execution requires supported project integrations; inspect_project lists unsupported modules.");
            var results = new List<LanguageBuildResult>();
            foreach (var language in snapshot.Modules.Select(m => m.Language).Distinct().Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var driver = languages.SingleOrDefault(x => x.Language == language)
                    ?? throw new InvalidOperationException("No execution integration registered for " + language + ".");
                results.Add(await driver.ExecuteAsync(snapshot, scratch, token));
            }
            return new(scratch, results.ToArray());
        }
        catch { Directory.Delete(scratch, true); throw; }
    }
}
