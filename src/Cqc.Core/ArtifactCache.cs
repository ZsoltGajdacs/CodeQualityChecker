
namespace Cqc.Core;

/// <summary>Shared atomic artifact IO; language modules own artifact names and contents.</summary>
public sealed class ArtifactCache(ProjectSnapshot snapshot)
{
    public async Task InvalidateAsync(string name, CancellationToken token)
    {
        if (Path.GetFileName(name) != name) throw new ArgumentException("Artifact name must be a simple filename.");
        EvidenceStore.EnsureArtifactDirectory(snapshot.Root);
        var path = Path.Combine(snapshot.Root, ".cqc", name);
        await ReplaceText(path + ".source", "incomplete", token);
        if (!File.Exists(path)) await ReplaceText(path, "", token);
    }

    public async Task ReplaceLinesAsync(string name, IEnumerable<string> lines, CancellationToken token)
    {
        if (Path.GetFileName(name) != name) throw new ArgumentException("Artifact name must be a simple filename.");
        EvidenceStore.EnsureArtifactDirectory(snapshot.Root);
        var path = Path.Combine(snapshot.Root, ".cqc", name);
        await ReplaceText(path + ".source", "updating", token);
        var content = string.Join(Environment.NewLine, lines.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) + Environment.NewLine;
        await ReplaceText(path, content, token);
        await ReplaceText(path + ".source", snapshot.SourceFingerprint, token);
    }

    public string PersistFile(string source)
    {
        EvidenceStore.EnsureArtifactDirectory(snapshot.Root);
        var directory = new DirectoryInfo(Path.Combine(snapshot.Root, ".cqc", "references"));
        return new ReferenceBinaryCache(directory).Persist(new FileInfo(source));
    }

    public Task ReplaceJsonAsync<T>(string name, T value, CancellationToken token)
    {
        if (Path.GetFileName(name) != name) throw new ArgumentException("Artifact name must be a simple filename.");
        EvidenceStore.EnsureArtifactDirectory(snapshot.Root);
        var path = Path.Combine(snapshot.Root, ".cqc", name);
        return ReplaceText(path, System.Text.Json.JsonSerializer.Serialize(value, Wire.Json), token);
    }

    private static async Task ReplaceText(string path, string content, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, token);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
