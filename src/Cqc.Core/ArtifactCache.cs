using System.Security.Cryptography;

namespace Cqc.Core;

/// <summary>Shared atomic artifact IO; language modules own artifact names and contents.</summary>
public sealed class ArtifactCache(ProjectSnapshot snapshot)
{
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
        using var stream = File.OpenRead(source);
        var directory = Path.Combine(snapshot.Root, ".cqc", "references");
        RejectLink(directory);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Convert.ToHexStringLower(SHA256.HashData(stream)) + Path.GetExtension(source));
        RejectLink(target);
        if (!File.Exists(target)) File.Copy(source, target);
        return target;
    }

    private static void RejectLink(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Artifact cache paths must not be symbolic links.");
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
