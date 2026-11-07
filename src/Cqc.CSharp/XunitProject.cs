using System.Xml.Linq;

namespace Cqc.CSharp;

internal static class XunitProject
{
    public static bool IsTestProject(bool hasFrameworkPackage, string? declared) => hasFrameworkPackage || bool.TryParse(declared, out var value) && value;

    public static bool HasPackage(XDocument document) => document.Descendants()
        .Where(node => node.Name.LocalName == "PackageReference")
        .Select(node => (string?)node.Attribute("Include") ?? (string?)node.Attribute("Update") ?? "")
        .Any(IsFrameworkPackage);

    private static bool IsFrameworkPackage(string name) => name.Equals("xunit", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("xunit.v3", StringComparison.OrdinalIgnoreCase) || name.StartsWith("xunit.v3.", StringComparison.OrdinalIgnoreCase);
}
