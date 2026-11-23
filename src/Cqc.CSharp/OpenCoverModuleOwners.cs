using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Cqc.CSharp;

/// <summary>Identifies production outputs even when a test project runs a copy of the assembly.</summary>
internal sealed class OpenCoverModuleOwners(IReadOnlyDictionary<string, string> outputs)
{
    private readonly ILookup<string, string> byPath = outputs.ToLookup(item => item.Value, item => item.Key, StringComparer.Ordinal);
    private readonly ILookup<Guid, string> byImage = outputs.Select(item => (Project: item.Key, Image: ImageId(item.Value)))
        .Where(item => item.Image != Guid.Empty).ToLookup(item => item.Image, item => item.Project);

    internal string? Find(string? path, string? outputDirectory)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (!Path.IsPathFullyQualified(path))
        {
            if (outputDirectory is null || Path.GetFileName(path) != path) return null;
            path = Path.Combine(outputDirectory, path);
        }
        var exact = byPath[path].Take(2).ToArray();
        if (exact.Length > 0) return exact.Length == 1 ? exact[0] : null;
        var images = byImage[ImageId(path)].Take(2).ToArray();
        return images.Length == 1 ? images[0] : null;
    }

    private static Guid ImageId(string path)
    {
        if (!File.Exists(path)) return Guid.Empty;
        try
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            if (!image.HasMetadata) return Guid.Empty;
            var metadata = image.GetMetadataReader();
            return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return Guid.Empty;
        }
    }
}
