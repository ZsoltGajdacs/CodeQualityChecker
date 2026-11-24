using System.Security.Cryptography;
using System.Text;

namespace Cqc.Java;

/// <summary>Fingerprints resolved Maven artifacts once per run without copying their contents.</summary>
internal sealed class JavaClasspathReferences
{
    private readonly Dictionary<string, JavaClasspathEntry> entries = new(StringComparer.Ordinal);

    internal JavaClasspathEntry Read(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var fullPath = MavenPaths.Canonical(path);
        if (entries.TryGetValue(fullPath, out var known)) return known;
        var entry = Directory.Exists(fullPath)
            ? new JavaClasspathEntry(fullPath, "directory", HashDirectory(fullPath, token))
            : new JavaClasspathEntry(fullPath, "jar", File.Exists(fullPath) ? Convert.ToHexStringLower(HashFile(fullPath)) : new string('0', 64));
        entries.Add(fullPath, entry);
        return entry;
    }

    private static string HashDirectory(string path, CancellationToken token)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var classes = EnumerateFiles(new DirectoryInfo(path), token)
            .Where(file => file.Name.EndsWith(".class", StringComparison.Ordinal))
            .Select(file => (File: file, Name: Path.GetRelativePath(path, file.FullName).Replace(Path.DirectorySeparatorChar, '/')))
            .OrderBy(item => item.Name, StringComparer.Ordinal);
        // Java worker: sorted UTF-8 relative name, NUL, then the raw 32-byte file digest.
        foreach (var (file, name) in classes)
        {
            token.ThrowIfCancellationRequested();
            digest.AppendData(Encoding.UTF8.GetBytes(name));
            digest.AppendData([0]);
            digest.AppendData(HashFile(file.FullName));
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static IEnumerable<FileInfo> EnumerateFiles(FileSystemInfo entry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Classpath output contains a symbolic link: " + entry.FullName);
        return entry is DirectoryInfo directory
            ? directory.EnumerateFileSystemInfos().SelectMany(child => EnumerateFiles(child, token))
            : [(FileInfo)entry];
    }

    private static byte[] HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
