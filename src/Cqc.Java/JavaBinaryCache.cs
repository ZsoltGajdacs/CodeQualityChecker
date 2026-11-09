using System.IO.Compression;
using System.Security.Cryptography;
using Cqc.Core;

namespace Cqc.Java;

/// <summary>Retains immutable classpath binaries, archiving temporary class directories as deterministic JARs.</summary>
internal sealed class JavaBinaryCache(ProjectSnapshot snapshot, string scratch)
{
    private readonly ArtifactCache artifacts = new(snapshot);
    private readonly Dictionary<string, JavaClasspathEntry> persisted = new(StringComparer.Ordinal);

    internal JavaClasspathEntry Persist(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var full = MavenPaths.Canonical(path);
        if (persisted.TryGetValue(full, out var entry)) return entry;
        if (!File.Exists(full) && !Directory.Exists(full)) return new(full, new string('0', 64));
        var source = Directory.Exists(full) ? ArchiveDirectory(full, token) : full;
        var cached = artifacts.PersistFile(source);
        using var stream = File.OpenRead(cached);
        entry = new(cached, Convert.ToHexStringLower(SHA256.HashData(stream)));
        persisted.Add(full, entry);
        return entry;
    }

    private string ArchiveDirectory(string directory, CancellationToken token)
    {
        var temporary = Path.Combine(scratch, ".cqc-binaries");
        Directory.CreateDirectory(temporary);
        var destination = Path.Combine(temporary, Wire.Hash(directory) + ".jar");
        using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Classpath output contains a symbolic link: " + file);
            var entry = archive.CreateEntry(Path.GetRelativePath(directory, file).Replace('\\', '/'));
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var input = File.OpenRead(file);
            using var output = entry.Open();
            input.CopyTo(output);
        }
        return destination;
    }

}
