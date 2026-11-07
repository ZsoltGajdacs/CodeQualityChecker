using System.Xml.Linq;
using Cqc.Core;

namespace Cqc.CSharp;

internal static class CSharpDiscovery
{
    public static LanguageDefinition Definition { get; } = new()
    {
        Language = "csharp", SourceExtensions = [".cs"], ManifestPatterns = ["*.csproj"],
        GeneratedSuffixes = [".g.cs", ".generated.cs", ".designer.cs"], InspectManifest = DescribeManifest,
        BuildInputPatterns = ["**/*.props", "**/*.targets", "**/global.json", "**/NuGet.Config", "**/nuget.config", "**/packages.lock.json"],
        BuildTools = ["dotnet"]
    };

    private static ProjectModule DescribeManifest(string root, string path)
    {
        try
        {
            var xml = XDocument.Load(Path.Combine(root, path));
            var sdk = xml.Root?.Attribute("Sdk")?.Value;
            return new("csharp", path, sdk is null ? "legacy-dotnet" : "sdk-dotnet", sdk is not null);
        }
        catch (System.Xml.XmlException) { return new("csharp", path, "invalid-project", false); }
    }
}
