using Cqc.Core;
using System.Xml;
using System.Xml.Linq;

namespace Cqc.Java;

internal static class MavenBuildRoots
{
    internal static IEnumerable<ProjectModule> Select(ProjectModule[] modules, string root)
    {
        var aggregated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parent in modules.Where(m => m.Language == "java" && m.Supported))
        {
            var manifest = Path.Combine(root, parent.Path);
            var xml = ReadManifest(manifest, parent.Path);
            var declared = xml.Root?.Elements().Where(e => e.Name.LocalName == "modules")
                .SelectMany(e => e.Elements().Where(c => c.Name.LocalName == "module")) ?? [];
            foreach (var module in declared)
            {
                var pom = ModulePom(manifest, module.Value);
                var relative = Path.GetRelativePath(root, pom).Replace('\\', '/');
                if (relative == parent.Path) throw new InvalidOperationException("Maven module cannot aggregate itself: " + parent.Path);
                aggregated.Add(relative);
            }
        }
        return EnsureRoots(modules, aggregated);
    }

    private static ProjectModule[] EnsureRoots(ProjectModule[] modules, HashSet<string> aggregated)
    {
        var roots = modules.Where(m => m.Language != "java" || !aggregated.Contains(m.Path)).ToArray();
        if (modules.Length > 0 && roots.Length == 0) throw new InvalidOperationException("Maven reactor has no build root; check for cyclic module declarations.");
        return roots;
    }

    private static XDocument ReadManifest(string manifest, string relativePath)
    {
        try { return XDocument.Load(manifest); }
        catch (XmlException exception) { throw new InvalidOperationException("Invalid Maven POM " + relativePath + ": " + exception.Message, exception); }
    }

    private static string ModulePom(string manifest, string declared)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest)!, declared.Trim()));
        return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? path : Path.Combine(path, "pom.xml");
    }
}
