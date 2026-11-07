using System.Reflection;
using System.Security.Cryptography;
using Cqc.Core;

namespace Cqc.CSharp;

internal static class CSharpReferenceMetadata
{
    public static async Task WriteAsync(ProjectSnapshot snapshot, string scratch, IEnumerable<string> references, CancellationToken token)
    {
        var paths = references.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        ValidateAssemblies(paths);
        var cache = new ArtifactCache(snapshot);
        var persistent = paths.Select(path => path.StartsWith(scratch + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? cache.PersistFile(path) : path);
        await cache.ReplaceLinesAsync("csharp-references.txt", persistent, token);
    }

    private static void ValidateAssemblies(string[] paths)
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var name = AssemblyName.GetAssemblyName(path).Name ?? Path.GetFileName(path);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (identities.TryGetValue(name, out var prior) && prior != hash)
                throw new InvalidOperationException("Modules resolve conflicting assemblies named " + name + ". Analyze those module roots separately; dependency scores require unambiguous references.");
            identities[name] = hash;
        }
    }
}
