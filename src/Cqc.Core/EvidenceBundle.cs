using System.IO.Compression;

namespace Cqc.Core;

internal static class EvidenceBundle
{
    internal const string FileName = "evidence-v1.zip";

    internal static async Task WriteAsync(string path, DirectoryInfo source, string[] names, CancellationToken token)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var name in names)
        {
            await using var input = File.OpenRead(EvidenceStore.EvidencePath(source, name));
            await using var entry = archive.CreateEntry(name).Open();
            await input.CopyToAsync(entry, token);
        }
    }

    internal static async Task ReadAsync(Stream input, DirectoryInfo? destination, CancellationToken token)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        ValidateEntries(archive);
        foreach (var entry in archive.Entries)
        {
            await using var data = entry.Open();
            await using var output = destination is null ? Stream.Null : new FileStream(
                EvidenceStore.EvidencePath(destination, entry.FullName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await data.CopyToAsync(output, token);
        }
    }

    private static void ValidateEntries(ZipArchive archive)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!EvidenceStore.IsEvidenceName(entry.FullName) || !names.Add(entry.FullName))
                throw new InvalidDataException("Evidence bundle entries must have distinct simple filenames.");
        }
        if (archive.GetEntry(EvidenceStore.CoverageFileName) is null)
            throw new InvalidDataException("Evidence bundle is missing coverage.json.");
    }
}
