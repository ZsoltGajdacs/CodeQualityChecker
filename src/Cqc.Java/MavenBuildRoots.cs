using Cqc.Core;
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
            var xml = XDocument.Load(manifest);
            var declared = xml.Root?.Elements().Where(e => e.Name.LocalName == "modules")
                .SelectMany(e => e.Elements().Where(c => c.Name.LocalName == "module")) ?? [];
            foreach (var module in declared)
            {
                var pom = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest)!, module.Value.Trim(), "pom.xml"));
                var relative = Path.GetRelativePath(root, pom).Replace('\\', '/');
                if (relative == parent.Path) throw new InvalidOperationException("Maven module cannot aggregate itself: " + parent.Path);
                aggregated.Add(relative);
            }
        }
        return modules.Where(m => m.Language != "java" || !aggregated.Contains(m.Path));
    }
}
