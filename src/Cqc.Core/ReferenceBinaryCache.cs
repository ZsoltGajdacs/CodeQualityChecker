using System.Security.Cryptography;

namespace Cqc.Core;

/// <summary>Maintains source-verified immutable reference binaries and repairs corrupted cache entries atomically.</summary>
internal sealed class ReferenceBinaryCache(DirectoryInfo directory)
{
    internal string Persist(FileInfo source)
    {
        if (directory.LinkTarget is not null) throw new ArgumentException("Reference cache directory must not be a symbolic link.");
        directory.Create();
        using var stream = source.OpenRead();
        var hash = SHA256.HashData(stream);
        var target = new FileInfo(Path.Combine(directory.FullName, Convert.ToHexStringLower(hash) + source.Extension));
        if (target.LinkTarget is not null) throw new ArgumentException("Reference cache entries must not be symbolic links.");
        if (!ValidContent(target, hash)) ReplaceArtifact(source, target, hash);
        return target.FullName;
    }

    private static bool ValidContent(FileInfo file, byte[] expectedHash)
    {
        if (!file.Exists) return false;
        using var stream = file.OpenRead();
        return SHA256.HashData(stream).AsSpan().SequenceEqual(expectedHash);
    }

    private static void ReplaceArtifact(FileInfo source, FileInfo target, byte[] expectedHash)
    {
        var temporary = new FileInfo(target.FullName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            source.CopyTo(temporary.FullName);
            if (!ValidContent(temporary, expectedHash))
                throw new IOException("Reference binary changed while being cached: " + source.FullName);
            File.Move(temporary.FullName, target.FullName, true);
        }
        finally { if (File.Exists(temporary.FullName)) File.Delete(temporary.FullName); }
    }
}
